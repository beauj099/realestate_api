namespace RealEstateApi.Domain.Models;

public class Listing
{
    public int Id { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? P24Ref { get; set; }
    public int? PropertyTypeId { get; set; }
    public int? ListingValuationId { get; set; }
    public DateTime? ListDate { get; set; }
    public string Status { get; set; } = "incomplete";
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
