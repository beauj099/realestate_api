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
    public async Task<IActionResult> GetAll(int listingId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomsAsync(listingId, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.CreateRoomAsync(listingId, request, cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpPost("{roomId}/photo")]
    public async Task<IActionResult> UploadPhoto(int listingId, int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest("Only .jpg, .jpeg, .png, .webp files are allowed.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("File size must not exceed 5 MB.");

        var uniqueName = $"{Guid.NewGuid()}{ext}";

        await using var stream = file.OpenReadStream();
        var result = await _roomService.UploadPhotoAsync(listingId, roomId, stream, uniqueName, file.ContentType, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{roomId}/photo")]
    public async Task<IActionResult> DeletePhoto(int listingId, int roomId, CancellationToken cancellationToken)
    {
        await _roomService.DeletePhotoAsync(listingId, roomId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{roomId}")]
    public async Task<IActionResult> Update(int listingId, int roomId, [FromBody] UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.UpdateRoomAsync(listingId, roomId, request, cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{roomId}")]
    public async Task<IActionResult> Delete(int listingId, int roomId, CancellationToken cancellationToken)
    {
        await _roomService.DeleteRoomAsync(listingId, roomId, cancellationToken);
        return NoContent();
    }

    // Condition
    [HttpPut("{roomId}/condition")]
    public async Task<IActionResult> UpsertCondition(int listingId, int roomId, [FromBody] UpsertRoomConditionRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.UpsertConditionAsync(listingId, roomId, request, cancellationToken);
        return Ok(result);
    }

    // Features (predefined)
    [HttpGet("{roomId}/features")]
    public async Task<IActionResult> GetFeatures(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomFeaturesAsync(listingId, roomId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{roomId}/features")]
    public async Task<IActionResult> LinkFeature(int listingId, int roomId, [FromBody] LinkFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.LinkFeatureAsync(listingId, roomId, request.FeatureId, cancellationToken);
        return StatusCode(201, result);
    }

    [HttpDelete("{roomId}/features/{featureId}")]
    public async Task<IActionResult> UnlinkFeature(int listingId, int roomId, int featureId, CancellationToken cancellationToken)
    {
        await _roomService.UnlinkFeatureAsync(listingId, roomId, featureId, cancellationToken);
        return NoContent();
    }

    // Custom Features
    [HttpGet("{roomId}/custom-features")]
    public async Task<IActionResult> GetCustomFeatures(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomCustomFeaturesAsync(listingId, roomId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{roomId}/custom-features")]
    public async Task<IActionResult> AddCustomFeature(int listingId, int roomId, [FromBody] AddCustomFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.AddCustomFeatureAsync(listingId, roomId, request, cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpDelete("{roomId}/custom-features/{customFeatureId}")]
    public async Task<IActionResult> DeleteCustomFeature(int listingId, int roomId, int customFeatureId, CancellationToken cancellationToken)
    {
        await _roomService.DeleteCustomFeatureAsync(listingId, roomId, customFeatureId, cancellationToken);
        return NoContent();
    }
}