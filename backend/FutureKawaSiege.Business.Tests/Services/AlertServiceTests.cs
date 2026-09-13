using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
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
    private readonly ILocalAlertPushClient _localAlertPushClient = Substitute.For<ILocalAlertPushClient>();
    private readonly ILogger<AlertService> _logger = Substitute.For<ILogger<AlertService>>();
    private readonly AlertService _service;

    public AlertServiceTests()
    {
        _service = new AlertService(_alertRepository, _warehouseRepository, _localAlertPushClient, _logger);
    }

    private static Warehouse MakeWarehouse(Guid id, string reference, string countryCode) => new()
    {
        Id = id,
        Name = "Test Warehouse",
        Reference = reference,
        Country = new Country { Id = Guid.NewGuid(), Code = countryCode, Name = countryCode }
    };

    [Fact]
    public async Task ReceiveAlertAsync_Should_CreateAlert_When_ValidRequest()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "temperature",
            MeasuredAt = DateTime.UtcNow
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a =>
                a.WarehouseId == warehouseId &&
                a.Type == AlertType.Temperature &&
                a.Status == AlertStatus.Active),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnWarehouseNotFound_When_ReferenceUnknown()
    {
        // Arrange
        _warehouseRepository.GetByReferenceAsync("WH-UNKNOWN", Arg.Any<CancellationToken>())
            .Returns((Warehouse?)null);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-UNKNOWN",
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.WarehouseNotFound, result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnWarehouseOutOfScope_When_WarehouseBelongsToAnotherCountry()
    {
        // Arrange: the key is scoped to CO, but the warehouse belongs to BR.
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "CO");

        // Assert
        Assert.Equal(AlertReceptionResult.WarehouseOutOfScope, result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnInvalidType_When_TypeIsInvalid()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "invalid_type"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.InvalidType, result);
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_ReturnSuccess_ButNotCreate_When_AlertAlreadyExists()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(true); // Alert already exists

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result); // Idempotent success
        await _alertRepository.DidNotReceive().AddAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("temperature", AlertType.Temperature)]
    [InlineData("TEMPERATURE", AlertType.Temperature)]
    [InlineData("Temperature", AlertType.Temperature)]
    [InlineData("humidity", AlertType.Humidity)]
    [InlineData("HUMIDITY", AlertType.Humidity)]
    [InlineData("Humidity", AlertType.Humidity)]
    [InlineData("condition", AlertType.Condition)]
    [InlineData("CONDITION", AlertType.Condition)]
    [InlineData("expiration", AlertType.Expiration)]
    [InlineData("EXPIRATION", AlertType.Expiration)]
    public async Task ReceiveAlertAsync_Should_ParseAlertType_CaseInsensitive(
        string inputType, AlertType expectedType)
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, expectedType, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = inputType
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.Type == expectedType),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetMeasuredAt_When_Provided()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");
        var measuredAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "temperature",
            MeasuredAt = measuredAt
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.MeasuredAt == measuredAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetSourceAlertId_When_Provided()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");
        var sourceAlertId = Guid.NewGuid();

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Condition, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "condition",
            SourceAlertId = sourceAlertId
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.SourceAlertId == sourceAlertId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveAlertAsync_Should_SetCreatedAt_ToUtcNow()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();
        var warehouse = MakeWarehouse(warehouseId, "WH-BR-01", "BR");
        var beforeTest = DateTime.UtcNow.AddSeconds(-1);

        _warehouseRepository.GetByReferenceAsync("WH-BR-01", Arg.Any<CancellationToken>())
            .Returns(warehouse);
        _alertRepository.ExistsActiveAsync(warehouseId, AlertType.Temperature, Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-BR-01",
            Type = "temperature"
        };

        // Act
        var result = await _service.ReceiveAlertAsync(request, "BR");
        var afterTest = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.Equal(AlertReceptionResult.Success, result);
        await _alertRepository.Received(1).AddAsync(
            Arg.Is<Alert>(a => a.CreatedAt >= beforeTest && a.CreatedAt <= afterTest),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAlertAsync_Should_ReturnNotFound_When_AlertDoesNotExist()
    {
        // Arrange
        _alertRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Alert?)null);

        // Act
        var result = await _service.ResolveAlertAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(AlertResolutionResult.NotFound, result);
        await _alertRepository.DidNotReceive().UpdateAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAlertAsync_Should_MarkAlertResolved_When_Active()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Condition,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);

        // Act
        var result = await _service.ResolveAlertAsync(alert.Id);

        // Assert
        Assert.Equal(AlertResolutionResult.Success, result);
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
        await _alertRepository.Received(1).UpdateAsync(alert, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAlertAsync_Should_PushResolution_When_SourceAlertIdIsSet()
    {
        // Arrange
        var sourceAlertId = Guid.NewGuid();
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Condition,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            SourceAlertId = sourceAlertId
        };

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);

        // Act
        await _service.ResolveAlertAsync(alert.Id);

        // Assert
        await _localAlertPushClient.Received(1).PushResolutionAsync(
            "BR", "WH-BR-01", sourceAlertId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAlertAsync_Should_NotPushResolution_When_SourceAlertIdIsNull()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Condition,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            SourceAlertId = null
        };

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);

        // Act
        await _service.ResolveAlertAsync(alert.Id);

        // Assert
        await _localAlertPushClient.DidNotReceive().PushResolutionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAlertAsync_Should_BeIdempotent_When_AlreadyResolved()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var resolvedAt = DateTime.UtcNow.AddHours(-2);
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Condition,
            Status = AlertStatus.Resolved,
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            ResolvedAt = resolvedAt,
            SourceAlertId = Guid.NewGuid()
        };

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);

        // Act
        var result = await _service.ResolveAlertAsync(alert.Id);

        // Assert
        Assert.Equal(AlertResolutionResult.Success, result);
        Assert.Equal(resolvedAt, alert.ResolvedAt); // Untouched
        await _alertRepository.DidNotReceive().UpdateAsync(Arg.Any<Alert>(), Arg.Any<CancellationToken>());
        await _localAlertPushClient.DidNotReceive().PushResolutionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
