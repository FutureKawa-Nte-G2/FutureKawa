using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

/// <summary>
/// Repository for accessing countries.
/// </summary>
public interface ICountryRepository
{
    /// <summary>
    /// Retrieves all countries ordered by name.
    /// </summary>
    Task<IEnumerable<Country>> GetAllAsync(CancellationToken cancellationToken = default);
}
