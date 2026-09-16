using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for managing warehouses.
/// </summary>
public interface IWarehouseService
{
    /// <summary>
    /// Retrieves all warehouses, optionally filtered by country code.
    /// </summary>
    Task<WarehouseListResponseDto> GetWarehousesAsync(
        string? countryCode,
        CancellationToken cancellationToken = default);
}
