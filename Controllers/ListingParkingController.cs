using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/parking")]
public class ListingParkingController : ControllerBase
{
    private readonly ListingParkingService _parkingService;

    public ListingParkingController(ListingParkingService parkingService)
    {
        _parkingService = parkingService;
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
        var result = await _parkingService.GetParkingAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] AddParkingRequest request, CancellationToken cancellationToken)
    {
        var result = await _parkingService.AddParkingAsync(listingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpPut("{parkingId}")]
    public async Task<IActionResult> Update(int listingId, int parkingId, [FromBody] UpdateParkingRequest request, CancellationToken cancellationToken)
    {
        var result = await _parkingService.UpdateParkingAsync(listingId, parkingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{parkingId}")]
    public async Task<IActionResult> Delete(int listingId, int parkingId, CancellationToken cancellationToken)
    {
        await _parkingService.DeleteParkingAsync(listingId, parkingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }
}