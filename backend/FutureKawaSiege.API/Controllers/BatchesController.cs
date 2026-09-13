using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for managing coffee batches and FIFO operations.
/// Provides endpoints to list batches with filtering, sorting and pagination.
/// All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/batches")]
[Authorize]
public class BatchesController : ControllerBase
{
    private readonly IBatchService _batchService;
    private readonly ILogger<BatchesController> _logger;

    public BatchesController(
        IBatchService batchService,
        ILogger<BatchesController> logger)
    {
        _batchService = batchService;
        _logger = logger;
    }

    /// <summary>
    /// Returns a paginated list of batches sorted by entry date (FIFO).
    /// Excludes shipped batches by default.
    /// Supports filtering by country code and warehouse ID.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<BatchListResponseDto>>> GetAll(
        [FromQuery] string? country,
        [FromQuery] Guid? warehouseId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        if (page < 1)
        {
            return BadRequest(ApiResponse<BatchListResponseDto>.Fail("Page must be at least 1."));
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(ApiResponse<BatchListResponseDto>.Fail("PageSize must be between 1 and 100."));
        }

        var result = await _batchService.GetBatchesAsync(
            country, warehouseId, page, pageSize, cancellationToken);

        return Ok(ApiResponse<BatchListResponseDto>.Ok(result));
    }

    /// <summary>
    /// Returns a single batch by ID, regardless of its shipped status.
    /// Used by the batch detail page (measurement curves) to resolve the
    /// batch's warehouse and storage period.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<BatchListItemDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var batch = await _batchService.GetBatchByIdAsync(id, cancellationToken);

        if (batch is null)
        {
            return NotFound(ApiResponse<BatchListItemDto>.Fail("Batch not found."));
        }

        return Ok(ApiResponse<BatchListItemDto>.Ok(batch));
    }
}
