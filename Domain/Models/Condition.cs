namespace RealEstateApi.Domain.Models;

public class Condition
{
    public int Id { get; set; }
    public int ListingRoomId { get; set; }
    public decimal? ConditionRating { get; set; }
    public string? Notes { get; set; }
    public int ConditionCategoryId { get; set; }
}
