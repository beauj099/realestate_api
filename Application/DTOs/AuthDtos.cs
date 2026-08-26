namespace RealEstateApi.Application.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string DisplayName, string Role, string RefreshToken);

public record RefreshTokenRequest(string RefreshToken);

public record RefreshTokenResponse(string Token, DateTime ExpiresAt, string RefreshToken);

public record RegisterRequest(string Username, string Password, string DisplayName);
