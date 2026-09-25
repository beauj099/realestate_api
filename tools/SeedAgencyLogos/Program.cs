// Uploads agency logos into the R2 bucket and points Agencies.LogoUrl at them.
//
//   dotnet run --project tools/SeedAgencyLogos -- [--api-dir <path>] [--logos <dir>] [--house-logo <file>] [--force] [--apply]
//
// For each listed (non-custom) agency, looks for <slug>.png / .jpg / .jpeg / .webp
// in the logos folder (default: the app repo's assets/images/agencies) and, for the
// house brand "realworth", the house logo (default: assets/images/logo.jpg). The file
// is uploaded to "agencies/<slug>.<ext>" and the agency's LogoUrl set to it.
//
// The same run adds a logo to a new agency later: insert its row, drop
// <slug>.png in a folder and point --logos at it.
//
// Without --apply it is a dry run. Agencies that already have a LogoUrl are skipped
// unless --force is given (use it to replace a logo). Safe to re-run.

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var apply = args.Contains("--apply");
var force = args.Contains("--force");
string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var apiDir = Path.GetFullPath(Arg("--api-dir") ?? Directory.GetCurrentDirectory());
var appAssets = Path.GetFullPath(Path.Combine(apiDir, "..", "realestate_app", "assets", "images"));
var logosDir = Path.GetFullPath(Arg("--logos") ?? Path.Combine(appAssets, "agencies"));
var houseLogo = Path.GetFullPath(Arg("--house-logo") ?? Path.Combine(appAssets, "logo.jpg"));

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

var bucket = Setting("R2:BucketName");
var publicUrl = Setting("R2:PublicUrl").TrimEnd('/');

Console.WriteLine($"API folder:    {apiDir}  (environment: {environment})");
Console.WriteLine($"Logos:         {logosDir}{(Directory.Exists(logosDir) ? "" : "  <-- NOT FOUND")}");
Console.WriteLine($"House logo:    {houseLogo}{(File.Exists(houseLogo) ? "" : "  <-- NOT FOUND")}");
Console.WriteLine($"Bucket:        {bucket}  ({publicUrl})");
Console.WriteLine($"Mode:          {(apply ? "APPLY" : "DRY RUN (no changes; add --apply)")}{(force ? ", replacing existing logos" : "")}");
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

await using var db = new SqlConnection(Setting("ConnectionStrings:DefaultConnection"));
await db.OpenAsync();

if (await db.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID('dbo.Agencies', 'U') IS NULL THEN 0 ELSE 1 END") == 0)
{
    Console.WriteLine("The Agencies table does not exist yet: apply Infrastructure/Database/Patches/2026-09-27_agencies.sql first.");
    return 1;
}

var agencies = (await db.QueryAsync<(int Id, string Slug, string Name, string? LogoUrl)>(
    "SELECT Id, Slug, Name, LogoUrl FROM dbo.Agencies WHERE IsCustom = 0 ORDER BY SortOrder, Name")).ToList();

int uploaded = 0, skipped = 0, noFile = 0;
foreach (var (id, slug, name, logoUrl) in agencies)
{
    if (logoUrl is not null && !force)
    {
        Console.WriteLine($"  has logo      {slug}");
        skipped++;
        continue;
    }

    var file = FindLogo(slug);
    if (file is null)
    {
        Console.WriteLine($"  no file       {slug}  ({name} keeps its monogram)");
        noFile++;
        continue;
    }

    var ext = Path.GetExtension(file).ToLowerInvariant();
    if (ext == ".jpeg") ext = ".jpg";
    var key = $"agencies/{slug}{ext}";
    var url = $"{publicUrl}/{key}";
    Console.WriteLine($"  {(apply ? "uploading   " : "would upload")}  {slug}  <- {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
    if (apply)
    {
        await using var stream = File.OpenRead(file);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = ext switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" },
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        });
        // A version marker, so a replaced logo is not served from a stale cache.
        var versioned = force ? $"{url}?v={DateTime.UtcNow:yyyyMMddHHmmss}" : url;
        await db.ExecuteAsync(
            "UPDATE dbo.Agencies SET LogoUrl = @url, UpdatedAt = GETUTCDATE() WHERE Id = @id",
            new { url = versioned, id });
    }
    uploaded++;
}

Console.WriteLine();
Console.WriteLine(apply
    ? $"Done: {uploaded} logo(s) uploaded, {skipped} already set, {noFile} without a file."
    : $"Dry run: {uploaded} logo(s) would upload, {skipped} already set, {noFile} without a file. Re-run with --apply.");
return 0;

string? FindLogo(string slug)
{
    if (slug == "realworth") return File.Exists(houseLogo) ? houseLogo : null;
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
    {
        var path = Path.Combine(logosDir, slug + ext);
        if (File.Exists(path)) return path;
    }
    return null;
}
