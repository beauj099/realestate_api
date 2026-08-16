using AutoMapper;
using Microsoft.Extensions.Options;
using RealEstateApi.Application.Interfaces;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class ListingRoomService : IListingRoomService
{
    private readonly IListingRepository _listingRepo;
    private readonly IListingRoomRepository _roomRepo;
    private readonly IRoomAssembler _roomAssembler;
    private readonly IImageService _imageService;
    private readonly IOptions<R2Options> _r2Options;
    private readonly IMapper _mapper;

    public ListingRoomService(
        IListingRepository listingRepo,
        IListingRoomRepository roomRepo,
        IRoomAssembler roomAssembler,
        IImageService imageService,
        IOptions<R2Options> r2Options,
        IMapper mapper)
    {
        _listingRepo = listingRepo;
        _roomRepo = roomRepo;
        _roomAssembler = roomAssembler;
        _imageService = imageService;
        _r2Options = r2Options;
        _mapper = mapper;
    }

    public async Task<IEnumerable<RoomDto>> GetRoomsAsync(int listingId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        return await _roomAssembler.GetRoomDtosAsync(listingId);
    }

    public async Task<RoomDto> CreateRoomAsync(int listingId, CreateRoomRequest request)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = _mapper.Map<ListingRoom>(request);
        room.ListingId = listingId;

        var created = await _roomRepo.CreateAsync(room);
        return new RoomDto(
            created.Id, created.ListingId, created.Name, created.RoomTypeId,
            created.RoomTypeOther, created.PhotoUrl, created.CreatedAt, created.UpdatedAt,
            null, new List<FeatureDto>(), new List<CustomFeatureDto>()
        );
    }

    public async Task<RoomDto?> UpdateRoomAsync(int listingId, int roomId, UpdateRoomRequest request)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = _mapper.Map<ListingRoom>(request);
        room.Id = roomId;

        var updated = await _roomRepo.UpdateAsync(room);
        if (updated == null) return null;

        var condition = await _roomRepo.GetConditionByRoomIdAsync(updated.Id);
        var features = await _roomRepo.GetLinkedFeaturesAsync(updated.Id);
        var customFeatures = await _roomRepo.GetCustomFeaturesAsync(updated.Id);

        return new RoomDto(
            updated.Id, updated.ListingId, updated.Name, updated.RoomTypeId,
            updated.RoomTypeOther, updated.PhotoUrl, updated.CreatedAt, updated.UpdatedAt,
            condition is null ? null : _mapper.Map<RoomConditionDto>(condition),
            features.Select(f => _mapper.Map<FeatureDto>(f)).ToList(),
            customFeatures.Select(cf => _mapper.Map<CustomFeatureDto>(cf)).ToList()
        );
    }

    public async Task DeleteRoomAsync(int listingId, int roomId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await _roomRepo.GetByIdAsync(roomId);
        if (room?.PhotoUrl is not null)
        {
            var key = ExtractKeyFromUrl(room.PhotoUrl);
            await _imageService.DeleteAsync(key);
        }

        await _roomRepo.DeleteAsync(roomId);
    }

    public async Task<PhotoUploadResponse> UploadPhotoAsync(int listingId, int roomId, Stream fileStream, string fileName, string contentType)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await _roomRepo.GetByIdAsync(roomId);
        if (room == null) throw new KeyNotFoundException($"Room {roomId} not found under listing {listingId}");

        if (room.PhotoUrl is not null)
        {
            var existingKey = ExtractKeyFromUrl(room.PhotoUrl);
            await _imageService.DeleteAsync(existingKey);
        }

        var key = $"rooms/{listingId}/{roomId}/{fileName}";
        var url = await _imageService.UploadAsync(fileStream, key, contentType);
        await _roomRepo.UpdatePhotoUrlAsync(roomId, url);

        return new PhotoUploadResponse(url);
    }

    public async Task DeletePhotoAsync(int listingId, int roomId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var room = await _roomRepo.GetByIdAsync(roomId);
        if (room == null) throw new KeyNotFoundException($"Room {roomId} not found under listing {listingId}");

        if (room.PhotoUrl is null) return;

        var key = ExtractKeyFromUrl(room.PhotoUrl);
        await _imageService.DeleteAsync(key);
        await _roomRepo.UpdatePhotoUrlAsync(roomId, null);
    }

    private string ExtractKeyFromUrl(string photoUrl)
    {
        var prefix = _r2Options.Value.PublicUrl.TrimEnd('/') + "/";
        return photoUrl.StartsWith(prefix) ? photoUrl[prefix.Length..] : photoUrl;
    }

    public async Task<RoomConditionDto> UpsertConditionAsync(int listingId, int roomId, UpsertRoomConditionRequest request)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var condition = _mapper.Map<Condition>(request);
        condition.ListingRoomId = roomId;

        var result = await _roomRepo.UpsertConditionAsync(condition);
        return _mapper.Map<RoomConditionDto>(result);
    }

    public async Task<IEnumerable<FeatureDto>> LinkFeatureAsync(int listingId, int roomId, int featureId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var features = await _roomRepo.LinkFeatureAsync(roomId, featureId);
        return _mapper.Map<IEnumerable<FeatureDto>>(features);
    }

    public async Task<IEnumerable<FeatureDto>> UnlinkFeatureAsync(int listingId, int roomId, int featureId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var features = await _roomRepo.UnlinkFeatureAsync(roomId, featureId);
        return _mapper.Map<IEnumerable<FeatureDto>>(features);
    }

    public async Task<CustomFeatureDto> AddCustomFeatureAsync(int listingId, int roomId, AddCustomFeatureRequest request)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        var feature = _mapper.Map<ListingRoomCustomFeature>(request);
        feature.ListingRoomId = roomId;

        var result = await _roomRepo.AddCustomFeatureAsync(feature);
        return _mapper.Map<CustomFeatureDto>(result);
    }

    public async Task DeleteCustomFeatureAsync(int listingId, int roomId, int customFeatureId)
    {
        var listing = await _listingRepo.GetByIdAsync(listingId);
        if (listing == null) throw new KeyNotFoundException($"Listing {listingId} not found");

        await _roomRepo.DeleteCustomFeatureAsync(customFeatureId);
    }
}
