using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Domain.Models;
using RealEstateApi.Infrastructure.Repositories;
using RealEstateApi.Infrastructure.Services;

namespace RealEstateApi.Application.Services;

public class AuthService
{
    public const int PasswordResetCodeExpiryMinutes = 15;
    public const int PasswordResetMaxAttempts = 5;

    private readonly UserRepository _userRepository;
    private readonly RefreshTokenRepository _refreshTokenRepository;
    private readonly PasswordResetCodeRepository _passwordResetCodeRepository;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthService> _logger;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        UserRepository userRepository,
        RefreshTokenRepository refreshTokenRepository,
        PasswordResetCodeRepository passwordResetCodeRepository,
        IEmailSender emailSender,
        ILogger<AuthService> logger,
        IOptions<JwtOptions> jwtOptions)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordResetCodeRepository = passwordResetCodeRepository;
        _emailSender = emailSender;
        _logger = logger;
        _jwtOptions = jwtOptions.Value;
    }

    /// <summary>
    /// Trims the login identifier (mobile keyboards add stray spaces) and lowercases
    /// it when it looks like an email, since registration stores emails lowercased.
    /// </summary>
    public static string? NormalizeLoginUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;
        var trimmed = username.Trim();
        return trimmed.Contains('@') ? trimmed.ToLowerInvariant() : trimmed;
    }

    /// <summary>Blank optional text becomes null; anything else is trimmed.</summary>
    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<LoginResponse?> LoginAsync(LoginRequest? request, CancellationToken cancellationToken = default)
    {
        var username = NormalizeLoginUsername(request?.Username);
        if (username is null || string.IsNullOrEmpty(request!.Password))
            return null;

        var user = await _userRepository.GetByUsernameAsync(username, cancellationToken);
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
        // Basic required validation (also enforced at controller, but double-checked here)
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Mobile) ||
            string.IsNullOrWhiteSpace(request.AgencyName) ||
            string.IsNullOrWhiteSpace(request.Password))
            return null;

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingByEmail = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existingByEmail is not null)
            return null;

        // Also block duplicate Username (email stored as username)
        var existingByUsername = await _userRepository.GetByUsernameAsync(normalizedEmail, cancellationToken);
        if (existingByUsername is not null)
            return null;

        var user = new User
        {
            Username = normalizedEmail,
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            DisplayName = request.FullName.Trim(),
            Mobile = request.Mobile.Trim(),
            AgencyName = request.AgencyName.Trim(),
            AgencyRegistrationNumber = TrimToNull(request.AgencyRegistrationNumber),
            LicenceNumber = TrimToNull(request.LicenceNumber),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
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

    /// <summary>
    /// Issues a password reset code if an active user owns <paramref name="email"/>.
    /// Deliberately silent when no account matches (and when the email fails to send)
    /// so the endpoint cannot be used to discover which emails are registered.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return;

        var code = GeneratePasswordResetCode();

        await _passwordResetCodeRepository.InvalidateActiveForUserAsync(user.Id, cancellationToken);
        await _passwordResetCodeRepository.CreateAsync(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = HashPasswordResetCode(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(PasswordResetCodeExpiryMinutes),
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        var body =
            $"Your RealWorth password reset code is {code}. It expires in {PasswordResetCodeExpiryMinutes} minutes. " +
            "If you didn't ask for this, ignore this email.";

        try
        {
            await _emailSender.SendAsync(user.Email, "Your RealWorth password reset code", body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surfacing this as a 500 would reveal that the account exists.
            _logger.LogError(ex, "Failed to send password reset email to user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Sets a new password if <paramref name="code"/> is the user's current, unexpired
    /// reset code. Returns false for any failure (unknown email, wrong / expired / used /
    /// locked-out code) without distinguishing between them.
    /// </summary>
    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null)
            return false;

        var stored = await _passwordResetCodeRepository.GetActiveForUserAsync(user.Id, PasswordResetMaxAttempts, cancellationToken);
        if (stored is null)
            return false;

        var suppliedHash = Encoding.UTF8.GetBytes(HashPasswordResetCode(code.Trim()));
        var storedHash = Encoding.UTF8.GetBytes(stored.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, storedHash))
        {
            await _passwordResetCodeRepository.RegisterFailedAttemptAsync(stored.Id, PasswordResetMaxAttempts, cancellationToken);
            return false;
        }

        // Claim the code first so two concurrent requests cannot both succeed.
        if (!await _passwordResetCodeRepository.MarkUsedAsync(stored.Id, PasswordResetMaxAttempts, cancellationToken))
            return false;

        await _userRepository.UpdatePasswordHashAsync(user.Id, BCrypt.Net.BCrypt.HashPassword(newPassword), cancellationToken);
        await _refreshTokenRepository.RevokeUserTokensAsync(user.Id, cancellationToken);
        return true;
    }

    /// <summary>A uniformly random 6-digit code (leading zeros kept).</summary>
    public static string GeneratePasswordResetCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>SHA-256, base64 -- the same scheme used for refresh tokens.</summary>
    public static string HashPasswordResetCode(string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
