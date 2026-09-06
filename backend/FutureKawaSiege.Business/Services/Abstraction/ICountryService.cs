using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for managing countries.
/// </summary>
public interface ICountryService
{
    /// <summary>
    /// Retrieves all countries.
    /// </summary>
    Task<CountryListResponseDto> GetCountriesAsync(CancellationToken cancellationToken = default);
}
