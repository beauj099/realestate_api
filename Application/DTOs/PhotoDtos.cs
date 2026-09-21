namespace RealEstateApi.Application.DTOs;

public record PhotoUploadResponse(string Url);

public record ListingPhotoDto(
    int Id,
    int ListingId,
    string Url,
    bool IsPrimary,
    int SortOrder,
    DateTime CreatedAt);
