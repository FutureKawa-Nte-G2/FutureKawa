using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for managing batches and FIFO operations.
/// </summary>
public interface IBatchService
{
    /// <summary>
    /// Retrieves a paginated list of batches with optional filtering by country and warehouse.
    /// Excludes shipped batches. Results are sorted by entry date (FIFO).
    /// </summary>
    Task<BatchListResponseDto> GetBatchesAsync(
        string? countryCode,
        Guid? warehouseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
