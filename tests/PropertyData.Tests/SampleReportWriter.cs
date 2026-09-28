using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyData.Core;
using PropertyData.Listings;
using PropertyData.National;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Data;
using RealEstateApi.Infrastructure.PropertyData;
using RealEstateApi.Infrastructure.Repositories;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>
/// Writes a report's API responses to a folder, for building a sample report pack in the app
/// without signing in. Runs only with RW_SAMPLE_OUT set (and the live services reachable):
///   RW_SAMPLE_OUT=C:\temp\bosman dotnet test --filter FullyQualifiedName~SampleReportWriter
/// </summary>
public sealed class SampleReportWriter
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Write_10_Bosman_Street()
    {
        var output = Environment.GetEnvironmentVariable("RW_SAMPLE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddCapeTownPropertyData("RealWorth-Tests/1.0");
        services.AddScoped<global::PropertyData.Johannesburg.JohannesburgPropertyProvider>();
        services.AddScoped<IPropertyDataProvider>(sp => sp.GetRequiredService<global::PropertyData.Johannesburg.JohannesburgPropertyProvider>());
        services.AddScoped<NationalCadastreProvider>();
        services.AddScoped<IPropertyDataProvider>(sp => sp.GetRequiredService<NationalCadastreProvider>());
        services.AddSingleton<IOptions<ImageryOptions>>(Options.Create(new ImageryOptions()));
        services.AddSingleton<ImageryLinkBuilder>();
        // No database here: agent-reported sales come back empty (the service tolerates it).
        services.AddSingleton(new DbConnectionFactory("Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True"));
        services.AddScoped<AgentComparableRepository>();
        services.AddScoped<AgentComparableService>();
        services.AddScoped<PropertyReportService>();
        services.AddHttpClient<Property24Client>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddScoped<ForSaleListingsService>();
        services.AddHttpClient(AreaDetailsService.HttpClientName, c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddSingleton<IWebHostEnvironment>(new Env(ApiRoot()));
        services.AddScoped<AreaDetailsService>();
        await using var sp = services.BuildServiceProvider();

        var reports = sp.GetRequiredService<PropertyReportService>();
        var report = await reports.GetReportAsync("coct", "4429", "STRAND", null, true, null, default);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, json));
        await File.WriteAllTextAsync(Path.Combine(output, "site_plan.svg"),
            await reports.GetSitePlanSvgAsync("coct", "4429", "STRAND", null, 900, 650, default));
        var area = await sp.GetRequiredService<AreaDetailsService>().GetAsync(report.Lat!.Value, report.Lng!.Value, default);
        await File.WriteAllTextAsync(Path.Combine(output, "area.json"), JsonSerializer.Serialize(area, json));
        var forSale = await sp.GetRequiredService<ForSaleListingsService>()
            .FindAsync("coct", "STRAND", "THE STRAND", 7819, 5, 390, 1151, 3, default);
        await File.WriteAllTextAsync(Path.Combine(output, "for_sale.json"), JsonSerializer.Serialize(forSale, json));
    }

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "realestate_api.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private sealed class Env(string root) : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "realestate_api";
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
