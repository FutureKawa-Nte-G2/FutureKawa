using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

public class AlertService : IAlertService
{
    private readonly IBatchAlertRepository _alertRepository;
    private readonly IBatchRepository _batchRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IBatchAlertRepository alertRepository,
        IBatchRepository batchRepository,
        IWarehouseRepository warehouseRepository,
        ILogger<AlertService> logger)
    {
        _alertRepository = alertRepository;
        _batchRepository = batchRepository;
        _warehouseRepository = warehouseRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> ReceiveAlertAsync(CreateAlertRequest request, CancellationToken cancellationToken = default)
    {
        // Validate warehouse exists
        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId, cancellationToken);
        if (warehouse == null)
        {
            _logger.LogWarning("Alert rejected: warehouse {WarehouseId} not found", request.WarehouseId);
            return false;
        }

        // Validate batch exists
        var batch = await _batchRepository.GetByIdAsync(request.BatchId, cancellationToken);
        if (batch == null)
        {
            _logger.LogWarning("Alert rejected: batch {BatchId} not found", request.BatchId);
            return false;
        }

        // Parse alert type
        if (!Enum.TryParse<AlertType>(request.Type, true, out var alertType))
        {
            _logger.LogWarning("Alert rejected: invalid alert type {Type}", request.Type);
            return false;
        }

        // Check for idempotency: active alert already exists for this batch and type
        var exists = await _alertRepository.ExistsActiveAsync(request.BatchId, alertType, cancellationToken);
        if (exists)
        {
            _logger.LogInformation(
                "Alert ignored: active alert already exists for batch {BatchId} and type {Type}",
                request.BatchId, alertType);
            return true; // Idempotent success
        }

        // Create the alert
        var alert = new BatchAlert
        {
            Id = Guid.NewGuid(),
            WarehouseId = request.WarehouseId,
            BatchId = request.BatchId,
            Type = alertType,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow,
            MeasuredAt = request.MeasuredAt
        };

        await _alertRepository.AddAsync(alert, cancellationToken);

        _logger.LogInformation(
            "Alert created: {AlertId} for batch {BatchId}, type {Type}, warehouse {WarehouseId}",
            alert.Id, request.BatchId, alertType, request.WarehouseId);

        return true;
    }
}
