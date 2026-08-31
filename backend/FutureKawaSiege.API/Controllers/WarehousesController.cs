using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for accessing warehouses.
/// Provides endpoints to list all warehouses or filter by country.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ILogger<WarehousesController> _logger;

    public WarehousesController(
        IWarehouseService warehouseService,
        ILogger<WarehousesController> logger)
    {
        _warehouseService = warehouseService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all warehouses, optionally filtered by country code.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WarehouseListResponseDto>>> GetAll(
        [FromQuery] string? country,
        CancellationToken cancellationToken)
    {
        var warehouses = await _warehouseService.GetWarehousesAsync(country, cancellationToken);
        return Ok(ApiResponse<WarehouseListResponseDto>.Ok(warehouses));
    }
}
