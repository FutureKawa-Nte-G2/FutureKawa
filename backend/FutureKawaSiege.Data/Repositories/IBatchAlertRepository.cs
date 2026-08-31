using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

/// <summary>
/// Repository for managing batch alerts.
/// </summary>
public interface IBatchAlertRepository
{
    /// <summary>
    /// Adds a new alert to the data store.
    /// </summary>
    Task AddAsync(BatchAlert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active alerts for a specific batch.
    /// </summary>
    Task<IEnumerable<BatchAlert>> GetActiveByBatchAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an active alert already exists for the given batch and type.
    /// Used for idempotency.
    /// </summary>
    Task<bool> ExistsActiveAsync(Guid batchId, AlertType type, CancellationToken cancellationToken = default);
}
