namespace RealEstateApi.Domain.Models;

public class ListingRoomPhoto
{
    public int Id { get; set; }
    public int ListingRoomId { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// IImageStorage key ("rooms/{listingId}/{roomId}/{guid}.ext"). Null only for rows
    /// backfilled from a legacy ListingRoom.PhotoUrl whose key could not be derived;
    /// those objects are never deleted from storage.
    /// </summary>
    public string? StorageKey { get; set; }

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}
