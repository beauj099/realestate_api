using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.National;
using PropertyData.Tshwane;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>
/// The City of Tshwane roll. The offline tests read a real response (txterf=62,
/// txttownship=WATERKLOOF, 2026-09-26) trimmed to a few rows; it shows both substring traps.
/// </summary>
public sealed class TshwaneRollTests
{
    private static IReadOnlyList<TshwaneRollEntry> Fixture() =>
        TshwaneRollClient.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tshwane_erf62_waterkloof.html")));

    [Fact]
    public void Reads_the_results_grid_by_its_headers()
    {
        var rows = Fixture();
        rows.Should().HaveCount(11);
        rows.Should().Contain(r => r.Description == "00062/ 1 - UNIT 0002" && r.Stand == 62 && r.Portion == 1 && r.Unit == 2);
        rows.Should().Contain(r => r.Township == "WATERKLOOF RIDGE X02" && r.LisKey == "074401162"
                                   && r.SizeM2 == 1646 && r.MarketValueZar == 4_500_000m);
    }

    [Fact]
    public void R1_is_a_placeholder_not_a_price()
    {
        TshwaneRollClient.Money("R 1").Should().BeNull();
        TshwaneRollClient.Money("R 3750000").Should().Be(3_750_000m);
        Fixture().Single(r => r.LisKey == "071600062/1" && r.Unit is null).MarketValueZar.Should().BeNull();
    }

    [Fact]
    public void Only_the_exact_stand_in_the_township_or_its_extensions_matches()
    {
        var rows = Fixture();
        // Searching "62" in "WATERKLOOF" also returned 162, 262, 1162… in Waterkloof Ridge.
        // Portion 1 of erf 62 is a sectional-title scheme: the erf's own row and its two units.
        rows.Where(r => TshwaneRollClient.Matches(r, "WATERKLOOF", 62, 1)).Select(r => r.Unit)
            .Should().Equal(null, 1, 2);
        // The cadastre's whole erf 62 is the roll's remainder, "00062/ R".
        rows.Where(r => TshwaneRollClient.Matches(r, "WATERKLOOF", 62, 0)).Select(r => r.LisKey)
            .Should().Equal("071600062/R");
        rows.Where(r => TshwaneRollClient.Matches(r, "WATERKLOOF RIDGE", 1162, 0)).Select(r => r.Township)
            .Should().Equal("WATERKLOOF RIDGE X02");
        rows.Where(r => TshwaneRollClient.Matches(r, "WATERKLOOF", 162, 0)).Select(r => r.MarketValueZar)
            .Should().Equal(3_000_000m);
    }

    [Theory]
    [InlineData("1104", 1104, 0)]
    [InlineData("510/1", 510, 1)]
    public void Splits_an_erf_label(string erf, int stand, int portion) =>
        TshwaneRollClient.SplitErf(erf).Should().Be(((int?)stand, portion));

    [Fact]
    public void Tshwane_parcels_are_recognised_by_their_key()
    {
        TshwanePropertyProvider.IsTshwaneParcel("GTSHT0JR027700001104000000").Should().BeTrue();
        TshwanePropertyProvider.IsTshwaneParcel("GJHBT0IR050600001017000000").Should().BeFalse();
    }

    /// <summary>Live: a pin in Waterkloof Ridge → erf 1104 → GV2025 R6 500 000, 3 936 m².</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_pin_in_Waterkloof_Ridge_gets_its_roll_value()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<NationalCadastreProvider>();
        services.AddHttpClient<TshwaneRollClient>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddScoped<TshwanePropertyProvider>();
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<TshwanePropertyProvider>();

        var refs = await provider.ResolveAsync(new ResolveQuery(Lat: -25.7905, Lng: 28.2505));
        refs.Should().ContainSingle().Which.Erf.Should().Be("1104");

        var record = await provider.FetchRecordAsync(refs[0]);
        record.Ref.Municipality.Should().Be("tshwane");
        record.Valuation!.ValueZar.Should().Be(6_500_000m);
        record.Valuation.RollVersion.Should().Be("GV2025");
        record.BestExtentM2.Should().Be(3936);
    }
}
