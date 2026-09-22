using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    public async Task<IActionResult> Login(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LoginRequest? request,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Emails a 6-digit reset code if an active account uses this email. Always 204 for a
    /// well-formed email, whether or not the account exists, so emails cannot be enumerated.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ForgotPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateForgotPasswordRequest(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more fields are invalid."
            });

        await _authService.RequestPasswordResetAsync(request!.Email!, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResetPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        var errors = ValidateResetPasswordRequest(request);
        if (errors.Count == 0 &&
            !await _authService.ResetPasswordAsync(request!.Email!, request.Code!, request.NewPassword!, cancellationToken))
        {
            errors["code"] = [InvalidResetCodeMessage];
        }

        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more fields are invalid."
            });

        return NoContent();
    }

    internal const string InvalidResetCodeMessage = "Invalid or expired code.";
    internal const string PasswordTooShortMessage = "Password must be at least 6 characters.";
    private const int MinPasswordLength = 6;

    private static bool IsValidEmail(string email) =>
        Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    internal static Dictionary<string, string[]> ValidateRegisterRequest(RegisterRequest r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r.FullName)) errors["fullName"] = ["Full name is required."];
        if (string.IsNullOrWhiteSpace(r.Email)) errors["email"] = ["Email address is required."];
        else if (!IsValidEmail(r.Email)) errors["email"] = ["Invalid email address."];
        if (string.IsNullOrWhiteSpace(r.Mobile)) errors["mobile"] = ["Mobile/contact number is required."];
        else if (!Regex.IsMatch(r.Mobile.Trim(), @"^[\d\+\-\s\(\)]{7,20}$")) errors["mobile"] = ["Invalid mobile/contact number."];
        if (string.IsNullOrWhiteSpace(r.AgencyName)) errors["agencyName"] = ["Agency/company name is required."];
        // agencyRegistrationNumber and licenceNumber are optional; blank is stored as NULL.
        if (string.IsNullOrWhiteSpace(r.Password)) errors["password"] = ["Password is required."];
        else if (r.Password.Length < MinPasswordLength) errors["password"] = [PasswordTooShortMessage];
        return errors;
    }

    internal static Dictionary<string, string[]> ValidateForgotPasswordRequest(ForgotPasswordRequest? r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r?.Email)) errors["email"] = ["Email address is required."];
        else if (!IsValidEmail(r.Email)) errors["email"] = ["Invalid email address."];
        return errors;
    }

    /// <summary>
    /// Shape checks only (no database). A blank/malformed email or a code that is not
    /// exactly 6 digits is reported as an invalid code -- the same answer an unknown
    /// email gets -- so the response never hints at which part was wrong.
    /// </summary>
    internal static Dictionary<string, string[]> ValidateResetPasswordRequest(ResetPasswordRequest? r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r?.Email) || !IsValidEmail(r.Email) ||
            string.IsNullOrWhiteSpace(r.Code) || !Regex.IsMatch(r.Code.Trim(), @"^\d{6}$"))
            errors["code"] = [InvalidResetCodeMessage];
        if (string.IsNullOrEmpty(r?.NewPassword) || r.NewPassword.Length < MinPasswordLength)
            errors["newPassword"] = [PasswordTooShortMessage];
        return errors;
    }
}