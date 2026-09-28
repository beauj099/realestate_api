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
/// Tshwane and Mossel Bay (values from their rolls, from a GPS pin; no sales), and the national cadastre anywhere
/// else (erf identity and size only, from a GPS pin).
/// </summary>
public class PropertyReportService(
    IEnumerable<IPropertyDataProvider> providers,
    PropertyData.CapeTown.Clients.CapeTownSpatialClient capeTown,
    PropertyData.Johannesburg.JohannesburgPropertyProvider johannesburg,
    IMemoryCache cache,
    ImageryLinkBuilder imagery,
    AgentComparableService agentComparables,
    ILogger<PropertyReportService> log)
{
    private static readonly TimeSpan RecordTtl = TimeSpan.FromHours(12);

    public const string CapeTown = "coct";
    public const string Johannesburg = PropertyData.Johannesburg.JohannesburgPropertyProvider.Municipality;
    public const string National = PropertyData.National.NationalCadastreProvider.Municipality;
    public const string Tshwane = PropertyData.Tshwane.TshwanePropertyProvider.Municipality;
    public const string MosselBay = PropertyData.MosselBay.MosselBayPropertyProvider.Municipality;

    /// <summary>
    /// Municipalities with a roll of their own on top of the national cadastre, by the cadastre's
    /// parcel-key prefix (the demarcation code).
    /// </summary>
    private static readonly (string Prefix, string Municipality)[] RollsByParcelKey =
    [
        (PropertyData.Tshwane.TshwanePropertyProvider.ParcelKeyPrefix, Tshwane),
        (PropertyData.MosselBay.MosselBayPropertyProvider.ParcelKeyPrefix, MosselBay),
        // Rolls read from published PDF books (Drakenstein, …).
        .. PropertyData.RollBooks.RollBookCatalogue.All.Select(m => (m.ParcelKeyPrefix, m.Municipality)),
    ];

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

            // A parcel in Tshwane ("GTSH…") or Mossel Bay ("W043…") also has a value on its roll.
            refs = refs.Select(r =>
            {
                if (r.Municipality != National || r.Sg26 is null) return r;
                var owner = RollsByParcelKey.FirstOrDefault(o => r.Sg26.StartsWith(o.Prefix, StringComparison.OrdinalIgnoreCase));
                return owner.Municipality is null ? r : r with { Municipality = owner.Municipality };
            }).ToList();
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

    private static string TitleCase(string s) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    public async Task<PropertyReportDto> GetReportAsync(string municipality, string erf, string? suburb,
        string? sg26, bool includeComparables, int? userId, CancellationToken ct)
    {
        var record = await GetRecordAsync(municipality, erf, suburb, sg26, includeComparables
            ? new RecordOptions()
            : new RecordOptions(IncludeComparables: false), includeComparables ? "full" : "nocomps", ct);
        var report = Map(record);
        if (!includeComparables) return report;

        // Sales the City recorded for the area, to check agent-reported ones against. Transfers
        // for R0 and implausible prices are not sales and would only raise false disputes.
        var municipalSales = record.Comparables?.All
            .Where(c => c.Exclusion is not (ComparableExclusion.ZeroPrice or ComparableExclusion.ImplausiblePrice))
            .Select(c => new MunicipalSale(c.Address, c.Erf, c.SaleDate, c.SalePriceZar))
            .ToList() ?? [];
        var agentSales = await agentComparables.ForReportAsync(userId, record.Ref.Municipality, record.Ref.Suburb,
            municipalSales, record.DataSource, record.DwellingExtentM2, record.BestExtentM2, ct);
        return report with { AgentComparables = agentSales };
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
            .Select(ToDto)
            .ToList() ?? [],

        ComparableSummary: r.Comparables is null ? null : new ComparableSummaryDto(
            r.Comparables.All.Count, r.Comparables.Included.Count,
            r.Comparables.ExcludedZeroPrice, r.Comparables.ExcludedImplausible,
            r.Comparables.ExcludedTooOld, r.Comparables.ExcludedDissimilar,
            r.Comparables.MedianPricePerDwellingM2, r.Comparables.MedianPricePerErfM2,
            RadiusM: r.Comparables.RadiusM,
            ExcludedMultiProperty: r.Comparables.All.Count(c => c.Exclusion == ComparableExclusion.MultiPropertySale),
            ExcludedNoBuilding: r.Comparables.All.Count(c => c.Exclusion == ComparableExclusion.NoBuilding),
            ExcludedTooFar: r.Comparables.All.Count(c => c.Exclusion == ComparableExclusion.TooFar)),

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
        GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
        LastSale: r.LastSale is null ? null : new SaleRecordDto(r.LastSale.Date.ToString("yyyy-MM-dd"), r.LastSale.PriceZar),
        StreetSales: StreetSales(r),
        AreaMarket: AreaMarket(r));

    private static ComparableDto ToDto(Comparable c) => new(
        c.Address, c.Erf, c.ErfExtentM2, c.DwellingExtentM2,
        c.SaleDate.ToString("yyyy-MM-dd"), c.SalePriceZar, c.IndexedPriceZar,
        c.PricePerDwellingM2 is null ? null : Math.Round(c.PricePerDwellingM2.Value),
        c.Included, c.Included ? null : Describe(c.Exclusion),
        c.DistanceM, c.Location?.Lat, c.Location?.Lng);

    /// <summary>Sales that are a market price for one property (not R0, not a bulk deal).</summary>
    private static bool IsMarketSale(Comparable c) => c.Exclusion is not
        (ComparableExclusion.ZeroPrice or ComparableExclusion.ImplausiblePrice
         or ComparableExclusion.MultiPropertySale or ComparableExclusion.IsSubject);

    /// <summary>"10 BOSMAN STREET STRAND" → "BOSMAN STREET" (the number and the suburb removed).</summary>
    internal static string? StreetOf(string address, string suburb)
    {
        var a = System.Text.RegularExpressions.Regex.Replace(address.ToUpperInvariant().Trim(), @"^\d+[A-Z]?\s+", "");
        var s = suburb.Trim().ToUpperInvariant();
        if (s.Length > 0 && a.EndsWith(" " + s, StringComparison.Ordinal)) a = a[..^(s.Length + 1)];
        return a.Length == 0 || a.StartsWith("ERF ", StringComparison.Ordinal) ? null : a;
    }

    /// <summary>The ten latest market sales in the subject's street.</summary>
    private static IReadOnlyList<ComparableDto>? StreetSales(PropertyRecord r)
    {
        if (r.Comparables is null || StreetOf(r.FormattedAddress, r.Ref.Suburb) is not { } street) return null;
        return r.Comparables.All
            .Where(c => c.SalePriceZar > 0 && c.Exclusion is not ComparableExclusion.MultiPropertySale)
            .Where(c => System.Text.RegularExpressions.Regex.Replace(c.Address.ToUpperInvariant(), @"^\d+[A-Z]?\s+", "")
                .StartsWith(street + " ", StringComparison.Ordinal)
                || System.Text.RegularExpressions.Regex.Replace(c.Address.ToUpperInvariant(), @"^\d+[A-Z]?\s+", "") == street)
            .OrderByDescending(c => c.SaleDate)
            .Take(10)
            .Select(ToDto)
            .ToList();
    }

    /// <summary>
    /// Every market sale within the comparables' radius (or the whole area list when there was
    /// none), by year and by price band.
    /// </summary>
    private static AreaMarketDto? AreaMarket(PropertyRecord r)
    {
        if (r.Comparables is null) return null;
        var radius = r.Comparables.RadiusM;
        var sales = r.Comparables.All
            .Where(IsMarketSale)
            .Where(c => radius is null || c.DistanceM <= radius)
            .ToList();
        if (sales.Count == 0) return new AreaMarketDto(radius, 0, null, [], []);

        var prices = sales.Select(c => c.SalePriceZar).Order().ToList();
        decimal At(double q) => prices[Math.Clamp((int)Math.Round(q * (prices.Count - 1)), 0, prices.Count - 1)];

        var byYear = sales.GroupBy(c => c.SaleDate.Year).OrderBy(g => g.Key)
            .Select(g => new YearlySalesDto(g.Key, g.Count(), Median(g.Select(c => c.SalePriceZar))))
            .ToList();

        // Ten equal bands between the 5th and 95th percentile, so one mansion does not flatten
        // the chart; the outer bands take what lies beyond.
        var lo = At(0.05);
        var hi = At(0.95);
        var bands = new List<PriceBandDto>();
        if (hi > lo)
        {
            var width = (hi - lo) / 10m;
            for (var i = 0; i < 10; i++)
            {
                var from = lo + width * i;
                var to = i == 9 ? hi : from + width;
                var n = sales.Count(c => (i == 0 || c.SalePriceZar >= from) && (i == 9 || c.SalePriceZar < to));
                bands.Add(new PriceBandDto(Math.Round(from / 1000m) * 1000m, Math.Round(to / 1000m) * 1000m, n,
                    Math.Round(100.0 * n / sales.Count, 1)));
            }
        }
        return new AreaMarketDto(radius, sales.Count, Median(prices), byYear, bands);
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var v = values.Order().ToList();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2m;
    }

    /// <summary>How the comparables were chosen, in a sentence the report prints as is.</summary>
    private static string? MethodFor(PropertyRecord r)
    {
        if (r.Comparables is null) return null;
        var near = r.Comparables.RadiusM is { } radius
            ? $"The nearest were used: sales within {radius} m of the property (the search widens to 1 km only when " +
              "fewer than six similar sales are that close). "
            : "Too few similar sales lie within 1 km, so the whole area was used. ";
        const string cleaned = "Transfers for R0, implausibly low prices (family transfers, part-transfers, correction " +
                               "deeds) and several properties sold together for one price were removed";
        return r.Ref.Municipality switch
        {
            Johannesburg =>
                "The last registered sale of every stand of the same category over the last four years (City of " +
                $"Johannesburg). {cleaned}. {near}Johannesburg does not publish building sizes, so sales were compared " +
                "by erf size: each price is carried over to this erf's size (a stand twice the size sells for about " +
                "1.5 times as much, not twice), and the median and quartiles of those give the range.",
            _ =>
                $"Sales recorded by the City of Cape Town around the property were filtered. {cleaned}, as were sales " +
                "older than four years, sales with no building on record and homes whose building size differs by more " +
                $"than 30% (50% where that leaves too few). {near}Older sales were indexed to today with the suburb's change between the 2022 and 2025 " +
                "rolls, and each was carried over to this home's size (a home twice the size sells for about 1.5 times " +
                "as much, not twice); the median of those gives the midpoint, and the quartiles the range.",
        };
    }

    /// <summary>What this report cannot contain for the property's area, for a note the app shows.</summary>
    private static string? CoverageFor(PropertyRecord r) => r.Ref.Municipality switch
    {
        Johannesburg =>
            "Johannesburg values are from the GV2023 roll (valued as at 1 July 2022). Building sizes are not " +
            "published, so floor area is not filled in and sales are compared by erf size.",
        Tshwane =>
            "Tshwane values are from the GV2025 roll (valued as at 1 July 2024, in effect from 1 July 2025). " +
            "The roll has no sales or building sizes, so there are no municipal comparable sales; sales " +
            "reported by agents are the comparables here. Erf details are from the national cadastre.",
        MosselBay =>
            "Mossel Bay values are from the 2022–2026 roll (valued as at 1 July 2021, in effect from 1 July 2022). " +
            "The roll has no sales or building sizes, so there are no municipal comparable sales; sales reported " +
            "by agents are the comparables here. Erf boundaries are from the national cadastre.",
        National =>
            "Only the national cadastre covers this property: its erf number, size and boundary, from records of " +
            "about 2017. There is no municipal value or sales data for this area yet.",
        var m when PropertyData.RollBooks.RollBookCatalogue.ForMunicipality(m) is { } book =>
            $"{book.Name} values are from its {book.RollVersion} roll ({book.PeriodLabel}; valued as at " +
            $"{book.DateOfValuation:d MMMM yyyy}), read from the roll books the municipality publishes. The roll has no " +
            "sales or building sizes, so there are no municipal comparable sales; sales reported by agents are the " +
            "comparables here. Erf boundaries are from the national cadastre.",
        _ => null,
    };

    private static string Describe(ComparableExclusion e) => e switch
    {
        ComparableExclusion.ZeroPrice => "Transferred for R0 (not a market sale)",
        ComparableExclusion.ImplausiblePrice => "Price too low to be a market sale",
        ComparableExclusion.DissimilarSize => "Size too different from this property",
        ComparableExclusion.TooOld => "Sold too long ago",
        ComparableExclusion.IsSubject => "This property",
        ComparableExclusion.MultiPropertySale => "Several properties sold together for one price",
        ComparableExclusion.NoBuilding => "No building on record",
        ComparableExclusion.TooFar => "Further away than the nearer sales used",
        _ => e.ToString(),
    };
}
