using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for receiving alerts from the local warehouse API.
/// This endpoint is NOT protected by JWT but by a shared API key header.
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
    /// Requires X-Api-Key header for authentication.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<string>>> Create(
        [FromBody] CreateAlertRequest request,
        CancellationToken cancellationToken)
    {
        // Note: X-Api-Key validation is handled by middleware in Program.cs
        // If we reach here, the key was validated

        var success = await _alertService.ReceiveAlertAsync(request, cancellationToken);

        if (!success)
        {
            return BadRequest(ApiResponse<string>.Fail(
                "Failed to create alert. Ensure warehouse exists, and type is valid (temperature/humidity)."));
        }

        return Ok(ApiResponse<string>.Ok("Alert created successfully.", "Alert received and processed."));
    }
}
