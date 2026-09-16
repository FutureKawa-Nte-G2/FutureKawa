using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class CountryServiceTests
{
    private readonly ICountryRepository _countryRepository = Substitute.For<ICountryRepository>();
    private readonly CountryService _service;

    public CountryServiceTests()
    {
        _service = new CountryService(_countryRepository);
    }

    [Fact]
    public async Task GetCountriesAsync_Should_ReturnAllCountries()
    {
        // Arrange
        var countries = new List<Country>
        {
            new() { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" },
            new() { Id = Guid.NewGuid(), Code = "EC", Name = "Équateur" },
            new() { Id = Guid.NewGuid(), Code = "CO", Name = "Colombie" }
        };

        _countryRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(countries);

        // Act
        var result = await _service.GetCountriesAsync();

        // Assert
        Assert.Equal(3, result.Countries.Count());
        Assert.Contains(result.Countries, c => c.Code == "BR");
        Assert.Contains(result.Countries, c => c.Code == "EC");
        Assert.Contains(result.Countries, c => c.Code == "CO");
    }

    [Fact]
    public async Task GetCountriesAsync_Should_ReturnEmptyList_WhenNoCountries()
    {
        // Arrange
        _countryRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Country>());

        // Act
        var result = await _service.GetCountriesAsync();

        // Assert
        Assert.Empty(result.Countries);
    }
}
