using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Repositories;

namespace FutureKawaSiege.Business.Services;

public class CountryService : ICountryService
{
    private readonly ICountryRepository _countryRepository;

    public CountryService(ICountryRepository countryRepository)
    {
        _countryRepository = countryRepository;
    }

    /// <inheritdoc/>
    public async Task<CountryListResponseDto> GetCountriesAsync(CancellationToken cancellationToken = default)
    {
        var countries = await _countryRepository.GetAllAsync(cancellationToken);
        return new CountryListResponseDto
        {
            Countries = countries.Select(c => new CountryDto
            {
                Code = c.Code,
                Name = c.Name
            })
        };
    }
}
