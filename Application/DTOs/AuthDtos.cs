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
    string Role);

public record UpdateAgentProfileRequest(
    string DisplayName,
    string Email,
    string Mobile,
    string? AgencyName,
    string? AgencyRegistrationNumber,
    string? LicenceNumber);
