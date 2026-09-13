using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for receiving alerts from the local warehouse API, and for resolving
/// them from head office.
/// </summary>
[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(
        IAlertService alertService,
        ILogger<AlertsController> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// Receives an alert from the local warehouse API.
    /// Requires X-Api-Key header for authentication. The key is validated by middleware in Program.cs, which also scopes
    /// it to the country it belongs to.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("alerts_ingest")]
    public async Task<ActionResult<ApiResponse<string>>> Create(
        [FromBody] CreateAlertRequest request,
        CancellationToken cancellationToken)
    {
        // Note: X-Api-Key validation is handled by middleware in Program.cs, which
        // stores the country the key is scoped to in HttpContext.Items. If we reach
        // here, the key was validated.
        var countryCode = (string)HttpContext.Items["LocalApiCountryCode"]!;

        var result = await _alertService.ReceiveAlertAsync(request, countryCode, cancellationToken);

        return result switch
        {
            AlertReceptionResult.Success => Ok(ApiResponse<string>.Ok(
                "Alert created successfully.", "Alert received and processed.")),
            AlertReceptionResult.WarehouseNotFound => NotFound(ApiResponse<string>.Fail(
                $"Warehouse '{request.WarehouseReference}' not found.")),
            AlertReceptionResult.WarehouseOutOfScope => Unauthorized(ApiResponse<string>.Fail(
                "The provided API key is not authorized for this warehouse.")),
            AlertReceptionResult.InvalidType => BadRequest(ApiResponse<string>.Fail(
                "Invalid alert type. Expected temperature, humidity, condition or expiration.")),
            _ => StatusCode(500, ApiResponse<string>.Fail("Unexpected error.")),
        };
    }

    /// <summary>
    /// Resolves an alert. Called by head office staff once the issue is under
    /// control; pushes the resolution back to the country API that raised it.
    /// </summary>
    [HttpPatch("{id:guid}/resolve")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<string>>> Resolve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _alertService.ResolveAlertAsync(id, cancellationToken);

        return result switch
        {
            AlertResolutionResult.Success => Ok(ApiResponse<string>.Ok(
                "Alert resolved successfully.", "Alert resolved.")),
            AlertResolutionResult.NotFound => NotFound(ApiResponse<string>.Fail(
                $"Alert '{id}' not found.")),
            _ => StatusCode(500, ApiResponse<string>.Fail("Unexpected error.")),
        };
    }
}
