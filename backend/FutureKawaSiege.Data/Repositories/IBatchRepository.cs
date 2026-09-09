using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

/// <summary>
/// Repository for accessing and managing batches.
/// </summary>
public interface IBatchRepository
{
    /// <summary>
    /// Retrieves a paginated list of batches with filtering and sorting.
    /// Excludes shipped batches by default.
    /// </summary>
    Task<(IEnumerable<Batch> Items, int TotalCount)> GetPagedAsync(
        string? countryCode,
        Guid? warehouseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single batch by ID with all navigations loaded.
    /// </summary>
    Task<Batch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a batch exists by ID.
    /// </summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
