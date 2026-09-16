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

    /// <summary>
    /// Retrieves the batches of a warehouse whose storage period overlaps the given
    /// period (#85 — "batches affected by an alert"). A batch's own period is
    /// <c>StoredAt</c> -> <c>ShippedAt</c> (or "still in stock" when null); pass
    /// <c>periodEnd: null</c> for a period that is still ongoing (e.g. an active
    /// alert). Includes <c>Warehouse.Country</c> and <c>Farm</c> for display fields.
    /// Ordered by <c>StoredAt</c> ascending.
    /// </summary>
    Task<IEnumerable<Batch>> GetOverlappingWarehousePeriodAsync(
        Guid warehouseId,
        DateTime periodStart,
        DateTime? periodEnd,
        CancellationToken cancellationToken = default);
}
