// Uploads an agency's brochure pages into R2 and sets Agencies.BrochurePagesJson: the pages every
// report pack of that agency ends with (agents can replace them with their own on their profile).
//
//   dotnet run --project tools/SeedAgencyBrochure -- --api-dir . --slug keller-williams --pages <folder> [--apply]
//
// Every .png / .jpg / .jpeg in the folder, in file-name order, becomes one page (replacing the
// agency's current pages). A4 portrait at ~150 dpi (1240 × 1754) prints well. Without --apply it
// is a dry run.

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var apply = args.Contains("--apply");
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
var slug = Arg("--slug") ?? throw new ArgumentException("--slug is required, e.g. --slug keller-williams");
var pagesDir = Path.GetFullPath(Arg("--pages") ?? throw new ArgumentException("--pages <folder> is required"));

var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
var config = new ConfigurationBuilder()
    .SetBasePath(apiDir)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

string Setting(string key) =>
    config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing setting '{key}'.");

var files = Directory.GetFiles(pagesDir)
    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToList();
if (files.Count == 0)
{
    Console.WriteLine($"No .png/.jpg pages in {pagesDir}.");
    return 1;
}

var bucket = Setting("R2:BucketName");
var publicUrl = Setting("R2:PublicUrl").TrimEnd('/');
Console.WriteLine($"Agency: {slug}   Pages: {files.Count} from {pagesDir}");
Console.WriteLine($"Mode:   {(apply ? "APPLY" : "DRY RUN (no changes; add --apply)")}");

await using var db = new SqlConnection(Setting("ConnectionStrings:DefaultConnection"));
await db.OpenAsync();
if (await db.ExecuteScalarAsync<int>("SELECT CASE WHEN COL_LENGTH('dbo.Agencies', 'BrochurePagesJson') IS NULL THEN 0 ELSE 1 END") == 0)
{
    Console.WriteLine("Agencies has no BrochurePagesJson yet: apply Infrastructure/Database/Patches/2026-09-28_agent_profiles.sql first.");
    return 1;
}
var agencyId = await db.ExecuteScalarAsync<int?>("SELECT Id FROM dbo.Agencies WHERE Slug = @slug", new { slug });
if (agencyId is null)
{
    Console.WriteLine($"No agency with slug '{slug}'.");
    return 1;
}

// Same client settings as the API's R2ImageService (R2 rejects the SDK's default checksum trailers).
using var s3 = new AmazonS3Client(Setting("R2:AccessKeyId"), Setting("R2:SecretAccessKey"), new AmazonS3Config
{
    ServiceURL = Setting("R2:Endpoint"),
    ForcePathStyle = true,
    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
});

var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var urls = new List<string>();
for (var i = 0; i < files.Count; i++)
{
    var ext = Path.GetExtension(files[i]).ToLowerInvariant() == ".png" ? ".png" : ".jpg";
    var key = $"agencies/{slug}/brochure-{stamp}-{i + 1:00}{ext}";
    Console.WriteLine($"  {(apply ? "uploading   " : "would upload")}  {Path.GetFileName(files[i])} -> {key}");
    if (apply)
    {
        await using var stream = File.OpenRead(files[i]);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = ext == ".png" ? "image/png" : "image/jpeg",
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        });
    }
    urls.Add($"{publicUrl}/{key}");
}

if (apply)
{
    await db.ExecuteAsync("UPDATE dbo.Agencies SET BrochurePagesJson = @json, UpdatedAt = GETUTCDATE() WHERE Id = @id",
        new { json = JsonSerializer.Serialize(urls), id = agencyId });
    Console.WriteLine($"Done: {urls.Count} page(s) set on {slug}.");
}
else
{
    Console.WriteLine("Dry run. Re-run with --apply.");
}
return 0;
