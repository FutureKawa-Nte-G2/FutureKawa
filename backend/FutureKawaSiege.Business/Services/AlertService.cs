using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

public class AlertService : IAlertService
{
    private readonly IAlertRepository _alertRepository;
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IAlertRepository alertRepository,
        IWarehouseRepository warehouseRepository,
        ILogger<AlertService> logger)
    {
        _alertRepository = alertRepository;
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

        // Parse alert type
        if (!Enum.TryParse<AlertType>(request.Type, true, out var alertType))
        {
            _logger.LogWarning("Alert rejected: invalid alert type {Type}", request.Type);
            return false;
        }

        // Check for idempotency: active alert already exists for this warehouse and type
        var exists = await _alertRepository.ExistsActiveAsync(request.WarehouseId, alertType, cancellationToken);
        if (exists)
        {
            _logger.LogInformation(
                "Alert ignored: active alert already exists for warehouse {WarehouseId} and type {Type}",
                request.WarehouseId, alertType);
            return true; // Idempotent success
        }

        // Create the alert
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = request.WarehouseId,
            Type = alertType,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow,
            MeasuredAt = request.MeasuredAt
        };

        await _alertRepository.AddAsync(alert, cancellationToken);

        _logger.LogInformation(
            "Alert created: {AlertId} for warehouse {WarehouseId}, type {Type}",
            alert.Id, request.WarehouseId, alertType);

        return true;
    }
}
