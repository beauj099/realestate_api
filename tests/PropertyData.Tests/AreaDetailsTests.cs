using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RealEstateApi.Application.Services;
using Xunit;
using Xunit.Abstractions;

namespace PropertyData.CapeTown.Tests;

public sealed class AreaDetailsTests(ITestOutputHelper output)
{
    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "realestate_api.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("API root not found");
    }

    [Fact]
    public void Crime_figures_come_from_the_precinct_the_boundaries_name()
    {
        var data = CrimeData.Load(ApiRoot());
        var crime = AreaDetailsService.CrimeFor("STRAND", data)!;   // the boundaries spell it in capitals
        crime.Precinct.Should().Be("Strand");
        crime.Population.Should().BeGreaterThan(50_000);
        crime.Crimes.Should().Contain(c => c.Crime == "Residential burglary" && c.Count > 0);
        crime.Band.Should().NotBeNull();
        data.Band(0).Should().Be("Very low");
        data.Band(1_000_000).Should().Be("Very high");
    }

    [Fact]
    public void The_median_income_band_holds_the_middle_household() =>
        AreaDetailsService.MedianBand([new("No income", 20), new("R1 - R4 800", 20), new("R4 801 - R9 600", 30), new("More", 30)])
            .Should().Be("R4 801 - R9 600");

    [Fact]
    public void Climate_is_summarised_from_daily_values()
    {
        var days = Enumerable.Range(0, 730).Select(i => new DateOnly(2024, 1, 1).AddDays(i)).ToList();
        var summary = AreaDetailsService.SummariseClimate(
            days.ToDictionary(d => d, d => 18.0), days.ToDictionary(d => d, d => d.Month == 2 ? 28.0 : 22.0),
            days.ToDictionary(d => d, d => d.Month == 7 ? 6.0 : 12.0), days.ToDictionary(d => d, _ => 2.0), "2024–2025")!;
        summary.HottestMonth.Should().Be("February");
        summary.ColdestMonth.Should().Be("July");
        summary.AnnualRainMm.Should().BeApproximately(730, 2);
    }

    /// <summary>Live: the Strand, next to 10 Bosman Street.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Area_details_for_the_strand()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddHttpClient(AreaDetailsService.HttpClientName, c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddSingleton<IWebHostEnvironment>(new TestEnv(ApiRoot()));
        services.AddScoped<AreaDetailsService>();
        await using var sp = services.BuildServiceProvider();

        var area = await sp.GetRequiredService<AreaDetailsService>().GetAsync(-34.10041, 18.833647, default);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(area, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        area.Climate.Should().NotBeNull();
        area.Population!.SubPlace.Should().Be("Langewacht");
        area.Income!.Bands.Should().NotBeEmpty();
        area.Crime!.Precinct.Should().Be("Strand");
    }

    private sealed class TestEnv(string root) : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "realestate_api";
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
