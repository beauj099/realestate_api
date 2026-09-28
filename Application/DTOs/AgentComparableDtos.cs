namespace RealEstateApi.Application.DTOs;

/// <summary>A sale the agent knows about. Municipality and suburb are the report's own.</summary>
public record CreateAgentComparableRequest(
    string Municipality,
    string Suburb,
    string Address,
    string? Erf,
    double? Lat,
    double? Lng,
    decimal? ErfM2,
    decimal? FloorM2,
    int? Bedrooms,
    int? Bathrooms,
    int? Garages,
    int? PropertyTypeId,
    string? Condition,
    DateOnly SaleDate,
    decimal SalePriceZar,
    string EvidenceLevel,
    string? Notes);

/// <summary>Saved; <see cref="WasDuplicate"/> when it corroborated another agent's entry instead.</summary>
public record AgentComparableSavedDto(Guid Id, bool WasDuplicate);

/// <summary>
/// A captured sale as reports and the capturing agent see it. Never carries who captured it
/// (<see cref="IsMine"/> is only true for the requesting agent).
/// </summary>
public record AgentComparableDto(
    Guid Id,
    string Address,
    string Suburb,
    string? Erf,
    decimal? ErfM2,
    decimal? FloorM2,
    int? Bedrooms,
    string? Condition,
    string SaleDate,
    decimal SalePriceZar,
    decimal? PricePerFloorM2,
    string EvidenceLevel,
    string Evidence,
    string Verification,
    int CorroborationCount,
    decimal Weight,
    bool IsMine);

/// <summary>Agent-captured sales in a report's suburb, and what the report can say about them.</summary>
public record AgentComparablesSummaryDto(
    IReadOnlyList<AgentComparableDto> Sales,
    string EvidenceStatement,
    decimal? WeightedMedianPerFloorM2,
    decimal? WeightedMedianPerErfM2)
{
    /// <summary>A range from these sales alone ("floor" or "erf" basis), when there are enough of them.</summary>
    public MoneyRangeDto? IndicativeValue { get; init; }
    public string? IndicativeBasis { get; init; }
}

/// <summary>
/// One of the agency's own listings in the same suburb, for "on the market nearby". Never
/// includes owners, and only agents of the same agency see each other's listings.
/// </summary>
public record MarketListingDto(
    int ListingId,
    string Address,
    string? Suburb,
    int? PropertyTypeId,
    decimal? AgentValuationZar,
    decimal? ErfM2,
    decimal? FloorM2,
    decimal? ValuationPerFloorM2,
    string Status,
    string? ListedOn,
    int DaysListed,
    bool DaysListedFromCapture,
    bool IsArchived);
