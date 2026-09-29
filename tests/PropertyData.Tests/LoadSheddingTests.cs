using FluentAssertions;
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
}
