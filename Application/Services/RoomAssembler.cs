using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Interfaces;

namespace RealEstateApi.Application.Services;

public class RoomAssembler : IRoomAssembler
{
    private readonly IListingRoomRepository _roomRepo;
    private readonly IMapper _mapper;

    public RoomAssembler(IListingRoomRepository roomRepo, IMapper mapper)
    {
        _roomRepo = roomRepo;
        _mapper = mapper;
    }

    public async Task<List<RoomDto>> GetRoomDtosAsync(int listingId)
    {
        var details = await _roomRepo.GetRoomDetailsByListingIdAsync(listingId);

        var conditionByRoom = details.Conditions
            .GroupBy(c => c.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.First());
        var featuresByRoom = details.Features.ToLookup(f => f.ListingRoomId);
        var customFeaturesByRoom = details.CustomFeatures.ToLookup(cf => cf.ListingRoomId);

        return details.Rooms.Select(room => new RoomDto(
            room.Id, room.ListingId, room.Name, room.RoomTypeId,
            room.RoomTypeOther, room.PhotoUrl, room.CreatedAt, room.UpdatedAt,
            conditionByRoom.TryGetValue(room.Id, out var condition)
                ? _mapper.Map<RoomConditionDto>(condition)
                : null,
            featuresByRoom[room.Id].Select(f => _mapper.Map<FeatureDto>(f)).ToList(),
            customFeaturesByRoom[room.Id].Select(cf => _mapper.Map<CustomFeatureDto>(cf)).ToList()
        )).ToList();
    }
}
