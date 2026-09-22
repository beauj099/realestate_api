namespace RealEstateApi.Infrastructure.Services;

/// <summary>
/// Abstracts where listing and room photos are stored. The R2 implementation
/// is the long-term target; the local implementation serves files from the
/// API's wwwroot so development and testing can proceed before the R2
/// credentials exist. Both use the same storage keys
/// (e.g. "listings/1/abc.jpg"), so switching providers later only changes
/// where new bytes land -- the URLs stored in the database keep working
/// because the local one returns relative "/uploads/..." paths.
/// </summary>
public interface IImageStorage
{
    /// <summary>Stores the stream under <paramref name="key"/> and returns the public URL.</summary>
    Task<string> UploadAsync(Stream fileStream, string key, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Deletes the object stored under <paramref name="key"/>.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
