using AutoMapper;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

internal static class RoomDtoBuilder
{
    public static async Task<List<RoomDto>> BuildAsync(
        ListingRoomRepository roomRepo,
        ListingRoomPhotoRepository roomPhotoRepo,
        IMapper mapper,
        int listingId,
        CancellationToken cancellationToken = default)
    {
        var rooms = (await roomRepo.GetByListingIdAsync(listingId, cancellationToken)).ToList();
        if (rooms.Count == 0) return new List<RoomDto>();

        var conditionsTask = roomRepo.GetConditionsByListingIdAsync(listingId, cancellationToken);
        var featuresTask = roomRepo.GetLinkedFeaturesByListingIdAsync(listingId, cancellationToken);
        var customFeaturesTask = roomRepo.GetCustomFeaturesByListingIdAsync(listingId, cancellationToken);
        var photosTask = roomPhotoRepo.GetByListingIdAsync(listingId, cancellationToken);

        await Task.WhenAll(conditionsTask, featuresTask, customFeaturesTask, photosTask);

        var conditionsByRoom = conditionsTask.Result
            .GroupBy(c => c.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.First());
        var featuresByRoom = featuresTask.Result;
        var customFeaturesByRoom = customFeaturesTask.Result
            .GroupBy(c => c.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Already ordered by room, SortOrder, Id; GroupBy keeps that order within each room.
        var photosByRoom = photosTask.Result
            .GroupBy(p => p.ListingRoomId)
            .ToDictionary(g => g.Key, g => g.Select(ToPhotoDto).ToList());

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
            mapper.Map<List<CustomFeatureDto>>(customFeaturesByRoom.GetValueOrDefault(room.Id) ?? new List<ListingRoomCustomFeature>()),
            photosByRoom.GetValueOrDefault(room.Id) ?? new List<RoomPhotoDto>()
        )).ToList();
    }

    public static RoomPhotoDto ToPhotoDto(ListingRoomPhoto photo) =>
        new(photo.Id, photo.Url, photo.SortOrder, photo.CreatedAt);
}