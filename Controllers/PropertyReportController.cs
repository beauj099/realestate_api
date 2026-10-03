using System.Security.Claims;
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
    private readonly ForSaleListingsService _forSale;

    public PropertyReportController(PropertyReportService reports, ImageryLinkBuilder imagery, IHttpClientFactory httpFactory,
        ForSaleListingsService forSale)
    {
        _reports = reports;
        _imagery = imagery;
        _httpFactory = httpFactory;
        _forSale = forSale;
    }

    /// <summary>
    /// Address type-ahead ("bosm", "17 pine rd clar", "unit 5, 12 main") from the Cape Town and
    /// Johannesburg parcel records: numbered addresses are real erfs with their location, plus
    /// matching streets and suburbs. Fast; the app also asks <see cref="SuggestNational"/> and
    /// merges the two. <paramref name="lat"/>/<paramref name="lng"/> (optional) favour nearby places.
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? q, [FromQuery] double? lat, [FromQuery] double? lng,
        [FromServices] AddressSearchService search, CancellationToken cancellationToken) =>
        Ok(await search.CityAsync(q ?? "", lat, lng, cancellationToken));

    /// <summary>
    /// The City's address at a GPS point (Cape Town, Johannesburg): the erf under the pin, then
    /// its nearest neighbours, with the house number and the City's official suburb. Empty
    /// elsewhere.
    /// </summary>
    [HttpGet("suggest/at")]
    public async Task<IActionResult> SuggestAt([FromQuery] double lat, [FromQuery] double lng,
        [FromServices] AddressSearchService search, CancellationToken cancellationToken) =>
        Ok(await search.AtAsync(lat, lng, cancellationToken));

    /// <summary>
    /// The erf under a GPS point and its outline, for drawing the boundary on the app's pin map.
    /// 204 when there is no parcel with a boundary there.
    /// </summary>
    [HttpGet("parcel-at")]
    public async Task<IActionResult> ParcelAt([FromQuery] double lat, [FromQuery] double lng,
        CancellationToken cancellationToken)
    {
        if (lat is < -35.5 or > -21.5 || lng is < 16 or > 33.5)
            return ValidationFailed("lat", "The point must be in South Africa.");
        try
        {
            var parcel = await _reports.GetParcelAtAsync(lat, lng, cancellationToken);
            return parcel is null ? NoContent() : Ok(parcel);
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>
    /// The same search anywhere in South Africa, from OpenStreetMap (Photon): streets, numbered
    /// houses where mapped, suburbs and towns. Slower (seconds); ranked on the same scale as
    /// <see cref="Suggest"/>.
    /// </summary>
    [HttpGet("suggest/national")]
    public async Task<IActionResult> SuggestNational([FromQuery] string? q, [FromQuery] double? lat, [FromQuery] double? lng,
        [FromServices] AddressSearchService search, CancellationToken cancellationToken) =>
        Ok(await search.NationalAsync(q ?? "", lat, lng, cancellationToken));

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
            return Ok(await _reports.GetReportAsync(municipality, erf, suburb, sg26, includeComparables,
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
                cancellationToken));
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
    /// The neighbourhood map (SVG, ours from City open data): <c>mode=area</c> the comparable
    /// sales, numbered as in the report, in their radius; <c>mode=block</c> the property's block.
    /// 404 outside Cape Town.
    /// </summary>
    [HttpGet("{municipality}/{erf}/area-map.svg")]
    public async Task<IActionResult> GetAreaMap(string municipality, string erf, [FromQuery] string? suburb,
        [FromQuery] string? sg26, [FromQuery] string mode = "area", [FromQuery] int width = 0, [FromQuery] int height = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var svg = await _reports.GetAreaMapSvgAsync(municipality, erf, suburb, sg26,
                string.Equals(mode, "block", StringComparison.OrdinalIgnoreCase), width, height, cancellationToken);
            return svg is null ? NotFound() : Content(svg, "image/svg+xml", System.Text.Encoding.UTF8);
        }
        catch (HttpRequestException)
        {
            return CityUnavailable();
        }
    }

    /// <summary>
    /// Area details for a point: climate, population and density, household income and crime,
    /// each from its own free public source and each left out when that source is down.
    /// </summary>
    [HttpGet("area")]
    public async Task<IActionResult> GetArea([FromQuery] double lat, [FromQuery] double lng,
        [FromServices] AreaDetailsService area, CancellationToken cancellationToken)
    {
        if (lat is < -35.5 or > -21.5 || lng is < 16 or > 33.5)
            return ValidationFailed("lat", "The point must be in South Africa.");
        return Ok(await area.GetAsync(lat, lng, cancellationToken));
    }

    /// <summary>
    /// Homes for sale like this one, from Property24 (credited and linked there): the listings in
    /// the property's suburb most alike in bedrooms and size. <paramref name="p24Suburb"/> picks
    /// a different Property24 suburb from the ones offered.
    /// </summary>
    [HttpGet("{municipality}/{erf}/for-sale")]
    public async Task<IActionResult> GetForSale(string municipality, string erf, [FromQuery] string suburb,
        [FromQuery] string? township, [FromQuery] int? p24Suburb, [FromQuery] int? bedrooms,
        [FromQuery] double? floorM2, [FromQuery] double? erfM2, [FromQuery] int max = 3,
        [FromQuery] double? lat = null, [FromQuery] double? lng = null, [FromQuery] decimal? priceZar = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(suburb)) return ValidationFailed("suburb", "The report's suburb is required.");
        try
        {
            return Ok(await _forSale.FindAsync(municipality, suburb, township, p24Suburb, bedrooms, floorM2, erfM2, max,
                cancellationToken, lat, lng, priceZar));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Type = "https://httpstatuses.io/502",
                Title = "Property24 unavailable",
                Status = StatusCodes.Status502BadGateway,
                Detail = "Property24 did not respond. Try again in a few minutes.",
            });
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
