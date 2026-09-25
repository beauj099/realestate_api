// Copies photos and documents stored by the temporary local-disk storage
// (wwwroot/uploads, links starting "/uploads/") into the R2 bucket, then points
// their database links at the bucket's public URL.
//
//   dotnet run --project tools/MigrateUploadsToR2 -- [--api-dir <path>] [--uploads <path>] [--apply]
//
// Without --apply it is a dry run: it reports what it would do and changes
// nothing. Safe to re-run: objects already in R2 are not uploaded again, and
// only links still starting "/uploads/" are changed. Local files are never
// deleted, so they remain as a backup.

using System.Data;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

const string LocalPrefix = "/uploads/";

var apply = args.Contains("--apply");
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Same configuration the API reads, from the API's folder (on a server, the
// published folder that holds appsettings.json and wwwroot).
var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var config = new ConfigurationBuilder()
    .SetBasePath(apiDir)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

string Setting(string key) =>
    config[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Missing setting '{key}'.");

var connectionString = Setting("ConnectionStrings:DefaultConnection");
var bucket = Setting("R2:BucketName");
var publicUrl = Setting("R2:PublicUrl").TrimEnd('/');
var uploadsDir = Path.GetFullPath(
    Arg("--uploads") ?? Path.Combine(apiDir, config["LocalStorage:BasePath"] ?? "wwwroot/uploads"));

Console.WriteLine($"API folder:    {apiDir}  (environment: {environment})");
Console.WriteLine($"Uploads:       {uploadsDir}{(Directory.Exists(uploadsDir) ? "" : "  <-- NOT FOUND")}");
Console.WriteLine($"Bucket:        {bucket}");
Console.WriteLine($"Public URL:    {publicUrl}");
Console.WriteLine($"Mode:          {(apply ? "APPLY (uploading and updating links)" : "DRY RUN (no changes; add --apply)")}");
Console.WriteLine();

// Same client settings as the API's R2ImageService (R2 rejects the SDK's
// default checksum trailers).
using var s3 = new AmazonS3Client(
    Setting("R2:AccessKeyId"),
    Setting("R2:SecretAccessKey"),
    new AmazonS3Config
    {
        ServiceURL = Setting("R2:Endpoint"),
        ForcePathStyle = true,
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    });

// Every column that can hold a local link.
(string Table, string Column)[] columns =
[
    ("ListingPhoto", "Url"),
    ("ListingRoomPhotos", "Url"),
    ("ListingRoom", "PhotoUrl"),
    ("ListingDocuments", "Url"),
];

await using var db = new SqlConnection(connectionString);
await db.OpenAsync();

var links = new SortedSet<string>(StringComparer.Ordinal);
foreach (var (table, column) in columns)
{
    if (await db.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN COL_LENGTH(@t, @c) IS NULL THEN 0 ELSE 1 END",
            new { t = $"dbo.{table}", c = column }) == 0)
    {
        Console.WriteLine($"  (skipping {table}.{column}: not in this database)");
        continue;
    }
    var found = await db.QueryAsync<string>(
        $"SELECT DISTINCT [{column}] FROM dbo.[{table}] WHERE [{column}] LIKE @p",
        new { p = LocalPrefix + "%" });
    foreach (var link in found) links.Add(link);
}

Console.WriteLine($"Local links in the database: {links.Count}");
var migrated = new List<(string Old, string New)>();
var missing = new List<string>();

foreach (var link in links)
{
    var key = link[LocalPrefix.Length..];
    var file = Path.Combine(uploadsDir, key.Replace('/', Path.DirectorySeparatorChar));
    var newUrl = $"{publicUrl}/{key}";

    if (!File.Exists(file))
    {
        Console.WriteLine($"  MISSING FILE  {link}  (left unchanged)");
        missing.Add(link);
        continue;
    }

    var exists = await ObjectExistsAsync(key);
    Console.WriteLine($"  {(exists ? "already in R2" : apply ? "uploading   " : "would upload")}  {key}");
    if (apply && !exists)
    {
        await using var stream = File.OpenRead(file);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = ContentTypeFor(key),
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        });
    }
    migrated.Add((link, newUrl));
}

if (apply && migrated.Count > 0)
{
    // All links move together, so a failure leaves the database as it was.
    await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable);
    var rows = 0;
    foreach (var (table, column) in columns)
    {
        if (await db.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN COL_LENGTH(@t, @c) IS NULL THEN 0 ELSE 1 END",
                new { t = $"dbo.{table}", c = column }, tx) == 0)
            continue;
        foreach (var (oldUrl, newUrl) in migrated)
        {
            rows += await db.ExecuteAsync(
                $"UPDATE dbo.[{table}] SET [{column}] = @newUrl WHERE [{column}] = @oldUrl",
                new { oldUrl, newUrl }, tx);
        }
    }
    await tx.CommitAsync();
    Console.WriteLine();
    Console.WriteLine($"Updated {rows} database link(s).");
}

Console.WriteLine();
Console.WriteLine(apply
    ? $"Done: {migrated.Count} file(s) in R2, {missing.Count} missing."
    : $"Dry run: {migrated.Count} file(s) would move, {missing.Count} missing. Re-run with --apply.");
return missing.Count == 0 ? 0 : 2;

async Task<bool> ObjectExistsAsync(string key)
{
    try
    {
        await s3.GetObjectMetadataAsync(bucket, key);
        return true;
    }
    catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return false;
    }
}

static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".webp" => "image/webp",
    ".heic" => "image/heic",
    ".pdf" => "application/pdf",
    _ => "application/octet-stream",
};
