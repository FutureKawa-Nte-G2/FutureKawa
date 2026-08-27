using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Business.Tests.Repositories;

public class MeasurementRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly MeasurementRepository _repository;

    public MeasurementRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _repository = new MeasurementRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetByWarehouseAsync_Should_ReturnOnlyMeasurementsOfWarehouse()
    {
        var warehouseA = CreateWarehouse("Warehouse A");
        var warehouseB = CreateWarehouse("Warehouse B");
        _context.Warehouses.AddRange(warehouseA, warehouseB);
        _context.Measurements.AddRange(
            CreateMeasurement(warehouseA.Id, new DateOnly(2026, 8, 1)),
            CreateMeasurement(warehouseA.Id, new DateOnly(2026, 8, 2)),
            CreateMeasurement(warehouseB.Id, new DateOnly(2026, 8, 1)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetByWarehouseAsync(warehouseA.Id);

        Assert.Equal(2, result.Count());
        Assert.All(result, m => Assert.Equal(warehouseA.Id, m.WarehouseId));
    }

    [Fact]
    public async Task GetByWarehouseAsync_Should_ReturnMeasurementsSortedNewestFirst()
    {
        var warehouse = CreateWarehouse("Warehouse A");
        _context.Warehouses.Add(warehouse);
        _context.Measurements.AddRange(
            CreateMeasurement(warehouse.Id, new DateOnly(2026, 8, 1)),
            CreateMeasurement(warehouse.Id, new DateOnly(2026, 8, 3)),
            CreateMeasurement(warehouse.Id, new DateOnly(2026, 8, 2)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetByWarehouseAsync(warehouse.Id);

        Assert.Equal(
            new[] { new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 1) },
            result.Select(m => m.MeasDate));
    }

    [Fact]
    public async Task GetByWarehouseAsync_Should_ReturnEmpty_When_NoMeasurementsForWarehouse()
    {
        var warehouse = CreateWarehouse("Warehouse A");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByWarehouseAsync(warehouse.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByWarehouseAsync_Should_IncludeWarehouseNavigation()
    {
        var warehouse = CreateWarehouse("Warehouse A");
        _context.Warehouses.Add(warehouse);
        _context.Measurements.Add(CreateMeasurement(warehouse.Id, new DateOnly(2026, 8, 1)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetByWarehouseAsync(warehouse.Id);

        Assert.All(result, m => Assert.NotNull(m.Warehouse));
    }

    [Fact]
    public async Task GetExistingAsync_Should_ReturnMeasurement_When_ExistsForWarehouseAndDate()
    {
        var warehouse = CreateWarehouse("Warehouse A");
        var measDate = new DateOnly(2026, 8, 1);
        var measurement = CreateMeasurement(warehouse.Id, measDate);
        _context.Warehouses.Add(warehouse);
        _context.Measurements.Add(measurement);
        await _context.SaveChangesAsync();

        var result = await _repository.GetExistingAsync(warehouse.Id, measDate);

        Assert.NotNull(result);
        Assert.Equal(measurement.Id, result.Id);
        Assert.Equal(warehouse.Id, result.WarehouseId);
        Assert.Equal(measDate, result.MeasDate);
    }

    [Fact]
    public async Task GetExistingAsync_Should_ReturnNull_When_NoMeasurementForDate()
    {
        var warehouse = CreateWarehouse("Warehouse A");
        _context.Warehouses.Add(warehouse);
        _context.Measurements.Add(CreateMeasurement(warehouse.Id, new DateOnly(2026, 8, 1)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetExistingAsync(warehouse.Id, new DateOnly(2026, 8, 2));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetExistingAsync_Should_ReturnNull_When_DateMatchesButWarehouseDiffers()
    {
        var warehouseA = CreateWarehouse("Warehouse A");
        var warehouseB = CreateWarehouse("Warehouse B");
        var measDate = new DateOnly(2026, 8, 1);
        _context.Warehouses.AddRange(warehouseA, warehouseB);
        _context.Measurements.Add(CreateMeasurement(warehouseA.Id, measDate));
        await _context.SaveChangesAsync();

        var result = await _repository.GetExistingAsync(warehouseB.Id, measDate);

        Assert.Null(result);
    }

    private static Warehouse CreateWarehouse(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Reference = $"REF-{name}",
    };

    private static Measurement CreateMeasurement(Guid warehouseId, DateOnly measDate) => new()
    {
        Id = Guid.NewGuid(),
        WarehouseId = warehouseId,
        MeasDate = measDate,
        AvgMeasTemp = 25.0m,
        MaxMeasTemp = 27.5m,
        MinMeasTemp = 22.5m,
        AvgMeasHumidity = 60.0m,
        MaxMeasHumidity = 64.0m,
        MinMeasHumidity = 56.0m,
    };
}
