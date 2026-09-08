namespace RealEstateApi.Domain.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = "Agent";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    // Agent profile fields — nullable for backward compat with existing rows
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public string? AgencyName { get; set; }
    public string? AgencyRegistrationNumber { get; set; }
    public string? LicenceNumber { get; set; }
}
