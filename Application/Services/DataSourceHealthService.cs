using System.Diagnostics;
using PropertyData.Core;
using PropertyData.Core.Models;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public record DataSourceCheck(string Source, bool Ok, string Detail, double Seconds);

/// <summary>
/// Checks that every municipal source still answers the way the adapters expect, against one
/// known property each. A City renaming a field or a form control is the likeliest way reports
/// quietly go blank; this makes it fail loudly (admin endpoint, and a monthly run that emails).
/// </summary>
public class DataSourceHealthService(IEnumerable<IPropertyDataProvider> providers, ILogger<DataSourceHealthService> log)
{
    private sealed record Probe(string Source, string Municipality, ResolveQuery Query, string Erf,
        Func<PropertyRecord, string?> Problem);

    // Known properties, recorded 2026-09-26. Problem() returns what is wrong, or null.
    private static readonly Probe[] Probes =
    [
        new("City of Cape Town", "coct", new ResolveQuery(Address: "17 Pine Road, Claremont"), "53927",
            r => r.Valuation is null ? "no municipal value"
                : r.BestExtentM2 is not (> 1000 and < 1200) ? $"erf extent {r.BestExtentM2} m², expected about 1 085"
                : null),
        new("City of Johannesburg", "coj", new ResolveQuery(Address: "10 Thirteenth Street, Parkhurst"), "1106",
            r => r.Valuation is null ? "no municipal value" : null),
        new("City of Tshwane roll", "tshwane", new ResolveQuery(Lat: -25.7905, Lng: 28.2505), "1104",
            r => r.Valuation is null ? "no municipal value (roll form or columns may have changed)"
                : r.BestExtentM2 is not (> 3800 and < 4100) ? $"erf extent {r.BestExtentM2} m², expected 3 936"
                : null),
        new("Mossel Bay roll", "mosselbay", new ResolveQuery(Lat: -34.1260, Lng: 22.1110), "207",
            r => r.Valuation is null ? "no municipal value (portal or columns may have changed)"
                : r.BestExtentM2 is not (> 39000 and < 41000) ? $"erf extent {r.BestExtentM2} m², expected 40 025"
                : null),
    ];

    public async Task<IReadOnlyList<DataSourceCheck>> RunAsync(CancellationToken ct)
    {
        var results = new List<DataSourceCheck>();
        foreach (var probe in Probes)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var provider = providers.First(p => p.Handles(probe.Municipality));
                var refs = await provider.ResolveAsync(probe.Query, ct);
                var hit = refs.FirstOrDefault(r => r.Erf == probe.Erf);
                if (hit is null)
                {
                    results.Add(new(probe.Source, false,
                        $"did not resolve to erf {probe.Erf} (got {string.Join(", ", refs.Select(r => r.Erf))})",
                        Seconds(sw)));
                    continue;
                }
                var record = await provider.FetchRecordAsync(hit,
                    new RecordOptions(IncludeComparables: false, IncludeBuildings: false,
                        IncludeApprovedWork: false, IncludeDwellingExtent: false), ct);
                var problem = probe.Problem(record);
                results.Add(new(probe.Source, problem is null, problem ?? "ok", Seconds(sw)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                results.Add(new(probe.Source, false, $"{ex.GetType().Name}: {ex.Message}", Seconds(sw)));
            }
        }

        foreach (var r in results.Where(r => !r.Ok))
            log.LogError("Data source check failed: {Source}: {Detail}", r.Source, r.Detail);
        return results;
    }

    private static double Seconds(Stopwatch sw) => Math.Round(sw.Elapsed.TotalSeconds, 1);
}

public class DataSourceCheckOptions
{
    public const string SectionName = "DataSourceChecks";

    /// <summary>Day of the month for the scheduled check; 0 turns it off.</summary>
    public int DayOfMonth { get; set; } = 3;

    /// <summary>Who hears about a failure. Empty: the failure is only logged.</summary>
    public string? AlertEmail { get; set; }
}

/// <summary>
/// Runs the data-source checks once a month at 01:40 (South African time; off the hour, when the
/// City's servers are quiet) and emails <see cref="DataSourceCheckOptions.AlertEmail"/> on failure.
/// </summary>
public class MonthlyDataSourceCheck(
    IServiceScopeFactory scopes,
    Microsoft.Extensions.Options.IOptions<DataSourceCheckOptions> options,
    ILogger<MonthlyDataSourceCheck> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (o.DayOfMonth is < 1 or > 28) return;
        var sast = TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sast);
            var next = new DateTimeOffset(now.Year, now.Month, o.DayOfMonth, 1, 40, 0, now.Offset);
            if (next <= now) next = next.AddMonths(1);
            try
            {
                await Task.Delay(next - now, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopes.CreateScope();
                var results = await scope.ServiceProvider.GetRequiredService<DataSourceHealthService>().RunAsync(stoppingToken);
                var failed = results.Where(r => !r.Ok).ToList();
                if (failed.Count > 0 && !string.IsNullOrWhiteSpace(o.AlertEmail))
                {
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(o.AlertEmail,
                        $"RealWorth: {failed.Count} property data source(s) failed their monthly check",
                        string.Join("\n", results.Select(r => $"{(r.Ok ? "OK  " : "FAIL")} {r.Source}: {r.Detail} ({r.Seconds}s)")) +
                        "\n\nReports for these areas may be missing values until the adapter is fixed.",
                        stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Monthly data source check could not run");
            }
        }
    }
}
