namespace RealEstateApi.Domain.Models;

/// <summary>
/// What the report pack prints about an agent beyond <see cref="User"/> (dbo.AgentProfiles).
/// Office fields left null fall back to the agency's defaults, in the app.
/// </summary>
public class AgentProfile
{
    public int UserId { get; set; }
    public string? AgencySlug { get; set; }
    public string? PpraNumber { get; set; }
    public string? JobTitle { get; set; }
    public string? Bio { get; set; }
    public string? Qualifications { get; set; }
    public string? Website { get; set; }
    public string? PhotoUrl { get; set; }
    public string? SignatureUrl { get; set; }
    public string? OfficeName { get; set; }
    public string? OfficeAddress { get; set; }
    public string? OfficePhone { get; set; }
    public string? OfficeEmail { get; set; }
    public string? OfficeWebsite { get; set; }
    public string? OfficeFooter { get; set; }
    public string? BrochurePagesJson { get; set; }
    public string? ReportSettingsJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}
