namespace RealEstateApi.Domain.Models;

public class Agency
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Monogram { get; set; } = string.Empty;
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? OnPrimaryColor { get; set; }
    public string? BannerColor { get; set; }
    public string? LogoUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsCustom { get; set; }
    public bool IsActive { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? OfficeName { get; set; }
    public string? OfficeAddress { get; set; }
    public string? OfficePhone { get; set; }
    public string? OfficeEmail { get; set; }
    public string? OfficeWebsite { get; set; }
    public string? OfficeFooter { get; set; }
    public string? OfficeSlogan { get; set; }
    public string? OfficeHeadline { get; set; }
    /// <summary>{"mark": url, "wide": url, "wideOnBrand": url}; see OfficeLogosDto.</summary>
    public string? OfficeLogosJson { get; set; }
    public string? BrochurePagesJson { get; set; }
}
