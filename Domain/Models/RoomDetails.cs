namespace RealEstateApi.Domain.Models;

/// <summary>
/// All rooms of a listing plus their conditions, linked features and custom features,
/// fetched in a single round trip so DTOs can be assembled without an N+1 query pattern.
/// </summary>
public record RoomDetails(
    IReadOnlyList<ListingRoom> Rooms,
    IReadOnlyList<Condition> Conditions,
    IReadOnlyList<RoomLinkedFeature> Features,
    IReadOnlyList<ListingRoomCustomFeature> CustomFeatures
);
