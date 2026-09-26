using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.CapeTown;
using PropertyData.Core;
using PropertyData.Core.Models;
using PropertyData.Johannesburg;
using PropertyData.National;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>
/// Live golden fixture for Johannesburg and the national cadastre, recorded 2026-09-26 against
/// 10 Thirteenth Street, Parkhurst (erf 1106): 496 m², Residential 1, GV2023 value R2 900 000.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JohannesburgFixtureTests
{
    private const string Erf = "1106";
    private const string Town = "PARKHURST";
    private const double Lat = -26.137444, Lng = 28.012148;

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<JohannesburgPropertyProvider>();
        services.AddScoped<NationalCadastreProvider>();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("10 Thirteenth Street, Parkhurst")]
    [InlineData("10 thirteenth st parkhurst")]
    public async Task Resolves_an_address_to_the_stand(string address)
    {
        await using var sp = Build();
        var refs = await sp.GetRequiredService<JohannesburgPropertyProvider>().ResolveAsync(new ResolveQuery(Address: address));
        refs.Should().ContainSingle(r => r.Erf == Erf && r.Suburb == Town && r.Municipality == "coj");
    }

    [Fact]
    public async Task Resolves_a_point_to_the_stand()
    {
        await using var sp = Build();
        var refs = await sp.GetRequiredService<JohannesburgPropertyProvider>().ResolveAsync(new ResolveQuery(Lat: Lat, Lng: Lng));
        refs.Should().Contain(r => r.Erf == Erf && r.Suburb == Town);
    }

    [Fact]
    public async Task Fetches_value_zoning_and_current_comparables()
    {
        await using var sp = Build();
        var rec = await sp.GetRequiredService<JohannesburgPropertyProvider>()
            .FetchRecordAsync(new PropertyRef(Erf, null, null, "", Town, "coj"));

        using var _ = new FluentAssertions.Execution.AssertionScope();
        rec.FormattedAddress.Should().Be("10 THIRTEENTH STREET PARKHURST");
        rec.ExtentM2Deed.Should().BeApproximately(496, 1);
        rec.ExtentM2Geodesic.Should().BeApproximately(496, 25);
        rec.ZoningDescription.Should().Be("Residential 1");
        rec.Valuation!.ValueZar.Should().BeGreaterThan(1_000_000);
        rec.Valuation!.RollVersion.Should().Be("GV2023");
        rec.Valuation!.AsAt.Should().Be(new DateOnly(2022, 7, 1), "GV2023 values as at 1 July 2022");
        rec.DataSource.Should().Contain("Johannesburg");

        rec.Comparables!.All.Count.Should().BeGreaterThan(50, "hundreds of stands nearby sold in four years");
        rec.Comparables!.All.Should().OnlyContain(c => c.SaleDate >= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-4));
        rec.Comparables!.All.Should().Contain(c => c.SaleDate.Year >= 2025, "sales here are current, not frozen in 2022");
        rec.Comparables!.All.Should().NotContain(c => c.Erf == Erf && c.Address.Contains("THIRTEENTH"), "not the subject itself");
        rec.Comparables!.Included.Count.Should().BeGreaterThanOrEqualTo(3);
        rec.Comparables!.ImpliedValueMidZar.Should().BeGreaterThan(1_000_000);
    }

    [Fact]
    public async Task Suggests_stands_while_typing()
    {
        await using var sp = Build();
        var found = await sp.GetRequiredService<JohannesburgPropertyProvider>().SuggestAsync("10 thirteenth st park");
        found.Should().Contain(s => s.Erf == Erf && s.Suburb == Town && s.StreetNumber == 10);
    }

    [Fact]
    public async Task National_cadastre_finds_the_same_erf_from_a_point()
    {
        await using var sp = Build();
        var national = sp.GetRequiredService<NationalCadastreProvider>();

        var refs = await national.ResolveAsync(new ResolveQuery(Lat: Lat, Lng: Lng));
        refs.Should().ContainSingle(r => r.Erf == Erf && r.Suburb == Town && r.Sg26 != null);

        var rec = await national.FetchRecordAsync(refs[0]);
        rec.ExtentM2Geodesic.Should().BeInRange(450, 600);
        rec.Valuation.Should().BeNull("the national cadastre has no values");
        rec.DataSource.Should().Contain("Surveyor-General");
    }
}
