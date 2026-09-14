using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for receiving alerts from the local warehouse API, listing them and
/// resolving them from head office.
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

    /// <summary>
    /// Returns a paginated list of alerts, most recent first. Requires JWT
    /// authentication (head office staff). Supports filtering by country code,
    /// warehouse ID and status (#85).
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<AlertListResponseDto>>> GetAll(
        [FromQuery] string? country,
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            return BadRequest(ApiResponse<AlertListResponseDto>.Fail("Page must be at least 1."));
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(ApiResponse<AlertListResponseDto>.Fail("PageSize must be between 1 and 100."));
        }

        AlertStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AlertStatus>(status, true, out var statusValue))
            {
                return BadRequest(ApiResponse<AlertListResponseDto>.Fail(
                    "Invalid status. Expected active or resolved."));
            }
            parsedStatus = statusValue;
        }

        var result = await _alertService.GetAlertsAsync(
            country, warehouseId, parsedStatus, page, pageSize, cancellationToken);

        return Ok(ApiResponse<AlertListResponseDto>.Ok(result));
    }
}
