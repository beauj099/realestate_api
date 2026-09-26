namespace RealEstateApi.Application.DTOs;

// What the app sees of a property report (GET /api/property/{municipality}/{erf}). Built from
// public municipal data by PropertyData.CapeTown; see Infrastructure/PropertyData.

public record ResolvePropertyRequest(string? Address, double? Lat, double? Lng, string? Erf, string? Suburb);

/// <summary>
/// An address the agent can pick while typing. <see cref="Erf"/> and the location are set for a
/// numbered address (a real erf); a bare street (no number yet) has neither.
/// </summary>
public record AddressSuggestionDto(
    string Label,
    string? StreetNumber,
    string StreetName,
    string Suburb,
    string City,
    string Province,
    string Country,
    string? Erf,
    string? Sg26,
    double? Lat,
    double? Lng,
    string Municipality);

public record PropertyCandidateDto(string Municipality, string Erf, string? Sg26, string Suburb, string Township);

public record MoneyRangeDto(decimal? Low, decimal? Mid, decimal? High);

public record ComparableDto(
    string Address, string? Erf, double ErfExtentM2, double DwellingExtentM2,
    string SaleDate, decimal SalePriceZar, decimal? IndexedPriceZar,
    decimal? PricePerDwellingM2, bool Included, string? ExcludedBecause);

/// <summary>
/// A picture of the property and the licence rule that goes with it. Satellite may be printed
/// with its attribution next to it; Street View is screen-only. The app's PDF export filters on
/// <see cref="AllowedInPrint"/> rather than deciding for itself.
/// </summary>
public record ImageryRefDto(string Kind, string Url, string Attribution, bool AllowedInPrint);

public record BuildingDto(double RoofM2, double? HeightM, int? EstimatedStoreys, string? CapturedPeriod);

public record ApprovedWorkDto(string? ApprovalDate, string? Description, string? Category, double? AreaM2, decimal? ValueZar);

public record SuburbDto(string Name, int ResidentialCount, double MedianLandM2, double MedianBuildingM2,
    decimal Gv2022, decimal Gv2025, double GrowthPercent, double AnnualGrowthPercent);

public record ComparableSummaryDto(int Raw, int Included, int ExcludedZeroPrice, int ExcludedImplausible,
    int ExcludedTooOld, int ExcludedDissimilar, decimal? MedianPricePerDwellingM2, decimal? MedianPricePerErfM2);

public record ProvenanceDto(string Field, string Source, string FetchedAtUtc);

public record PropertyReportDto(
    string Municipality,
    string Erf,
    string? ValuationRef,
    string Address,
    string Suburb,
    string Township,
    double? Lat,
    double? Lng,

    double? ExtentM2,
    double? ExtentM2Geodesic,
    string? ZoningCode,
    string? ZoningDescription,
    string? Ward,
    string? SubCouncil,
    string? LegalStatus,

    double? DwellingExtentM2,
    double? TotalRoofM2,
    IReadOnlyList<BuildingDto> Buildings,
    IReadOnlyList<ApprovedWorkDto> ApprovedWork,

    decimal? MunicipalValueZar,
    string? MunicipalValueAsAt,
    string? RatingCategory,
    string? RollVersion,
    string? RollEffectiveFrom,

    SuburbDto? Suburbs,
    IReadOnlyList<ComparableDto> Comparables,
    ComparableSummaryDto? ComparableSummary,
    MoneyRangeDto? IndicativeValue,

    IReadOnlyList<ImageryRefDto> Imagery,
    string SitePlanUrl,
    IReadOnlyList<ProvenanceDto> Provenance,
    string GeneratedAtUtc);
