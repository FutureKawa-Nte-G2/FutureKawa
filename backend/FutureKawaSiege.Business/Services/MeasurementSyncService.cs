using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Service that orchestrates measurement synchronization from the local API
/// and provides read access to stored measurements.
/// </summary>
public class MeasurementSyncService : IMeasurementSyncService
{
    private readonly AppDbContext _dbContext;
    private readonly ILocalMeasurementApiService _localApiService;
    private readonly IMeasurementRepository _measurementRepository;
    private readonly ILogger<MeasurementSyncService> _logger;

    public MeasurementSyncService(
        AppDbContext dbContext,
        ILocalMeasurementApiService localApiService,
        IMeasurementRepository measurementRepository,
        ILogger<MeasurementSyncService> logger)
    {
        _dbContext = dbContext;
        _localApiService = localApiService;
        _measurementRepository = measurementRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SyncAllWarehousesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting measurement sync for all warehouses...");

        var warehouses = await _dbContext.Warehouses
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var total = warehouses.Count;
        var persisted = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var warehouse in warehouses)
        {
            try
            {
                var dto = await _localApiService.FetchMeasurementsAsync(warehouse.Id, cancellationToken);

                if (dto is null)
                {
                    _logger.LogWarning("No measurement data returned for warehouse {WarehouseId} ({WarehouseName}).", warehouse.Id, warehouse.Name);
                    errors++;
                    continue;
                }

                // Idempotency check: skip if a measurement already exists for this warehouse + date
                var existing = await _measurementRepository.GetExistingAsync(warehouse.Id, dto.MeasDate, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation("Measurement already exists for warehouse {WarehouseId} on {MeasDate}. Skipping.", warehouse.Id, dto.MeasDate);
                    skipped++;
                    continue;
                }

                var measurement = new Measurement
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = warehouse.Id,
                    MeasDate = dto.MeasDate,
                    AvgMeasTemp = dto.AvgTemp,
                    MaxMeasTemp = dto.MaxTemp,
                    MinMeasTemp = dto.MinTemp,
                    AvgMeasHumidity = dto.AvgHumidity,
                    MinMeasHumidity = dto.MinHumidity,
                    MaxMeasHumidity = dto.MaxHumidity,
                };

                await _measurementRepository.AddAsync(measurement, cancellationToken);
                persisted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing measurements for warehouse {WarehouseId}.", warehouse.Id);
                errors++;
            }
        }

        _logger.LogInformation(
            "Measurement sync completed: {Total} warehouses processed, {Persisted} persisted, {Skipped} skipped (already exists), {Errors} errors.",
            total, persisted, skipped, errors);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<MeasurementResponseDto>> GetMeasurementsByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var measurements = await _measurementRepository.GetByWarehouseAsync(warehouseId, cancellationToken);

        return measurements
            .Where(m => m.WarehouseId == warehouseId)
            .Select(MapToDto);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<MeasurementResponseDto>> GetAllMeasurementsAsync(CancellationToken cancellationToken = default)
    {
        var measurements = await _measurementRepository.GetAllAsync(cancellationToken);

        return measurements.Select(MapToDto);
    }

    private static MeasurementResponseDto MapToDto(Measurement m) => new()
    {
        Id = m.Id,
        WarehouseId = m.WarehouseId,
        WarehouseName = m.Warehouse?.Name ?? string.Empty,
        MeasDate = m.MeasDate,
        AvgMeasTemp = m.AvgMeasTemp,
        MaxMeasTemp = m.MaxMeasTemp,
        MinMeasTemp = m.MinMeasTemp,
        AvgMeasHumidity = m.AvgMeasHumidity,
        MinMeasHumidity = m.MinMeasHumidity,
        MaxMeasHumidity = m.MaxMeasHumidity,
    };
}