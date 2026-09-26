using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.Core;
using PropertyData.Johannesburg;
using PropertyData.National;
using PropertyData.Tshwane;
using RealEstateApi.Application.Services;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>Live: the monthly check passes against today's municipal services.</summary>
[Trait("Category", "Integration")]
public sealed class DataSourceHealthTests
{
    [Fact]
    public async Task Every_source_passes_its_check()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<IPropertyDataProvider, JohannesburgPropertyProvider>();
        services.AddScoped<NationalCadastreProvider>();
        services.AddHttpClient<TshwaneRollClient>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddScoped<IPropertyDataProvider, TshwanePropertyProvider>();
        services.AddScoped<DataSourceHealthService>();
        await using var sp = services.BuildServiceProvider();

        var results = await sp.GetRequiredService<DataSourceHealthService>().RunAsync(CancellationToken.None);

        results.Select(r => r.Source).Should().HaveCount(3);
        results.Where(r => !r.Ok).Should().BeEmpty();
    }
}
