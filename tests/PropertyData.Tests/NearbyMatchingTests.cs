using PropertyData.Core.Models;
using PropertyData.Listings;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.Repositories;
using Xunit;

namespace PropertyData.Tests;

/// <summary>
/// Suburb names differ between sources (the City's "Lynn's View" is partly Property24's
/// "Steynsrust"; a map's "Die Vlakte" is the City's "Strand"), so nearby things are matched by
/// location and neighbouring suburbs, not only by name.
/// </summary>
public class NearbyMatchingTests
{
    [Fact]
    public void Reads_Property24s_surrounding_suburbs()
    {
        const string html = """
            <input class="js_wasUnchecked p24_checkbox js-p24-area" type="checkbox" name="surroundingAreas" value="8966"  />
            <img src="/x.svg" /><a class="js_areaCheckedToggle"><label>Heldervue <span>(15)</span></label></a>
            <input class="js_wasUnchecked p24_checkbox js-p24-area" type="checkbox" name="surroundingAreas" value="9006"  />
            <a class="js_areaCheckedToggle"><label>Steynsrust <span>(6)</span></label></a>
            """;
        var n = Property24Client.ParseSurrounding(html);
        Assert.Equal([new P24Neighbour(8966, "Heldervue", 15), new P24Neighbour(9006, "Steynsrust", 6)], n);
    }

    [Fact]
    public void Reads_a_listings_map_position_but_not_the_country_centre()
    {
        Assert.Equal((-34.061438, 18.814422),
            Property24Client.ParseLocation("""{"latitude":-34.061438,"longitude":18.814422}"""));
        Assert.Equal((null, null),
            Property24Client.ParseLocation("""{"Latitude":-30.969313,"Longitude":22.937506}"""));
        Assert.Equal((null, null), Property24Client.ParseLocation("<html>no map</html>"));
    }

    private static P24Listing L(string number, int beds, double floor) =>
        new(number, $"https://www.property24.com/for-sale/x/{number}", 5_000_000, "House", null, null, null,
            beds, 2, 2, floor, 500, null);

    [Fact]
    public void Keeps_the_nearest_alike_homes_whatever_their_suburb()
    {
        var listings = new List<(P24Listing, double?)>
        {
            (L("far-twin", 3, 200), 9_000),     // identical, but 9 km away
            (L("steynsrust", 3, 210), 600),     // next door, filed under another suburb name
            (L("heldervue", 4, 230), 1_400),
            (L("unplaced", 3, 200), null),
        };
        var chosen = ForSaleListingsService.NearestAlike(listings, 3, 200, null, 2);
        Assert.Equal(["steynsrust", "heldervue"], chosen.Select(c => c.Listing.ListingNumber));
    }

    [Fact]
    public void A_town_typed_after_the_street_ranks_the_right_erf_first()
    {
        IReadOnlyList<PropertyRef> refs =
        [
            new("1414-RE", null, null, "HOUT BAY", "LLANDUDNO") { Municipality = "coct" },
            new("1846", null, null, "KUILS RIVER (S)", "SONEIKE I") { Municipality = "coct" },
            new("4429", null, null, "THE STRAND", "STRAND") { Municipality = "coct" },
        ];
        var ranked = PropertyReportService.ByPlacesTyped(refs, "10 Bosman Street, Die Vlakte, Strand");
        Assert.Equal("4429", ranked[0].Erf);
    }

    [Fact]
    public void The_near_box_covers_the_radius()
    {
        var (latMin, latMax, lngMin, lngMax) = AgentComparableRepository.NearBox(-34.05, 18.82, 1500);
        Assert.InRange(latMax!.Value - latMin!.Value, 0.026, 0.028);
        Assert.True(lngMax - lngMin > latMax - latMin);   // degrees of longitude are shorter here
        Assert.Equal((null, null, null, null), AgentComparableRepository.NearBox(null, null, 1500));
    }
}
