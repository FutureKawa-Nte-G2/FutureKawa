using FutureKawaSiege.Business.Services.Abstraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Background service that periodically synchronizes measurements from the local API
/// for all warehouses. The sync interval is configurable via
/// <c>MeasurementSync:IntervalMinutes</c> (default: 60 minutes).
/// </summary>
public class MeasurementSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MeasurementSyncBackgroundService> _logger;

    public MeasurementSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MeasurementSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _configuration.GetValue<int>("MeasurementSync:IntervalMinutes");
        if (intervalMinutes <= 0)
            intervalMinutes = 60;

        var interval = TimeSpan.FromMinutes(intervalMinutes);

        _logger.LogInformation("MeasurementSyncBackgroundService started. Interval: {IntervalMinutes} minutes.", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting scheduled measurement sync cycle...");

                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IMeasurementSyncService>();
                await syncService.SyncAllWarehousesAsync(stoppingToken);

                _logger.LogInformation("Scheduled measurement sync cycle completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled measurement sync cycle.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("MeasurementSyncBackgroundService stopped.");
    }
}