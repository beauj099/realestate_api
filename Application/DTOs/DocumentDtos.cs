namespace RealEstateApi.Application.DTOs;

public record ListingDocumentDto(
    int Id,
    string Category,
    string FileName,
    string Url,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt);
