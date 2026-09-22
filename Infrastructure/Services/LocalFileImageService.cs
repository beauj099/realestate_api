using Microsoft.Extensions.Options;

namespace RealEstateApi.Infrastructure.Services;

/// <summary>
/// Temporary development storage: writes uploads to wwwroot/uploads so they
/// are served back by the static-files middleware at "/uploads/...". Keeps
/// the R2 key layout ("listings/{id}/...", "rooms/{listingId}/{roomId}/..."),
/// so the services above it need no changes when R2 is switched on.
/// </summary>
public class LocalFileImageService : IImageStorage
{
    private readonly string _basePath;
    private readonly string _publicPrefix;

    public LocalFileImageService(IOptions<LocalStorageOptions> options, IWebHostEnvironment environment)
    {
        var configured = options.Value.BasePath;
        _basePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        _publicPrefix = options.Value.PublicPrefix.TrimEnd('/') + "/";
    }

    public async Task<string> UploadAsync(Stream fileStream, string key, string contentType, CancellationToken cancellationToken = default)
    {
        var fullPath = ToFullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var destination = File.Create(fullPath);
        await fileStream.CopyToAsync(destination, cancellationToken);
        return _publicPrefix + key.Replace('\\', '/');
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = ToFullPath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ToFullPath(string key)
    {
        var relative = key.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relative));
        // A crafted key must never escape the uploads folder.
        if (!fullPath.StartsWith(Path.GetFullPath(_basePath) + Path.DirectorySeparatorChar))
            throw new ArgumentException($"Invalid storage key: {key}", nameof(key));
        return fullPath;
    }
}
