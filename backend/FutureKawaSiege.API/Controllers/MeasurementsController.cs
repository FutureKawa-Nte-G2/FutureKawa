using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for accessing stored measurement history.
///
/// Provides endpoints to list all measurements or filter by warehouse.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/measurements")]
[Authorize]
public class MeasurementsController : ControllerBase
{
    private readonly IMeasurementSyncService _measurementSyncService;
    private readonly ILogger<MeasurementsController> _logger;

    public MeasurementsController(
        IMeasurementSyncService measurementSyncService,
        ILogger<MeasurementsController> logger)
    {
        _measurementSyncService = measurementSyncService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all stored measurements across all warehouses, sorted by date (newest first).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<MeasurementResponseDto>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var measurements = await _measurementSyncService.GetAllMeasurementsAsync(cancellationToken);
        return Ok(ApiResponse<IEnumerable<MeasurementResponseDto>>.Ok(measurements));
    }

    /// <summary>
    /// Returns all stored measurements for a specific warehouse, sorted by date (newest first).
    /// </summary>
    [HttpGet("{warehouseId:guid}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<MeasurementResponseDto>>>> GetByWarehouse(
        Guid warehouseId,
        CancellationToken cancellationToken)
    {
        var measurements = await _measurementSyncService.GetMeasurementsByWarehouseAsync(warehouseId, cancellationToken);
        return Ok(ApiResponse<IEnumerable<MeasurementResponseDto>>.Ok(measurements));
    }

    /// <summary>
    /// Manually triggers a measurement sync cycle for all warehouses.
    /// Useful for testing without waiting for the scheduled background service.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<ApiResponse<string>>> SyncNow(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Manual measurement sync triggered via API.");
        await _measurementSyncService.SyncAllWarehousesAsync(cancellationToken);
        return Ok(ApiResponse<string>.Ok("Sync completed.", "Measurement sync cycle executed successfully."));
    }
}