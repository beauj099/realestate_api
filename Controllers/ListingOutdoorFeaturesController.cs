using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/outdoor-features")]
public class ListingOutdoorFeaturesController : ControllerBase
{
    private readonly ListingOutdoorFeatureService _outdoorFeatureService;

    public ListingOutdoorFeaturesController(ListingOutdoorFeatureService outdoorFeatureService)
    {
        _outdoorFeatureService = outdoorFeatureService;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetAll(int listingId, CancellationToken cancellationToken)
    {
        var result = await _outdoorFeatureService.GetByListingIdAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Add(int listingId, [FromBody] AddOutdoorFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _outdoorFeatureService.AddAsync(listingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int listingId, int id, CancellationToken cancellationToken)
    {
        await _outdoorFeatureService.DeleteAsync(listingId, id, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> ReplaceAll(int listingId, [FromBody] ReplaceOutdoorFeaturesRequest request, CancellationToken cancellationToken)
    {
        var result = await _outdoorFeatureService.ReplaceAllAsync(listingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }
}