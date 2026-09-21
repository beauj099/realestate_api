namespace RealEstateApi.Domain.Models;

public class ListingPhoto
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string Url { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}
