using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class AlertServiceTests
{
    private readonly IBatchAlertRepository _alertRepository = Substitute.For<IBatchAlertRepository>();
    private readonly IBatchRepository _batchRepository = Substitute.For<IBatchRepository>();
    private readonly IWarehouseRepository _warehouseRepository = Substitute.For<IWarehouseRepository>();
    private readonly ILogger<AlertService> _logger = Substitute.For<ILogger<AlertService>>();
    private readonly AlertService _service;

    public AlertServiceTests()
    {
        _service = new AlertService(_alertRepository, _batchRepository, _warehouseRepository, _logger);
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_CreateAlert_When_ValidRequest()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });
        _alertRepository.ExistsActiveAsync(batchId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature",
            MeasuredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<BatchAlert>(a => 
                a.WarehouseId == warehouseId && 
                a.BatchId == batchId && 
                a.Type == AlertType.Temperature &&
                a.Status == AlertStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnFalse_When_WarehouseNotFound()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns((Warehouse?)null);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.False(result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<BatchAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnFalse_When_BatchNotFound()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.False(result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<BatchAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnFalse_When_InvalidAlertType()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "invalid_type"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.False(result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<BatchAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnTrue_ButNotCreate_When_AlertAlreadyExists()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });
        _alertRepository.ExistsActiveAsync(batchId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(true); // Alert already exists

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result); // Returns true for idempotency
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<BatchAlert>(), Arg.Any<CancellationToken>());
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
        var batchId = Guid.NewGuid();
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });
        _alertRepository.ExistsActiveAsync(batchId, expectedType, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = inputType
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<BatchAlert>(a => a.Type == expectedType),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetMeasuredAt_When_Provided()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var measuredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });
        _alertRepository.ExistsActiveAsync(batchId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature",
            MeasuredAt = measuredAt
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<BatchAlert>(a => a.MeasuredAt == measuredAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetCreatedAt_ToUtcNow()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var beforeTest = DateTime.UtcNow.AddSeconds(-1);
        
        _warehouseRepository.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Warehouse { Id = warehouseId, Name = "Test Warehouse" });
        _batchRepository.GetByIdAsync(batchId, Arg.Any<CancellationToken>())
            .Returns(new Batch { Id = batchId, Reference = "BATCH-001" });
        _alertRepository.ExistsActiveAsync(batchId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            BatchId = batchId,
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request);
        var afterTest = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.True(result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<BatchAlert>(a => a.CreatedAt >= beforeTest && a.CreatedAt <= afterTest),
            Arg.Any<CancellationToken>());
    }
}
