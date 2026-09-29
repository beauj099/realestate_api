using PropertyData.Geocoding;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;
using Xunit;

namespace PropertyData.Tests;

public class AddressSearchTests
{
    [Theory]
    [InlineData("10 bosman", null, "10", "bosman")]
    [InlineData("Unit 5, 12 Main Road", "5", "12", "Main Road")]
    [InlineData("unit 5 12 main", "5", "12", "main")]
    [InlineData("5/12 Main Road", "5", "12", "Main Road")]
    [InlineData("Flat 3B 7 Kerk Str", "3B", "7", "Kerk Str")]
    [InlineData("bosm", null, null, "bosm")]
    [InlineData("12A Pine Rd", null, "12A", "Pine Rd")]
    [InlineData("north road", null, null, "north road")]
    [InlineData("10", null, null, "10")]
    public void Reads_unit_number_and_street(string text, string? unit, string? number, string rest)
    {
        var typed = AddressSearch.Parse(text);
        Assert.Equal(unit, typed.Unit);
        Assert.Equal(number, typed.Number);
        Assert.Equal(rest, typed.Rest);
    }

    [Fact]
    public void Photon_keeps_streets_houses_and_areas_but_not_landmarks()
    {
        var places = PhotonClient.Parse(File.ReadAllText("photon_bosm.json"));
        Assert.DoesNotContain(places, p => p.Name == "Bosmansdam High School");
        Assert.Contains(places, p => p.Kind == PhotonKind.Street && p.Name == "Bosmansdam Road");
        Assert.Contains(places, p => p.Kind == PhotonKind.Area && p.Name == "Bosmont");
        // A named residential area is a complex or estate; a farm is not an area at all.
        Assert.Contains(places, p => p.Kind == PhotonKind.Estate && p.Name == "Bosman House");
        Assert.DoesNotContain(places, p => p.Name == "Boesmandrink");
        Assert.All(places, p => Assert.NotEqual(0, p.Lat));
    }

    [Fact]
    public void Photon_names_the_suburb_not_the_ward_and_the_metro_as_the_city()
    {
        var bosman = PhotonClient.Parse(File.ReadAllText("photon_10_bosman_str.json"))
            .First(p => p.Name == "Bosman Street");
        Assert.DoesNotContain("Ward", bosman.Suburb);
        Assert.Equal("Cape Town", bosman.CityName);
    }

    private static AddressSuggestionDto S(string kind, string title, string street = "", string suburb = "Strand",
        string? number = null, bool verified = false) =>
        new("", number, street, suburb, "Cape Town", "Western Cape", "South Africa", null, null, null, null, "",
            Kind: kind, Title: title, Subtitle: $"{suburb}, Cape Town", NumberVerified: verified);

    [Fact]
    public void Streets_and_areas_are_ranked_together_not_areas_first()
    {
        var typed = AddressSearch.Parse("bosm");
        var list = AddressSearch.Order(new[]
        {
            S("area", "Bosmont", suburb: "Bosmont"),
            S("area", "Boesmansrivier", suburb: "Boesmansrivier"),
            S("street", "Bosman Street", "Bosman Street"),
            S("street", "Bosmansdam Road", "Bosmansdam Road", "Milnerton"),
        }.Select(s => s with { Rank = AddressSearch.Rank(typed, s, null, null) }), 6);

        // Street, area, street: mixed, not all areas first.
        Assert.Equal(["street", "area", "street"], list.Take(3).Select(s => s.Kind));
        // "Boesmansrivier" does not start with "bosm": it is dropped.
        Assert.DoesNotContain(list, s => s.Title == "Boesmansrivier");
    }

    [Fact]
    public void An_area_typed_in_full_leads()
    {
        var typed = AddressSearch.Parse("heldervue");
        var list = AddressSearch.Order(new[]
        {
            S("street", "Heldervue Street", "Heldervue Street", "Kraaifontein"),
            S("area", "Heldervue", suburb: "Heldervue"),
        }.Select(s => s with { Rank = AddressSearch.Rank(typed, s, null, null) }), 6);
        Assert.Equal("area", list[0].Kind);
    }

    [Fact]
    public void A_town_typed_at_the_end_counts()
    {
        var typed = AddressSearch.Parse("12 main road herm");
        var hermanus = S("street", "12 Main Road", "Main Road", "Hermanus", "12");
        var kalkBay = S("property", "12 Main Road", "Main Road", "Kalk Bay", "12", verified: true);
        Assert.True(AddressSearch.Rank(typed, hermanus, null, null) > AddressSearch.Rank(typed, kalkBay, null, null));
    }

    [Fact]
    public void A_typed_number_puts_the_numbered_erf_first_and_drops_areas()
    {
        var typed = AddressSearch.Parse("10 bosman");
        var list = AddressSearch.Order(new[]
        {
            S("area", "Bosmont", suburb: "Bosmont"),
            S("street", "10 Bosman Street", "Bosman Street", number: "10"),
            S("property", "10 Bosman Street", "Bosman Street", "Eersterivier", "10", verified: true),
        }.Select(s => s with { Rank = AddressSearch.Rank(typed, s, null, null) }), 6);

        Assert.Equal("property", list[0].Kind);
        // Typing a number means an address: the area is not offered.
        Assert.DoesNotContain(list, s => s.Kind == "area");
    }

    [Fact]
    public void The_same_address_from_both_sources_is_listed_once()
    {
        var city = S("property", "10 Bosman Street", "Bosman Street", number: "10", verified: true) with { Rank = 110 };
        var osm = S("street", "10 Bosman Street", "Bosman Street", number: "10") with { Rank = 90 };
        var list = AddressSearch.Order([osm, city], 6);
        Assert.Single(list);
        Assert.Equal("property", list[0].Kind);
    }

    [Fact]
    public void No_more_than_two_areas()
    {
        var typed = AddressSearch.Parse("sand");
        var areas = new[] { "Sandton", "Sandown", "Sandringham", "Sandbaai" }
            .Select(n => S("area", n, suburb: n))
            .Select(s => s with { Rank = AddressSearch.Rank(typed, s, null, null) });
        Assert.Equal(2, AddressSearch.Order(areas, 6).Count);
    }

    [Theory]
    [InlineData("florida rd", "florida road")]
    [InlineData("10 Bosman Str strand", "10 Bosman street strand")]
    [InlineData("unit 5, 12 main rd", "12 main road")]
    public void OpenStreetMap_gets_street_types_written_out(string text, string expected) =>
        Assert.Equal(expected, AddressSearch.Expanded(AddressSearch.Parse(text)));

    [Fact]
    public void Abbreviated_street_types_match()
    {
        var typed = AddressSearch.Parse("bosman str");
        var street = S("street", "Bosman Street", "Bosman Street");
        var other = S("street", "Bosman Avenue", "Bosman Avenue", "Llandudno");
        Assert.True(AddressSearch.Rank(typed, street, null, null) > AddressSearch.Rank(typed, other, null, null));
    }
}
