using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class ListingPhotoService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingPhotoRepository _photoRepo;
    private readonly IImageStorage _imageService;
    private readonly IOptions<R2Options> _r2Options;

    public ListingPhotoService(
        ListingRepository listingRepo,
        ListingPhotoRepository photoRepo,
        IImageStorage imageService,
        IOptions<R2Options> r2Options)
    {
        _listingRepo = listingRepo;
        _photoRepo = photoRepo;
        _imageService = imageService;
        _r2Options = r2Options;
    }

    public async Task<IEnumerable<ListingPhotoDto>> GetPhotosAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        var photos = await _photoRepo.GetByListingIdAsync(listingId, cancellationToken);
        return photos.Select(p => new ListingPhotoDto(p.Id, p.ListingId, p.Url, p.IsPrimary, p.SortOrder, p.CreatedAt));
    }

    public async Task<ListingPhotoDto> UploadAsync(int listingId, int? userId, bool isAdmin, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var count = await _photoRepo.CountByListingIdAsync(listingId, cancellationToken);
        // First photo on a listing becomes primary automatically.
        var isPrimary = count == 0;

        var key = $"listings/{listingId}/{fileName}";
        var url = await _imageService.UploadAsync(fileStream, key, contentType);
        var created = await _photoRepo.CreateAsync(listingId, url, isPrimary, count, cancellationToken);
        return new ListingPhotoDto(created.Id, created.ListingId, created.Url, created.IsPrimary, created.SortOrder, created.CreatedAt);
    }

    public async Task SetPrimaryAsync(int listingId, int photoId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        await _photoRepo.SetPrimaryAsync(listingId, photoId, cancellationToken);
    }

    /// <summary>Saves the agent's photo order; the first is the main photo. False when the ids do not match the listing's photos.</summary>
    public async Task<bool> ReorderAsync(int listingId, int? userId, bool isAdmin, IReadOnlyList<int> photoIds, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        return await _photoRepo.ReorderAsync(listingId, photoIds, cancellationToken);
    }

    public async Task DeleteAsync(int listingId, int photoId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);

        var photo = await _photoRepo.GetByIdAsync(photoId, cancellationToken);
        if (photo is null || photo.ListingId != listingId)
            throw new KeyNotFoundException($"Photo {photoId} not found under listing {listingId}");

        await _imageService.DeleteAsync(ExtractKeyFromUrl(photo.Url));
        await _photoRepo.DeleteAsync(photoId, cancellationToken);
    }

    private string ExtractKeyFromUrl(string photoUrl)
    {
        // Local dev storage returns relative "/uploads/..." URLs; R2 returns
        // absolute URLs under PublicUrl. Both share the same key layout.
        const string localPrefix = "/uploads/";
        if (photoUrl.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
            return photoUrl[localPrefix.Length..];
        var prefix = _r2Options.Value.PublicUrl.TrimEnd('/') + "/";
        return photoUrl.StartsWith(prefix) ? photoUrl[prefix.Length..] : photoUrl;
    }
}
