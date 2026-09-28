using FluentAssertions;
using PropertyData.RollBooks;
using RealEstateApi.Infrastructure.Repositories;
using Xunit;

namespace PropertyData.CapeTown.Tests;

/// <summary>
/// PenSoft roll books, read from real Drakenstein GV2024 pages: the Paarl cover and page 3 (land
/// in hectares, consolidated groups), and the whole Bainskloof Pass book.
/// </summary>
public sealed class RollBookTests
{
    private static RollBookResult Parse(string name)
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, name));
        return RollBookParser.Parse(file);
    }

    [Fact]
    public void Reads_the_area_and_the_date_of_valuation_from_the_book()
    {
        var book = Parse("rollbook_paarl_p1_p3.pdf");
        book.Area.Should().Be("Paarl");
        book.DateOfValuation.Should().Be(new DateOnly(2024, 7, 1));
        book.Rejected.Should().BeEmpty();
    }

    [Fact]
    public void Joins_space_separated_thousands_and_converts_hectares()
    {
        var rows = Parse("rollbook_paarl_p1_p3.pdf").Rows;
        // "2 793.2484 Ha", "30 575 000": the first digit group sits left of the Extent header.
        var erf1 = rows.Single(r => r.Erf == 1);
        erf1.ExtentM2.Should().Be(27_932_484);
        erf1.ValueZar.Should().Be(30_575_000m);
        // "1 561 m²", "4 545 000", address with the street number last.
        var erf107 = rows.Single(r => r.Erf == 107);
        erf107.ExtentM2.Should().Be(1561);
        erf107.ValueZar.Should().Be(4_545_000m);
        erf107.Address.Should().Be("150 Retiefstraat");
        erf107.Category.Should().Be("RES");
    }

    [Fact]
    public void Reads_consolidated_groups()
    {
        var rows = Parse("rollbook_paarl_p1_p3.pdf").Rows;
        var head = rows.Single(r => r.Erf == 5 && r.IsGroupHead);
        head.ValueZar.Should().Be(34_800_000m);
        head.Particulars.Should().Be("Including :- Paarl 5, Paarl 7, Paarl 9");
        var member = rows.Single(r => r.Erf == 7);
        member.GroupHeadErf.Should().Be(5);
        member.ValueZar.Should().Be(0);
    }

    [Fact]
    public void A_whole_small_book_passes_its_sanity_checks()
    {
        var book = Parse("rollbook_bainskloof.pdf");
        book.Area.Should().Be("Bainskloof Pass");
        book.Rows.Should().HaveCount(37);
        book.Sanity.LooksSane.Should().BeTrue(string.Join("; ", book.Sanity.Warnings));
        // "Bainskloof 33 … 803 m²": 33 is the street number (address column), not a thousands group
        // of the extent — plain text extraction runs them together into "33 803 m²".
        book.Rows.Should().Contain(r => r.Erf == 1 && r.Address == "33 Bainskloof" && r.ExtentM2 == 803 && r.ValueZar == 1_575_000m);
    }

    [Theory]
    [InlineData("Retiefstraat 17-19", "17-19 Retiefstraat")]
    [InlineData("Irene 0", "Irene")]
    [InlineData("Paarlberg Veld Blommetuin + Huis 0", "Paarlberg Veld Blommetuin + Huis")]
    [InlineData("Hoofstraat", "Hoofstraat")]
    public void Puts_the_street_number_first(string raw, string tidy) => RollBookParser.TidyAddress(raw).Should().Be(tidy);

    private static RollBookEntryRow Row(int erf, decimal? value, bool head = false, int? headErf = null,
        string? scheme = null, string? particulars = null, decimal extent = 1000) => new()
    {
        Erf = erf, IsGroupHead = head, GroupHeadErf = headErf, ValuedUnder = scheme, ValueZar = value,
        ExtentM2 = extent, Category = "RES", Particulars = particulars, RollVersion = "GV2024",
        DateOfValuation = new DateTime(2024, 7, 1),
    };

    [Fact]
    public void A_group_head_is_valued_for_the_group_and_a_member_points_to_it()
    {
        var rows = new[]
        {
            Row(5, 34_800_000m, head: true, particulars: "Including :- Paarl 5, Paarl 7, Paarl 9", extent: 1_465_753),
            Row(5, 0, headErf: 5, extent: 662_266),
            Row(7, 0, headErf: 5, extent: 780_872),
        };

        var five = RollBookLookup.Find(rows, 5, 0)!;
        five.ValueZar.Should().Be(34_800_000m);
        five.ExtentM2.Should().Be(662_266);
        five.Note.Should().Contain("erven 5, 7, 9");

        var seven = RollBookLookup.Find(rows, 7, 0)!;
        seven.ValueZar.Should().BeNull();
        seven.Note.Should().Contain("erf 5").And.Contain("R 34 800 000");
    }

    [Fact]
    public void An_erf_under_a_scheme_has_no_value_of_its_own()
    {
        var found = RollBookLookup.Find([Row(8293, 0, scheme: "The Mews")], 8293, 0)!;
        found.ValueZar.Should().BeNull();
        found.Note.Should().Contain("The Mews");
        RollBookLookup.Find([Row(107, 4_545_000m)], 107, 0)!.ValueZar.Should().Be(4_545_000m);
        RollBookLookup.Find([Row(107, 4_545_000m)], 108, 0).Should().BeNull();
    }
}
