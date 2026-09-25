using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings")]
public class ListingsController : ControllerBase
{
    private readonly ListingService _listingService;

    public ListingsController(ListingService listingService)
    {
        _listingService = listingService;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateListingRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null && !IsAdmin()) return Unauthorized();
        var result = await _listingService.CreateAsync(request, userId ?? 0, IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo, CancellationToken cancellationToken)
    {
        var result = await _listingService.GetAllAsync(status, dateFrom, dateTo, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _listingService.GetByIdAsync(id, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateListingRequest request, CancellationToken cancellationToken)
    {
        var result = await _listingService.UpdateAsync(id, request, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _listingService.DeleteAsync(id, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id}/submit")]
    public async Task<IActionResult> Submit(int id, CancellationToken cancellationToken)
    {
        var result = await _listingService.SubmitAsync(id, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}/house-score")]
    public async Task<IActionResult> UpdateHouseScore(int id, [FromBody] UpdateHouseScoreRequest request, CancellationToken cancellationToken)
    {
        if (request.Score is < 0 or > 100)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["score"] = ["Score must be between 0 and 100."]
            })
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        await _listingService.UpdateHouseScoreAsync(id, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    // Address
    [HttpGet("{id}/address")]
    public async Task<IActionResult> GetAddress(int id, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.GetAddressAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}/address")]
    public async Task<IActionResult> UpsertAddress(int id, [FromBody] UpsertAddressRequest request, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.UpsertAddressAsync(id, request, cancellationToken);
        return Ok(result);
    }

    // Building Info
    [HttpGet("{id}/building-info")]
    public async Task<IActionResult> GetBuildingInfo(int id, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.GetBuildingInfoAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}/building-info")]
    public async Task<IActionResult> UpsertBuildingInfo(int id, [FromBody] UpsertBuildingInfoRequest request, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.UpsertBuildingInfoAsync(id, request, cancellationToken);
        return Ok(result);
    }

    // Valuation
    [HttpGet("{id}/valuation")]
    public async Task<IActionResult> GetValuation(int id, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.GetValuationAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}/valuation")]
    public async Task<IActionResult> UpsertValuation(int id, [FromBody] UpsertValuationRequest request, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.UpsertValuationAsync(id, request, cancellationToken);
        return Ok(result);
    }

    // Running Costs
    [HttpGet("{id}/running-costs")]
    public async Task<IActionResult> GetRunningCosts(int id, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.GetRunningCostsAsync(id, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPut("{id}/running-costs")]
    public async Task<IActionResult> UpsertRunningCosts(int id, [FromBody] UpsertRunningCostsRequest request, CancellationToken cancellationToken)
    {
        try { await _listingService.AssertOwnedAsync(id, CurrentUserId(), IsAdmin(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        var result = await _listingService.UpsertRunningCostsAsync(id, request, cancellationToken);
        return Ok(result);
    }
}