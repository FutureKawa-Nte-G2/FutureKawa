using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

/// <summary>
/// Repository for accessing warehouses.
/// </summary>
public interface IWarehouseRepository
{
    /// <summary>
    /// Retrieves all warehouses ordered by name.
    /// </summary>
    Task<IEnumerable<Warehouse>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves warehouses filtered by country code.
    /// </summary>
    Task<IEnumerable<Warehouse>> GetByCountryCodeAsync(string countryCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single warehouse by ID.
    /// </summary>
    Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
