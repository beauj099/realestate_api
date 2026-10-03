using System.Globalization;

namespace RealEstateApi.Application.Services;

// Plain BCL only: tools/ImportLoadShedding compiles this file too, so its dry run prints the
// same hours the report will.

/// <summary>One year of load-shedding for an area.</summary>
public record LoadSheddingYearDto(int Year, double Hours, int Days, double HoursPerDay, string? WorstMonth,
    double WorstMonthHours, double PercentOfYear);

/// <summary>
/// Past load-shedding for the property's area: per year, and the times of day it fell most often.
/// Either the municipality's record of load-shedding carried out per area (Cape Town), or, where
/// there is none, the published schedule against the announced stages.
/// </summary>
public record LoadSheddingDto(string Area, string AreaLabel, IReadOnlyList<LoadSheddingYearDto> Years,
    IReadOnlyList<string> UsualTimes, string CoverageFrom, string CoverageTo, string Summary, string Source,
    string Caveat);

/// <summary>A stage announced for a period (SAST); <see cref="ExcludeTag"/> "coct" = not in Cape Town.</summary>
public sealed record StagePeriod(DateTime Start, DateTime End, int Stage, string? ExcludeTag = null);

/// <summary>A slot in an area's recurring monthly pattern.</summary>
public sealed record AreaSlot(int DateOfMonth, int Stage, TimeOnly Start, TimeOnly End);

/// <summary>One recorded outage in an area: local start (SAST) and how long it lasted.</summary>
public sealed record Outage(DateTime Start, int Minutes, int? Stage = null);

/// <summary>
/// The calculation, kept pure so it is tested offline. THE ORDER IS THE ALGORITHM: every stage
/// period's clipped slots are gathered per calendar day first, and each day is merged once.
/// Announcements overlap and supersede one another; merging per period and adding up
/// double-counts every overlap (more hours than a year has, in testing).
/// </summary>
public static class LoadSheddingCalculator
{
    /// <summary>The longest single outage believed (a few records read 600+ minutes: typing slips).</summary>
    public const int MaxOutageMinutes = 480;

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
        return Summarise(perDay);
    }

    /// <summary>
    /// The same figures from recorded outages. All of an area's outages are merged once (records
    /// repeat and back-to-back slots overlap), and each merged spell counts on the day it began,
    /// as a scheduled slot past midnight does. Rows of no length, or longer than
    /// <see cref="MaxOutageMinutes"/>, are ignored.
    /// </summary>
    public static (IReadOnlyList<LoadSheddingYearDto> Years, int[] HalfHours) ComputeFromOutages(
        IReadOnlyList<Outage> outages)
    {
        var merged = Merge(outages.Where(o => o.Minutes is > 0 and <= MaxOutageMinutes)
            .Select(o => (o.Start, o.Start.AddMinutes(o.Minutes))));
        var perDay = merged.GroupBy(m => DateOnly.FromDateTime(m.Start)).ToDictionary(g => g.Key, g => g.ToList());
        return Summarise(perDay);
    }

    private static (IReadOnlyList<LoadSheddingYearDto> Years, int[] HalfHours) Summarise(
        Dictionary<DateOnly, List<(DateTime Start, DateTime End)>> perDay)
    {
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

    /// <summary>The key of a Cape Town load-shedding area: 9 → "city-of-cape-town-area-9".</summary>
    public static string CapeTownArea(int number) => $"city-of-cape-town-area-{number}";
}
