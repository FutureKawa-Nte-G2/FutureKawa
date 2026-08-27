using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FutureKawaSiege.Business.Tests.Services;

public class MeasurementSyncServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ILocalMeasurementApiService _localApiService = Substitute.For<ILocalMeasurementApiService>();
    private readonly IMeasurementRepository _measurementRepository = Substitute.For<IMeasurementRepository>();
    private readonly MeasurementSyncService _service;

    public MeasurementSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _service = new MeasurementSyncService(
            _context,
            _localApiService,
            _measurementRepository,
            Substitute.For<ILogger<MeasurementSyncService>>());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SyncAllWarehousesAsync_Should_Skip_When_MeasurementAlreadyExistsForWarehouseAndDate()
    {
        var warehouse = CreateWarehouse();
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var dto = CreateDto(new DateOnly(2026, 8, 1));
        _localApiService.FetchMeasurementsAsync(warehouse.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _measurementRepository.GetExistingAsync(warehouse.Id, dto.MeasDate, Arg.Any<CancellationToken>())
            .Returns(new Measurement { Id = Guid.NewGuid(), WarehouseId = warehouse.Id, MeasDate = dto.MeasDate });

        await _service.SyncAllWarehousesAsync();

        await _measurementRepository.DidNotReceive()
            .AddAsync(Arg.Any<Measurement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllWarehousesAsync_Should_Persist_When_NoMeasurementExistsForWarehouseAndDate()
    {
        var warehouse = CreateWarehouse();
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var dto = CreateDto(new DateOnly(2026, 8, 1));
        _localApiService.FetchMeasurementsAsync(warehouse.Id, Arg.Any<CancellationToken>()).Returns(dto);
        _measurementRepository.GetExistingAsync(warehouse.Id, dto.MeasDate, Arg.Any<CancellationToken>())
            .Returns((Measurement?)null);

        await _service.SyncAllWarehousesAsync();

        await _measurementRepository.Received(1)
            .AddAsync(Arg.Is<Measurement>(m =>
                m.WarehouseId == warehouse.Id &&
                m.MeasDate == dto.MeasDate &&
                m.AvgMeasTemp == dto.AvgTemp &&
                m.MaxMeasTemp == dto.MaxTemp &&
                m.MinMeasTemp == dto.MinTemp &&
                m.AvgMeasHumidity == dto.AvgHumidity &&
                m.MaxMeasHumidity == dto.MaxHumidity &&
                m.MinMeasHumidity == dto.MinHumidity),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllWarehousesAsync_Should_NotThrow_And_NotPersist_When_LocalApiDoesNotRespond()
    {
        var warehouse = CreateWarehouse();
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        // Simulates a network outage: the local API service returns null instead of throwing.
        _localApiService.FetchMeasurementsAsync(warehouse.Id, Arg.Any<CancellationToken>())
            .Returns((LocalMeasurementDto?)null);

        var exception = await Record.ExceptionAsync(() => _service.SyncAllWarehousesAsync());

        Assert.Null(exception);
        await _measurementRepository.DidNotReceive()
            .GetExistingAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _measurementRepository.DidNotReceive()
            .AddAsync(Arg.Any<Measurement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllWarehousesAsync_Should_ContinueSyncingOtherWarehouses_When_LocalApiFailsForOne()
    {
        var failingWarehouse = CreateWarehouse();
        var healthyWarehouse = CreateWarehouse();
        _context.Warehouses.AddRange(failingWarehouse, healthyWarehouse);
        await _context.SaveChangesAsync();

        var dto = CreateDto(new DateOnly(2026, 8, 1));
        _localApiService.FetchMeasurementsAsync(failingWarehouse.Id, Arg.Any<CancellationToken>())
            .Returns((LocalMeasurementDto?)null);
        _localApiService.FetchMeasurementsAsync(healthyWarehouse.Id, Arg.Any<CancellationToken>())
            .Returns(dto);
        _measurementRepository.GetExistingAsync(healthyWarehouse.Id, dto.MeasDate, Arg.Any<CancellationToken>())
            .Returns((Measurement?)null);

        await _service.SyncAllWarehousesAsync();

        await _measurementRepository.Received(1)
            .AddAsync(Arg.Is<Measurement>(m => m.WarehouseId == healthyWarehouse.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAllWarehousesAsync_Should_NotThrow_When_LocalApiThrows()
    {
        var warehouse = CreateWarehouse();
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        _localApiService.FetchMeasurementsAsync(warehouse.Id, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var exception = await Record.ExceptionAsync(() => _service.SyncAllWarehousesAsync());

        Assert.Null(exception);
        await _measurementRepository.DidNotReceive()
            .AddAsync(Arg.Any<Measurement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMeasurementsByWarehouseAsync_Should_ReturnMappedDtos()
    {
        var warehouse = CreateWarehouse();
        var measurement = new Measurement
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            MeasDate = new DateOnly(2026, 8, 1),
            AvgMeasTemp = 25.0m,
            MaxMeasTemp = 27.5m,
            MinMeasTemp = 22.5m,
            AvgMeasHumidity = 60.0m,
            MaxMeasHumidity = 64.0m,
            MinMeasHumidity = 56.0m,
            Warehouse = warehouse,
        };
        _measurementRepository.GetByWarehouseAsync(warehouse.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { measurement });

        var result = await _service.GetMeasurementsByWarehouseAsync(warehouse.Id);

        var dto = Assert.Single(result);
        Assert.Equal(measurement.Id, dto.Id);
        Assert.Equal(warehouse.Id, dto.WarehouseId);
        Assert.Equal(warehouse.Name, dto.WarehouseName);
        Assert.Equal(measurement.MeasDate, dto.MeasDate);
        Assert.Equal(measurement.AvgMeasTemp, dto.AvgMeasTemp);
        Assert.Equal(measurement.MaxMeasTemp, dto.MaxMeasTemp);
        Assert.Equal(measurement.MinMeasTemp, dto.MinMeasTemp);
        Assert.Equal(measurement.AvgMeasHumidity, dto.AvgMeasHumidity);
        Assert.Equal(measurement.MaxMeasHumidity, dto.MaxMeasHumidity);
        Assert.Equal(measurement.MinMeasHumidity, dto.MinMeasHumidity);
    }

    private static Warehouse CreateWarehouse() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Warehouse Test",
        Reference = "REF-TEST",
    };

    private static LocalMeasurementDto CreateDto(DateOnly measDate) => new()
    {
        AvgTemp = 25.0m,
        MaxTemp = 27.5m,
        MinTemp = 22.5m,
        AvgHumidity = 60.0m,
        MaxHumidity = 64.0m,
        MinHumidity = 56.0m,
        MeasDate = measDate,
    };
}
