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
            return Unauthorized(new ProblemDetails
            {
                Type = "https://httpstatuses.io/401",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Invalid username or password."
            });

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        if (result is null)
            return Unauthorized(new ProblemDetails
            {
                Type = "https://httpstatuses.io/401",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Invalid or expired refresh token."
            });

        return Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var errors = ValidateRegisterRequest(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more registration fields are invalid."
            });

        var result = await _authService.RegisterAsync(request, cancellationToken);
        if (result is null)
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["email"] = ["Email address is already registered."]
            })
            {
                Type = "https://httpstatuses.io/409",
                Title = "Conflict",
                Detail = "Email address is already registered."
            });

        return Ok(result);
    }

    private static Dictionary<string, string[]> ValidateRegisterRequest(RegisterRequest r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r.FullName)) errors["fullName"] = ["Full name is required."];
        if (string.IsNullOrWhiteSpace(r.Email)) errors["email"] = ["Email address is required."];
        else if (!System.Text.RegularExpressions.Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) errors["email"] = ["Invalid email address."];
        if (string.IsNullOrWhiteSpace(r.Mobile)) errors["mobile"] = ["Mobile/contact number is required."];
        else if (!System.Text.RegularExpressions.Regex.IsMatch(r.Mobile.Trim(), @"^[\d\+\-\s\(\)]{7,20}$")) errors["mobile"] = ["Invalid mobile/contact number."];
        if (string.IsNullOrWhiteSpace(r.AgencyName)) errors["agencyName"] = ["Agency/company name is required."];
        if (string.IsNullOrWhiteSpace(r.AgencyRegistrationNumber)) errors["agencyRegistrationNumber"] = ["Agency registration number is required."];
        if (string.IsNullOrWhiteSpace(r.LicenceNumber)) errors["licenceNumber"] = ["Licence / FFC number is required."];
        if (string.IsNullOrWhiteSpace(r.Password)) errors["password"] = ["Password is required."];
        else if (r.Password.Length < 6) errors["password"] = ["Password must be at least 6 characters."];
        return errors;
    }
}