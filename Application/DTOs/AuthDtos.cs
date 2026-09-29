namespace RealEstateApi.Application.DTOs;

// Nullable so a missing field reaches AuthService and yields 401, rather than
// the automatic [ApiController] 400 that non-nullable reference types trigger.
public record LoginRequest(string? Username, string? Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string DisplayName, string Role, string RefreshToken);

public record RefreshTokenRequest(string RefreshToken);

public record RefreshTokenResponse(string Token, DateTime ExpiresAt, string RefreshToken);

public record RegisterRequest(
    string FullName,
    string Email,
    string Mobile,
    string AgencyName,
    string? AgencyRegistrationNumber,
    string? LicenceNumber,
    string Password
);

// Fields are nullable so blank/missing values are reported through our own
// camelCase ValidationProblemDetails instead of the automatic model-state 400.
public record ForgotPasswordRequest(string? Email);

public record ResetPasswordRequest(string? Email, string? Code, string? NewPassword);

public record AgentProfileDto(
    int Id,
    string DisplayName,
    string? Email,
    string? Mobile,
    string? AgencyName,
    string? AgencyRegistrationNumber,
    string? LicenceNumber,
    string Role)
{
    // From dbo.AgentProfiles. LicenceNumber above is the FFC number.
    public string? AgencySlug { get; init; }
    public string? PpraNumber { get; init; }
    public string? JobTitle { get; init; }
    public string? Bio { get; init; }
    public IReadOnlyList<string> Qualifications { get; init; } = [];
    public string? Website { get; init; }
    public string? PhotoUrl { get; init; }
    public string? SignatureUrl { get; init; }

    /// <summary>The agent's own office details; a null field means "use the agency's".</summary>
    public OfficeDto Office { get; init; } = new(null, null, null, null, null, null);

    /// <summary>The agent's own brochure pages; null means "use the agency's".</summary>
    public IReadOnlyList<string>? BrochurePages { get; init; }

    /// <summary>Report defaults (calculator rates, room weights) as the app stores them.</summary>
    public System.Text.Json.JsonElement? ReportSettings { get; init; }
}

/// <summary>An office's details as a report prints them. Any field may be null.</summary>
public record OfficeDto(string? Name, string? Address, string? Phone, string? Email, string? Website, string? Footer,
    string? Slogan = null, string? Headline = null, OfficeLogosDto? Logos = null);

/// <summary>
/// An office's logo variants: <see cref="Mark"/> a square mark, <see cref="Wide"/> a wide logo for a
/// light background, <see cref="WideOnBrand"/> a wide logo drawn for the agency colour.
/// </summary>
public record OfficeLogosDto(string? Mark = null, string? Wide = null, string? WideOnBrand = null)
{
    public static readonly string[] Kinds = ["mark", "wide", "wideOnBrand"];

    public static OfficeLogosDto? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
        return new OfficeLogosDto(d.GetValueOrDefault("mark"), d.GetValueOrDefault("wide"), d.GetValueOrDefault("wideOnBrand"));
    }

    public static string? With(string? json, string kind, string? url)
    {
        var d = string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
        if (url is null) d.Remove(kind); else d[kind] = url;
        return d.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(d);
    }

    public string? Get(string kind) => kind switch
    {
        "mark" => Mark,
        "wide" => Wide,
        "wideOnBrand" => WideOnBrand,
        _ => null,
    };
}

public record UpdateAgentProfileRequest(
    string DisplayName,
    string Email,
    string Mobile,
    string? AgencyName,
    string? AgencyRegistrationNumber,
    string? LicenceNumber)
{
    // Report-pack fields. Null leaves a field as it is (so an older app that does not send them
    // cannot wipe them); an empty string clears it.
    public string? AgencySlug { get; init; }
    public string? PpraNumber { get; init; }
    public string? JobTitle { get; init; }
    public string? Bio { get; init; }
    public IReadOnlyList<string>? Qualifications { get; init; }
    public string? Website { get; init; }
    public OfficeDto? Office { get; init; }
}
