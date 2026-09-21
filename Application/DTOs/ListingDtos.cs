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
    int RoomCount = 0
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
    List<OutdoorFeatureDto> OutdoorFeatures
);

public record ListingFilterRequest(string? Status, DateTime? DateFrom, DateTime? DateTo);
