using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/rooms")]
public class ListingRoomsController : ControllerBase
{
    private readonly ListingRoomService _roomService;

    public ListingRoomsController(ListingRoomService roomService)
    {
        _roomService = roomService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int listingId)
    {
        var result = await _roomService.GetRoomsAsync(listingId);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] CreateRoomRequest request)
    {
        var result = await _roomService.CreateRoomAsync(listingId, request);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpPost("{roomId}/photo")]
    public async Task<IActionResult> UploadPhoto(int listingId, int roomId, IFormFile file)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest("Only .jpg, .jpeg, .png, .webp files are allowed.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("File size must not exceed 5 MB.");

        var uniqueName = $"{Guid.NewGuid()}{ext}";

        await using var stream = file.OpenReadStream();
        var result = await _roomService.UploadPhotoAsync(listingId, roomId, stream, uniqueName, file.ContentType);
        return Ok(result);
    }

    [HttpDelete("{roomId}/photo")]
    public async Task<IActionResult> DeletePhoto(int listingId, int roomId)
    {
        await _roomService.DeletePhotoAsync(listingId, roomId);
        return NoContent();
    }

    [HttpPut("{roomId}")]
    public async Task<IActionResult> Update(int listingId, int roomId, [FromBody] UpdateRoomRequest request)
    {
        var result = await _roomService.UpdateRoomAsync(listingId, roomId, request);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{roomId}")]
    public async Task<IActionResult> Delete(int listingId, int roomId)
    {
        await _roomService.DeleteRoomAsync(listingId, roomId);
        return NoContent();
    }

    // Condition
    [HttpPut("{roomId}/condition")]
    public async Task<IActionResult> UpsertCondition(int listingId, int roomId, [FromBody] UpsertRoomConditionRequest request)
    {
        var result = await _roomService.UpsertConditionAsync(listingId, roomId, request);
        return Ok(result);
    }

    // Features (predefined)
    [HttpGet("{roomId}/features")]
    public async Task<IActionResult> GetFeatures(int listingId, int roomId)
    {
        var result = await _roomService.GetRoomFeaturesAsync(listingId, roomId);
        return Ok(result);
    }

    [HttpPost("{roomId}/features")]
    public async Task<IActionResult> LinkFeature(int listingId, int roomId, [FromBody] LinkFeatureRequest request)
    {
        var result = await _roomService.LinkFeatureAsync(listingId, roomId, request.FeatureId);
        return StatusCode(201, result);
    }

    [HttpDelete("{roomId}/features/{featureId}")]
    public async Task<IActionResult> UnlinkFeature(int listingId, int roomId, int featureId)
    {
        await _roomService.UnlinkFeatureAsync(listingId, roomId, featureId);
        return NoContent();
    }

    // Custom Features
    [HttpGet("{roomId}/custom-features")]
    public async Task<IActionResult> GetCustomFeatures(int listingId, int roomId)
    {
        var result = await _roomService.GetRoomCustomFeaturesAsync(listingId, roomId);
        return Ok(result);
    }

    [HttpPost("{roomId}/custom-features")]
    public async Task<IActionResult> AddCustomFeature(int listingId, int roomId, [FromBody] AddCustomFeatureRequest request)
    {
        var result = await _roomService.AddCustomFeatureAsync(listingId, roomId, request);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpDelete("{roomId}/custom-features/{customFeatureId}")]
    public async Task<IActionResult> DeleteCustomFeature(int listingId, int roomId, int customFeatureId)
    {
        await _roomService.DeleteCustomFeatureAsync(listingId, roomId, customFeatureId);
        return NoContent();
    }
}
