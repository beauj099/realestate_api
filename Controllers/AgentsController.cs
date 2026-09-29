using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AgentProfileService _profileService;

    public AgentsController(AgentProfileService profileService)
    {
        _profileService = profileService;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Deletes the signed-in agent's account and everything that is theirs (see
    /// <see cref="AccountDeletionService"/>). The password is asked again. 204 when done, 400 with
    /// a "password" error when it is wrong.
    /// </summary>
    [HttpPost("me/delete")]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteAccountRequest request,
        [FromServices] AccountDeletionService deletion, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        return await deletion.DeleteAsync(userId.Value, request.Password ?? "", cancellationToken) switch
        {
            AccountDeletionService.Result.Deleted => NoContent(),
            AccountDeletionService.Result.NotFound => NotFound(),
            _ => BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["password"] = ["The password is not right."],
            })),
        };
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var result = await _profileService.GetAsync(userId.Value, cancellationToken);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateAgentProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var errors = Validate(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed",
                Detail = "One or more profile fields are invalid."
            });

        try
        {
            var result = await _profileService.UpdateAsync(userId.Value, request, cancellationToken);
            if (result is null) return NotFound();
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["email"] = [ex.Message]
            })
            {
                Type = "https://httpstatuses.io/409",
                Title = "Conflict",
                Detail = ex.Message
            });
        }
    }

    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
    };

    /// <summary>The agent's profile photo (multipart: <c>image</c>; PNG or JPEG, up to 5 MB).</summary>
    [HttpPut("me/photo")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    public Task<IActionResult> SetPhoto(IFormFile? image, CancellationToken cancellationToken) =>
        WithImage(image, (userId, upload) => _profileService.SetPhotoAsync(userId, upload, cancellationToken));

    /// <summary>
    /// One of the office's logos for the report pack (multipart: <c>image</c>): <c>mark</c> (square),
    /// <c>wide</c> (for a light background) or <c>wideOnBrand</c> (for the agency colour).
    /// </summary>
    [HttpPut("me/logos/{kind}")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    public Task<IActionResult> SetOfficeLogo(string kind, IFormFile? image, CancellationToken cancellationToken) =>
        !OfficeLogosDto.Kinds.Contains(kind)
            ? Task.FromResult<IActionResult>(ImageInvalid("Logo kind must be mark, wide or wideOnBrand."))
            : WithImage(image, (userId, upload) => _profileService.SetOfficeLogoAsync(userId, kind, upload, cancellationToken));

    /// <summary>Removes one of the office's logos (the agency's is used again).</summary>
    [HttpDelete("me/logos/{kind}")]
    public async Task<IActionResult> RemoveOfficeLogo(string kind, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!OfficeLogosDto.Kinds.Contains(kind)) return ImageInvalid("Logo kind must be mark, wide or wideOnBrand.");
        var profile = await _profileService.RemoveOfficeLogoAsync(userId.Value, kind, cancellationToken);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>The agent's signature for the valuation letter (multipart: <c>image</c>).</summary>
    [HttpPut("me/signature")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    public Task<IActionResult> SetSignature(IFormFile? image, CancellationToken cancellationToken) =>
        WithImage(image, (userId, upload) => _profileService.SetSignatureAsync(userId, upload, cancellationToken));

    /// <summary>
    /// Adds pages to the agent's own brochure (multipart: one or more <c>pages</c>), appended in
    /// order after the existing ones. Replaces the agency's pages in the agent's reports.
    /// </summary>
    [HttpPost("me/brochure-pages")]
    [RequestSizeLimit(AgentProfileService.MaxBrochurePages * MaxImageBytes)]
    public async Task<IActionResult> AddBrochurePages(List<IFormFile> pages, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (pages.Count == 0) return ImageInvalid("Choose at least one page.");
        foreach (var page in pages)
            if (CheckImage(page) is { } error) return ImageInvalid(error);

        var streams = pages.Select(p => p.OpenReadStream()).ToList();
        try
        {
            var uploads = pages.Select((p, i) => ToUpload(p, streams[i])).ToList();
            var result = await _profileService.AddBrochurePagesAsync(userId.Value, uploads, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return ImageInvalid(ex.Message);
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }

    /// <summary>
    /// Reorders or removes the agent's brochure pages (body: the page URLs to keep, in order), or
    /// with <c>null</c> goes back to the agency's pages.
    /// </summary>
    [HttpPut("me/brochure-pages")]
    public async Task<IActionResult> SetBrochurePages([FromBody] List<string>? keep, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        try
        {
            var result = await _profileService.SetBrochurePagesAsync(userId.Value, keep, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return ImageInvalid(ex.Message);
        }
    }

    /// <summary>The agent's report defaults (calculator rates, room weights): stored as sent.</summary>
    [HttpPut("me/report-settings")]
    public async Task<IActionResult> SetReportSettings([FromBody] System.Text.Json.JsonElement settings, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (settings.ValueKind is not (System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Null))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["settings"] = ["Send a JSON object."] }));
        if (settings.GetRawText().Length > 20_000)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["settings"] = ["Settings are too large."] }));
        var result = await _profileService.SetReportSettingsAsync(userId.Value, settings, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private async Task<IActionResult> WithImage(IFormFile? image,
        Func<int, ImageUpload, Task<AgentProfileDto?>> save)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (CheckImage(image) is { } error) return ImageInvalid(error);
        await using var stream = image!.OpenReadStream();
        var result = await save(userId.Value, ToUpload(image, stream));
        return result is null ? NotFound() : Ok(result);
    }

    private static string? CheckImage(IFormFile? image)
    {
        if (image is null || image.Length == 0) return "An image file is required.";
        if (!ImageTypes.ContainsKey(Path.GetExtension(image.FileName))) return "Only .png, .jpg and .jpeg images are allowed.";
        if (image.Length > MaxImageBytes) return "The image must not exceed 5 MB.";
        return null;
    }

    private static ImageUpload ToUpload(IFormFile file, Stream stream)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        return new ImageUpload(stream, ext == ".jpeg" ? ".jpg" : ext, ImageTypes[ext]);
    }

    private BadRequestObjectResult ImageInvalid(string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["image"] = [message] })
        {
            Type = "https://httpstatuses.io/400",
            Title = "Validation failed",
        });

    private static Dictionary<string, string[]> Validate(UpdateAgentProfileRequest r)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(r.DisplayName)) errors["displayName"] = ["Display name is required."];
        if (string.IsNullOrWhiteSpace(r.Email)) errors["email"] = ["Email address is required."];
        else if (!System.Text.RegularExpressions.Regex.IsMatch(r.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            errors["email"] = ["Invalid email address."];
        if (string.IsNullOrWhiteSpace(r.Mobile)) errors["mobile"] = ["Mobile/contact number is required."];
        return errors;
    }
}
