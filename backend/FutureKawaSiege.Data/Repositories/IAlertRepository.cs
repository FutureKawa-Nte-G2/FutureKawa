using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

/// <summary>
/// Repository for managing warehouse alerts.
/// </summary>
public interface IAlertRepository
{
    /// <summary>
    /// Adds a new alert to the data store.
    /// </summary>
    Task AddAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active alerts for a specific warehouse.
    /// </summary>
    Task<IEnumerable<Alert>> GetActiveByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an active alert already exists for the given warehouse and type.
    /// Used for idempotency.
    /// </summary>
    Task<bool> ExistsActiveAsync(Guid warehouseId, AlertType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single alert by ID, including its warehouse and country, or
    /// null if it does not exist.
    /// </summary>
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes made to an existing alert.
    /// </summary>
    Task UpdateAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paginated list of alerts, optionally filtered by country code,
    /// warehouse ID and status (#85). Includes Warehouse and Warehouse.Country for
    /// display names. Ordered by CreatedAt descending (most recent first).
    /// </summary>
    Task<(IEnumerable<Alert> Items, int TotalCount)> GetPagedAsync(
        string? countryCode,
        Guid? warehouseId,
        AlertStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
