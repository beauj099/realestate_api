using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/photos")]
public class ListingPhotosController : ControllerBase
{
    private readonly ListingPhotoService _photoService;

    public ListingPhotosController(ListingPhotoService photoService)
    {
        _photoService = photoService;
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
        var result = await _photoService.GetPhotosAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Upload(int listingId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["A file is required."]
            })
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["Only .jpg, .jpeg, .png, .webp files are allowed."]
            })
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["file"] = ["File size must not exceed 5 MB."]
            })
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        var uniqueName = $"{Guid.NewGuid()}{ext}";

        await using var stream = file.OpenReadStream();
        var result = await _photoService.UploadAsync(listingId, CurrentUserId(), IsAdmin(), stream, uniqueName, file.ContentType, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{photoId}/primary")]
    public async Task<IActionResult> SetPrimary(int listingId, int photoId, CancellationToken cancellationToken)
    {
        await _photoService.SetPrimaryAsync(listingId, photoId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{photoId}")]
    public async Task<IActionResult> Delete(int listingId, int photoId, CancellationToken cancellationToken)
    {
        await _photoService.DeleteAsync(listingId, photoId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }
}
