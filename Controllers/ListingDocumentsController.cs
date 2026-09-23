using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/documents")]
public class ListingDocumentsController : ControllerBase
{
    private readonly ListingDocumentService _documentService;

    public ListingDocumentsController(ListingDocumentService documentService)
    {
        _documentService = documentService;
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
        var result = await _documentService.GetDocumentsAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    /// <summary>multipart/form-data with "file" and "category".</summary>
    [HttpPost]
    public async Task<IActionResult> Upload(
        int listingId,
        IFormFile? file,
        [FromForm] string? category,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var normalizedCategory = ListingDocumentRules.NormalizeCategory(category);
        if (string.IsNullOrWhiteSpace(category))
            errors["category"] = ["Category is required."];
        else if (normalizedCategory is null)
            errors["category"] = [$"Category must be one of: {string.Join(", ", ListingDocumentRules.Categories)}."];

        string? contentType = null;
        if (file is null || file.Length == 0)
            errors["file"] = ["A file is required."];
        else if (file.Length > ListingDocumentRules.MaxSizeBytes)
            errors["file"] = ["File size must not exceed 10 MB."];
        else if ((contentType = ListingDocumentRules.ResolveContentType(file.ContentType, file.FileName)) is null)
            errors["file"] = ["Only PDF, JPEG, PNG, WebP and HEIC files are allowed."];

        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors)
            {
                Type = "https://httpstatuses.io/400",
                Title = "Validation failed"
            });

        var displayName = ListingDocumentRules.SanitizeFileName(file!.FileName)
                          ?? "document" + ListingDocumentRules.AllowedContentTypes[contentType!];

        await using var stream = file.OpenReadStream();
        var result = await _documentService.UploadAsync(
            listingId, CurrentUserId(), IsAdmin(),
            stream, normalizedCategory!, displayName, contentType!, file.Length,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpDelete("{documentId}")]
    public async Task<IActionResult> Delete(int listingId, int documentId, CancellationToken cancellationToken)
    {
        await _documentService.DeleteAsync(listingId, documentId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }
}
