using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class WarehouseServiceTests
{
    private readonly IWarehouseRepository _warehouseRepository = Substitute.For<IWarehouseRepository>();
    private readonly WarehouseService _service;

    public WarehouseServiceTests()
    {
        _service = new WarehouseService(_warehouseRepository);
    }

    [Fact]
    public async Task GetWarehousesAsync_WithoutCountryFilter_Should_ReturnAllWarehouses()
    {
        // Arrange
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouses = new List<Warehouse>
        {
            new() { Id = Guid.NewGuid(), Name = "Santos", Country = country },
            new() { Id = Guid.NewGuid(), Name = "Cerrado", Country = country }
        };

        _warehouseRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(warehouses);

        // Act
        var result = await _service.GetWarehousesAsync(null);

        // Assert
        Assert.Equal(2, result.Warehouses.Count());
        await _warehouseRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
        await _warehouseRepository.DidNotReceive().GetByCountryCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWarehousesAsync_WithCountryFilter_Should_ReturnFilteredWarehouses()
    {
        // Arrange
        var countryCode = "BR";
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouses = new List<Warehouse>
        {
            new() { Id = Guid.NewGuid(), Name = "Santos", Country = country }
        };

        _warehouseRepository.GetByCountryCodeAsync(countryCode, Arg.Any<CancellationToken>()).Returns(warehouses);

        // Act
        var result = await _service.GetWarehousesAsync(countryCode);

        // Assert
        Assert.Single(result.Warehouses);
        Assert.All(result.Warehouses, w => Assert.Equal("BR", w.CountryCode));
        await _warehouseRepository.Received(1).GetByCountryCodeAsync(countryCode, Arg.Any<CancellationToken>());
        await _warehouseRepository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWarehousesAsync_WithEmptyCountryFilter_Should_ReturnAllWarehouses()
    {
        // Arrange
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouses = new List<Warehouse>
        {
            new() { Id = Guid.NewGuid(), Name = "Santos", Country = country }
        };

        _warehouseRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(warehouses);

        // Act
        var result = await _service.GetWarehousesAsync("");

        // Assert
        Assert.Single(result.Warehouses);
        await _warehouseRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
        await _warehouseRepository.DidNotReceive().GetByCountryCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWarehousesAsync_Should_ReturnEmptyList_WhenNoWarehouses()
    {
        // Arrange
        _warehouseRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Warehouse>());

        // Act
        var result = await _service.GetWarehousesAsync(null);

        // Assert
        Assert.Empty(result.Warehouses);
    }
}
