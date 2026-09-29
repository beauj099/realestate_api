using System.Globalization;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using RealEstateApi.Infrastructure.Data;

namespace RealEstateApi.Application.Services;

/// <summary>One year of scheduled load-shedding for an area.</summary>
public record LoadSheddingYearDto(int Year, double Hours, int Days, double HoursPerDay, string? WorstMonth,
    double WorstMonthHours, double PercentOfYear);

/// <summary>
/// Past load-shedding for the property's area: per year, and the times of day it fell most often.
/// Scheduled hours (the published schedule against the announced stages), not measured outages.
/// </summary>
public record LoadSheddingDto(string Area, string AreaLabel, IReadOnlyList<LoadSheddingYearDto> Years,
    IReadOnlyList<string> UsualTimes, string CoverageFrom, string CoverageTo, string Summary, string Source,
    string Caveat);

/// <summary>A stage announced for a period (SAST); <see cref="ExcludeTag"/> "coct" = not in Cape Town.</summary>
public sealed record StagePeriod(DateTime Start, DateTime End, int Stage, string? ExcludeTag = null);

/// <summary>A slot in an area's recurring monthly pattern.</summary>
public sealed record AreaSlot(int DateOfMonth, int Stage, TimeOnly Start, TimeOnly End);

/// <summary>
/// The calculation, kept pure so it is tested offline. THE ORDER IS THE ALGORITHM: every stage
/// period's clipped slots are gathered per calendar day first, and each day is merged once.
/// Announcements overlap and supersede one another; merging per period and adding up
/// double-counts every overlap (more hours than a year has, in testing).
/// </summary>
public static class LoadSheddingCalculator
{
    public static (IReadOnlyList<LoadSheddingYearDto> Years, int[] HalfHours) Compute(
        IReadOnlyList<AreaSlot> pattern, IReadOnlyList<StagePeriod> periods)
    {
        var byDay = pattern.GroupBy(s => s.DateOfMonth).ToDictionary(g => g.Key, g => g.ToList());
        var perDay = new Dictionary<DateOnly, List<(DateTime Start, DateTime End)>>();

        foreach (var p in periods.Where(p => p.Stage >= 1 && p.End > p.Start))
        {
            // A slot that runs past midnight belongs to the day it starts, so start a day early.
            for (var d = DateOnly.FromDateTime(p.Start).AddDays(-1); d <= DateOnly.FromDateTime(p.End); d = d.AddDays(1))
            {
                if (!byDay.TryGetValue(d.Day, out var slots)) continue;
                // Stages 1..N together: right whether a schedule lists each stage's slots
                // cumulatively or only the extra ones.
                foreach (var slot in slots.Where(s => s.Stage <= p.Stage))
                {
                    var start = d.ToDateTime(slot.Start);
                    var end = slot.End <= slot.Start ? d.AddDays(1).ToDateTime(slot.End) : d.ToDateTime(slot.End);
                    var from = start > p.Start ? start : p.Start;
                    var to = end < p.End ? end : p.End;
                    if (to <= from) continue;
                    if (!perDay.TryGetValue(d, out var list)) perDay[d] = list = [];
                    list.Add((from, to));
                }
            }
        }

        var hoursByYear = new Dictionary<int, double>();
        var daysByYear = new Dictionary<int, int>();
        var byMonth = new Dictionary<(int Year, int Month), double>();
        var halfHours = new int[48];
        foreach (var (day, intervals) in perDay)
        {
            var merged = Merge(intervals);                       // once, after gathering
            var hours = merged.Sum(m => (m.End - m.Start).TotalHours);
            if (hours <= 0) continue;
            hoursByYear[day.Year] = hoursByYear.GetValueOrDefault(day.Year) + hours;
            daysByYear[day.Year] = daysByYear.GetValueOrDefault(day.Year) + 1;
            byMonth[(day.Year, day.Month)] = byMonth.GetValueOrDefault((day.Year, day.Month)) + hours;
            foreach (var m in merged)
                for (var t = m.Start; t < m.End; t = t.AddMinutes(30))
                    halfHours[t.Hour * 2 + (t.Minute >= 30 ? 1 : 0)]++;
        }

        var years = hoursByYear.Keys.Order().Select(y =>
        {
            var worst = byMonth.Where(kv => kv.Key.Year == y).OrderByDescending(kv => kv.Value).FirstOrDefault();
            var inYear = DateTime.IsLeapYear(y) ? 8784.0 : 8760.0;
            return new LoadSheddingYearDto(y, Math.Round(hoursByYear[y], 1), daysByYear[y],
                Math.Round(hoursByYear[y] / daysByYear[y], 1),
                worst.Value > 0 ? new DateTime(y, worst.Key.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) : null,
                Math.Round(worst.Value, 1), Math.Round(hoursByYear[y] / inYear * 100, 1));
        }).ToList();
        return (years, halfHours);
    }

    /// <summary>Overlapping or touching intervals joined.</summary>
    public static List<(DateTime Start, DateTime End)> Merge(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var i in intervals.OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && i.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, i.End > merged[^1].End ? i.End : merged[^1].End);
            else
                merged.Add(i);
        }
        return merged;
    }

    /// <summary>The blocks of the day hit clearly more often than average ("06:00–08:30").</summary>
    public static IReadOnlyList<string> UsualTimes(int[] halfHours, double threshold = 1.25)
    {
        var avg = halfHours.Average();
        if (avg <= 0) return [];
        var windows = new List<string>();
        int? runStart = null;
        for (var i = 0; i <= 48; i++)
        {
            var hot = i < 48 && halfHours[i] >= avg * threshold;
            if (hot && runStart is null) runStart = i;
            else if (!hot && runStart is { } s)
            {
                if (i - s >= 2) windows.Add($"{s / 2:00}:{s % 2 * 30:00}–{i / 2:00}:{i % 2 * 30:00}");
                runStart = null;
            }
        }
        return windows;
    }

    /// <summary>The corpus's names: lowercase and hyphenated ("somerset-west").</summary>
    public static string Slug(string s) =>
        string.Join('-', s.Trim().ToLowerInvariant().Split([' ', '\t', '_', '-'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"city-of-cape-town-area-9" → "City of Cape Town, Area 9".</summary>
    public static string Label(string area)
    {
        var words = area.Split('-').Select(w => w.Length == 0 || char.IsDigit(w[0]) ? w
            : w is "of" or "and" ? w : char.ToUpperInvariant(w[0]) + w[1..]).ToList();
        var at = words.FindIndex(w => w is "Area" or "Block");
        return at > 0 ? $"{string.Join(' ', words.Take(at))}, {string.Join(' ', words.Skip(at))}" : string.Join(' ', words);
    }
}

/// <summary>
/// Past load-shedding for a suburb from the imported tables (tools/ImportLoadShedding). Nothing
/// until they are filled: the report then leaves the section out.
/// </summary>
public class LoadSheddingService(DbConnectionFactory db, IMemoryCache cache, ILogger<LoadSheddingService> log)
{
    public const string Caveat =
        "Scheduled load-shedding for this area's block, from the published schedule and the announced stages. " +
        "Actual supply may have differed: blocks were sometimes spared, and unplanned outages and faults are not included.";

    /// <summary>The first suburb name that has an area (the sub place, then the main place).</summary>
    public async Task<LoadSheddingDto?> ForAsync(IEnumerable<string?> suburbs, string? municipality, CancellationToken ct)
    {
        foreach (var suburb in suburbs.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            try
            {
                if (await AreaForAsync(suburb!, municipality, ct) is { } area) return await HistoryAsync(area, ct);
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                log.LogWarning(ex, "Load-shedding history unavailable");
                return null;
            }
        }
        return null;
    }

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
            var worst = years.MaxBy(y => y.Hours)!;
            var latest = years.MaxBy(y => y.Year)!;
            var summary = $"In {worst.Year}, the worst year on record, this area was scheduled off for {worst.Hours:N0} hours " +
                          $"over {worst.Days} days, about {worst.HoursPerDay:0.#} hours on each of them ({worst.PercentOfYear:0.#}% of the year)." +
                          (latest.Year != worst.Year ? $" In {latest.Year} it was {latest.Hours:N0} hours over {latest.Days} days." : "") +
                          (times.Count > 0 ? $" It fell most often between {string.Join(" and ", times.Take(2))}." : "");
            return new LoadSheddingDto(area, LoadSheddingCalculator.Label(area), years, times,
                periods.Min(p => p.Start).ToString("yyyy-MM-dd"), periods.Max(p => p.End).ToString("yyyy-MM-dd"),
                summary, "Imported load-shedding schedule and stage record", Caveat);
        });
}
