using System.Threading.Channels;
using FutureKawaSiege.Business.Services.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Background service that consumes scheduled shipment jobs and executes them
/// after their requested delay by calling <see cref="IOrderService.ShipOrderAsync"/>.
/// </summary>
public class OrderShipmentBackgroundService : BackgroundService
{
    private readonly Channel<ShipmentJob> _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderShipmentBackgroundService> _logger;

    public OrderShipmentBackgroundService(
        Channel<ShipmentJob> channel,
        IServiceProvider serviceProvider,
        ILogger<OrderShipmentBackgroundService> logger)
    {
        _channel = channel;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var delay = job.ExecuteAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
                await orderService.ShipOrderAsync(job.OrderId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-ship order {OrderId}", job.OrderId);
            }
        }
    }
}
