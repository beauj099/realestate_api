using FluentAssertions;
using PropertyData.Core.Models;
using RealEstateApi.Application.Services;
using Xunit;

namespace PropertyData.Tests;

public class LoadSheddingTests
{
    private static readonly TimeOnly T6 = new(6, 0), T830 = new(8, 30), T20 = new(20, 0), T2230 = new(22, 30);

    /// Every day of the month: stage 1 06:00-08:30, stage 2 adds 20:00-22:30.
    private static List<AreaSlot> Pattern() =>
        [.. Enumerable.Range(1, 31).SelectMany(d => new[]
        {
            new AreaSlot(d, 1, T6, T830),
            new AreaSlot(d, 2, T20, T2230),
        })];

    [Fact]
    public void Counts_the_slots_of_the_stage_and_those_below_it()
    {
        var periods = new[] { new StagePeriod(new DateTime(2023, 5, 1), new DateTime(2023, 5, 2), 2) };
        var (years, _) = LoadSheddingCalculator.Compute(Pattern(), periods);
        years.Should().ContainSingle();
        years[0].Hours.Should().Be(5);      // 2.5 h at stage 1 + 2.5 h at stage 2
        years[0].Days.Should().Be(1);
    }

    /// The trap: announcements overlap. Merged per day once, never summed per period.
    [Fact]
    public void Overlapping_announcements_are_not_counted_twice()
    {
        var day = new DateTime(2023, 5, 1);
        var periods = new[]
        {
            new StagePeriod(day, day.AddDays(1), 2),
            new StagePeriod(day, day.AddDays(1), 2),
            new StagePeriod(day.AddHours(5), day.AddHours(12), 1),
        };
        var (years, _) = LoadSheddingCalculator.Compute(Pattern(), periods);
        years[0].Hours.Should().Be(5);
        years[0].HoursPerDay.Should().BeLessThanOrEqualTo(24);
    }

    [Fact]
    public void A_stage_period_clips_the_slots_it_covers()
    {
        // Stage 1 from 07:00 to 12:00: only 07:00-08:30 of the 06:00 slot.
        var day = new DateTime(2023, 5, 1);
        var (years, _) = LoadSheddingCalculator.Compute(Pattern(), [new StagePeriod(day.AddHours(7), day.AddHours(12), 1)]);
        years[0].Hours.Should().Be(1.5);
    }

    [Fact]
    public void A_slot_past_midnight_counts_in_full()
    {
        var pattern = new List<AreaSlot> { new(1, 1, new TimeOnly(22, 0), new TimeOnly(0, 30)) };
        var day = new DateTime(2023, 5, 1);
        var (years, _) = LoadSheddingCalculator.Compute(pattern, [new StagePeriod(day, day.AddDays(2), 1)]);
        years[0].Hours.Should().Be(2.5);
    }

    [Fact]
    public void Says_when_it_usually_falls()
    {
        var day = new DateTime(2023, 5, 1);
        var (_, halfHours) = LoadSheddingCalculator.Compute(Pattern(), [new StagePeriod(day, day.AddDays(10), 1)]);
        LoadSheddingCalculator.UsualTimes(halfHours).Should().Equal("06:00–08:30");
    }

    [Theory]
    [InlineData("city-of-cape-town-area-9", "City of Cape Town, Area 9")]
    [InlineData("eskom-direct-7-odd", "Eskom Direct 7 Odd")]
    public void Labels_read_like_a_report_line(string area, string label) =>
        LoadSheddingCalculator.Label(area).Should().Be(label);

    [Fact]
    public void Suburb_names_match_the_import_shape() =>
        LoadSheddingCalculator.Slug(" Somerset West ").Should().Be("somerset-west");

    // ---- recorded outages (City of Cape Town) ---------------------------------------------------

    private static Outage At(int y, int mo, int d, int h, int minutes, int min = 0) =>
        new(new DateTime(y, mo, d, h, min, 0), minutes);

    [Fact]
    public void Outages_add_up_per_year_with_days_and_share_of_the_year()
    {
        var (years, _) = LoadSheddingCalculator.ComputeFromOutages(
        [
            At(2023, 5, 1, 6, 120), At(2023, 5, 1, 20, 150), At(2023, 6, 3, 14, 125),
            At(2024, 1, 2, 22, 150),
        ]);
        years.Select(y => y.Year).Should().Equal(2023, 2024);
        years[0].Hours.Should().BeApproximately(6.6, 0.05);   // 2 + 2.5 + 2.08
        years[0].Days.Should().Be(2);
        years[0].HoursPerDay.Should().Be(3.3);
        years[0].PercentOfYear.Should().Be(0.1);              // 6.6 of 8760 h
        years[1].Hours.Should().Be(2.5);
        years[1].Days.Should().Be(1);
    }

    [Fact]
    public void Repeated_and_overlapping_outage_records_are_not_counted_twice()
    {
        var (years, _) = LoadSheddingCalculator.ComputeFromOutages(
        [
            At(2023, 5, 1, 6, 120), At(2023, 5, 1, 6, 120),   // the same record twice
            At(2023, 5, 1, 7, 90),                              // 07:00-08:30 runs on from 06:00-08:00
        ]);
        years[0].Hours.Should().Be(2.5);
        years[0].Days.Should().Be(1);
    }

    [Fact]
    public void An_outage_past_midnight_counts_in_full_on_the_day_it_began()
    {
        var (years, _) = LoadSheddingCalculator.ComputeFromOutages([At(2023, 12, 31, 22, 150)]);
        years.Should().ContainSingle();
        years[0].Year.Should().Be(2023);
        years[0].Hours.Should().Be(2.5);
        years[0].WorstMonth.Should().Be("December 2023");
    }

    [Fact]
    public void The_worst_month_is_the_one_with_most_hours()
    {
        var outages = new List<Outage>();
        for (var d = 1; d <= 3; d++) outages.Add(At(2023, 2, d, 10, 120));   // 6 h in February
        for (var d = 1; d <= 5; d++) outages.Add(At(2023, 5, d, 10, 120));   // 10 h in May
        outages.Add(At(2023, 9, 1, 10, 240));                                 // 4 h in September
        var (years, _) = LoadSheddingCalculator.ComputeFromOutages(outages);
        years[0].WorstMonth.Should().Be("May 2023");
        years[0].WorstMonthHours.Should().Be(10);
    }

    [Fact]
    public void Outage_records_of_no_length_or_implausibly_long_are_ignored()
    {
        var (years, _) = LoadSheddingCalculator.ComputeFromOutages(
            [At(2023, 5, 1, 6, 120), At(2023, 5, 2, 6, -120), At(2023, 5, 3, 2, 734), At(2023, 5, 4, 2, 0)]);
        years[0].Hours.Should().Be(2);
        years[0].Days.Should().Be(1);
    }

    [Fact]
    public void Outages_say_when_load_shedding_usually_fell()
    {
        // Most days 18:00-20:00 (a few minutes late, as recorded), once 04:00-06:00.
        var outages = new List<Outage>();
        for (var d = 1; d <= 20; d++) outages.Add(At(2023, 5, d, 18, 118, 2));
        outages.Add(At(2023, 6, 1, 4, 120));
        var (_, halfHours) = LoadSheddingCalculator.ComputeFromOutages(outages);
        LoadSheddingCalculator.UsualTimes(halfHours).Should().Equal("18:00–20:00");
    }

    [Fact]
    public void The_report_section_from_outages_names_the_City_and_says_what_it_covers()
    {
        var dto = LoadSheddingService.FromOutages("city-of-cape-town-area-9",
            [At(2022, 7, 1, 20, 120), At(2023, 5, 1, 6, 120), At(2023, 5, 2, 6, 120)])!;
        dto.AreaLabel.Should().Be("City of Cape Town, Area 9");
        dto.Years.Select(y => y.Hours).Should().Equal(2, 4);
        dto.CoverageFrom.Should().Be("2022-07-01");
        dto.CoverageTo.Should().Be("2023-05-02");
        dto.Summary.Should().StartWith("In 2023, the worst year on record, this area had load-shedding for 4 hours over 2 days");
        dto.Source.Should().Contain("City of Cape Town");
        dto.Caveat.Should().Contain("City of Cape Town").And.Contain("Eskom").And.NotContain("Scheduled");
        LoadSheddingService.FromOutages("city-of-cape-town-area-9", []).Should().BeNull();
    }

    // ---- finding the area by location ------------------------------------------------------------

    /// A lng/lat box as stored ([[lng,lat],...], closed).
    private static string Box(double west, double south, double east, double north) =>
        FormattableString.Invariant(
            $"[[{west},{south}],[{east},{south}],[{east},{north}],[{west},{north}],[{west},{south}]]");

    [Fact]
    public void The_area_is_the_outline_that_holds_the_point()
    {
        var shapes = new[]
        {
            LoadSheddingAreaShape.FromJson("city-of-cape-town-area-5", $"[{Box(18.40, -34.00, 18.50, -33.95)}]")!,
            LoadSheddingAreaShape.FromJson("city-of-cape-town-area-9", $"[{Box(18.80, -34.12, 18.90, -34.05)}]")!,
        };
        LoadSheddingService.AreaAt(shapes, new LatLng(-33.9827, 18.4656)).Should().Be("city-of-cape-town-area-5");
        LoadSheddingService.AreaAt(shapes, new LatLng(-34.09, 18.85)).Should().Be("city-of-cape-town-area-9");
        LoadSheddingService.AreaAt(shapes, new LatLng(-26.20, 28.04)).Should().BeNull();   // Johannesburg
    }

    [Fact]
    public void Outlines_with_several_parts_and_holes_are_read_even_odd()
    {
        // Two parts, the first with a hole in the middle.
        var shape = LoadSheddingAreaShape.FromJson("city-of-cape-town-area-3",
            $"[{Box(18.0, -34.0, 18.4, -33.6)},{Box(18.1, -33.9, 18.3, -33.7)},{Box(19.0, -34.0, 19.1, -33.9)}]")!;
        shape.Rings.Should().HaveCount(3);
        shape.Contains(new LatLng(-33.95, 18.05)).Should().BeTrue();    // in the first part
        shape.Contains(new LatLng(-33.80, 18.20)).Should().BeFalse();   // in the hole
        shape.Contains(new LatLng(-33.95, 19.05)).Should().BeTrue();    // in the second part
        shape.Contains(new LatLng(-33.95, 18.70)).Should().BeFalse();   // between them
        shape.MinLng.Should().Be(18.0);
        shape.MaxLng.Should().Be(19.1);
    }

    [Fact]
    public void An_outline_without_a_usable_ring_is_no_shape() =>
        LoadSheddingAreaShape.FromJson("x", "[[[18.0,-34.0],[18.1,-34.0]]]").Should().BeNull();
}
