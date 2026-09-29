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
}
