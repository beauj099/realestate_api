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
    List<CustomFeatureDto> CustomFeatures
);

public record UpsertRoomConditionRequest(
    decimal? ConditionRating,
    string? Notes,
    int ConditionCategoryId
);

public record RoomConditionDto(
    int Id,
    int ListingRoomId,
    decimal? ConditionRating,
    string? Notes,
    int ConditionCategoryId
);

public record LinkFeatureRequest(int FeatureId);

public record AddCustomFeatureRequest(string Description);

public record CustomFeatureDto(int Id, int ListingRoomId, string Description);
