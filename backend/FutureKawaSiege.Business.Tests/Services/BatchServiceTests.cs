using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class BatchServiceTests
{
    private readonly IBatchRepository _batchRepository = Substitute.For<IBatchRepository>();
    private readonly BatchService _service;

    public BatchServiceTests()
    {
        _service = new BatchService(_batchRepository);
    }

    [Fact]
    public async Task GetBatchesAsync_Should_ReturnPaginatedResults()
    {
        // Arrange
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };

        var batches = new List<Batch>
        {
            CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-10)),
            CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-5))
        };

        _batchRepository.GetPagedAsync(null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns((batches, 2));

        // Act
        var result = await _service.GetBatchesAsync(null, null, 1, 10);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(2, result.Batches.Count());
    }

    [Fact]
    public async Task GetBatchesAsync_Should_FilterByCountryCode()
    {
        // Arrange
        var countryCode = "BR";

        _batchRepository.GetPagedAsync(countryCode, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Batch>(), 0));

        // Act
        var result = await _service.GetBatchesAsync(countryCode, null, 1, 10);

        // Assert
        await _batchRepository.Received(1).GetPagedAsync(countryCode, null, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBatchesAsync_Should_FilterByWarehouseId()
    {
        // Arrange
        var warehouseId = Guid.NewGuid();

        _batchRepository.GetPagedAsync(null, warehouseId, 1, 10, Arg.Any<CancellationToken>())
            .Returns((new List<Batch>(), 0));

        // Act
        var result = await _service.GetBatchesAsync(null, warehouseId, 1, 10);

        // Assert
        await _batchRepository.Received(1).GetPagedAsync(null, warehouseId, 1, 10, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(366, "expired")]  // More than 365 days = expired
    [InlineData(365, "expired")]  // Exactly 365 days = expired (per business rule: "dépassant 365 jours")
    [InlineData(364, "compliant")] // Less than 365 days = compliant
    [InlineData(100, "compliant")] // Way less than 365 days = compliant
    [InlineData(400, "expired")]   // Way more than 365 days = expired
    public async Task GetBatchesAsync_Should_ComputeCorrectStatus_BasedOnStorageDuration(
        int daysInStorage, string expectedStatus)
    {
        // Arrange
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };

        var batch = CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-daysInStorage));

        _batchRepository.GetPagedAsync(null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(([batch], 1));

        // Act
        var result = await _service.GetBatchesAsync(null, null, 1, 10);
        var batchDto = result.Batches.First();

        // Assert
        Assert.Equal(expectedStatus, batchDto.Status);
    }
    [Fact]
    public async Task GetBatchesAsync_Should_ReturnAlertStatus_When_WarehouseHasActiveAlert()
    {
        // Arrange: batch well within the 365-day window, but its warehouse has an
        // active sensor alert -> the display status must be "alert", not "compliant".
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        warehouse.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };
        var batch = CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-10));
        _batchRepository.GetPagedAsync(null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(([batch], 1));
        // Act
        var result = await _service.GetBatchesAsync(null, null, 1, 10);
        // Assert
        Assert.Equal("alert", result.Batches.First().Status);
    }
    [Fact]
    public async Task GetBatchesAsync_Should_ReturnAlertStatus_OverExpired_When_BothApply()
    {
        // Arrange: a batch older than 365 days AND stored in a warehouse with an
        // active alert. Assumed priority (to confirm with the team): the active
        // alert wins over the age-based "expired" status.
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        warehouse.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Humidity,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };
        var batch = CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-400));
        _batchRepository.GetPagedAsync(null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(([batch], 1));
        // Act
        var result = await _service.GetBatchesAsync(null, null, 1, 10);
        // Assert
        Assert.Equal("alert", result.Batches.First().Status);
    }
    [Fact]
    public async Task GetBatchesAsync_Should_IgnoreResolvedAlerts_When_ComputingStatus()
    {
        // Arrange: the warehouse only has a resolved alert -> should not show "alert".
        // This also covers the "status must come back to compliant" expectation for
        // the part that IS implemented today (resolved alerts are simply ignored);
        // the resolution mechanism itself (Active -> Resolved) is tracked separately.
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        warehouse.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Type = AlertType.Temperature,
            Status = AlertStatus.Resolved,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            ResolvedAt = DateTime.UtcNow.AddDays(-1)
        });
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };
        var batch = CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-10));
        _batchRepository.GetPagedAsync(null, null, 1, 10, Arg.Any<CancellationToken>())
            .Returns(([batch], 1));
        // Act
        var result = await _service.GetBatchesAsync(null, null, 1, 10);
        // Assert
        Assert.Equal("compliant", result.Batches.First().Status);
    }
    [Fact]
    public async Task GetBatchByIdAsync_Should_ReturnMappedBatch_When_Found()
    {
        // Arrange
        var country = new Country { Id = Guid.NewGuid(), Code = "BR", Name = "Brésil" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Santos", Country = country };
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Fazenda Boa Vista" };
        var batch = CreateBatch(Guid.NewGuid(), warehouse, farm, DateTime.UtcNow.AddDays(-10));

        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        // Act
        var result = await _service.GetBatchByIdAsync(batch.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(batch.Id, result!.Id);
        Assert.Equal(warehouse.Id, result.WarehouseId);
        Assert.Equal(country.Code, result.CountryCode);
        Assert.Equal("compliant", result.Status);
    }

    [Fact]
    public async Task GetBatchByIdAsync_Should_ReturnNull_When_NotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _batchRepository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        // Act
        var result = await _service.GetBatchByIdAsync(id);

        // Assert
        Assert.Null(result);
    }

    private static Batch CreateBatch(Guid id, Warehouse warehouse, Farm farm, DateTime storedAt)
    {
        return new Batch
        {
            Id = id,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            FarmId = farm.Id,
            Farm = farm,
            Reference = $"BATCH-{id.ToString()[..8]}",
            StoredAt = storedAt,
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
    }
}