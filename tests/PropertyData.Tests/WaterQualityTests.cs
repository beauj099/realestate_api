using System.Text.Json;
using FluentAssertions;
using RealEstateApi.Application.Services;
using Xunit;

namespace PropertyData.Tests;

public class WaterQualityTests
{
    [Theory]
    [InlineData(98.1, "Blue Drop certified")]
    [InlineData(94.95, "Excellent")]
    [InlineData(85.0, "Good")]
    [InlineData(62.8, "Average")]
    [InlineData(35.9, "Poor")]
    [InlineData(4.3, "Critical")]
    public void Bands_follow_the_Blue_Drop_report(double score, string band) =>
        AreaDetailsService.WaterBand(score).Should().Be(band);

    /// Every municipality (2016 boundaries, by code) has its water authority's 2023 score.
    [Fact]
    public void Every_municipality_has_a_score()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "Infrastructure", "PropertyData", "Data", "blue-drop-2023.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var byCode = doc.RootElement.GetProperty("byMunicipalityCode");
        byCode.EnumerateObject().Should().HaveCount(213);
        byCode.GetProperty("CPT").GetProperty("score").GetDouble().Should().Be(98.1);
        byCode.GetProperty("JHB").GetProperty("score").GetDouble().Should().Be(98.1);
        byCode.EnumerateObject().Should().OnlyContain(m =>
            m.Value.GetProperty("score").GetDouble() >= 0 && m.Value.GetProperty("score").GetDouble() <= 100);
    }
}
