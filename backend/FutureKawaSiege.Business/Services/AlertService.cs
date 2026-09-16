using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

public class AlertService : IAlertService
{
    private readonly IAlertRepository _alertRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IBatchRepository _batchRepository;
    private readonly ILocalAlertPushClient _localAlertPushClient;
    private readonly IAlertEmailService _alertEmailService;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IAlertRepository alertRepository,
        IWarehouseRepository warehouseRepository,
        IBatchRepository batchRepository,
        ILocalAlertPushClient localAlertPushClient,
        IAlertEmailService alertEmailService,
        ILogger<AlertService> logger)
    {
        _alertRepository = alertRepository;
        _warehouseRepository = warehouseRepository;
        _batchRepository = batchRepository;
        _localAlertPushClient = localAlertPushClient;
        _alertEmailService = alertEmailService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AlertReceptionResult> ReceiveAlertAsync(
        CreateAlertRequest request,
        string countryCode,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouseRepository.GetByReferenceAsync(request.WarehouseReference, cancellationToken);
        if (warehouse == null)
        {
            _logger.LogWarning("Alert rejected: warehouse reference {WarehouseReference} not found", request.WarehouseReference);
            return AlertReceptionResult.WarehouseNotFound;
        }

        // The API key only authenticates the request; it must also authorize it for
        // this specific warehouse, otherwise a key compromised in one country could
        // forge alerts for any warehouse in any other country.
        if (!string.Equals(warehouse.Country.Code, countryCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Alert rejected: warehouse {WarehouseReference} belongs to country {WarehouseCountry}, not {CallerCountry}",
                request.WarehouseReference, warehouse.Country.Code, countryCode);
            return AlertReceptionResult.WarehouseOutOfScope;
        }

        // Parse alert type
        if (!Enum.TryParse<AlertType>(request.Type, true, out var alertType))
        {
            _logger.LogWarning("Alert rejected: invalid alert type {Type}", request.Type);
            return AlertReceptionResult.InvalidType;
        }

        // Check for idempotency: active alert already exists for this warehouse and type
        var exists = await _alertRepository.ExistsActiveAsync(warehouse.Id, alertType, cancellationToken);
        if (exists)
        {
            _logger.LogInformation(
                "Alert ignored: active alert already exists for warehouse {WarehouseReference} and type {Type}",
                request.WarehouseReference, alertType);
            return AlertReceptionResult.Success; // Idempotent success
        }

        // Create the alert
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = alertType,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow,
            MeasuredAt = request.MeasuredAt,
            SourceAlertId = request.SourceAlertId
        };

        await _alertRepository.AddAsync(alert, cancellationToken);

        _logger.LogInformation(
            "Alert created: {AlertId} for warehouse {WarehouseReference}, type {Type}",
            alert.Id, request.WarehouseReference, alertType);

        // Attached only after the save: `warehouse` comes from an AsNoTracking
        // query, and attaching it before AddAsync makes EF treat the whole
        // graph (Warehouse + Country) as new rows to insert.
        alert.Warehouse = warehouse;
        await _alertEmailService.SendAlertCreatedNotificationAsync(alert, CancellationToken.None);

        return AlertReceptionResult.Success;
    }

    /// <inheritdoc/>
    public async Task<AlertResolutionResult> ResolveAlertAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetByIdAsync(alertId, cancellationToken);
        if (alert == null)
        {
            return AlertResolutionResult.NotFound;
        }

        if (alert.Status == AlertStatus.Resolved)
        {
            // Idempotent: already resolved, nothing left to do or push.
            return AlertResolutionResult.Success;
        }

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = DateTime.UtcNow;
        await _alertRepository.UpdateAsync(alert, cancellationToken);

        _logger.LogInformation("Alert {AlertId} resolved", alert.Id);

        if (alert.SourceAlertId is not null)
        {
            await _localAlertPushClient.PushResolutionAsync(
                alert.Warehouse.Country.Code,
                alert.Warehouse.Reference,
                alert.SourceAlertId.Value,
                cancellationToken);
        }

        return AlertResolutionResult.Success;
    }

    /// <inheritdoc/>
    public async Task<AlertListResponseDto> GetAlertsAsync(
        string? countryCode,
        Guid? warehouseId,
        AlertStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _alertRepository.GetPagedAsync(
            countryCode, warehouseId, status, page, pageSize, cancellationToken);

        var alerts = items.Select(MapToDto);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new AlertListResponseDto
        {
            Alerts = alerts,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AlertBatchDto>?> GetAlertBatchesAsync(
        Guid alertId, CancellationToken cancellationToken = default)
    {
        var alert = await _alertRepository.GetByIdAsync(alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var batches = await _batchRepository.GetOverlappingWarehousePeriodAsync(
            alert.WarehouseId, alert.CreatedAt, alert.ResolvedAt, cancellationToken);

        return batches.Select(MapToAlertBatchDto);
    }

    /// <summary>
    /// Maps an alert to its DTO, exposing Type/Status as lowercase strings on the
    /// wire (this project's convention for backend enums, see BatchStatus/Batch.ComputeStatus)
    /// even though the entity enum members are PascalCase.
    /// </summary>
    private static AlertListItemDto MapToDto(Alert alert)
    {
        return new AlertListItemDto
        {
            Id = alert.Id,
            WarehouseId = alert.WarehouseId,
            WarehouseName = alert.Warehouse.Name,
            CountryCode = alert.Warehouse.Country.Code,
            CountryName = alert.Warehouse.Country.Name,
            Type = alert.Type.ToString().ToLowerInvariant(),
            Status = alert.Status.ToString().ToLowerInvariant(),
            CreatedAt = alert.CreatedAt,
            ResolvedAt = alert.ResolvedAt,
            MeasuredAt = alert.MeasuredAt
        };
    }

    private static AlertBatchDto MapToAlertBatchDto(Batch batch)
    {
        return new AlertBatchDto
        {
            Id = batch.Id,
            CountryCode = batch.Warehouse.Country.Code,
            BatchRef = batch.Reference,
            FarmName = batch.Farm.Name,
            QualityGrade = batch.QualityGrade.ToString(),
            EnteredAt = batch.StoredAt
        };
    }
}
