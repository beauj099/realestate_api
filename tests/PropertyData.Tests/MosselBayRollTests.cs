using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.MosselBay;
using PropertyData.National;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>
/// Mossel Bay's online roll. The offline tests read real responses (2026-09-26) with the owner
/// cells removed.
/// </summary>
public sealed class MosselBayRollTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    [Fact]
    public void Reads_the_results_by_header_name()
    {
        var rows = NdkRollClient.Parse(Fixture("mosselbay_roll.html"));
        rows.Should().HaveCount(3);
        var hartenbos = rows.Single(r => r.Erf == 207);
        hartenbos.Township.Should().Be("HARTENBOS");
        hartenbos.Address.Should().Be("28 KAAP DE GOEDEHOOP AVE");
        hartenbos.ExtentM2.Should().Be(40025);
        hartenbos.MarketValueZar.Should().Be(21_175_000m);
    }

    [Fact]
    public void R0_is_a_placeholder_not_a_price()
    {
        NdkRollClient.Money("R 0.00").Should().BeNull();
        NdkRollClient.Parse(Fixture("mosselbay_roll.html")).Single(r => r.Erf == 6474).MarketValueZar.Should().BeNull();
    }

    [Fact]
    public void No_match_is_an_empty_list() =>
        NdkRollClient.Parse(Fixture("mosselbay_none.html")).Should().BeEmpty();

    [Theory]
    [InlineData("5,567 m²", 5567)]
    [InlineData("12.5 ha", 125000)]
    public void Reads_extents(string text, double m2) => NdkRollClient.Extent(text).Should().Be(m2);

    [Fact]
    public void Mossel_Bay_parcels_are_recognised_by_their_key()
    {
        MosselBayPropertyProvider.IsMosselBayParcel("W043C051000400000207000000").Should().BeTrue();
        MosselBayPropertyProvider.IsMosselBayParcel("GTSHT0JR027700001104000000").Should().BeFalse();
    }

    /// <summary>Live: a pin in Hartenbos → erf 207 → R21 175 000, 40 025 m², with its street address.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task A_pin_in_Hartenbos_gets_its_roll_value()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<NationalCadastreProvider>();
        services.AddHttpClient<NdkRollClient>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddScoped<MosselBayPropertyProvider>();
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<MosselBayPropertyProvider>();

        var refs = await provider.ResolveAsync(new ResolveQuery(Lat: -34.1260, Lng: 22.1110));
        refs.Should().ContainSingle().Which.Erf.Should().Be("207");

        var record = await provider.FetchRecordAsync(refs[0]);
        record.Ref.Municipality.Should().Be("mosselbay");
        record.Valuation!.ValueZar.Should().Be(21_175_000m);
        record.BestExtentM2.Should().Be(40025);
        record.FormattedAddress.Should().Be("28 KAAP DE GOEDEHOOP AVE HARTENBOS");
    }
}
