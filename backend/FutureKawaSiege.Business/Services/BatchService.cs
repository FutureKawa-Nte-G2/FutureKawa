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
    /// Computes the compliance status of a batch based on storage duration and active alerts.
    /// </summary>
    private static string ComputeStatus(Batch batch)
    {
        // Expired: stored for 365 days or more ("dépassant 365 jours")
        var daysInStorage = (DateTime.UtcNow - batch.StoredAt).TotalDays;
        if (daysInStorage >= 365)
        {
            return "expired";
        }

        // Alert: has any active alert
        if (batch.Alerts.Any(a => a.Status == AlertStatus.Active))
        {
            return "alert";
        }

        // Compliant: no issues
        return "compliant";
    }
}
