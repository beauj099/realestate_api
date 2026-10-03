using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using PropertyData.CapeTown.Internal;
using PropertyData.Core.Models;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Application.Services;

/// <summary>
/// A load-shedding area's outline (WGS84), with its bounding box for a quick first test. Several
/// rings are parts and holes alike: a point is inside when an odd number of them hold it.
/// </summary>
public sealed record LoadSheddingAreaShape(string Area, IReadOnlyList<Ring> Rings)
{
    public double MinLat { get; } = Rings.SelectMany(r => r.Points).Min(p => p.Lat);
    public double MaxLat { get; } = Rings.SelectMany(r => r.Points).Max(p => p.Lat);
    public double MinLng { get; } = Rings.SelectMany(r => r.Points).Min(p => p.Lng);
    public double MaxLng { get; } = Rings.SelectMany(r => r.Points).Max(p => p.Lng);

    public bool Contains(LatLng pt) =>
        pt.Lat >= MinLat && pt.Lat <= MaxLat && pt.Lng >= MinLng && pt.Lng <= MaxLng
        && Rings.Count(r => Geo.Contains(r, pt)) % 2 == 1;

    /// <summary>Rings stored as JSON: <c>[[[lng,lat],...],...]</c> (GeoJSON order).</summary>
    public static LoadSheddingAreaShape? FromJson(string area, string ringsJson)
    {
        var rings = (JsonSerializer.Deserialize<double[][][]>(ringsJson) ?? [])
            .Where(r => r.Length >= 4 && r.All(p => p.Length >= 2))
            .Select(r => new Ring([.. r.Select(p => new LatLng(p[1], p[0]))]))
            .ToList();
        return rings.Count > 0 ? new LoadSheddingAreaShape(area, rings) : null;
    }
}

/// <summary>
/// Past load-shedding for a property from the imported tables (tools/ImportLoadShedding). The
/// area is found by the property's location in the imported area outlines, else by its suburb
/// name; its history is the municipality's record of outages when there is one, else the
/// schedule against the announced stages. Nothing until the tables are filled: the report then
/// leaves the section out.
/// </summary>
public class LoadSheddingService(DbConnectionFactory db, IMemoryCache cache, ILogger<LoadSheddingService> log)
{
    public const string Caveat =
        "Scheduled load-shedding for this area's block, from the published schedule and the announced stages. " +
        "Actual supply may have differed: blocks were sometimes spared, and unplanned outages and faults are not included.";

    public const string OutageCaveat =
        "The City of Cape Town's record of the load-shedding it carried out in this area, at the City's own stage " +
        "(often lower than Eskom's). Homes supplied directly by Eskom rather than by the City followed Eskom's " +
        "schedule instead and are not covered by it; unplanned outages and faults are not included.";

    public const string OutageSource = "City of Cape Town, load-shedding per area (open data)";

    /// <summary>
    /// The area holding the point, else the first suburb name that has an area (the sub place,
    /// then the main place). An area outline without any history falls through to the names.
    /// </summary>
    public async Task<LoadSheddingDto?> ForAsync(double? lat, double? lng, IEnumerable<string?> suburbs,
        string? municipality, CancellationToken ct)
    {
        try
        {
            if (lat is { } la && lng is { } ln
                && AreaAt(await ShapesAsync(ct), new LatLng(la, ln)) is { } byPoint
                && await HistoryAsync(byPoint, ct) is { } history)
                return history;

            foreach (var suburb in suburbs.Where(s => !string.IsNullOrWhiteSpace(s)))
                if (await AreaForAsync(suburb!, municipality, ct) is { } area) return await HistoryAsync(area, ct);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            log.LogWarning(ex, "Load-shedding history unavailable");
        }
        return null;
    }

    /// <summary>The area whose outline holds the point; the smallest when outlines overlap.</summary>
    public static string? AreaAt(IEnumerable<LoadSheddingAreaShape> shapes, LatLng pt) =>
        shapes.Where(s => s.Contains(pt))
            .OrderBy(s => (s.MaxLat - s.MinLat) * (s.MaxLng - s.MinLng))
            .Select(s => s.Area).FirstOrDefault();

    private async Task<IReadOnlyList<LoadSheddingAreaShape>> ShapesAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync("ls:shapes", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            using var c = db.CreateConnection();
            var rows = await c.QueryAsync<(string Area, string RingsJson)>(new CommandDefinition(
                "SELECT Area, RingsJson FROM LoadSheddingAreaShapes", cancellationToken: ct));
            var shapes = new List<LoadSheddingAreaShape>();
            foreach (var (area, json) in rows)
            {
                try
                {
                    if (LoadSheddingAreaShape.FromJson(area, json) is { } shape) shapes.Add(shape);
                }
                catch (JsonException ex)
                {
                    log.LogWarning(ex, "Load-shedding outline for {Area} unreadable", area);
                }
            }
            return (IReadOnlyList<LoadSheddingAreaShape>)shapes;
        }) ?? [];

    private async Task<string?> AreaForAsync(string suburb, string? municipality, CancellationToken ct)
    {
        var slug = LoadSheddingCalculator.Slug(suburb);
        var muni = LoadSheddingCalculator.Slug(municipality ?? "");
        return await cache.GetOrCreateAsync($"ls:area:{slug}:{muni}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            using var c = db.CreateConnection();
            // The municipality first: suburb names repeat across the country.
            return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
                @"SELECT TOP 1 Area FROM LoadSheddingSuburbAreas
                  WHERE Suburb = @slug
                  ORDER BY CASE WHEN Municipality = @muni THEN 0 ELSE 1 END, Area",
                new { slug, muni }, cancellationToken: ct));
        });
    }

    private async Task<LoadSheddingDto?> HistoryAsync(string area, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"ls:history:{area}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);   // the past does not change
            using var c = db.CreateConnection();
            // The municipality's record of what was carried out wins over the schedule.
            var outages = (await c.QueryAsync<(DateTime StartLocal, int Minutes, byte? Stage)>(new CommandDefinition(
                    "SELECT StartLocal, Minutes, Stage FROM LoadSheddingOutages WHERE Area = @area ORDER BY StartLocal",
                    new { area }, cancellationToken: ct)))
                .Select(r => new Outage(r.StartLocal, r.Minutes, r.Stage)).ToList();
            if (outages.Count > 0) return FromOutages(area, outages);

            var capeTown = area.StartsWith("city-of-cape-town", StringComparison.OrdinalIgnoreCase);
            var periods = (await c.QueryAsync<(DateTime StartLocal, DateTime FinshLocal, byte Stage, string? ExcludeTag)>(
                    new CommandDefinition(
                        @"SELECT StartLocal, FinshLocal, Stage, ExcludeTag FROM LoadSheddingStagePeriods
                          WHERE @capeTown = 0 OR ISNULL(ExcludeTag, '') <> 'coct' ORDER BY StartLocal",
                        new { capeTown = capeTown ? 1 : 0 }, cancellationToken: ct)))
                .Select(r => new StagePeriod(r.StartLocal, r.FinshLocal, r.Stage, r.ExcludeTag)).ToList();
            var pattern = (await c.QueryAsync<(byte DateOfMonth, byte Stage, TimeSpan StartTime, TimeSpan FinshTime)>(
                    new CommandDefinition(
                        "SELECT DateOfMonth, Stage, StartTime, FinshTime FROM LoadSheddingAreaSlots WHERE Area = @area",
                        new { area }, cancellationToken: ct)))
                .Select(r => new AreaSlot(r.DateOfMonth, r.Stage, TimeOnly.FromTimeSpan(r.StartTime), TimeOnly.FromTimeSpan(r.FinshTime)))
                .ToList();
            if (periods.Count == 0 || pattern.Count == 0) return null;

            var (years, halfHours) = LoadSheddingCalculator.Compute(pattern, periods);
            if (years.Count == 0) return null;
            var times = LoadSheddingCalculator.UsualTimes(halfHours);
            return new LoadSheddingDto(area, LoadSheddingCalculator.Label(area), years, times,
                periods.Min(p => p.Start).ToString("yyyy-MM-dd"), periods.Max(p => p.End).ToString("yyyy-MM-dd"),
                Summary(years, times, "was scheduled off for"), "Imported load-shedding schedule and stage record", Caveat);
        });

    /// <summary>The report section from recorded outages (pure, so it is tested offline).</summary>
    public static LoadSheddingDto? FromOutages(string area, IReadOnlyList<Outage> outages)
    {
        var (years, halfHours) = LoadSheddingCalculator.ComputeFromOutages(outages);
        if (years.Count == 0) return null;
        var times = LoadSheddingCalculator.UsualTimes(halfHours);
        return new LoadSheddingDto(area, LoadSheddingCalculator.Label(area), years, times,
            outages.Min(o => o.Start).ToString("yyyy-MM-dd"), outages.Max(o => o.Start).ToString("yyyy-MM-dd"),
            Summary(years, times, "had load-shedding for"), OutageSource, OutageCaveat);
    }

    private static string Summary(IReadOnlyList<LoadSheddingYearDto> years, IReadOnlyList<string> times, string verb)
    {
        var worst = years.MaxBy(y => y.Hours)!;
        var latest = years.MaxBy(y => y.Year)!;
        return $"In {worst.Year}, the worst year on record, this area {verb} {worst.Hours:N0} hours " +
               $"over {worst.Days} days, about {worst.HoursPerDay:0.#} hours on each of them ({worst.PercentOfYear:0.#}% of the year)." +
               (latest.Year != worst.Year ? $" In {latest.Year} it was {latest.Hours:N0} hours over {latest.Days} days." : "") +
               (times.Count > 0 ? $" It fell most often between {string.Join(" and ", times.Take(2))}." : "");
    }
}
