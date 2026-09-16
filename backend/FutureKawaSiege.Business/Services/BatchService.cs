using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;

namespace FutureKawaSiege.Business.Services;

public class BatchService : IBatchService
{
    private readonly IBatchRepository _batchRepository;

    public BatchService(IBatchRepository batchRepository)
    {
        _batchRepository = batchRepository;
    }

    /// <inheritdoc/>
    public async Task<BatchListResponseDto> GetBatchesAsync(
        string? countryCode,
        Guid? warehouseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _batchRepository.GetPagedAsync(
            countryCode, warehouseId, page, pageSize, cancellationToken);

        var batches = items.Select(MapToDto);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new BatchListResponseDto
        {
            Batches = batches,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    /// <inheritdoc/>
    public async Task<BatchListItemDto?> GetBatchByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var batch = await _batchRepository.GetByIdAsync(id, cancellationToken);
        return batch is null ? null : MapToDto(batch);
    }

    private static BatchListItemDto MapToDto(Batch batch)
    {
        return new BatchListItemDto
        {
            Id = batch.Id,
            CountryCode = batch.Warehouse.Country.Code,
            CountryName = batch.Warehouse.Country.Name,
            WarehouseId = batch.WarehouseId,
            WarehouseName = batch.Warehouse.Name,
            FarmName = batch.Farm.Name,
            BatchRef = batch.Reference,
            QualityGrade = batch.QualityGrade.ToString(),
            Status = ComputeStatus(batch),
            EnteredAt = batch.StoredAt,
            ShippedAt = batch.ShippedAt
        };
    }

    /// <summary>
    /// Computes the display status of a batch.
    /// "alert" when the batch's warehouse currently has at least one active sensor
    /// alert (temperature/humidity) — computed dynamically from
    /// <c>batch.Warehouse.Alerts</c> rather than stored on the batch, per the decision
    /// recorded for this PR. Because it is re-evaluated on every read:
    ///   - a batch entering an already-alerting warehouse is reflected immediately,
    ///     with no extra update needed;
    ///   - the status will automatically fall back to "expired"/"compliant" once the
    ///     alert is resolved (resolution mechanism itself tracked as a separate issue).
    /// Otherwise "expired" when stored 365 days or more ("dépassant 365 jours" —
    /// exact boundary still to confirm with the team), otherwise "compliant".
    /// Priority assumption: an active warehouse alert wins over the age-based
    /// "expired" status when both apply — confirm with the team if the reverse is
    /// wanted instead.
    /// </summary>
    private static string ComputeStatus(Batch batch)
    {
        // "Status alert" : the warehouse of this batch has an active alert.
        var hasActiveWarehouseAlert = batch.Warehouse.Alerts.Any(a => a.Status == AlertStatus.Active);
        if (hasActiveWarehouseAlert)
        {
            return "alert";
        }
        // Expired: stored for 365 days or more 
        var daysInStorage = (DateTime.UtcNow - batch.StoredAt).TotalDays;
        if (daysInStorage >= 365)
        {
            return "expired";
        }

        // Compliant: no issues
        return "compliant";
    }
}