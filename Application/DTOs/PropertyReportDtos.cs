namespace RealEstateApi.Application.DTOs;

// What the app sees of a property report (GET /api/property/{municipality}/{erf}). Built from
// public municipal data by PropertyData.CapeTown; see Infrastructure/PropertyData.

public record ResolvePropertyRequest(string? Address, double? Lat, double? Lng, string? Erf, string? Suburb);

/// <summary>
/// An address the agent can pick while typing, from City records or OpenStreetMap.
/// <see cref="Kind"/>: "property" (a numbered erf from City records, with its erf and location),
/// "address" (a numbered house from OpenStreetMap, with its location), "street" (the number,
/// if one was typed, is the agent's and not on record) or "area" (a suburb or town).
/// <see cref="Title"/> and <see cref="Subtitle"/> are the two lines to show; <see cref="Key"/>
/// is the same for the same place from either source, so lists can be merged.
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
    string Municipality,
    string Kind = "street",
    string Title = "",
    string Subtitle = "",
    string? Unit = null,
    bool NumberVerified = false,
    string? PostalCode = null,
    string Source = "",
    double Rank = 0,
    string Key = "");

public record PropertyCandidateDto(string Municipality, string Erf, string? Sg26, string Suburb, string Township);

public record MoneyRangeDto(decimal? Low, decimal? Mid, decimal? High);

public record ComparableDto(
    string Address, string? Erf, double ErfExtentM2, double DwellingExtentM2,
    string SaleDate, decimal SalePriceZar, decimal? IndexedPriceZar,
    decimal? PricePerDwellingM2, bool Included, string? ExcludedBecause,
    double? DistanceM = null, double? Lat = null, double? Lng = null,
    // Listed for reference to show at least ten sales; not used for the range.
    bool Reference = false);

/// <summary>A registered sale of the subject property itself.</summary>
public record SaleRecordDto(string SaleDate, decimal PriceZar);

public record YearlySalesDto(int Year, int Sales, decimal MedianPriceZar);

/// <summary>A price band and the share of the area's sales in it, for the distribution chart.</summary>
public record PriceBandDto(decimal FromZar, decimal ToZar, int Sales, double Percent);

/// <summary>
/// The market around the property: every plausible sale in the area the comparables came from
/// (not only similar homes), by year and by price band.
/// </summary>
public record AreaMarketDto(int? RadiusM, int Sales, decimal? MedianPriceZar,
    IReadOnlyList<YearlySalesDto> ByYear, IReadOnlyList<PriceBandDto> PriceBands);

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
    int ExcludedTooOld, int ExcludedDissimilar, decimal? MedianPricePerDwellingM2, decimal? MedianPricePerErfM2,
    int? RadiusM = null, int ExcludedMultiProperty = 0, int ExcludedNoBuilding = 0, int ExcludedTooFar = 0);

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
    string DataSource,
    string? ComparablesMethod,
    string? CoverageNote,
    IReadOnlyList<ProvenanceDto> Provenance,
    string GeneratedAtUtc,
    // Sales agents reported for the suburb: read per request (never cached with the record).
    AgentComparablesSummaryDto? AgentComparables = null,
    SaleRecordDto? LastSale = null,
    // The latest sales in the subject's own street, newest first.
    IReadOnlyList<ComparableDto>? StreetSales = null,
    AreaMarketDto? AreaMarket = null,
    // The neighbourhood map (SVG); add "&mode=block" for the close-up. Null where not drawn.
    string? AreaMapUrl = null);
