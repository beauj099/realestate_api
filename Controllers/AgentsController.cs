using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AgentProfileService _profileService;

    public AgentsController(AgentProfileService profileService)
    {
        _profileService = profileService;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _profileService.GetAsync(userId.Value, cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateAgentProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var errors = Validate(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more profile fields are invalid."
            });

        try
        {
            var result = await _profileService.UpdateAsync(userId.Value, request, cancellationToken);
            if (result is null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["email"] = [ex.Message]
            })
            {
                Type = "https://httpstatuses.io/409",
                Title = "Conflict",
                Detail = ex.Message
            });
        }
    }

    private static Dictionary<string, string[]> Validate(UpdateAgentProfileRequest r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r.DisplayName)) errors["displayName"] = ["Display name is required."];
        if (string.IsNullOrWhiteSpace(r.Email)) errors["email"] = ["Email address is required."];
        else if (!System.Text.RegularExpressions.Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            errors["email"] = ["Invalid email address."];
        if (string.IsNullOrWhiteSpace(r.Mobile)) errors["mobile"] = ["Mobile/contact number is required."];
        return errors;
    }
}
