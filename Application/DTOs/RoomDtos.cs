namespace RealEstateApi.Application.DTOs;

public record CreateRoomRequest(
    string Name,
    int RoomTypeId,
    string? RoomTypeOther,
    string? PhotoUrl
);

public record UpdateRoomRequest(
    string? Name,
    int? RoomTypeId,
    string? RoomTypeOther,
    string? PhotoUrl
);

public record RoomDto(
    int Id,
    int ListingId,
    string Name,
    int RoomTypeId,
    string? RoomTypeOther,
    string? PhotoUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    RoomConditionDto? Condition,
    List<FeatureDto> Features,
    List<CustomFeatureDto> CustomFeatures,
    // All of the room's photos, ordered by SortOrder then Id. PhotoUrl above is the
    // cover (the first of these) and is kept for older app builds.
    List<RoomPhotoDto> Photos
);

public record RoomPhotoDto(
    int Id,
    string Url,
    int SortOrder,
    DateTime CreatedAt
);

public record UpsertRoomConditionRequest(
    decimal? ConditionRating,
    string? Notes,
    int ConditionCategoryId,
    // Agent's overall 0-10 score for the room; averaged into the house score.
    decimal? Score = null
);

public record RoomConditionDto(
    int Id,
    int ListingRoomId,
    decimal? ConditionRating,
    string? Notes,
    int ConditionCategoryId,
    decimal? Score
);

public record LinkFeatureRequest(int FeatureId);

public record AddCustomFeatureRequest(string Description);

public record CustomFeatureDto(int Id, int ListingRoomId, string Description);
