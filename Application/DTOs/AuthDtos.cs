namespace RealEstateApi.Application.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string DisplayName, string Role, string RefreshToken);

public record RefreshTokenRequest(string RefreshToken);

public record RefreshTokenResponse(string Token, DateTime ExpiresAt, string RefreshToken);

public record RegisterRequest(
    string FullName,
    string Email,
    string Mobile,
    string AgencyName,
    string AgencyRegistrationNumber,
    string LicenceNumber,
    string Password
);

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
