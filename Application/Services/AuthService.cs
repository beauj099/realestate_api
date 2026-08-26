using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;

namespace RealEstateApi.Application.Services;

public class AuthService
{
    private readonly UserRepository _userRepository;
    private readonly RefreshTokenRepository _refreshTokenRepository;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        UserRepository userRepository,
        RefreshTokenRepository refreshTokenRepository,
        IOptions<JwtOptions> jwtOptions)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);
        var expires = DateTime.UtcNow.AddHours(_jwtOptions.ExpiryHours);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("displayName", user.DisplayName)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        var refreshTokenRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenRaw));

        await _refreshTokenRepository.RevokeUserTokensAsync(user.Id, cancellationToken);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Convert.ToBase64String(refreshTokenHash),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _refreshTokenRepository.CreateAsync(refreshTokenEntity, cancellationToken);

        return new LoginResponse(tokenString, expires, user.DisplayName, user.Role, refreshTokenRaw);
    }

    public async Task<RefreshTokenResponse?> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var rawHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.RefreshToken));
        var tokenHash = Convert.ToBase64String(rawHash);

        var storedToken = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (storedToken is null || storedToken.IsRevoked || storedToken.ExpiresAt < DateTime.UtcNow)
            return null;

        var user = await _userRepository.GetByIdAsync(storedToken.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return null;

        await _refreshTokenRepository.RevokeAsync(storedToken.Id, cancellationToken);

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);
        var expires = DateTime.UtcNow.AddHours(_jwtOptions.ExpiryHours);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("displayName", user.DisplayName)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        var newAccessToken = tokenHandler.CreateToken(tokenDescriptor);
        var newAccessTokenString = tokenHandler.WriteToken(newAccessToken);

        var newRefreshRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newRefreshHash = SHA256.HashData(Encoding.UTF8.GetBytes(newRefreshRaw));

        var newRefreshEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Convert.ToBase64String(newRefreshHash),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _refreshTokenRepository.CreateAsync(newRefreshEntity, cancellationToken);

        return new RefreshTokenResponse(newAccessTokenString, expires, newRefreshRaw);
    }

    public async Task<LoginResponse?> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _userRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (existing is not null)
            return null;

        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = request.DisplayName,
            Role = "Agent",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _userRepository.CreateAsync(user, cancellationToken);

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);
        var expires = DateTime.UtcNow.AddHours(_jwtOptions.ExpiryHours);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, created.Id.ToString()),
            new Claim(ClaimTypes.Name, created.Username),
            new Claim(ClaimTypes.Role, created.Role),
            new Claim("displayName", created.DisplayName)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        var refreshTokenRaw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshTokenRaw));

        var refreshTokenEntity = new RefreshToken
        {
            UserId = created.Id,
            TokenHash = Convert.ToBase64String(refreshTokenHash),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _refreshTokenRepository.CreateAsync(refreshTokenEntity, cancellationToken);

        return new LoginResponse(tokenString, expires, created.DisplayName, created.Role, refreshTokenRaw);
    }
}