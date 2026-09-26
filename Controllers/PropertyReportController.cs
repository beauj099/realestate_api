using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.DTOs;
using RealEstateApi.Application.Services;
using RealEstateApi.Infrastructure.PropertyData;

namespace RealEstateApi.Controllers;

/// <summary>
/// Property reports from public municipal data (Cape Town so far). Agents only: every request
/// fans out to the City's services, so this must never become an open scraping proxy.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Agent")]
[Route("api/property")]
public class PropertyReportController : ControllerBase
{
    private readonly PropertyReportService _reports;
    private readonly ImageryLinkBuilder _imagery;
    private readonly IHttpClientFactory _httpFactory;

    public PropertyReportController(PropertyReportService reports, ImageryLinkBuilder imagery, IHttpClientFactory httpFactory)
    {
        _reports = reports;
        _imagery = imagery;
        _httpFactory = httpFactory;
    }

    /// <summary>
    /// Address type-ahead ("17 pine", "17 pine rd clar", "pine rd") from the City of Cape Town's
    /// parcel records: up to 8 real addresses with their erf and location. The app debounces;
    /// fewer than 3 characters returns nothing.
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? q, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _reports.SuggestAsync(q ?? "", cancellationToken));
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>Address, coordinate or erf → candidate properties (usually one).</summary>
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolvePropertyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Address) && request.Erf is null && (request.Lat is null || request.Lng is null))
            return ValidationFailed("address", "Give an address, a lat/lng or an erf.");
        try
        {
            return Ok(await _reports.ResolveAsync(request, cancellationToken));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return ValidationFailed("address", "Could not read that address. Use e.g. \"17 Pine Road, Claremont\".");
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>The full record for a report. The first call for a property takes a few seconds (it
    /// reads the City's cadastre, valuation roll and area sales); repeats come from the cache.</summary>
    [HttpGet("{municipality}/{erf}")]
    public async Task<IActionResult> GetReport(string municipality, string erf, [FromQuery] string? suburb,
        [FromQuery] string? sg26, [FromQuery] bool includeComparables = true, CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _reports.GetReportAsync(municipality, erf, suburb, sg26, includeComparables, cancellationToken));
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>SVG site plan drawn from the municipal cadastre and footprints: ours, safe to print.</summary>
    [HttpGet("{municipality}/{erf}/site-plan.svg")]
    public async Task<IActionResult> GetSitePlan(string municipality, string erf, [FromQuery] string? suburb,
        [FromQuery] string? sg26, [FromQuery] int width = 0, [FromQuery] int height = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var svg = await _reports.GetSitePlanSvgAsync(municipality, erf, suburb, sg26, width, height, cancellationToken);
            return Content(svg, "image/svg+xml", System.Text.Encoding.UTF8);
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>
    /// Google imagery, proxied so the key stays on the server. The bytes are streamed through and
    /// never stored. Satellite may be printed with its attribution; Street View is screen-only.
    /// </summary>
    [HttpGet("imagery/{kind}")]
    public async Task<IActionResult> GetImagery(string kind, [FromQuery] string erf, [FromQuery] string municipality,
        [FromQuery] string? suburb, [FromQuery] string? sg26, CancellationToken cancellationToken)
    {
        if (!_imagery.IsConfigured) return NotFound();

        var location = await _reports.GetLocationAsync(municipality, erf, suburb, sg26, cancellationToken);
        if (location is null) return NotFound();

        var url = kind.ToLowerInvariant() switch
        {
            "satellite" => _imagery.SatelliteUrl(location.Lat, location.Lng),
            "streetview" => _imagery.StreetViewUrl(location.Lat, location.Lng),
            _ => null,
        };
        if (url is null) return ValidationFailed("kind", "kind must be satellite or streetview.");

        var http = _httpFactory.CreateClient("imagery");
        using var upstream = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // Street View answers 404 where there is no panorama.
        if (!upstream.IsSuccessStatusCode) return StatusCode((int)upstream.StatusCode);

        var bytes = await upstream.Content.ReadAsByteArrayAsync(cancellationToken);
        return File(bytes, upstream.Content.Headers.ContentType?.MediaType ?? "image/png");
    }

    private BadRequestObjectResult ValidationFailed(string field, string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] })
        {
            Type = "https://httpstatuses.io/400",
            Title = "Validation failed",
        });

    private ObjectResult CityUnavailable() =>
        StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Type = "https://httpstatuses.io/502",
            Title = "Property data unavailable",
            Status = StatusCodes.Status502BadGateway,
            Detail = "The City of Cape Town's property services did not respond. Try again in a few minutes.",
        });
}
