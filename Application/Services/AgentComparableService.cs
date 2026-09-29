using System.Globalization;
using System.Text.RegularExpressions;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

/// <summary>
/// Sales agents capture themselves, shared with every agent working the same suburb (the
/// capturing agent is never named), and the agency's own listings nearby.
///
/// Agent-reported prices are not registered transfers. Each is weighted by how it is known
/// (<see cref="AgentComparableMath.WeightFor"/>), corroborated when a second agent logs the same
/// sale, and checked against the municipal sales record whenever a report has one for the area.
/// </summary>
public class AgentComparableService(AgentComparableRepository repository, ILogger<AgentComparableService> log)
{
    /// <summary>Evidence an agent can claim. DeedsVerified is only ever set by a match.</summary>
    public static readonly string[] CapturableEvidence = ["Hearsay", "ColleagueConfirmed", "SignedOffer", "OwnTransaction"];

    public static readonly string[] Conditions = ["Poor", "Fair", "Good", "VeryGood", "Renovated"];

    /// <summary>How far back a report looks for agent-reported sales.</summary>
    public const int WindowMonths = 24;

    public static Dictionary<string, string[]> Validate(CreateAgentComparableRequest r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r.Municipality)) errors["municipality"] = ["Municipality is required."];
        if (string.IsNullOrWhiteSpace(r.Suburb)) errors["suburb"] = ["Suburb is required."];
        if (string.IsNullOrWhiteSpace(r.Address)) errors["address"] = ["Address is required."];
        else if (r.Address.Trim().Length > 200) errors["address"] = ["Address is too long."];
        if (r.SalePriceZar < 10_000) errors["salePriceZar"] = ["Enter the sale price in rand."];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (r.SaleDate > today.AddDays(1)) errors["saleDate"] = ["The sale date cannot be in the future."];
        else if (r.SaleDate < today.AddYears(-5)) errors["saleDate"] = ["Only sales from the last five years are useful."];
        if (!CapturableEvidence.Contains(r.EvidenceLevel)) errors["evidenceLevel"] = ["Say how you know about this sale."];
        if (r.Condition is not null && !Conditions.Contains(r.Condition)) errors["condition"] = ["Unknown condition."];
        if (r.FloorM2 is <= 0 or > 20_000) errors["floorM2"] = ["Floor size must be in square metres."];
        if (r.ErfM2 is <= 0 or > 1_000_000) errors["erfM2"] = ["Erf size must be in square metres."];
        if (r.Notes is { Length: > 600 }) errors["notes"] = ["Notes are limited to 600 characters."];
        return errors;
    }

    public async Task<AgentComparableSavedDto> CreateAsync(int userId, CreateAgentComparableRequest r, CancellationToken ct)
    {
        var sale = new AgentComparable
        {
            CapturedByUserId = userId,
            Municipality = r.Municipality.Trim().ToLowerInvariant(),
            Suburb = r.Suburb.Trim().ToUpperInvariant(),
            Address = Regex.Replace(r.Address.Trim(), @"\s+", " "),
            Erf = string.IsNullOrWhiteSpace(r.Erf) ? null : r.Erf.Trim(),
            Latitude = r.Lat is null ? null : Math.Round((decimal)r.Lat.Value, 6),
            Longitude = r.Lng is null ? null : Math.Round((decimal)r.Lng.Value, 6),
            ErfM2 = r.ErfM2,
            FloorM2 = r.FloorM2,
            Bedrooms = ToByte(r.Bedrooms),
            Bathrooms = ToByte(r.Bathrooms),
            Garages = ToByte(r.Garages),
            HasPool = r.HasPool,
            PropertyTypeId = r.PropertyTypeId,
            Condition = r.Condition,
            SaleDate = r.SaleDate.ToDateTime(TimeOnly.MinValue),
            SalePriceZar = Math.Round(r.SalePriceZar),
            EvidenceLevel = r.EvidenceLevel,
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
        };
        var (id, duplicate) = await repository.AddOrCorroborateAsync(sale,
            existing => AgentComparableMath.SameProperty(existing.Address, existing.Erf, sale.Address, sale.Erf), ct);
        return new AgentComparableSavedDto(id, duplicate);
    }

    public async Task<IReadOnlyList<AgentComparableDto>> MineAsync(int userId, CancellationToken ct) =>
        (await repository.GetByUserAsync(userId, ct)).Select(c => ToDto(c, userId)).ToList();

    public Task<bool> DeleteAsync(Guid id, int userId, CancellationToken ct) => repository.DeleteOwnAsync(id, userId, ct);

    /// <summary>
    /// Agent-reported sales for a report: checked against the report's municipal sales first
    /// (and the outcome stored), then summarised. Null when the table cannot be read, so a
    /// missing patch or a database hiccup never costs the agent the rest of the report.
    /// </summary>
    public async Task<AgentComparablesSummaryDto?> ForReportAsync(int? userId, string municipality, string suburb,
        IReadOnlyList<MunicipalSale> municipalSales, string municipalSource, double? subjectFloorM2, double? subjectErfM2,
        CancellationToken ct, double? lat = null, double? lng = null)
    {
        if (string.IsNullOrWhiteSpace(suburb)) return null;
        List<AgentComparable> sales;
        try
        {
            sales = (await repository.GetInAreaAsync(municipality.ToLowerInvariant(), suburb.Trim().ToUpperInvariant(),
                DateTime.UtcNow.Date.AddMonths(-WindowMonths), ct, lat, lng)).ToList();

            foreach (var sale in sales.Where(s => s.Verification == "Unverified"))
            {
                var outcome = AgentComparableMath.Reconcile(sale, municipalSales);
                if (outcome is null) continue;
                var (verification, evidence, match) = outcome.Value;
                var against = $"{municipalSource}: {match.SaleDate:yyyy-MM-dd}, " +
                              $"R{match.PriceZar.ToString("N0", CultureInfo.InvariantCulture)}";
                await repository.SetVerificationAsync(sale.Id, verification, evidence, against, ct);
                sale.Verification = verification;
                sale.EvidenceLevel = evidence ?? sale.EvidenceLevel;
                sale.VerifiedAgainst = against;
                sale.Weight = AgentComparableMath.WeightFor(sale.EvidenceLevel, sale.Verification);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Agent comparables unavailable for {Municipality}/{Suburb}", municipality, suburb);
            return null;
        }

        return AgentComparableMath.Summarise(sales.Select(s => ToDto(s, userId)).ToList(), suburb,
            subjectFloorM2, subjectErfM2);
    }

    /// <summary>The agency's listings in a suburb (or the agent's own when they have no agency).</summary>
    /// <summary>How far "nearby" reaches when the property's location is known.</summary>
    public const double MarketRadiusM = 2000;

    public async Task<IReadOnlyList<MarketListingDto>> MarketAsync(int userId, string suburb, int? excludeListingId,
        CancellationToken ct, double? lat = null, double? lng = null)
    {
        var today = DateTime.UtcNow.Date;
        double? DistanceM(MarketListingRow l) =>
            lat is null || lng is null || l.Latitude is null || l.Longitude is null ? null
            : PropertyData.CapeTown.Internal.Geo.DistanceM(new PropertyData.Core.Models.LatLng(lat.Value, lng.Value),
                new PropertyData.Core.Models.LatLng(l.Latitude.Value, l.Longitude.Value));
        var rows = (await repository.GetAgencyListingsInSuburbAsync(userId, suburb, excludeListingId, ct, lat, lng,
                MarketRadiusM))
            .Select(l => (Row: l, Distance: DistanceM(l)))
            // In the same-named suburb, or truly within reach (the box's corners are further).
            .Where(x => x.Distance is null || x.Distance <= MarketRadiusM
                        || string.Equals(x.Row.Suburb?.Trim(), suburb.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Distance ?? double.MaxValue)
            .ToList();
        return rows
            .Select(x =>
            {
                var l = x.Row;
                var start = l.ListDate ?? l.CreatedAt.Date;
                var end = l.ArchivedAt?.Date ?? today;
                var address = string.Join(" ", new[] { l.StreetNumber, l.Street }
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
                return new MarketListingDto(
                    l.ListingId,
                    address.Length == 0 ? "Address not captured" : address,
                    l.Suburb?.Trim(),
                    l.PropertyTypeId,
                    l.AgentValuation is > 0 ? l.AgentValuation : null,
                    l.ErfSize is > 0 ? l.ErfSize : null,
                    l.FloorArea is > 0 ? l.FloorArea : null,
                    l.AgentValuation is > 0 && l.FloorArea is > 0 ? Math.Round(l.AgentValuation.Value / l.FloorArea.Value) : null,
                    l.Status,
                    l.ListDate?.ToString("yyyy-MM-dd"),
                    Math.Max(0, (int)(end - start).TotalDays),
                    l.ListDate is null,
                    l.ArchivedAt is not null,
                    x.Distance is null ? null : Math.Round(x.Distance.Value));
            })
            .ToList();
    }

    private static byte? ToByte(int? v) => v is null ? null : (byte)Math.Clamp(v.Value, 0, 50);

    private static AgentComparableDto ToDto(AgentComparable c, int? userId) => new(
        c.Id, c.Address, c.Suburb, c.Erf, c.ErfM2, c.FloorM2, c.Bedrooms, c.Condition,
        c.SaleDate.ToString("yyyy-MM-dd"), c.SalePriceZar,
        c.PricePerFloorM2 is null ? null : Math.Round(c.PricePerFloorM2.Value),
        c.EvidenceLevel, AgentComparableMath.DescribeEvidence(c.EvidenceLevel), c.Verification,
        c.CorroborationCount, c.Weight, userId is not null && c.CapturedByUserId == userId,
        c.Bathrooms, c.HasPool);
}

/// <summary>A municipal sale a captured one can be checked against.</summary>
public record MunicipalSale(string Address, string? Erf, DateOnly SaleDate, decimal PriceZar);

/// <summary>The arithmetic behind agent-reported sales, kept pure so it can be tested.</summary>
public static class AgentComparableMath
{
    /// <summary>Mirrors the computed Weight column in dbo.AgentComparables.</summary>
    public static decimal WeightFor(string evidence, string verification)
    {
        var e = evidence switch
        {
            "DeedsVerified" => 1.00m,
            "OwnTransaction" => 0.90m,
            "SignedOffer" => 0.80m,
            "ColleagueConfirmed" => 0.55m,
            _ => 0.33m,
        };
        var v = verification switch
        {
            "Verified" => 1.00m,
            "Disputed" or "Rejected" => 0m,
            _ => 0.90m,
        };
        return Math.Round(e * v, 2);
    }

    public static string DescribeEvidence(string evidence) => evidence switch
    {
        "DeedsVerified" => "Matches the municipal sales record",
        "OwnTransaction" => "Agent's own sale",
        "SignedOffer" => "Signed offer seen",
        "ColleagueConfirmed" => "Confirmed by a second agent",
        _ => "Heard from another agent",
    };

    private static readonly (string Pattern, string Replacement)[] StreetWords =
    [
        (@"\bST\b", "STREET"), (@"\bRD\b", "ROAD"), (@"\bAVE?\b", "AVENUE"), (@"\bDR\b", "DRIVE"),
        (@"\bCRES\b", "CRESCENT"), (@"\bCL\b", "CLOSE"), (@"\bLN\b", "LANE"), (@"\bPL\b", "PLACE"),
        (@"\bTCE\b", "TERRACE"), (@"\bBLVD\b", "BOULEVARD"),
    ];

    /// <summary>"10 Thirteenth St, Parkhurst" → "10 THIRTEENTH STREET": the street line only.</summary>
    public static string NormalizeAddress(string address)
    {
        var line = address.Split(',')[0].ToUpperInvariant();
        line = Regex.Replace(line, @"[^A-Z0-9 ]", " ");
        foreach (var (pattern, replacement) in StreetWords) line = Regex.Replace(line, pattern, replacement);
        return Regex.Replace(line, @"\s+", " ").Trim();
    }

    /// <summary>The same erf when both have one, otherwise the same street line.</summary>
    public static bool SameProperty(string addressA, string? erfA, string addressB, string? erfB) =>
        !string.IsNullOrWhiteSpace(erfA) && !string.IsNullOrWhiteSpace(erfB)
            ? string.Equals(erfA.Trim(), erfB.Trim(), StringComparison.OrdinalIgnoreCase)
            : NormalizeAddress(addressA) == NormalizeAddress(addressB);

    /// <summary>
    /// Checks a captured sale against municipal sales of the same property (same erf, or the same
    /// street address) registered within 90 days: within 3% of the price it is verified; further
    /// off it is disputed. Null when no municipal sale is close enough in time to say.
    /// </summary>
    public static (string Verification, string? Evidence, MunicipalSale Match)? Reconcile(
        AgentComparable sale, IReadOnlyList<MunicipalSale> municipal)
    {
        var saleDate = DateOnly.FromDateTime(sale.SaleDate);
        var candidates = municipal
            .Where(m => m.PriceZar > 0)
            .Where(m => SameProperty(m.Address, m.Erf, sale.Address, sale.Erf))
            .Where(m => Math.Abs(m.SaleDate.DayNumber - saleDate.DayNumber) <= 90)
            .OrderBy(m => Math.Abs(m.PriceZar - sale.SalePriceZar))
            .ToList();
        if (candidates.Count == 0) return null;

        var best = candidates[0];
        return Math.Abs(best.PriceZar - sale.SalePriceZar) <= best.PriceZar * 0.03m
            ? ("Verified", "DeedsVerified", best)
            : ("Disputed", null, best);
    }

    /// <summary>
    /// The value at which the running weight passes <paramref name="fraction"/> of the total
    /// (0.5 is the weighted median). Null without any positive weight.
    /// </summary>
    public static decimal? WeightedQuantile(IEnumerable<(decimal Value, decimal Weight)> items, decimal fraction)
    {
        var list = items.Where(i => i.Weight > 0).OrderBy(i => i.Value).ToList();
        var total = list.Sum(i => i.Weight);
        if (total <= 0) return null;
        var target = total * fraction;
        decimal running = 0;
        foreach (var (value, weight) in list)
        {
            running += weight;
            if (running >= target) return value;
        }
        return list[^1].Value;
    }

    /// <summary>At least this many weighted sales before the app shows a range from them.</summary>
    public const int MinSalesForRange = 3;

    public static AgentComparablesSummaryDto Summarise(IReadOnlyList<AgentComparableDto> sales, string suburb,
        double? subjectFloorM2, double? subjectErfM2)
    {
        var usable = sales.Where(s => s.Weight > 0).ToList();
        var perFloor = usable.Where(s => s.FloorM2 is > 0)
            .Select(s => (s.SalePriceZar / s.FloorM2!.Value, s.Weight)).ToList();
        var perErf = usable.Where(s => s.ErfM2 is > 0)
            .Select(s => (s.SalePriceZar / s.ErfM2!.Value, s.Weight)).ToList();

        var medianFloor = Round(WeightedQuantile(perFloor, 0.5m), 1);
        var medianErf = Round(WeightedQuantile(perErf, 0.5m), 1);

        MoneyRangeDto? range = null;
        string? basis = null;
        if (subjectFloorM2 is > 0 && perFloor.Count >= MinSalesForRange)
        {
            range = RangeFrom(perFloor, (decimal)subjectFloorM2.Value);
            basis = "floor";
        }
        else if (subjectErfM2 is > 0 && perErf.Count >= MinSalesForRange)
        {
            range = RangeFrom(perErf, (decimal)subjectErfM2.Value);
            basis = "erf";
        }

        return new AgentComparablesSummaryDto(sales, Statement(sales, suburb, basis), medianFloor, medianErf)
        {
            IndicativeValue = range,
            IndicativeBasis = basis,
        };
    }

    private static MoneyRangeDto RangeFrom(List<(decimal, decimal)> perM2, decimal size) => new(
        Low: Round(WeightedQuantile(perM2, 0.25m) * size, 10_000),
        Mid: Round(WeightedQuantile(perM2, 0.5m) * size, 10_000),
        High: Round(WeightedQuantile(perM2, 0.75m) * size, 10_000));

    private static decimal? Round(decimal? v, decimal step) =>
        v is null ? null : Math.Round(v.Value / step, MidpointRounding.AwayFromZero) * step;

    /// <summary>A sentence the report prints as is: how many, how each is known, how they were used.</summary>
    public static string Statement(IReadOnlyList<AgentComparableDto> sales, string suburb, string? basis)
    {
        var area = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(suburb.Trim().ToLowerInvariant());
        if (sales.Count == 0)
            return $"No agent has reported a sale in {area} in the last {AgentComparableService.WindowMonths} months.";

        var byEvidence = sales.Where(s => s.Verification is not ("Disputed" or "Rejected"))
            .GroupBy(s => s.EvidenceLevel)
            .OrderByDescending(g => AgentComparableMath.WeightFor(g.Key, "Verified"))
            .Select(g => $"{g.Count()} {DescribeEvidence(g.Key).ToLowerInvariant()}")
            .ToList();
        var disputed = sales.Count(s => s.Verification == "Disputed");
        var corroborated = sales.Count(s => s.CorroborationCount > 0);

        var text = $"{Plural(sales.Count, "sale")} in {area} reported by agents in the last " +
                   $"{AgentComparableService.WindowMonths} months";
        if (byEvidence.Count > 0) text += $" ({string.Join(", ", byEvidence)})";
        text += ".";
        if (corroborated > 0) text += $" {Plural(corroborated, "was", "were")} logged independently by more than one agent.";
        if (disputed > 0)
            text += $" {Plural(disputed, "differs", "differ")} materially from the municipal sales record and " +
                    (disputed == 1 ? "is" : "are") + " left out of the figures.";
        text += " Agent-reported prices are not registered transfers; each is weighted by how it is known";
        text += basis switch
        {
            "floor" => ", and the range is the weighted spread of price per square metre of floor applied to this property.",
            "erf" => ", and the range is the weighted spread of price per square metre of erf applied to this property.",
            _ => ".",
        };
        return text;
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private static string Plural(int n, string singularVerb, string pluralVerb) =>
        $"{n} {(n == 1 ? singularVerb : pluralVerb)}";
}
