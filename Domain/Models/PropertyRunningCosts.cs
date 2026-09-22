namespace RealEstateApi.Domain.Models;

public class PropertyRunningCosts
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public decimal? MonthlyLevy { get; set; }
    public decimal? MonthlyRates { get; set; }
    public decimal? Electricity { get; set; }
    public decimal? Water { get; set; }
    public decimal? Sewage { get; set; }
    public decimal? Refuse { get; set; }
}
