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
    private readonly IBatchRepository _batchRepository = Substitute.For<IBatchRepository>();
    private readonly ILocalAlertPushClient _localAlertPushClient = Substitute.For<ILocalAlertPushClient>();
    private readonly ILogger<AlertService> _logger = Substitute.For<ILogger<AlertService>>();
    private readonly AlertService _service;

    public AlertServiceTests()
    {
        _service = new AlertService(_alertRepository, _warehouseRepository, _batchRepository, _localAlertPushClient, _logger);
    }

    private static Warehouse MakeWarehouse(Guid id, string reference, string countryCode) => new()
    {
        Id = id,
        Name = "Test Warehouse",
        Reference = reference,
        Country = new Country { Id = Guid.NewGuid(), Code = countryCode, Name = countryCode }
    };

    private static Alert MakeAlert(Warehouse warehouse, AlertType type, AlertStatus status, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        Type = type,
        Status = status,
        CreatedAt = createdAt
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

    [Fact]
    public async Task GetAlertsAsync_Should_ReturnPaginatedResults()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alerts = new List<Alert>
        {
            MakeAlert(warehouse, AlertType.Temperature, AlertStatus.Active, DateTime.UtcNow.AddHours(-1)),
            MakeAlert(warehouse, AlertType.Humidity, AlertStatus.Resolved, DateTime.UtcNow.AddDays(-2))
        };

        _alertRepository.GetPagedAsync(null, null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns((alerts, 2));

        // Act
        var result = await _service.GetAlertsAsync(null, null, null, 1, 10);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(2, result.Alerts.Count());
    }

    [Fact]
    public async Task GetAlertsAsync_Should_FilterByCountryCode()
    {
        // Arrange
        var countryCode = "BR";

        _alertRepository.GetPagedAsync(countryCode, null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Alert>(), 0));

        // Act
        await _service.GetAlertsAsync(countryCode, null, null, 1, 10);

        // Assert
        await _alertRepository.Received(1).GetPagedAsync(countryCode, null, null, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAlertsAsync_Should_FilterByWarehouseId()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();

        _alertRepository.GetPagedAsync(null, warehouseId, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Alert>(), 0));

        // Act
        await _service.GetAlertsAsync(null, warehouseId, null, 1, 10);

        // Assert
        await _alertRepository.Received(1).GetPagedAsync(null, warehouseId, null, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAlertsAsync_Should_FilterByStatus()
    {
        // Arrange
        _alertRepository.GetPagedAsync(null, null, AlertStatus.Active, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Alert>(), 0));

        // Act
        await _service.GetAlertsAsync(null, null, AlertStatus.Active, 1, 10);

        // Assert
        await _alertRepository.Received(1).GetPagedAsync(null, null, AlertStatus.Active, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAlertsAsync_Should_MapFieldsCorrectly()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        warehouse.Name = "Santos";
        warehouse.Country.Name = "Brésil";
        var createdAt = DateTime.UtcNow.AddHours(-3);
        var measuredAt = DateTime.UtcNow.AddHours(-4);
        var alert = MakeAlert(warehouse, AlertType.Humidity, AlertStatus.Active, createdAt);
        alert.MeasuredAt = measuredAt;

        _alertRepository.GetPagedAsync(null, null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(([alert], 1));

        // Act
        var result = await _service.GetAlertsAsync(null, null, null, 1, 10);
        var dto = result.Alerts.Single();

        // Assert
        Assert.Equal(alert.Id, dto.Id);
        Assert.Equal(warehouse.Id, dto.WarehouseId);
        Assert.Equal("Santos", dto.WarehouseName);
        Assert.Equal("BR", dto.CountryCode);
        Assert.Equal("Brésil", dto.CountryName);
        Assert.Equal("humidity", dto.Type);
        Assert.Equal("active", dto.Status);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Null(dto.ResolvedAt);
        Assert.Equal(measuredAt, dto.MeasuredAt);
    }

    [Fact]
    public async Task GetAlertBatchesAsync_Should_ReturnNull_When_AlertNotFound()
    {
        // Arrange
        _alertRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Alert?)null);

        // Act
        var result = await _service.GetAlertBatchesAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlertBatchesAsync_Should_QueryBatchRepository_WithAlertWarehouseAndPeriod()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alert = MakeAlert(warehouse, AlertType.Temperature, AlertStatus.Active, DateTime.UtcNow.AddDays(-2));

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);
        _batchRepository.GetOverlappingWarehousePeriodAsync(
                warehouse.Id, alert.CreatedAt, alert.ResolvedAt, Arg.Any<CancellationToken>())
            .Returns(new List<Batch>());

        // Act
        await _service.GetAlertBatchesAsync(alert.Id);

        // Assert
        await _batchRepository.Received(1).GetOverlappingWarehousePeriodAsync(
            warehouse.Id, alert.CreatedAt, alert.ResolvedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAlertBatchesAsync_Should_ReturnMappedBatches_When_AlertExists()
    {
        // Arrange
        var warehouse = MakeWarehouse(Guid.NewGuid(), "WH-BR-01", "BR");
        var alert = MakeAlert(warehouse, AlertType.Temperature, AlertStatus.Active, DateTime.UtcNow.AddDays(-2));
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };
        var storedAt = DateTime.UtcNow.AddDays(-5);
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            FarmId = farm.Id,
            Farm = farm,
            Reference = "BATCH-BR-001",
            StoredAt = storedAt,
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };

        _alertRepository.GetByIdAsync(alert.Id, Arg.Any<CancellationToken>())
            .Returns(alert);
        _batchRepository.GetOverlappingWarehousePeriodAsync(
                warehouse.Id, alert.CreatedAt, alert.ResolvedAt, Arg.Any<CancellationToken>())
            .Returns(new List<Batch> { batch });

        // Act
        var result = await _service.GetAlertBatchesAsync(alert.Id);

        // Assert
        Assert.NotNull(result);
        var dto = Assert.Single(result!);
        Assert.Equal(batch.Id, dto.Id);
        Assert.Equal("BR", dto.CountryCode);
        Assert.Equal("BATCH-BR-001", dto.BatchRef);
        Assert.Equal("Fazenda Boa Vista", dto.FarmName);
        Assert.Equal("A", dto.QualityGrade);
        Assert.Equal(storedAt, dto.EnteredAt);
    }
}
