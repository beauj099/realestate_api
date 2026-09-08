using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        if (result is null)
            return Unauthorized(new { message = "Invalid username or password" });

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        if (result is null)
            return Unauthorized(new { message = "Invalid or expired refresh token" });

        return Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var validationError = ValidateRegisterRequest(request);
        if (validationError is not null)
            return BadRequest(new { message = validationError });

        var result = await _authService.RegisterAsync(request, cancellationToken);
        if (result is null)
            return Conflict(new { message = "Email already registered" });

        return Ok(result);
    }

    private static string? ValidateRegisterRequest(RegisterRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.FullName)) return "Full name is required";
        if (string.IsNullOrWhiteSpace(r.Email)) return "Email address is required";
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return "Invalid email address";
        if (string.IsNullOrWhiteSpace(r.Mobile)) return "Mobile/contact number is required";
        // basic phone: 7-20 chars, digits, +, spaces, dashes
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Mobile.Trim(), @"^[\d\+\-\s\(\)]{7,20}$")) return "Invalid mobile/contact number";
        if (string.IsNullOrWhiteSpace(r.AgencyName)) return "Agency/company name is required";
        if (string.IsNullOrWhiteSpace(r.AgencyRegistrationNumber)) return "Agency registration number is required";
        if (string.IsNullOrWhiteSpace(r.LicenceNumber)) return "Licence / FFC number is required";
        if (string.IsNullOrWhiteSpace(r.Password)) return "Password is required";
        if (r.Password.Length < 6) return "Password must be at least 6 characters";
        return null;
    }
}