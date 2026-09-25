using AutoMapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class ListingRoomService
{
    public const int MaxPhotosPerRoom = 20;

    private readonly ListingRepository _listingRepo;
    private readonly ListingRoomRepository _roomRepo;
    private readonly ListingRoomPhotoRepository _roomPhotoRepo;
    private readonly IImageStorage _imageService;
    private readonly IOptions<R2Options> _r2Options;
    private readonly IMapper _mapper;

    public ListingRoomService(
        ListingRepository listingRepo,
        ListingRoomRepository roomRepo,
        ListingRoomPhotoRepository roomPhotoRepo,
        IImageStorage imageService,
        IOptions<R2Options> r2Options,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _roomRepo = roomRepo;
        _roomPhotoRepo = roomPhotoRepo;
        _imageService = imageService;
        _r2Options = r2Options;
        _mapper = mapper;
    }

    public async Task<IEnumerable<RoomDto>> GetRoomsAsync(int listingId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        return await RoomDtoBuilder.BuildAsync(_roomRepo, _roomPhotoRepo, _mapper, listingId, cancellationToken);
    }

    public async Task<RoomDto> CreateRoomAsync(int listingId, CreateRoomRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = _mapper.Map<ListingRoom>(request);
        room.ListingId = listingId;
        // PhotoUrl is only ever set by the upload endpoint. A client-supplied value could
        // point at another listing's object, which a later room delete would then remove.
        room.PhotoUrl = null;

        var created = await _roomRepo.CreateAsync(room, cancellationToken);
        return new RoomDto(
            created.Id, created.ListingId, created.Name, created.RoomTypeId,
            created.RoomTypeOther, created.PhotoUrl, created.CreatedAt, created.UpdatedAt,
            null, new List<FeatureDto>(), new List<CustomFeatureDto>(), new List<RoomPhotoDto>()
        );
    }

    public async Task<RoomDto?> UpdateRoomAsync(int listingId, int roomId, UpdateRoomRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        // Omitted fields stay as they are. Mapping onto ListingRoom would turn a missing
        // RoomTypeId into 0 and a missing Name into "", overwriting the stored values.
        // PhotoUrl is ignored for the same reason as in CreateRoomAsync.
        var updated = await _roomRepo.UpdateAsync(roomId, request.Name, request.RoomTypeId, request.RoomTypeOther, cancellationToken);
        if (updated == null) return null;

        var conditionTask = _roomRepo.GetConditionByRoomIdAsync(updated.Id, cancellationToken);
        var featuresTask = _roomRepo.GetLinkedFeaturesAsync(updated.Id, cancellationToken);
        var customFeaturesTask = _roomRepo.GetCustomFeaturesAsync(updated.Id, cancellationToken);
        var photosTask = _roomPhotoRepo.GetByRoomIdAsync(updated.Id, cancellationToken);

        await Task.WhenAll(conditionTask, featuresTask, customFeaturesTask, photosTask);

        return new RoomDto(
            updated.Id, updated.ListingId, updated.Name, updated.RoomTypeId,
            updated.RoomTypeOther, updated.PhotoUrl, updated.CreatedAt, updated.UpdatedAt,
            conditionTask.Result is null ? null : _mapper.Map<RoomConditionDto>(conditionTask.Result),
            _mapper.Map<List<FeatureDto>>(featuresTask.Result),
            _mapper.Map<List<CustomFeatureDto>>(customFeaturesTask.Result),
            photosTask.Result.Select(RoomDtoBuilder.ToPhotoDto).ToList()
        );
    }

    public async Task DeleteRoomAsync(int listingId, int roomId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        // Collect the stored objects before the rows go (photo rows cascade with the room).
        var photos = await _roomPhotoRepo.GetByRoomIdAsync(roomId, cancellationToken);
        var keys = photos.Select(p => p.StorageKey)
            .Append(room.PhotoUrl is null ? null : ExtractKeyFromUrl(room.PhotoUrl))
            .ToList();

        // Database first: if it fails, the room still has all its photos.
        await _roomRepo.DeleteAsync(roomId, cancellationToken);

        await DeleteRoomObjectsAsync(listingId, roomId, keys);
    }

    public async Task<IEnumerable<RoomPhotoDto>> GetPhotosAsync(int listingId, int roomId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var photos = await _roomPhotoRepo.GetByRoomIdAsync(roomId, cancellationToken);
        return photos.Select(RoomDtoBuilder.ToPhotoDto).ToList();
    }

    /// <summary>
    /// Appends a photo to the room (SortOrder = max + 1) and refreshes the room's cover
    /// (ListingRoom.PhotoUrl). Throws <see cref="RoomPhotoLimitExceededException"/> when
    /// the room already has <see cref="MaxPhotosPerRoom"/> photos.
    /// </summary>
    public async Task<RoomPhotoDto> AddPhotoAsync(int listingId, int roomId, Stream fileStream, string fileName, string contentType, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        // Cheap pre-check so a full room does not cost an upload; the insert re-checks atomically.
        if (await _roomPhotoRepo.CountByRoomIdAsync(roomId, cancellationToken) >= MaxPhotosPerRoom)
            throw new RoomPhotoLimitExceededException(MaxPhotosPerRoom);

        var key = $"rooms/{listingId}/{roomId}/{fileName}";
        var url = await _imageService.UploadAsync(fileStream, key, contentType, cancellationToken);

        ListingRoomPhoto? created;
        try
        {
            created = await _roomPhotoRepo.TryCreateAsync(roomId, url, key, MaxPhotosPerRoom, cancellationToken);
        }
        catch
        {
            // No row, so nothing would ever reference or clean up the object.
            await DeleteRoomObjectsAsync(listingId, roomId, [key]);
            throw;
        }

        if (created is null)
        {
            // Lost a race with a concurrent upload that filled the room.
            await DeleteRoomObjectsAsync(listingId, roomId, [key]);
            throw new RoomPhotoLimitExceededException(MaxPhotosPerRoom);
        }

        return RoomDtoBuilder.ToPhotoDto(created);
    }

    public async Task DeletePhotoAsync(int listingId, int roomId, int photoId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var photo = await _roomPhotoRepo.GetByIdAsync(photoId, cancellationToken);
        if (photo is null || photo.ListingRoomId != roomId
            || !await _roomPhotoRepo.DeleteAsync(photoId, roomId, cancellationToken))
            throw new KeyNotFoundException($"Photo {photoId} not found under room {roomId}");

        await DeleteRoomObjectsAsync(listingId, roomId, [photo.StorageKey]);
    }

    /// <summary>
    /// Legacy single-photo upload (POST {roomId}/photo, older app builds): now appends
    /// to the room's photos like <see cref="AddPhotoAsync"/>, cap included.
    /// </summary>
    public async Task<PhotoUploadResponse> UploadPhotoAsync(int listingId, int roomId, Stream fileStream, string fileName, string contentType, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var created = await AddPhotoAsync(listingId, roomId, fileStream, fileName, contentType, userId, isAdmin, cancellationToken);
        return new PhotoUploadResponse(created.Url);
    }

    /// <summary>
    /// Legacy single-photo delete (DELETE {roomId}/photo, older app builds): removes all
    /// of the room's photos and clears the cover.
    /// </summary>
    public async Task DeleteAllPhotosAsync(int listingId, int roomId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await _listingRepo.AssertOwnedAsync(listingId, userId, isAdmin, cancellationToken);
        var room = await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var deleted = await _roomPhotoRepo.DeleteAllForRoomAsync(roomId, cancellationToken);

        // The cover may predate the photos table (not yet backfilled), so clean it up too.
        var keys = deleted.Select(p => p.StorageKey)
            .Append(room.PhotoUrl is null ? null : ExtractKeyFromUrl(room.PhotoUrl))
            .ToList();
        await DeleteRoomObjectsAsync(listingId, roomId, keys);
    }

    /// <summary>
    /// Best-effort removal of a room's stored photos, only for keys under this room's
    /// own prefix. Rows written before PhotoUrl was server-only (or backfilled with a
    /// NULL key) may point anywhere, and deleting that could remove another listing's
    /// file. A failed delete only leaves a harmless orphan; the rows are already gone.
    /// </summary>
    private async Task DeleteRoomObjectsAsync(int listingId, int roomId, IEnumerable<string?> keys)
    {
        var prefix = $"rooms/{listingId}/{roomId}/";
        foreach (var key in keys.OfType<string>().Where(k => k.StartsWith(prefix)).Distinct())
        {
            try { await _imageService.DeleteAsync(key, CancellationToken.None); }
            catch { /* best effort */ }
        }
    }

    private string ExtractKeyFromUrl(string photoUrl)
    {
        const string localPrefix = "/uploads/";
        if (photoUrl.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
            return photoUrl[localPrefix.Length..];
        var prefix = _r2Options.Value.PublicUrl.TrimEnd('/') + "/";
        return photoUrl.StartsWith(prefix) ? photoUrl[prefix.Length..] : photoUrl;
    }

    public async Task<RoomConditionDto> UpsertConditionAsync(int listingId, int roomId, UpsertRoomConditionRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var condition = _mapper.Map<Condition>(request);
        condition.ListingRoomId = roomId;

        var result = await _roomRepo.UpsertConditionAsync(condition, cancellationToken);
        return _mapper.Map<RoomConditionDto>(result);
    }

    public async Task<IEnumerable<FeatureDto>> GetRoomFeaturesAsync(int listingId, int roomId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.GetLinkedFeaturesAsync(roomId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<IEnumerable<CustomFeatureDto>> GetRoomCustomFeaturesAsync(int listingId, int roomId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.GetCustomFeaturesAsync(roomId, cancellationToken);
        return _mapper.Map<List<CustomFeatureDto>>(features);
    }

    public async Task<IEnumerable<FeatureDto>> LinkFeatureAsync(int listingId, int roomId, int featureId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.LinkFeatureAsync(roomId, featureId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<IEnumerable<FeatureDto>> UnlinkFeatureAsync(int listingId, int roomId, int featureId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.UnlinkFeatureAsync(roomId, featureId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<CustomFeatureDto> AddCustomFeatureAsync(int listingId, int roomId, AddCustomFeatureRequest request, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var feature = _mapper.Map<ListingRoomCustomFeature>(request);
        feature.ListingRoomId = roomId;

        var result = await _roomRepo.AddCustomFeatureAsync(feature, cancellationToken);
        return _mapper.Map<CustomFeatureDto>(result);
    }

    public async Task DeleteCustomFeatureAsync(int listingId, int roomId, int customFeatureId, int? userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetOwnedByIdAsync(listingId, userId, isAdmin, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var deleted = await _roomRepo.DeleteCustomFeatureAsync(customFeatureId, roomId, cancellationToken);
        if (!deleted)
            throw new KeyNotFoundException($"Custom feature {customFeatureId} not found under room {roomId}");
    }

    private async Task<ListingRoom> GetOwnedRoomAsync(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var room = await _roomRepo.GetByIdAsync(roomId, cancellationToken);
        if (room is null || room.ListingId != listingId)
            throw new KeyNotFoundException($"Room {roomId} not found under listing {listingId}");
        return room;
    }
}

/// <summary>The room already holds <see cref="ListingRoomService.MaxPhotosPerRoom"/> photos; mapped to a 400 by the controller.</summary>
public sealed class RoomPhotoLimitExceededException(int max)
    : Exception($"A room can have at most {max} photos.");
