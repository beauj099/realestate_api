using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/listings/{listingId}/contacts")]
public class ListingContactsController : ControllerBase
{
    private readonly ListingContactService _contactService;

    public ListingContactsController(ListingContactService contactService)
    {
        _contactService = contactService;
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
        var result = await _contactService.GetContactsAsync(listingId, CurrentUserId(), IsAdmin(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listingId, [FromBody] AddContactRequest request, CancellationToken cancellationToken)
    {
        var result = await _contactService.AddContactAsync(listingId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { listingId }, result);
    }

    [HttpPut("{contactId}")]
    public async Task<IActionResult> Update(int listingId, int contactId, [FromBody] UpdateContactRequest request, CancellationToken cancellationToken)
    {
        var result = await _contactService.UpdateContactAsync(listingId, contactId, request, CurrentUserId(), IsAdmin(), cancellationToken);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{contactId}")]
    public async Task<IActionResult> Delete(int listingId, int contactId, CancellationToken cancellationToken)
    {
        await _contactService.DeleteContactAsync(listingId, contactId, CurrentUserId(), IsAdmin(), cancellationToken);
        return NoContent();
    }
}