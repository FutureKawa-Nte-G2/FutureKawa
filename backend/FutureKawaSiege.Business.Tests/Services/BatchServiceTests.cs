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
