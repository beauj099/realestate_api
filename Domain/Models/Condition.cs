namespace RealEstateApi.Domain.Models;

public class Condition
{
    public int Id { get; set; }
    public int ListingRoomId { get; set; }
    public decimal? ConditionRating { get; set; }
    public string? Notes { get; set; }
    public int ConditionCategoryId { get; set; }

    /// <summary>Agent's overall 0-10 score for the room, separate from the condition band.</summary>
    public decimal? Score { get; set; }
}
