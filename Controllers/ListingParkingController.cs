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

    [HttpGet]
    public async Task<IActionResult> GetAll(int listingId, CancellationToken cancellationToken)
    {
        var result = await _parkingService.GetParkingAsync(listingId, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] AddParkingRequest request, CancellationToken cancellationToken)
    {
        var result = await _parkingService.AddParkingAsync(listingId, request, cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpPut("{parkingId}")]
    public async Task<IActionResult> Update(int listingId, int parkingId, [FromBody] UpdateParkingRequest request, CancellationToken cancellationToken)
    {
        var result = await _parkingService.UpdateParkingAsync(listingId, parkingId, request, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{parkingId}")]
    public async Task<IActionResult> Delete(int listingId, int parkingId, CancellationToken cancellationToken)
    {
        await _parkingService.DeleteParkingAsync(listingId, parkingId, cancellationToken);
        return NoContent();
    }
}