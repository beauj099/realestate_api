using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateApi.Application.Services;

namespace RealEstateApi.Controllers;

/// <summary>Admin only: each call reads one known property from every municipal source.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/data-sources")]
public class DataSourcesController : ControllerBase
{
    private readonly DataSourceHealthService _health;

    public DataSourcesController(DataSourceHealthService health)
    {
        _health = health;
    }

    /// <summary>Checks Cape Town, Johannesburg and the Tshwane roll against known properties.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var results = await _health.RunAsync(cancellationToken);
        return Ok(new { ok = results.All(r => r.Ok), checks = results });
    }
}
