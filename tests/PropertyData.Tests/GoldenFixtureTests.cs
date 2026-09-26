// ---------------------------------------------------------------------------------------------
// Tests for the Cape Town adapter.
//
//   Unit tests          run offline, always.
//   Integration tests   hit the live City endpoints. Tagged [Trait("Category","Integration")] so
//                       CI can skip them:  dotnet test --filter Category!=Integration
//
// The integration assertions are a GOLDEN FIXTURE recorded 2026-09-26 against 17 Pine Road,
// Claremont. When the City changes a schema or a URL, these fail loudly instead of the app quietly
// producing an empty report.
//
// NuGet: xunit, xunit.runner.visualstudio, FluentAssertions, Microsoft.Extensions.DependencyInjection,
//        Microsoft.Extensions.Logging.Abstractions
// ---------------------------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyData.CapeTown;
using PropertyData.CapeTown.Clients;
using PropertyData.CapeTown.Internal;
using PropertyData.CapeTown.Services;
using PropertyData.Core;
using PropertyData.Core.Models;
using Xunit;

namespace PropertyData.CapeTown.Tests;

public sealed class AddressNormalizerTests
{
    [Theory]
    [InlineData("17 Pine Road, Claremont", 17, "PINE", "ROAD", "CLAREMONT")]
    [InlineData("17 Pine Rd Claremont", 17, "PINE", "ROAD", "CLAREMONT")]
    [InlineData("8 Feldhausen Ave, Claremont, Cape Town", 8, "FELDHAUSEN", "AVENUE", "CLAREMONT")]
    [InlineData("21B Livingstone Road, Claremont", 21, "LIVINGSTONE", "ROAD", "CLAREMONT")]
    [InlineData("54 Keurboom Rd Claremont", 54, "KEURBOOM", "ROAD", "CLAREMONT")]
    [InlineData("2 Alpina Rd, Claremont", 2, "ALPINA", "ROAD", "CLAREMONT")]
    [InlineData("16 Herschel Walk, Claremont", 16, "HERSCHEL", "WALK", "CLAREMONT")]
    public void Parses_addresses(string raw, int no, string street, string type, string suburb)
    {
        var a = AddressNormalizer.Parse(raw);
        a.StreetNo.Should().Be(no);
        a.StreetName.Should().Be(street);          // never "PINE ROAD" — the layer stores the type apart
        a.StreetType.Should().Be(type);
        a.Suburb.Should().Be(suburb);
    }

    [Fact]
    public void Keeps_multiword_street_names()
    {
        var a = AddressNormalizer.Parse("5 Upper Orange Street, Oranjezicht");
        a.StreetName.Should().Be("UPPER ORANGE");
        a.StreetType.Should().Be("STREET");
        a.Suburb.Should().Be("ORANJEZICHT");
    }

    [Fact]
    public void Drops_unit_prefix()
    {
        var a = AddressNormalizer.Parse("Unit 12, 30 Main Road, Rondebosch");
        a.StreetNo.Should().Be(30);
        a.StreetName.Should().Be("MAIN");
    }

    [Fact]
    public void Captures_number_suffix()
    {
        AddressNormalizer.Parse("21B Livingstone Road, Claremont").StreetNoSuffix.Should().Be("B");
    }

    [Theory]
    [InlineData("Claremont")]           // no street number
    [InlineData("")]
    public void Rejects_unusable_input(string raw)
    {
        var act = () => AddressNormalizer.Parse(raw);
        act.Should().Throw<Exception>();
    }
}

public sealed class GeoTests
{
    // The real parcel ring for erf 53927 is ~1085 m². This square is an independent check.
    [Fact]
    public void Geodesic_area_matches_a_known_square()
    {
        // ~100m x ~100m at Cape Town's latitude
        const double lat = -33.98974, lng = 18.470992;
        double dLat = 100 / 110_540.0;
        double dLng = 100 / (111_320.0 * Math.Cos(lat * Math.PI / 180));

        var ring = new Ring(new List<LatLng>
        {
            new(lat, lng), new(lat, lng + dLng), new(lat + dLat, lng + dLng), new(lat + dLat, lng), new(lat, lng)
        });

        Geo.RingAreaM2(ring).Should().BeApproximately(10_000, 60);   // 10 000 m² ±0.6%
    }

    [Fact]
    public void Contains_rejects_points_outside()
    {
        var ring = new Ring(new List<LatLng> { new(0, 0), new(0, 1), new(1, 1), new(1, 0), new(0, 0) });
        Geo.Contains(ring, new LatLng(0.5, 0.5)).Should().BeTrue();
        Geo.Contains(ring, new LatLng(1.5, 0.5)).Should().BeFalse();
    }
}

public sealed class ComparableAnalyzerTests
{
    private static PropertyRecord Subject(double dwelling = 300, double erf = 1085) => new()
    {
        Ref = new PropertyRef("53927", "C0160007000539270000000000", "CCT010812600000", "CAPE TOWN", "CLAREMONT"),
        FormattedAddress = "17 PINE ROAD CLAREMONT",
        ExtentM2Deed = erf,
        DwellingExtentM2 = dwelling,
    };

    // Real rows from the City's sales list for this parcel, including the R0 one.
    private static List<Comparable> RealSample() =>
    [
        Row("CCT000233000000", "8 FELDHAUSEN AVE CLAREMONT",  "55775 CAPE TOWN", 396, 199, "2026-02-05",  9_050_000),
        Row("CCT000233500000", "15 SANATORIUM RD CLAREMONT",   "57059 CAPE TOWN", 496, 222, "2023-04-05",          0),
        Row("CCT000237600000", "32 LAURIER RD CLAREMONT",      "56991 CAPE TOWN", 485, 208, "2025-03-17",  4_000_000),
        Row("CCT000244200000", "8 ANGELINA AVE CLAREMONT",     "56790 CAPE TOWN", 998, 269, "2023-11-07",  7_750_000),
        Row("CCT000244600000", "12B PARRY ROAD CLAREMONT",     "52679 CAPE TOWN", 749, 214, "2024-07-29",  5_450_000),
        Row("SPM011454700000", "16B HERSCHEL WALK CLAREMONT",  "172490 CAPE TOWN", 769, 319, "2025-03-05", 10_400_000),
        Row("CCT000276700000", "2 ALPINA RD CLAREMONT",        "58430 CAPE TOWN", 208,  55, "2024-09-09",    916_114),
    ];

    private static Comparable Row(string @ref, string addr, string desc, double erf, double dwell, string date, decimal price) => new()
    {
        ValuationRef = @ref, Address = addr, RegisteredDescription = desc, Erf = desc.Split(' ')[0],
        ErfExtentM2 = erf, DwellingExtentM2 = dwell, SaleDate = DateOnly.Parse(date), SalePriceZar = price,
    };

    private static SuburbBenchmark Claremont() =>
        new("CLAREMONT", 3000, 500, 220, 5_000_000m, 6_500_000m);

    [Fact]
    public void Excludes_zero_price_transfers()
    {
        var set = ComparableAnalyzer.Analyze(RealSample(), Subject(), Claremont(), new DateOnly(2026, 9, 26));
        set.ExcludedZeroPrice.Should().Be(1);
        set.Included.Should().NotContain(c => c.SalePriceZar == 0);
    }

    [Fact]
    public void Indexes_older_sales_forward()
    {
        var set = ComparableAnalyzer.Analyze(RealSample(), Subject(), Claremont(), new DateOnly(2026, 9, 26));
        var old = set.All.Single(c => c.ValuationRef == "CCT000244200000");   // Nov 2023
        if (old.Included)
            old.IndexedPriceZar.Should().BeGreaterThan(old.SalePriceZar, "a 2023 sale must be adjusted to report date");
    }

    [Fact]
    public void Produces_a_range_not_a_point()
    {
        var set = ComparableAnalyzer.Analyze(RealSample(), Subject(), Claremont(), new DateOnly(2026, 9, 26));
        set.ImpliedValueMidZar.Should().NotBeNull();
        set.ImpliedValueLowZar.Should().BeLessThan(set.ImpliedValueMidZar!.Value + 1);
        set.ImpliedValueHighZar.Should().BeGreaterThan(set.ImpliedValueMidZar!.Value - 1);
    }

    [Fact]
    public void Relaxes_size_filter_rather_than_returning_two_comparables()
    {
        // A 900 m² dwelling has no close peers in the sample; we should still get the minimum set.
        var set = ComparableAnalyzer.Analyze(RealSample(), Subject(dwelling: 900), Claremont(), new DateOnly(2026, 9, 26));
        set.Included.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Never_includes_the_subject_property()
    {
        var rows = RealSample();
        rows.Add(Row("CCT010812600000", "17 PINE ROAD CLAREMONT", "53927 CAPE TOWN", 1085, 300, "2024-01-15", 6_900_000));
        var set = ComparableAnalyzer.Analyze(rows, Subject(), Claremont(), new DateOnly(2026, 9, 26));
        set.Included.Should().NotContain(c => c.ValuationRef == "CCT010812600000");
    }
}

public sealed class SitePlanRendererTests
{
    [Fact]
    public void Renders_a_placeholder_when_there_is_no_boundary()
    {
        var svg = SitePlanRenderer.Render(new PropertyRecord
        {
            Ref = new PropertyRef("1", null, null, "CAPE TOWN", "CLAREMONT"),
            FormattedAddress = "somewhere",
        });
        svg.Should().StartWith("<svg").And.Contain("No cadastral boundary");
    }

    [Fact]
    public void Renders_parcel_and_buildings()
    {
        // A South African server formats decimals with a comma by default.
        CultureInfo.CurrentCulture = new CultureInfo("en-ZA");

        var ring = new Ring(new List<LatLng>
        {
            new(-33.9900, 18.4705), new(-33.9900, 18.4715), new(-33.9895, 18.4715), new(-33.9895, 18.4705), new(-33.9900, 18.4705)
        });
        var building = new Ring(new List<LatLng>
        {
            new(-33.9899, 18.4707), new(-33.9899, 18.4711), new(-33.9897, 18.4711), new(-33.9897, 18.4707), new(-33.9899, 18.4707)
        });

        var svg = SitePlanRenderer.Render(new PropertyRecord
        {
            Ref = new PropertyRef("53927", null, "CCT010812600000", "CAPE TOWN", "CLAREMONT"),
            FormattedAddress = "17 PINE ROAD CLAREMONT",
            Boundary = ring,
            ExtentM2Deed = 1085,
            Buildings = [new BuildingFootprint(345, 7.6, 201312, "Photogrammetry", building)],
        });

        svg.Should().Contain("<path").And.Contain("345 m²").And.Contain("17 PINE ROAD CLAREMONT");
        svg.Should().Contain("7.6 m", "labels use a decimal point whatever the server's culture");
        svg.Should().Contain("City of Cape Town open data", "the drawing must cite its source");
        svg.Should().Contain(">N<", "north arrow");
    }
}

[Trait("Category", "Integration")]
public sealed class CapeTownGoldenFixtureTests
{
    // ---- the fixture, recorded 2026-09-26 -----------------------------------------------------
    private const string Address = "17 Pine Road, Claremont";
    private const string Erf = "53927";
    private const string Sg26 = "C0160007000539270000000000";
    private const string ValuationRef = "CCT010812600000";
    private const double Lat = -33.989740, Lng = 18.470992;
    private const double ExtentDeed = 1085, ExtentGeodesic = 1079;
    // Buildings on the erf itself, from the parcel-polygon query with the centroid filter: a
    // 258 m² roof at 6.5 m and a 68 m² outbuilding. (The 345 m² at 7.6 m first recorded here was
    // a neighbour's building, pulled in by a bounding-box query — the exact trap noted below.)
    private const double DwellingExtent = 300, MainRoof = 258, MainHeight = 6.5;
    private const decimal MunicipalValue = 7_100_000m;

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddCapeTownPropertyData("PropValu-Tests/1.0 (+https://example.co.za; dev@example.co.za)");
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Resolves_address_to_the_right_erf()
    {
        await using var sp = Build();
        var provider = sp.GetRequiredService<IPropertyDataProvider>();

        var refs = await provider.ResolveAsync(new ResolveQuery(Address: Address));

        refs.Should().ContainSingle(r => r.Erf == Erf && r.Sg26 == Sg26);
    }

    [Fact]
    public async Task Resolves_a_coordinate_to_the_same_erf()
    {
        await using var sp = Build();
        var provider = sp.GetRequiredService<IPropertyDataProvider>();

        var refs = await provider.ResolveAsync(new ResolveQuery(Lat: Lat, Lng: Lng));

        refs.Should().Contain(r => r.Erf == Erf);
    }

    [Fact]
    public async Task Fetches_the_full_record()
    {
        await using var sp = Build();
        var provider = sp.GetRequiredService<IPropertyDataProvider>();

        // No SG26 on the way in: that is how the API asks (by erf and suburb).
        var rec = await provider.FetchRecordAsync(
            new PropertyRef(Erf, null, null, "CAPE TOWN", "CLAREMONT"));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        rec.Ref.ValuationRef.Should().Be(ValuationRef);
        rec.FormattedAddress.Should().ContainEquivalentOf("17 PINE ROAD CLAREMONT");
        rec.Location!.Lat.Should().BeApproximately(Lat, 0.0005);
        rec.Location!.Lng.Should().BeApproximately(Lng, 0.0005);

        rec.ExtentM2Deed.Should().BeApproximately(ExtentDeed, 1);
        rec.ExtentM2Geodesic.Should().BeApproximately(ExtentGeodesic, 15);

        // The anti-assertion: Web Mercator Shape__Area for this parcel is 1569 m². If the extent
        // ever comes back near that, someone has wired the wrong field in.
        rec.BestExtentM2.Should().BeLessThan(1300, "Shape__Area must never be used as the extent");

        rec.ZoningDescription.Should().ContainEquivalentOf("Residential");
        rec.ZoningCode.Should().NotBeNullOrEmpty("the zoning layer is joined on the parcel's SG26");
        rec.Ward.Should().Be("59");
        rec.LegalStatus.Should().Be("Registered");

        rec.Valuation!.ValueZar.Should().Be(MunicipalValue);
        rec.Valuation!.Category.Should().Be("RESIDENTIAL");
        rec.Valuation!.RollVersion.Should().Be("GV2025");

        rec.Buildings.Should().NotBeEmpty();
        rec.Buildings[0].RoofM2.Should().BeApproximately(MainRoof, 8);
        rec.Buildings[0].HeightM.Should().BeApproximately(MainHeight, 0.2);
        rec.Buildings[0].CapturedYyyyMm.Should().Be(201312);
        rec.Buildings.Count.Should().BeLessThan(4, "an envelope query pulls in the neighbours' buildings");

        rec.DwellingExtentM2.Should().BeApproximately(DwellingExtent, 1, "roll attribute page");

        rec.Comparables!.All.Count.Should().BeGreaterThan(1500, "the City lists ~2237 area sales here");
        rec.Comparables!.Included.Should().NotBeEmpty();
        rec.Comparables!.ExcludedZeroPrice.Should().BeGreaterThan(0, "R0 transfers exist in this set");
        rec.Comparables!.MedianPricePerDwellingM2.Should().BeGreaterThan(5_000);

        rec.Provenance.Should().Contain(p => p.Field == "comparables");
    }

    [Theory]
    [InlineData("17 pine")]
    [InlineData("17 Pine Rd Clar")]
    [InlineData("17 pine road, claremont")]
    [InlineData("17 pine r")]                 // half-typed "road"
    public async Task Suggests_the_erf_while_typing(string typed)
    {
        await using var sp = Build();
        var spatial = sp.GetRequiredService<CapeTownSpatialClient>();

        var found = await spatial.SuggestAsync(typed);

        found.Count.Should().BeInRange(1, 8, "a suggestion list, not the whole register");
        found.Should().Contain(s => s.Erf == Erf && s.Suburb == "CLAREMONT" && s.StreetName == "PINE");
        var hit = found.First(s => s.Erf == Erf);
        hit.StreetNumber.Should().Be(17);
        hit.Location!.Lat.Should().BeApproximately(Lat, 0.0005);
    }

    [Fact]
    public async Task Suggests_streets_before_a_number_is_typed()
    {
        await using var sp = Build();
        var spatial = sp.GetRequiredService<CapeTownSpatialClient>();

        var found = await spatial.SuggestAsync("pine rd clare");

        found.Should().Contain(s => s.StreetName == "PINE" && s.Suburb == "CLAREMONT" && s.Erf == null);
        found.Should().NotContain(s => s.StreetName == "PINETREE", "a typed street type (Rd) narrows the match");
    }

    [Fact]
    public async Task Sales_list_arrives_in_one_request()
    {
        await using var sp = Build();
        var roll = sp.GetRequiredService<CapeTownRollClient>();

        var sales = await roll.GetAreaSalesAsync(ValuationRef);

        // DataTables paginates client-side, so the single GET carries every row.
        sales.Count.Should().BeGreaterThan(1500);
        sales.Should().Contain(s => s.SalePriceZar == 0, "unfiltered list includes non-arm's-length transfers");
        sales.Should().OnlyContain(s => s.SaleDate.Year >= 1990);
    }

    [Fact]
    public async Task Site_plan_renders_from_live_geometry()
    {
        await using var sp = Build();
        var provider = sp.GetRequiredService<IPropertyDataProvider>();

        var rec = await provider.FetchRecordAsync(
            new PropertyRef(Erf, Sg26, null, "CAPE TOWN", "CLAREMONT"),
            new RecordOptions(IncludeComparables: false, IncludeDwellingExtent: false));

        var svg = SitePlanRenderer.Render(rec);

        svg.Should().StartWith("<svg").And.EndWith("</svg>");
        svg.Should().Contain("Erf 53927");
        svg.Length.Should().BeGreaterThan(1000);
    }
}
