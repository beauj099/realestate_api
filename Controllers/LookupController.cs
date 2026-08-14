using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api")]
public class LookupController : ControllerBase
{
    private readonly LookupService _lookupService;

    public LookupController(LookupService lookupService)
    {
        _lookupService = lookupService;
    }

    [HttpGet("property-types")]
    public async Task<IActionResult> GetPropertyTypes(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetPropertyTypesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("room-types")]
    public async Task<IActionResult> GetRoomTypes(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetRoomTypesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("features")]
    public async Task<IActionResult> GetFeatures(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetFeaturesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("condition-categories")]
    public async Task<IActionResult> GetConditionCategories(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetConditionCategoriesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("parking-types")]
    public async Task<IActionResult> GetParkingTypes(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetParkingTypesAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("facing")]
    public async Task<IActionResult> GetFacing(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetFacingAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("zoning")]
    public async Task<IActionResult> GetZoning(CancellationToken cancellationToken)
    {
        var result = await _lookupService.GetZoningAsync(cancellationToken);
        return Ok(result);
    }
}