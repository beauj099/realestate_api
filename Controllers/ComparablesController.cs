using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

/// <summary>
/// Sales agents capture themselves (shared by suburb, never showing who captured them), and the
/// agency's own listings in a suburb. Reports read captured sales through the property report.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/comparables")]
public class ComparablesController : ControllerBase
{
    private readonly AgentComparableService _comparables;

    public ComparablesController(AgentComparableService comparables)
    {
        _comparables = comparables;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Logs a sale. When another agent already logged it (same address, within 45 days and 2% of
    /// the price) it corroborates theirs instead: <c>wasDuplicate</c> is then true.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentComparableRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var errors = AgentComparableService.Validate(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more fields of the sale are invalid.",
            });

        var saved = await _comparables.CreateAsync(userId.Value, request, cancellationToken);
        return saved.WasDuplicate ? Ok(saved) : StatusCode(StatusCodes.Status201Created, saved);
    }

    /// <summary>The sales this agent has captured, newest first.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        return Ok(await _comparables.MineAsync(userId.Value, cancellationToken));
    }

    /// <summary>Removes a sale this agent captured. Other agents' entries cannot be deleted.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        return await _comparables.DeleteAsync(id, userId.Value, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>
    /// The agency's own listings in a suburb ("on the market nearby"): addresses, the agents'
    /// valuations and sizes, never owners. Agents without an agency see only their own.
    /// </summary>
    [HttpGet("market")]
    public async Task<IActionResult> Market([FromQuery] string? suburb, [FromQuery] int? excludeListingId,
        [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(suburb)) return Ok(Array.Empty<MarketListingDto>());
        return Ok(await _comparables.MarketAsync(userId.Value, suburb, excludeListingId, cancellationToken, lat, lng));
    }
}
