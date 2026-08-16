namespace RealEstateApi.Domain.Models;

/// <summary>
/// A feature linked to a specific room. Carries the owning <see cref="ListingRoomId"/>
/// so a batched fetch across a whole listing can be grouped back per room.
/// </summary>
public class RoomLinkedFeature
{
    public int ListingRoomId { get; set; }
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
