using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.Listings;
using RealEstateApi.Application.Services;
using Xunit;
using Xunit.Abstractions;

namespace PropertyData.CapeTown.Tests;

public sealed class Property24Tests(ITestOutputHelper output)
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    [Fact]
    public void Reads_listing_tiles()
    {
        var listings = Property24Client.ParseListings(Fixture("p24_strand_north.html"));
        listings.Should().HaveCountGreaterThanOrEqualTo(3);
        var marais = listings.Single(l => l.ListingNumber == "117574306");
        marais.PriceZar.Should().Be(5_195_000m);
        marais.Title.Should().Be("5 Bedroom House");
        marais.Address.Should().Be("99 Marais St");
        marais.Bedrooms.Should().Be(5);
        marais.Bathrooms.Should().Be(4);
        marais.ErfM2.Should().Be(500);
        marais.Url.Should().Be("https://www.property24.com/for-sale/strand-north/strand/western-cape/7819/117574306");
        marais.ImageUrl.Should().StartWith("https://images.prop24.com/");
    }

    [Fact]
    public void Reads_the_listing_date_and_sizes_from_a_listing_page() =>
        Property24Client.ParseDetailsText("Listing Date 29 August 2026 Erf Size 500 m² Floor Size 300 m² Rates and Taxes R 1 500")
            .Should().Be((new DateOnly(2026, 8, 29), 300d, 500d));

    [Fact]
    public void Matches_the_citys_suburb_to_property24s_finer_ones()
    {
        var all = Property24Client.ParseSuburbs("""
            <loc>https://www.property24.com/houses-for-sale/strand-north/strand/western-cape/7819</loc>
            <loc>https://www.property24.com/houses-for-sale/strand-central/strand/western-cape/7816</loc>
            <loc>https://www.property24.com/houses-for-sale/strand-south/strand/western-cape/10438</loc>
            <loc>https://www.property24.com/houses-for-sale/heldervue/somerset-west/western-cape/8966</loc>
            <loc>https://www.property24.com/houses-for-sale/st-georges-strand/gqeberha/eastern-cape/6954</loc>
            """);
        Property24Client.Match(all, "HELDERVUE", "SOMERSET WEST", "western-cape").Select(s => s.Id).Should().Equal(8966);
        Property24Client.Match(all, "STRAND", "THE STRAND", "western-cape").Select(s => s.Id).Should().Equal(7816, 7819, 10438);
    }

    [Fact]
    public void Ranks_the_most_alike_first()
    {
        P24Listing L(string n, int beds, double floor, decimal? price = 1m) =>
            new(n, n, price, "House", null, null, null, beds, 2, 2, floor, null, null);
        var ranked = ForSaleListingsService.Rank([L("a", 3, 150), L("b", 5, 320), L("c", 5, 900), L("d", 5, 330, null)], 5, 310, null);
        ranked.Select(l => l.ListingNumber).Should().Equal("b", "c", "a", "d");
    }

    /// <summary>Live: 5-bedroom homes like 10 Bosman Street in Strand North.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Finds_similar_homes_for_sale_in_strand_north()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddHttpClient<Property24Client>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("RealWorth-Tests/1.0"));
        services.AddScoped<ForSaleListingsService>();
        await using var sp = services.BuildServiceProvider();

        var result = await sp.GetRequiredService<ForSaleListingsService>()
            .FindAsync("coct", "STRAND", "THE STRAND", suburbId: 7819, bedrooms: 5, floorM2: 390, erfM2: 1151, max: 3, default);
        foreach (var l in result.Listings)
            output.WriteLine($"{l.ListedOn} R{l.PriceZar:N0} {l.Title} {l.Bedrooms}bd {l.FloorM2}m² {l.Address} {l.Url}");
        result.Listings.Should().NotBeEmpty().And.OnlyContain(l => l.Url.StartsWith("https://www.property24.com/"));
    }
}
