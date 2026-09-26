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
/// imagery and a second look at the same property never go back to the City.
///
/// Coverage: Cape Town (everything), Johannesburg (values and current sales, no building sizes),
/// and the national cadastre anywhere else (erf identity and size only, from a GPS pin).
/// </summary>
public class PropertyReportService(
    IEnumerable<IPropertyDataProvider> providers,
    PropertyData.CapeTown.Clients.CapeTownSpatialClient capeTown,
    PropertyData.Johannesburg.JohannesburgPropertyProvider johannesburg,
    IMemoryCache cache,
    ImageryLinkBuilder imagery,
    ILogger<PropertyReportService> log)
{
    private static readonly TimeSpan RecordTtl = TimeSpan.FromHours(12);

    public const string CapeTown = "coct";
    public const string Johannesburg = PropertyData.Johannesburg.JohannesburgPropertyProvider.Municipality;
    public const string National = PropertyData.National.NationalCadastreProvider.Municipality;

    /// <summary>
    /// A GPS pin goes to each city in turn, then to the national cadastre. An address or erf has
    /// no city attached, so Cape Town and Johannesburg are both asked, at once.
    /// </summary>
    public async Task<IReadOnlyList<PropertyCandidateDto>> ResolveAsync(ResolvePropertyRequest request, CancellationToken ct)
    {
        var query = new ResolveQuery(request.Address, request.Lat, request.Lng, request.Erf, request.Suburb);
        IReadOnlyList<PropertyRef> refs = [];

        if (request.Lat is not null && request.Lng is not null)
        {
            foreach (var municipality in new[] { CapeTown, Johannesburg, National })
            {
                refs = await TryResolve(ProviderFor(municipality), query, ct);
                if (refs.Count > 0) break;
            }
        }
        else
        {
            var found = await Task.WhenAll(
                TryResolve(ProviderFor(CapeTown), query, ct),
                TryResolve(ProviderFor(Johannesburg), query, ct));
            refs = found.SelectMany(r => r).ToList();

            // A city that has no match in the typed suburb falls back to the street anywhere in
            // it ("10 Thirteenth Street, Parkhurst" → Bishop Lavis, Cape Town). When some city
            // does have the suburb, only its matches are the answer.
            var suburb = TypedSuburb(request);
            if (suburb is not null)
            {
                var inSuburb = refs.Where(r => r.Suburb.StartsWith(suburb, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inSuburb.Count > 0) refs = inSuburb;
            }
        }

        return refs.Select(r => new PropertyCandidateDto(r.Municipality, r.Erf, r.Sg26, r.Suburb, r.Township)).ToList();
    }

    private static string? TypedSuburb(ResolvePropertyRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Suburb)) return request.Suburb.Trim();
        if (string.IsNullOrWhiteSpace(request.Address)) return null;
        try
        {
            return global::PropertyData.CapeTown.Internal.AddressNormalizer.Parse(request.Address).Suburb;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>One source being down must not hide the others' answers.</summary>
    private async Task<IReadOnlyList<PropertyRef>> TryResolve(IPropertyDataProvider provider, ResolveQuery query, CancellationToken ct)
    {
        try
        {
            return await provider.ResolveAsync(query, ct);
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "{Provider} did not answer a resolve", provider.Name);
            return [];
        }
    }

    /// <summary>Address type-ahead from the City's parcel records. Cached an hour per query.</summary>
    public async Task<IReadOnlyList<AddressSuggestionDto>> SuggestAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 3) return [];
        var key = $"suggest:{q.ToUpperInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<AddressSuggestionDto>? hit) && hit is not null) return hit;

        // Both cities at once; a city that is down just contributes nothing.
        async Task<List<PropertyData.CapeTown.Clients.AddressSuggestion>> Safe(
            Func<Task<List<PropertyData.CapeTown.Clients.AddressSuggestion>>> call)
        {
            try { return await call(); }
            catch (HttpRequestException ex) { log.LogWarning(ex, "Suggestion source did not answer"); return []; }
        }
        var ctTask = Safe(() => capeTown.SuggestAsync(q, 8, ct));
        var jhbTask = Safe(() => johannesburg.SuggestAsync(q, 8, ct));
        await Task.WhenAll(ctTask, jhbTask);

        AddressSuggestionDto Dto(PropertyData.CapeTown.Clients.AddressSuggestion s, string municipality, string city, string province)
        {
            var street = TitleCase(string.Join(' ', new[] { s.StreetName, s.StreetType }.Where(p => !string.IsNullOrWhiteSpace(p))));
            var number = s.StreetNumber is null ? null : $"{s.StreetNumber}{s.StreetNumberSuffix}";
            var suburb = TitleCase(s.Suburb);
            return new AddressSuggestionDto(
                Label: $"{(number is null ? "" : number + " ")}{street}, {suburb}",
                StreetNumber: number, StreetName: street, Suburb: suburb,
                City: city, Province: province, Country: "South Africa",
                Erf: s.Erf, Sg26: s.Sg26, Lat: s.Location?.Lat, Lng: s.Location?.Lng,
                Municipality: municipality);
        }

        IReadOnlyList<AddressSuggestionDto> result = ctTask.Result.Select(s => Dto(s, CapeTown, "Cape Town", "Western Cape"))
            .Concat(jhbTask.Result.Select(s => Dto(s, Johannesburg, "Johannesburg", "Gauteng")))
            .Take(10)
            .ToList();

        cache.Set(key, result, TimeSpan.FromHours(1));
        return result;
    }

    private static string TitleCase(string s) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public async Task<PropertyReportDto> GetReportAsync(string municipality, string erf, string? suburb,
        string? sg26, bool includeComparables, CancellationToken ct)
    {
        var record = await GetRecordAsync(municipality, erf, suburb, sg26, includeComparables
            ? new RecordOptions()
            : new RecordOptions(IncludeComparables: false), includeComparables ? "full" : "nocomps", ct);
        return Map(record);
    }

    public async Task<string> GetSitePlanSvgAsync(string municipality, string erf, string? suburb, string? sg26,
        int width, int height, CancellationToken ct)
    {
        var record = Cached(municipality, erf, suburb)
            ?? await GetRecordAsync(municipality, erf, suburb, sg26,
                new RecordOptions(IncludeComparables: false, IncludeApprovedWork: false, IncludeDwellingExtent: false),
                "plan", ct);
        return SitePlanRenderer.Render(record, new SitePlanOptions(
            WidthPx: width > 0 ? width : 900,
            HeightPx: height > 0 ? height : 650));
    }

    /// <summary>The property's centre, for imagery. Null when the cadastre has no boundary.</summary>
    public async Task<LatLng?> GetLocationAsync(string municipality, string erf, string? suburb, string? sg26, CancellationToken ct)
    {
        var record = Cached(municipality, erf, suburb)
            ?? await GetRecordAsync(municipality, erf, suburb, sg26,
                new RecordOptions(IncludeComparables: false, IncludeBuildings: false,
                    IncludeApprovedWork: false, IncludeDwellingExtent: false),
                "location", ct);
        return record.Location;
    }

    private IPropertyDataProvider ProviderFor(string municipality) =>
        providers.FirstOrDefault(p => p.Handles(municipality))
        ?? throw new KeyNotFoundException($"No property data source for '{municipality}'.");

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

    private async Task<PropertyRecord> GetRecordAsync(string municipality, string erf, string? suburb, string? sg26,
        RecordOptions options, string level, CancellationToken ct)
    {
        var provider = ProviderFor(municipality);
        var record = await cache.GetOrCreateAsync(Key(municipality, erf, suburb, level), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RecordTtl;
            return await provider.FetchRecordAsync(
                new PropertyRef(erf, string.IsNullOrWhiteSpace(sg26) ? null : sg26, null, "",
                    (suburb ?? "").ToUpperInvariant(), municipality),
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

        Imagery: imagery.For(r.Ref.Erf, r.Ref.Municipality, r.Ref.Suburb, r.Ref.Sg26, r.Location?.Lat, r.Location?.Lng),
        SitePlanUrl: $"/api/property/{r.Ref.Municipality}/{Uri.EscapeDataString(r.Ref.Erf)}/site-plan.svg" +
                     $"?suburb={Uri.EscapeDataString(r.Ref.Suburb)}" +
                     (r.Ref.Sg26 is null ? "" : $"&sg26={Uri.EscapeDataString(r.Ref.Sg26)}"),
        DataSource: r.DataSource,
        ComparablesMethod: MethodFor(r),
        CoverageNote: CoverageFor(r),
        Provenance: r.Provenance.Select(p => new ProvenanceDto(p.Field, p.Source, p.FetchedAt.ToString("O"))).ToList(),
        GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"));

    /// <summary>How the comparables were chosen, in a sentence the report prints as is.</summary>
    private static string? MethodFor(PropertyRecord r) => r.Comparables is null ? null : r.Ref.Municipality switch
    {
        Johannesburg =>
            $"The last registered sale of every stand of the same category within {PropertyData.Johannesburg.JohannesburgPropertyProvider.ComparableRadiusM:0} m, " +
            "over the last four years (City of Johannesburg). Transfers for R0 and implausibly low prices were removed. " +
            "Johannesburg does not publish building sizes, so sales were compared by erf size and the range is the " +
            "median price per square metre of erf applied to this property.",
        _ =>
            "Sales recorded by the City of Cape Town in the property's area were filtered: transfers for R0 and " +
            "implausibly low prices (family transfers, part-transfers, correction deeds) were removed, as were sales " +
            "older than four years and homes whose building size differs by more than 30%. Older sales were indexed " +
            "to today with the suburb's change between the 2022 and 2025 rolls; the median price per square metre of " +
            "building applied to this property gives the midpoint, and the quartiles the range.",
    };

    /// <summary>What this report cannot contain for the property's area, for a note the app shows.</summary>
    private static string? CoverageFor(PropertyRecord r) => r.Ref.Municipality switch
    {
        Johannesburg =>
            "Johannesburg values are from the GV2023 roll (valued as at 1 July 2022). Building sizes are not " +
            "published, so floor area is not filled in and sales are compared by erf size.",
        National =>
            "Only the national cadastre covers this property: its erf number, size and boundary, from records of " +
            "about 2017. There is no municipal value or sales data for this area yet.",
        _ => null,
    };

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
