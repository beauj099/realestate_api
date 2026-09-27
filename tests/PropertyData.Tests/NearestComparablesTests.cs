using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.CapeTown.Services;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.Johannesburg;
using Xunit;
using Xunit.Abstractions;

namespace PropertyData.CapeTown.Tests;

public sealed class NearestComparablesTests(ITestOutputHelper output)
{
    private static Comparable Sale(string erf, decimal price, DateOnly date, double distance, double dwelling = 300) => new()
    {
        ValuationRef = $"REF{erf}",
        Address = $"{erf} TEST STREET",
        RegisteredDescription = $"{erf} STRAND",
        Erf = erf,
        ErfExtentM2 = 600,
        DwellingExtentM2 = dwelling,
        SaleDate = date,
        SalePriceZar = price,
        DistanceM = distance,
    };

    private static PropertyRecord Subject() => new()
    {
        Ref = new PropertyRef("1", null, "SUBJECT", "STRAND", "STRAND"),
        FormattedAddress = "1 TEST STREET STRAND",
        DwellingExtentM2 = 300,
        ExtentM2Deed = 600,
    };

    [Fact]
    public void Keeps_the_nearest_and_drops_bulk_deals_and_bare_land()
    {
        var today = new DateOnly(2026, 9, 27);
        var sales = new List<Comparable>();
        for (var i = 0; i < 7; i++) sales.Add(Sale($"{100 + i}", 3_000_000m + i * 10_000, today.AddMonths(-i - 1), 150 + i * 40));
        for (var i = 0; i < 5; i++) sales.Add(Sale($"{200 + i}", 5_000_000m + i * 25_000, today.AddMonths(-2 - i), 800));   // further off
        // Three erven transferred together for one price on one day: one bulk deal.
        sales.Add(Sale("300", 22_500_000m, today.AddMonths(-3), 300));
        sales.Add(Sale("301", 22_500_000m, today.AddMonths(-3), 320));
        sales.Add(Sale("302", 22_500_000m, today.AddMonths(-3), 340));
        // Vacant land next door.
        sales.Add(Sale("400", 900_000m, today.AddMonths(-1), 100, dwelling: 0));

        var set = ComparableAnalyzer.Analyze(sales, Subject(), suburb: null, today);

        set.RadiusM.Should().Be(500);
        set.Included.Should().HaveCount(7).And.OnlyContain(c => c.DistanceM <= 500);
        sales.Where(c => c.SalePriceZar == 22_500_000m).Should().OnlyContain(c => c.Exclusion == ComparableExclusion.MultiPropertySale);
        sales.Single(c => c.Erf == "400").Exclusion.Should().Be(ComparableExclusion.NoBuilding);
        sales.Where(c => c.Erf!.StartsWith('2')).Should().OnlyContain(c => c.Exclusion == ComparableExclusion.TooFar);
    }

    [Fact]
    public void Widens_to_one_kilometre_and_then_the_whole_area_when_too_few_are_near()
    {
        var today = new DateOnly(2026, 9, 27);
        var sales = Enumerable.Range(0, 6).Select(i => Sale($"{100 + i}", 3_000_000m + i, today.AddMonths(-1), 600 + i * 50)).ToList();
        ComparableAnalyzer.Analyze(sales, Subject(), null, today).RadiusM.Should().Be(1000);

        var sparse = Enumerable.Range(0, 4).Select(i => Sale($"{100 + i}", 3_000_000m + i, today.AddMonths(-1), 1500)).ToList();
        var set = ComparableAnalyzer.Analyze(sparse, Subject(), null, today);
        set.RadiusM.Should().BeNull();
        set.Included.Should().HaveCount(4);
    }

    /// <summary>Live: 10 Bosman Street, Strand (erf 4429) — the City's list covers all of Strand.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Strand_comparables_come_from_the_neighbourhood_not_the_whole_suburb()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetServices<IPropertyDataProvider>().First(p => p.Handles("coct"));

        var refs = await provider.ResolveAsync(new ResolveQuery(Address: "10 Bosman Street, Strand"));
        var record = await provider.FetchRecordAsync(refs.First(r => r.Erf == "4429"));
        var set = record.Comparables!;

        foreach (var c in set.Included.OrderBy(c => c.DistanceM))
            output.WriteLine($"{c.DistanceM,6} m  {c.SaleDate}  R{c.SalePriceZar,12:N0}  {c.DwellingExtentM2,5} m²  {c.Address}");
        output.WriteLine($"radius {set.RadiusM}, range {set.ImpliedValueLowZar:N0} – {set.ImpliedValueHighZar:N0}, mid {set.ImpliedValueMidZar:N0}; last sale {record.LastSale}");

        set.RadiusM.Should().NotBeNull();
        set.Included.Should().OnlyContain(c => c.DistanceM <= set.RadiusM);
        set.All.Should().Contain(c => c.Exclusion == ComparableExclusion.MultiPropertySale);   // the Brewery Street deal
    }

    /// <summary>Live: 10 Thirteenth Street, Parkhurst (erf 1106).</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Parkhurst_comparables_are_measured_by_distance()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<JohannesburgPropertyProvider>();
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<JohannesburgPropertyProvider>();

        var refs = await provider.ResolveAsync(new ResolveQuery(Address: "10 Thirteenth Street, Parkhurst"));
        var record = await provider.FetchRecordAsync(refs.First(r => r.Erf == "1106"));
        var set = record.Comparables!;
        output.WriteLine($"radius {set.RadiusM}, {set.Included.Count} included, range {set.ImpliedValueLowZar:N0} – {set.ImpliedValueHighZar:N0}; last sale {record.LastSale}");

        set.All.Should().OnlyContain(c => c.DistanceM != null);
        set.Included.Should().OnlyContain(c => set.RadiusM == null || c.DistanceM <= set.RadiusM);
    }
}
