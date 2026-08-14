using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

internal static class RoomDtoBuilder
{
    public static async Task<List<RoomDto>> BuildAsync(
        ListingRoomRepository roomRepo,
        IMapper mapper,
        int listingId,
        CancellationToken cancellationToken = default)
    {
        var rooms = (await roomRepo.GetByListingIdAsync(listingId, cancellationToken)).ToList();
        if (rooms.Count == 0) return new List<RoomDto>();

        var conditionsTask = roomRepo.GetConditionsByListingIdAsync(listingId, cancellationToken);
        var featuresTask = roomRepo.GetLinkedFeaturesByListingIdAsync(listingId, cancellationToken);
        var customFeaturesTask = roomRepo.GetCustomFeaturesByListingIdAsync(listingId, cancellationToken);

        await Task.WhenAll(conditionsTask, featuresTask, customFeaturesTask);

        var conditionsByRoom = conditionsTask.Result
            .GroupBy(c => c.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.First());
        var featuresByRoom = featuresTask.Result;
        var customFeaturesByRoom = customFeaturesTask.Result
            .GroupBy(c => c.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return rooms.Select(room => new RoomDto(
            room.Id,
            room.ListingId,
            room.Name,
            room.RoomTypeId,
            room.RoomTypeOther,
            room.PhotoUrl,
            room.CreatedAt,
            room.UpdatedAt,
            conditionsByRoom.TryGetValue(room.Id, out var condition)
                ? mapper.Map<RoomConditionDto>(condition)
                : null,
            mapper.Map<List<FeatureDto>>(featuresByRoom.GetValueOrDefault(room.Id) ?? new List<Feature>()),
            mapper.Map<List<CustomFeatureDto>>(customFeaturesByRoom.GetValueOrDefault(room.Id) ?? new List<ListingRoomCustomFeature>())
        )).ToList();
    }
}