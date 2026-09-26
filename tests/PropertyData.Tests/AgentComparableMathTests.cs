using FluentAssertions;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;
using RealEstateApi.Domain.Models;
using Xunit;

namespace PropertyData.Tests;

public class AgentComparableMathTests
{
    [Theory]
    [InlineData("DeedsVerified", "Verified", 1.00)]
    [InlineData("OwnTransaction", "Unverified", 0.81)]
    [InlineData("Hearsay", "Unverified", 0.30)]
    [InlineData("SignedOffer", "Disputed", 0.00)]
    public void Weight_mirrors_the_computed_column(string evidence, string verification, double expected) =>
        AgentComparableMath.WeightFor(evidence, verification).Should().Be((decimal)expected);

    [Fact]
    public void Addresses_compare_on_the_street_line_with_abbreviations_expanded() =>
        AgentComparableMath.NormalizeAddress("10 Thirteenth St., Parkhurst")
            .Should().Be(AgentComparableMath.NormalizeAddress("10 THIRTEENTH STREET"));

    [Fact]
    public void Weighted_median_leans_towards_the_stronger_evidence()
    {
        var items = new[] { (10m, 0.3m), (20m, 0.3m), (30m, 0.9m) };
        AgentComparableMath.WeightedQuantile(items, 0.5m).Should().Be(30m);
        AgentComparableMath.WeightedQuantile([], 0.5m).Should().BeNull();
    }

    private static AgentComparable Sale(decimal price, string date, string? erf = null) => new()
    {
        Address = "10 Thirteenth Street, Parkhurst",
        Erf = erf,
        SaleDate = DateTime.Parse(date),
        SalePriceZar = price,
    };

    [Fact]
    public void A_matching_municipal_sale_verifies_and_a_different_price_disputes()
    {
        var city = new[] { new MunicipalSale("10 THIRTEENTH STREET", "1106", new DateOnly(2026, 5, 20), 2_900_000m) };

        AgentComparableMath.Reconcile(Sale(2_950_000m, "2026-04-01"), city)!.Value.Verification.Should().Be("Verified");
        AgentComparableMath.Reconcile(Sale(3_400_000m, "2026-04-01"), city)!.Value.Verification.Should().Be("Disputed");
        // Too far apart in time to be the same sale.
        AgentComparableMath.Reconcile(Sale(2_900_000m, "2025-09-01"), city).Should().BeNull();
    }

    private static AgentComparableDto Dto(decimal price, decimal? floor, string evidence, string verification = "Unverified") =>
        new(Guid.NewGuid(), "1 A Street", "PARKHURST", null, 500m, floor, 3, null, "2026-01-01", price,
            floor is null ? null : price / floor, evidence, AgentComparableMath.DescribeEvidence(evidence),
            verification, 0, AgentComparableMath.WeightFor(evidence, verification), false);

    [Fact]
    public void Three_usable_sales_give_a_range_and_disputed_ones_are_left_out()
    {
        var sales = new[]
        {
            Dto(2_000_000m, 100m, "OwnTransaction"),
            Dto(2_200_000m, 100m, "SignedOffer"),
            Dto(2_400_000m, 100m, "Hearsay"),
            Dto(9_000_000m, 100m, "Hearsay", "Disputed"),
        };

        var summary = AgentComparableMath.Summarise(sales, "PARKHURST", subjectFloorM2: 150, subjectErfM2: null);

        summary.IndicativeBasis.Should().Be("floor");
        summary.IndicativeValue!.Mid.Should().Be(3_300_000m);
        summary.EvidenceStatement.Should().Contain("4 sales in Parkhurst").And.Contain("left out of the figures");
    }

    [Fact]
    public void Too_few_sales_give_no_range() =>
        AgentComparableMath.Summarise([Dto(2_000_000m, 100m, "OwnTransaction")], "PARKHURST", 150, 500)
            .IndicativeValue.Should().BeNull();
}
