namespace RealEstateApi.Application.DTOs;

public record CreateListingRequest(int? PropertyTypeId, string? P24Ref);
public record UpdateListingRequest(string? Status, string? P24Ref, int? PropertyTypeId);

public record ListingSummaryDto(
    int Id,
    string ReferenceNumber,
    string? P24Ref,
    int? PropertyTypeId,
    int? ListingValuationId,
    DateTime? ListDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? StreetNumber = null,
    string? Street = null,
    string? Suburb = null,
    string? City = null,
    string? PrimaryOwnerName = null,
    string? PrimaryPhotoUrl = null,
    int RoomCount = 0,
    // Stored Listings.HouseScore: a percentage (0-100) set by the app/agent; null until scored.
    decimal? HouseScore = null,
    bool HouseScoreIsManual = false,
    // Set when the agent archived the listing; null while active.
    DateTime? ArchivedAt = null,
    // Every contact's display name (FullName, else CompanyName) ordered by contact Id,
    // joined with '|'. Null when the listing has no contacts.
    string? OwnerNames = null
);

public record ListingResponse(
    int Id,
    string ReferenceNumber,
    string? P24Ref,
    int? PropertyTypeId,
    int? ListingValuationId,
    DateTime? ListDate,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    ListingAddressDto? Address,
    BuildingInfoDto? BuildingInfo,
    ValuationDto? Valuation,
    RunningCostsDto? RunningCosts,
    List<RoomDto> Rooms,
    List<ParkingDto> Parking,
    List<ContactDto> Contacts,
    List<OutdoorFeatureDto> OutdoorFeatures,
    decimal? HouseScore = null,
    bool HouseScoreIsManual = false,
    DateTime? ArchivedAt = null
);

/// <summary>PUT /api/listings/{id}/house-score. Score is a percentage (0-100); null clears it.</summary>
public record UpdateHouseScoreRequest(decimal? Score, bool IsManual);

/// <summary>PUT /api/listings/{id}/archive. True archives (keeping an existing ArchivedAt), false restores.</summary>
public record ArchiveListingRequest(bool Archived);

public record ListingFilterRequest(string? Status, DateTime? DateFrom, DateTime? DateTo);
