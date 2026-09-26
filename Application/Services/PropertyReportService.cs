using Microsoft.Extensions.Caching.Memory;
using PropertyData.CapeTown.Services;
using PropertyData.Core;
using PropertyData.Core.Models;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Infrastructure.PropertyData;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Property reports from public municipal data: resolve an address to an erf, then assemble the
/// record (site, buildings, municipal value, suburb trend, filtered comparable sales).
///
/// One report is one fetch: records are cached in memory for 12 hours, so the site plan, the
/// imagery and a second look at the same property never go back to the City. Only Cape Town is
/// wired so far; other metros are further IPropertyDataProvider implementations.
/// </summary>
public class PropertyReportService(
    IEnumerable<IPropertyDataProvider> providers,
    PropertyData.CapeTown.Clients.CapeTownSpatialClient spatial,
    IMemoryCache cache,
    ImageryLinkBuilder imagery)
{
    private static readonly TimeSpan RecordTtl = TimeSpan.FromHours(12);

    public const string CapeTown = "coct";

    public async Task<IReadOnlyList<PropertyCandidateDto>> ResolveAsync(ResolvePropertyRequest request, CancellationToken ct)
    {
        var provider = ProviderFor(CapeTown);
        var refs = await provider.ResolveAsync(
            new ResolveQuery(request.Address, request.Lat, request.Lng, request.Erf, request.Suburb), ct);
        return refs.Select(r => new PropertyCandidateDto(r.Municipality, r.Erf, r.Sg26, r.Suburb, r.Township)).ToList();
    }

    /// <summary>Address type-ahead from the City's parcel records. Cached an hour per query.</summary>
    public async Task<IReadOnlyList<AddressSuggestionDto>> SuggestAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 3) return [];
        var key = $"suggest:{q.ToUpperInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<AddressSuggestionDto>? hit) && hit is not null) return hit;

        var found = await spatial.SuggestAsync(q, 8, ct);
        IReadOnlyList<AddressSuggestionDto> result = found.Select(s =>
        {
            var street = TitleCase(string.Join(' ', new[] { s.StreetName, s.StreetType }.Where(p => !string.IsNullOrWhiteSpace(p))));
            var number = s.StreetNumber is null ? null : $"{s.StreetNumber}{s.StreetNumberSuffix}";
            var suburb = TitleCase(s.Suburb);
            return new AddressSuggestionDto(
                Label: $"{(number is null ? "" : number + " ")}{street}, {suburb}",
                StreetNumber: number, StreetName: street, Suburb: suburb,
                City: "Cape Town", Province: "Western Cape", Country: "South Africa",
                Erf: s.Erf, Sg26: s.Sg26, Lat: s.Location?.Lat, Lng: s.Location?.Lng,
                Municipality: CapeTown);
        }).ToList();

        cache.Set(key, result, TimeSpan.FromHours(1));
        return result;
    }

    private static string TitleCase(string s) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public async Task<PropertyReportDto> GetReportAsync(string municipality, string erf, string? suburb,
        bool includeComparables, CancellationToken ct)
    {
        var record = await GetRecordAsync(municipality, erf, suburb, includeComparables
            ? new RecordOptions()
            : new RecordOptions(IncludeComparables: false), includeComparables ? "full" : "nocomps", ct);
        return Map(record);
    }

    public async Task<string> GetSitePlanSvgAsync(string municipality, string erf, string? suburb,
        int width, int height, CancellationToken ct)
    {
        var record = Cached(municipality, erf, suburb)
            ?? await GetRecordAsync(municipality, erf, suburb,
                new RecordOptions(IncludeComparables: false, IncludeApprovedWork: false, IncludeDwellingExtent: false),
                "plan", ct);
        return SitePlanRenderer.Render(record, new SitePlanOptions(
            WidthPx: width > 0 ? width : 900,
            HeightPx: height > 0 ? height : 650));
    }

    /// <summary>The property's centre, for imagery. Null when the cadastre has no boundary.</summary>
    public async Task<LatLng?> GetLocationAsync(string municipality, string erf, string? suburb, CancellationToken ct)
    {
        var record = Cached(municipality, erf, suburb)
            ?? await GetRecordAsync(municipality, erf, suburb,
                new RecordOptions(IncludeComparables: false, IncludeBuildings: false,
                    IncludeApprovedWork: false, IncludeDwellingExtent: false),
                "location", ct);
        return record.Location;
    }

    private IPropertyDataProvider ProviderFor(string municipality) =>
        providers.FirstOrDefault(p => p.Handles(municipality))
        ?? throw new KeyNotFoundException($"No property data source for '{municipality}' yet — only Cape Town (coct).");

    private static string Key(string municipality, string erf, string? suburb, string level) =>
        $"prop:{municipality.ToLowerInvariant()}:{erf}:{(suburb ?? "").ToUpperInvariant()}:{level}";

    /// <summary>Any cached record for the property, fullest first.</summary>
    private PropertyRecord? Cached(string municipality, string erf, string? suburb)
    {
        foreach (var level in new[] { "full", "nocomps", "plan", "location" })
        {
            if (cache.TryGetValue(Key(municipality, erf, suburb, level), out PropertyRecord? record) && record is not null)
                return record;
        }
        return null;
    }

    private async Task<PropertyRecord> GetRecordAsync(string municipality, string erf, string? suburb,
        RecordOptions options, string level, CancellationToken ct)
    {
        var provider = ProviderFor(municipality);
        var record = await cache.GetOrCreateAsync(Key(municipality, erf, suburb, level), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RecordTtl;
            return await provider.FetchRecordAsync(
                new PropertyRef(erf, null, null, "CAPE TOWN", (suburb ?? "").ToUpperInvariant(), municipality),
                options, ct);
        });
        return record!;
    }

    private PropertyReportDto Map(PropertyRecord r) => new(
        Municipality: r.Ref.Municipality,
        Erf: r.Ref.Erf,
        ValuationRef: r.Ref.ValuationRef,
        Address: r.FormattedAddress,
        Suburb: r.Ref.Suburb,
        Township: r.Ref.Township,
        Lat: r.Location?.Lat,
        Lng: r.Location?.Lng,

        ExtentM2: r.BestExtentM2,
        ExtentM2Geodesic: r.ExtentM2Geodesic,
        ZoningCode: r.ZoningCode,
        ZoningDescription: r.ZoningDescription,
        Ward: r.Ward,
        SubCouncil: r.SubCouncil,
        LegalStatus: r.LegalStatus,

        DwellingExtentM2: r.DwellingExtentM2,
        TotalRoofM2: r.TotalRoofM2 is null ? null : Math.Round(r.TotalRoofM2.Value),
        Buildings: r.Buildings.Select(b => new BuildingDto(
            b.RoofM2, b.HeightM is null ? null : Math.Round(b.HeightM.Value, 1), b.EstimatedStoreys,
            b.CapturedYyyyMm is { } c ? $"{c / 100}-{c % 100:00}" : null)).ToList(),
        ApprovedWork: r.ApprovedWork.Select(w => new ApprovedWorkDto(
            (w.ApprovalDate ?? w.SubmissionDate)?.ToString("yyyy-MM-dd"), w.Description, w.PrimaryCategory,
            w.AreaM2, w.ValueZar)).ToList(),

        MunicipalValueZar: r.Valuation?.ValueZar,
        MunicipalValueAsAt: r.Valuation?.AsAt.ToString("yyyy-MM-dd"),
        RatingCategory: r.Valuation?.Category,
        RollVersion: r.Valuation?.RollVersion,
        RollEffectiveFrom: r.Valuation?.EffectiveFrom?.ToString("yyyy-MM-dd"),

        Suburbs: r.Suburb is null ? null : new SuburbDto(
            r.Suburb.Suburb, r.Suburb.ResidentialCount, r.Suburb.MedianLandM2, r.Suburb.MedianBuildingM2,
            r.Suburb.Gv2022Zar, r.Suburb.Gv2025Zar,
            Math.Round(r.Suburb.GrowthPercent, 1), Math.Round(r.Suburb.AnnualGrowthPercent, 2)),

        // The included sales plus the near misses (marked), newest first: shows the filtering
        // was done, which is what lets an agent defend the range.
        Comparables: r.Comparables?.All
            .Where(c => c.Included || c.Exclusion == ComparableExclusion.DissimilarSize)
            .OrderByDescending(c => c.Included)
            .ThenByDescending(c => c.SaleDate)
            .Take(40)
            .Select(c => new ComparableDto(
                c.Address, c.Erf, c.ErfExtentM2, c.DwellingExtentM2,
                c.SaleDate.ToString("yyyy-MM-dd"), c.SalePriceZar, c.IndexedPriceZar,
                c.PricePerDwellingM2 is null ? null : Math.Round(c.PricePerDwellingM2.Value),
                c.Included, c.Included ? null : Describe(c.Exclusion)))
            .ToList() ?? [],

        ComparableSummary: r.Comparables is null ? null : new ComparableSummaryDto(
            r.Comparables.All.Count, r.Comparables.Included.Count,
            r.Comparables.ExcludedZeroPrice, r.Comparables.ExcludedImplausible,
            r.Comparables.ExcludedTooOld, r.Comparables.ExcludedDissimilar,
            r.Comparables.MedianPricePerDwellingM2, r.Comparables.MedianPricePerErfM2),

        IndicativeValue: r.Comparables?.ImpliedValueMidZar is null ? null : new MoneyRangeDto(
            r.Comparables.ImpliedValueLowZar, r.Comparables.ImpliedValueMidZar, r.Comparables.ImpliedValueHighZar),

        Imagery: imagery.For(r.Ref.Erf, r.Ref.Municipality, r.Ref.Suburb, r.Location?.Lat, r.Location?.Lng),
        SitePlanUrl: $"/api/property/{r.Ref.Municipality}/{Uri.EscapeDataString(r.Ref.Erf)}/site-plan.svg" +
                     $"?suburb={Uri.EscapeDataString(r.Ref.Suburb)}",
        Provenance: r.Provenance.Select(p => new ProvenanceDto(p.Field, p.Source, p.FetchedAt.ToString("O"))).ToList(),
        GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"));

    private static string Describe(ComparableExclusion e) => e switch
    {
        ComparableExclusion.ZeroPrice => "Transferred for R0 (not a market sale)",
        ComparableExclusion.ImplausiblePrice => "Price too low to be a market sale",
        ComparableExclusion.DissimilarSize => "Size too different from this property",
        ComparableExclusion.TooOld => "Sold too long ago",
        ComparableExclusion.IsSubject => "This property",
        _ => e.ToString(),
    };
}
