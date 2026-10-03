namespace RealEstateApi.Application.DTOs;

public record UpsertAddressRequest(
    string? ErfNumber,
    string? EstateName,
    string? StreetNumber,
    string? UnitNumber,
    string? Street,
    string? Suburb,
    string? City,
    string? Province,
    string? Country,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    // Property24's name for the area (see ListingAddress.MarketingArea). Omitted: unchanged;
    // "" clears it.
    string? MarketingArea = null
);

public record ListingAddressDto(
    int ListingAddressId,
    int ListingId,
    string? ErfNumber,
    string? EstateName,
    string? StreetNumber,
    string? UnitNumber,
    string? Street,
    string? Suburb,
    string? City,
    string? Province,
    string? Country,
    string? PostalCode,
    decimal? Latitude,
    decimal? Longitude,
    // Property24's name for the area (see ListingAddress.MarketingArea). Omitted: unchanged;
    // "" clears it.
    string? MarketingArea = null
);
