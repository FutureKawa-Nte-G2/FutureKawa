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
}
