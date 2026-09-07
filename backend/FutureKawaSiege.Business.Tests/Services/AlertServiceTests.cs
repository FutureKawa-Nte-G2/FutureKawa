using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class AlertServiceTests
{
    private readonly IAlertRepository _alertRepository = Substitute.For<IAlertRepository>();
    private readonly IWarehouseRepository _warehouseRepository = Substitute.For<IWarehouseRepository>();
    private readonly ILogger<AlertService> _logger = Substitute.For<ILogger<AlertService>>();
    private readonly AlertService _service;

    public AlertServiceTests()
    {
        _service = new AlertService(_alertRepository, _warehouseRepository, _logger);
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_CreateAlert_When_ValidRequest()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature",
            MeasuredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => 
                a.WarehouseId == warehouseId && 
                a.Type == AlertType.Temperature &&
                a.Status == AlertStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnFalse_When_WarehouseNotFound()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns((Warehouse?)null);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.False(result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnFalse_When_InvalidAlertType()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "invalid_type"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.False(result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnTrue_ButNotCreate_When_AlertAlreadyExists()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(true); // Alert already exists

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result); // Returns true for idempotency
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("temperature", AlertType.Temperature)]
    [InlineData("TEMPERATURE", AlertType.Temperature)]
    [InlineData("Temperature", AlertType.Temperature)]
    [InlineData("humidity", AlertType.Humidity)]
    [InlineData("HUMIDITY", AlertType.Humidity)]
    [InlineData("Humidity", AlertType.Humidity)]
    public async Task ReceiveAlertAsync_Should_ParseAlertType_CaseInsensitive(
        string inputType, AlertType expectedType)
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _alertRepository.ExistsActiveAsync(warehouseId, expectedType, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = inputType
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.Type == expectedType),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetMeasuredAt_When_Provided()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var measuredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature",
            MeasuredAt = measuredAt
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.MeasuredAt == measuredAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetCreatedAt_ToUtcNow()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var beforeTest = DateTime.UtcNow.AddSeconds(-1);
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);
        var afterTest = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.CreatedAt >= beforeTest && a.CreatedAt <= afterTest),
            Arg.Any<CancellationToken>());
    }
}
