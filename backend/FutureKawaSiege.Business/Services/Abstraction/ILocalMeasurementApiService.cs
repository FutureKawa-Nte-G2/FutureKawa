using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for fetching aggregated measurements from the local warehouse API.
/// </summary>
public interface ILocalMeasurementApiService
{
    /// <summary>
    /// Fetches aggregated measurements (avg/min/max temperature and humidity)
    /// from the local API for the specified warehouse.
    /// </summary>
    /// <param name="warehouseId">The warehouse identifier to fetch measurements for.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The measurement DTO if successful; <c>null</c> if the URL is not configured or the call fails.</returns>
    Task<LocalMeasurementDto?> FetchMeasurementsAsync(Guid warehouseId, CancellationToken cancellationToken = default);
}