namespace RealEstateApi.Domain.Models;

public class ListingRoom
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RoomTypeId { get; set; }
    public string? RoomTypeOther { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>A flatlet / garden cottage's own layout as JSON (bedrooms, bathrooms, kitchens,
    /// let out and rent…); null for other rooms. The app owns the shape.</summary>
    public string? UnitDetails { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
