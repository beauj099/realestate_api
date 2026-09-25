using System.Security.Claims;
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

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetAll(int listingId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomsAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.CreateRoomAsync(listingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    private static BadRequestObjectResult FileValidationError(string message) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["file"] = [message]
        })
        {
            Type = "https://httpstatuses.io/400",
            Title = "Validation failed"
        });

    /// <summary>Shared checks for room photo uploads; null when the file is acceptable.</summary>
    private static BadRequestObjectResult? ValidatePhoto(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return FileValidationError("A file is required.");

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return FileValidationError("Only .jpg, .jpeg, .png, .webp files are allowed.");

        if (file.Length > 5 * 1024 * 1024)
            return FileValidationError("File size must not exceed 5 MB.");

        return null;
    }

    // Room photos (up to ListingRoomService.MaxPhotosPerRoom per room)
    [HttpGet("{roomId}/photos")]
    public async Task<IActionResult> GetPhotos(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetPhotosAsync(listingId, roomId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{roomId}/photos")]
    public async Task<IActionResult> AddPhoto(int listingId, int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        if (ValidatePhoto(file) is { } invalid) return invalid;

        var uniqueName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _roomService.AddPhotoAsync(listingId, roomId, stream, uniqueName, file.ContentType, CurrentUserId(), IsAdmin(), cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (RoomPhotoLimitExceededException ex)
        {
            return FileValidationError(ex.Message);
        }
    }

    [HttpDelete("{roomId}/photos/{photoId}")]
    public async Task<IActionResult> DeletePhotoById(int listingId, int roomId, int photoId, CancellationToken cancellationToken)
    {
        await _roomService.DeletePhotoAsync(listingId, roomId, photoId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    /// <summary>Legacy single-photo upload for older app builds: appends a photo (cap applies) and returns its URL.</summary>
    [HttpPost("{roomId}/photo")]
    public async Task<IActionResult> UploadPhoto(int listingId, int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        if (ValidatePhoto(file) is { } invalid) return invalid;

        var uniqueName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _roomService.UploadPhotoAsync(listingId, roomId, stream, uniqueName, file.ContentType, CurrentUserId(), IsAdmin(), cancellationToken);
            return Ok(result);
        }
        catch (RoomPhotoLimitExceededException ex)
        {
            return FileValidationError(ex.Message);
        }
    }

    /// <summary>Legacy single-photo delete for older app builds: removes all of the room's photos.</summary>
    [HttpDelete("{roomId}/photo")]
    public async Task<IActionResult> DeletePhoto(int listingId, int roomId, CancellationToken cancellationToken)
    {
        await _roomService.DeleteAllPhotosAsync(listingId, roomId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    [HttpPut("{roomId}")]
    public async Task<IActionResult> Update(int listingId, int roomId, [FromBody] UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.UpdateRoomAsync(listingId, roomId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{roomId}")]
    public async Task<IActionResult> Delete(int listingId, int roomId, CancellationToken cancellationToken)
    {
        await _roomService.DeleteRoomAsync(listingId, roomId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    // Condition
    [HttpPut("{roomId}/condition")]
    public async Task<IActionResult> UpsertCondition(int listingId, int roomId, [FromBody] UpsertRoomConditionRequest request, CancellationToken cancellationToken)
    {
        if (request.Score is < 0 or > 10)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["score"] = ["Score must be between 0 and 10."]
            })
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        var result = await _roomService.UpsertConditionAsync(listingId, roomId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    // Features (predefined)
    [HttpGet("{roomId}/features")]
    public async Task<IActionResult> GetFeatures(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomFeaturesAsync(listingId, roomId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{roomId}/features")]
    public async Task<IActionResult> LinkFeature(int listingId, int roomId, [FromBody] LinkFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.LinkFeatureAsync(listingId, roomId, request.FeatureId, CurrentUserId(), IsAdmin(), cancellationToken);
        return StatusCode(201, result);
    }

    [HttpDelete("{roomId}/features/{featureId}")]
    public async Task<IActionResult> UnlinkFeature(int listingId, int roomId, int featureId, CancellationToken cancellationToken)
    {
        await _roomService.UnlinkFeatureAsync(listingId, roomId, featureId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    // Custom Features
    [HttpGet("{roomId}/custom-features")]
    public async Task<IActionResult> GetCustomFeatures(int listingId, int roomId, CancellationToken cancellationToken)
    {
        var result = await _roomService.GetRoomCustomFeaturesAsync(listingId, roomId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{roomId}/custom-features")]
    public async Task<IActionResult> AddCustomFeature(int listingId, int roomId, [FromBody] AddCustomFeatureRequest request, CancellationToken cancellationToken)
    {
        var result = await _roomService.AddCustomFeatureAsync(listingId, roomId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpDelete("{roomId}/custom-features/{customFeatureId}")]
    public async Task<IActionResult> DeleteCustomFeature(int listingId, int roomId, int customFeatureId, CancellationToken cancellationToken)
    {
        await _roomService.DeleteCustomFeatureAsync(listingId, roomId, customFeatureId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }
}