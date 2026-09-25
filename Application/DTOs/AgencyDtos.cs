namespace RealEstateApi.Application.DTOs;

/// <summary>
/// A white-label agency brand. Colours are "#RRGGBB"; a null colour means the
/// app uses the RealWorth house palette (agent-added agencies have none), and a
/// null <see cref="BannerColor"/> means "same as primary".
/// </summary>
public record AgencyDto(
    int Id,
    string Slug,
    string Name,
    string Monogram,
    string? PrimaryColor,
    string? SecondaryColor,
    string? OnPrimaryColor,
    string? BannerColor,
    string? LogoUrl,
    int SortOrder,
    bool IsCustom);

/// <summary>Admin restyle of an agency. Every field is replaced.</summary>
public record UpdateAgencyRequest(
    string Name,
    string? Monogram,
    string? PrimaryColor,
    string? SecondaryColor,
    string? OnPrimaryColor,
    string? BannerColor,
    int SortOrder,
    bool IsActive);
