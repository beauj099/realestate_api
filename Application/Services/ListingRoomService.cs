using AutoMapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class ListingRoomService
{
    private readonly ListingRepository _listingRepo;
    private readonly ListingRoomRepository _roomRepo;
    private readonly R2ImageService _imageService;
    private readonly IOptions<R2Options> _r2Options;
    private readonly IMapper _mapper;

    public ListingRoomService(
        ListingRepository listingRepo,
        ListingRoomRepository roomRepo,
        R2ImageService imageService,
        IOptions<R2Options> r2Options,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _roomRepo = roomRepo;
        _imageService = imageService;
        _r2Options = r2Options;
        _mapper = mapper;
    }

    public async Task<IEnumerable<RoomDto>> GetRoomsAsync(int listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        return await RoomDtoBuilder.BuildAsync(_roomRepo, _mapper, listingId, cancellationToken);
    }

    public async Task<RoomDto> CreateRoomAsync(int listingId, CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = _mapper.Map<ListingRoom>(request);
        room.ListingId = listingId;

        var created = await _roomRepo.CreateAsync(room, cancellationToken);
        return new RoomDto(
            created.Id, created.ListingId, created.Name, created.RoomTypeId,
            created.RoomTypeOther, created.PhotoUrl, created.CreatedAt, created.UpdatedAt,
            null, new List<FeatureDto>(), new List<CustomFeatureDto>()
        );
    }

    public async Task<RoomDto?> UpdateRoomAsync(int listingId, int roomId, UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var room = _mapper.Map<ListingRoom>(request);
        room.Id = roomId;

        var updated = await _roomRepo.UpdateAsync(room, cancellationToken);
        if (updated == null) return null;

        var conditionTask = _roomRepo.GetConditionByRoomIdAsync(updated.Id, cancellationToken);
        var featuresTask = _roomRepo.GetLinkedFeaturesAsync(updated.Id, cancellationToken);
        var customFeaturesTask = _roomRepo.GetCustomFeaturesAsync(updated.Id, cancellationToken);

        await Task.WhenAll(conditionTask, featuresTask, customFeaturesTask);

        return new RoomDto(
            updated.Id, updated.ListingId, updated.Name, updated.RoomTypeId,
            updated.RoomTypeOther, updated.PhotoUrl, updated.CreatedAt, updated.UpdatedAt,
            conditionTask.Result is null ? null : _mapper.Map<RoomConditionDto>(conditionTask.Result),
            _mapper.Map<List<FeatureDto>>(featuresTask.Result),
            _mapper.Map<List<CustomFeatureDto>>(customFeaturesTask.Result)
        );
    }

    public async Task DeleteRoomAsync(int listingId, int roomId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await GetOwnedRoomAsync(listingId, roomId, cancellationToken);
        if (room.PhotoUrl is not null)
        {
            var key = ExtractKeyFromUrl(room.PhotoUrl);
            await _imageService.DeleteAsync(key);
        }

        await _roomRepo.DeleteAsync(roomId, cancellationToken);
    }

    public async Task<PhotoUploadResponse> UploadPhotoAsync(int listingId, int roomId, Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        if (room.PhotoUrl is not null)
        {
            var existingKey = ExtractKeyFromUrl(room.PhotoUrl);
            await _imageService.DeleteAsync(existingKey);
        }

        var key = $"rooms/{listingId}/{roomId}/{fileName}";
        var url = await _imageService.UploadAsync(fileStream, key, contentType);
        await _roomRepo.UpdatePhotoUrlAsync(roomId, url, cancellationToken);

        return new PhotoUploadResponse(url);
    }

    public async Task DeletePhotoAsync(int listingId, int roomId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        if (room.PhotoUrl is null) return;

        var key = ExtractKeyFromUrl(room.PhotoUrl);
        await _imageService.DeleteAsync(key);
        await _roomRepo.UpdatePhotoUrlAsync(roomId, null, cancellationToken);
    }

    private string ExtractKeyFromUrl(string photoUrl)
    {
        var prefix = _r2Options.Value.PublicUrl.TrimEnd('/') + "/";
        return photoUrl.StartsWith(prefix) ? photoUrl[prefix.Length..] : photoUrl;
    }

    public async Task<RoomConditionDto> UpsertConditionAsync(int listingId, int roomId, UpsertRoomConditionRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var condition = _mapper.Map<Condition>(request);
        condition.ListingRoomId = roomId;

        var result = await _roomRepo.UpsertConditionAsync(condition, cancellationToken);
        return _mapper.Map<RoomConditionDto>(result);
    }

    public async Task<IEnumerable<FeatureDto>> GetRoomFeaturesAsync(int listingId, int roomId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.GetLinkedFeaturesAsync(roomId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<IEnumerable<CustomFeatureDto>> GetRoomCustomFeaturesAsync(int listingId, int roomId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.GetCustomFeaturesAsync(roomId, cancellationToken);
        return _mapper.Map<List<CustomFeatureDto>>(features);
    }

    public async Task<IEnumerable<FeatureDto>> LinkFeatureAsync(int listingId, int roomId, int featureId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.LinkFeatureAsync(roomId, featureId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<IEnumerable<FeatureDto>> UnlinkFeatureAsync(int listingId, int roomId, int featureId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var features = await _roomRepo.UnlinkFeatureAsync(roomId, featureId, cancellationToken);
        return _mapper.Map<List<FeatureDto>>(features);
    }

    public async Task<CustomFeatureDto> AddCustomFeatureAsync(int listingId, int roomId, AddCustomFeatureRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        var feature = _mapper.Map<ListingRoomCustomFeature>(request);
        feature.ListingRoomId = roomId;

        var result = await _roomRepo.AddCustomFeatureAsync(feature, cancellationToken);
        return _mapper.Map<CustomFeatureDto>(result);
    }

    public async Task DeleteCustomFeatureAsync(int listingId, int roomId, int customFeatureId, CancellationToken cancellationToken = default)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId, cancellationToken);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await GetOwnedRoomAsync(listingId, roomId, cancellationToken);

        await _roomRepo.DeleteCustomFeatureAsync(customFeatureId, cancellationToken);
    }

    private async Task<ListingRoom> GetOwnedRoomAsync(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var room = await _roomRepo.GetByIdAsync(roomId, cancellationToken);
        if (room is null || room.ListingId != listingId)
            throw new KeyNotFoundException($"Room {roomId} not found under listing {listingId}");
        return room;
    }
}