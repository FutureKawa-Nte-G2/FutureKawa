using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for synchronizing measurements from the local API and providing read access to stored measurements.
/// </summary>
public interface IMeasurementSyncService
{
    /// <summary>
    /// Synchronizes measurements for all warehouses by calling the local API and persisting the results.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task SyncAllWarehousesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all stored measurements for a specific warehouse, sorted by date (newest first).
    /// </summary>
    /// <param name="warehouseId">The warehouse identifier.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A list of measurement response DTOs.</returns>
    Task<IEnumerable<MeasurementResponseDto>> GetMeasurementsByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all stored measurements across all warehouses, sorted by date (newest first).
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A list of measurement response DTOs.</returns>
    Task<IEnumerable<MeasurementResponseDto>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default);
}