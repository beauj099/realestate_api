namespace RealEstateApi.Application.DTOs;

public record PhotoUploadResponse(string Url);

/// <summary>Every photo id in the order the agent wants them; the first is the main/cover photo.</summary>
public record ReorderPhotosRequest(List<int> PhotoIds);

public record ListingPhotoDto(
    int Id,
    int ListingId,
    string Url,
    bool IsPrimary,
    int SortOrder,
    DateTime CreatedAt);
