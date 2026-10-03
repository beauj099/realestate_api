namespace RealEstateApi.Domain.Models;

public class ListingValuation
{
    public int Id { get; set; }
    public decimal? OwnersNetPrice { get; set; }
    public decimal? AgentValuation { get; set; }
    public decimal? CommissionPercent { get; set; }
    /// <summary>When the owners bought, and for how much, as they tell the agent.</summary>
    public DateTime? LastPurchaseDate { get; set; }
    public decimal? LastPurchasePriceZar { get; set; }

    /// <summary>The agent's range and asking price, and why the range differs from the
    /// recorded sales: what the report pack uses. Written back by the app's pack sheet.</summary>
    public decimal? ValueLowZar { get; set; }
    public decimal? ValueHighZar { get; set; }
    public decimal? ListingPriceZar { get; set; }
    public string? AdjustmentReason { get; set; }

    /// <summary>This listing's commission and bond figures for the pack's calculators;
    /// null means the agent's Report settings (kept on the phone).</summary>
    public decimal? CommissionLatePercent { get; set; }
    public int? CommissionEarlyMonths { get; set; }
    public bool? CommissionIncludesVat { get; set; }
    public decimal? InterestRatePercent { get; set; }
    public int? BondTermYears { get; set; }
    public decimal? DepositPercent { get; set; }

    /// <summary>The owners' bond, as they tell the agent.</summary>
    public string? BondInstitution { get; set; }
    public decimal? BondAmountZar { get; set; }
}
