using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

public interface IMeasurementRepository
{
    /// <summary>
    /// Retrieves all measurements for a specific warehouse, sorted by date (newest first).
    /// </summary>
    Task<IEnumerable<Measurement>> GetByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all measurements across all warehouses, sorted by date (newest first).
    /// </summary>
    Task<IEnumerable<Measurement>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a measurement already exists for the given warehouse and date.
    /// Used for idempotency during sync.
    /// </summary>
    Task<Measurement?> GetExistingAsync(Guid warehouseId, DateTime measDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new measurement to the data store.
    /// </summary>
    Task AddAsync(Measurement measurement, CancellationToken cancellationToken = default);
}