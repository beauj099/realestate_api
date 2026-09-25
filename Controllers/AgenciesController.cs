using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

/// <summary>
/// The white-label agencies the app offers. Listing is public, because the
/// register screen asks for the agency before an account exists. Agents add
/// agencies that are not listed ("Other"), which every agent then sees; only
/// admins restyle agencies.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/agencies")]
public class AgenciesController : ControllerBase
{
    private const long MaxLogoBytes = 2 * 1024 * 1024;
    private static readonly Dictionary<string, string> LogoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    private readonly AgencyService _agencyService;

    public AgenciesController(AgencyService agencyService)
    {
        _agencyService = agencyService;
    }

    private int? CurrentUserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _agencyService.GetAllAsync(cancellationToken));
    }

    /// <summary>
    /// Adds an unlisted agency (multipart: <c>name</c>, optional <c>logo</c>).
    /// 201 when created; 200 with the existing agency when the name is taken.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public async Task<IActionResult> Add([FromForm] string? name, IFormFile? logo, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Agency name is required."];
        else if (name.Trim().Length > 150) errors["name"] = ["Agency name must be 150 characters or fewer."];
        var logoError = ValidateLogo(logo, required: false);
        if (logoError is not null) errors["logo"] = [logoError];
        if (errors.Count > 0) return ValidationFailed(errors);

        await using var stream = logo?.OpenReadStream();
        var (agency, created) = await _agencyService.AddCustomAsync(
            name!, CurrentUserId(), ToUpload(logo, stream), cancellationToken);
        return created ? StatusCode(StatusCodes.Status201Created, agency) : Ok(agency);
    }

    /// <summary>Restyles an agency: name, monogram, colours, order, visibility.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAgencyRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Agency name is required."];
        else if (request.Name.Trim().Length > 150) errors["name"] = ["Agency name must be 150 characters or fewer."];
        if (request.Monogram is { Length: > 4 }) errors["monogram"] = ["Monogram must be 4 characters or fewer."];
        foreach (var (field, value) in new[]
                 {
                     ("primaryColor", request.PrimaryColor), ("secondaryColor", request.SecondaryColor),
                     ("onPrimaryColor", request.OnPrimaryColor), ("bannerColor", request.BannerColor),
                 })
        {
            if (!AgencyService.IsValidColor(value)) errors[field] = ["Use a colour like #1B365D, or leave it empty."];
        }
        if (errors.Count > 0) return ValidationFailed(errors);

        return Ok(await _agencyService.UpdateAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// Replaces an agency's logo (multipart: <c>logo</c>). Admins may change any
    /// agency; an agent only one they added.
    /// </summary>
    [HttpPut("{id}/logo")]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public async Task<IActionResult> SetLogo(int id, IFormFile? logo, CancellationToken cancellationToken)
    {
        var logoError = ValidateLogo(logo, required: true);
        if (logoError is not null) return ValidationFailed(new() { ["logo"] = [logoError] });

        await using var stream = logo!.OpenReadStream();
        var agency = await _agencyService.SetLogoAsync(
            id, CurrentUserId(), IsAdmin(), ToUpload(logo, stream)!, cancellationToken);
        return Ok(agency);
    }

    private static string? ValidateLogo(IFormFile? logo, bool required)
    {
        if (logo is null || logo.Length == 0) return required ? "A logo file is required." : null;
        if (!LogoTypes.ContainsKey(Path.GetExtension(logo.FileName)))
            return "Only .png, .jpg, .jpeg and .webp logos are allowed.";
        if (logo.Length > MaxLogoBytes) return "The logo must not exceed 2 MB.";
        return null;
    }

    private static LogoUpload? ToUpload(IFormFile? logo, Stream? stream)
    {
        if (logo is null || stream is null || logo.Length == 0) return null;
        var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
        return new LogoUpload(stream, ext == ".jpeg" ? ".jpg" : ext, LogoTypes[ext]);
    }

    private BadRequestObjectResult ValidationFailed(Dictionary<string, string[]> errors) =>
        BadRequest(new ValidationProblemDetails(errors)
        {
            Type = "https://httpstatuses.io/400",
            Title = "Validation failed"
        });
}
