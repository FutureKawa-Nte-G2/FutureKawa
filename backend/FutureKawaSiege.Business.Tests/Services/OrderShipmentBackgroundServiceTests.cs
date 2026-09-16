using System.Threading.Channels;
using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class OrderShipmentBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_CallShipOrderAsync_AfterDelay()
    {
        var channel = Channel.CreateUnbounded<ShipmentJob>();
        var orderService = Substitute.For<IOrderService>();
        var serviceProvider = new FakeServiceProvider(orderService);
        var cts = new CancellationTokenSource();

        var backgroundService = new OrderShipmentBackgroundService(
            channel,
            serviceProvider,
            Substitute.For<ILogger<OrderShipmentBackgroundService>>());

        var executeTask = backgroundService.StartAsync(cts.Token);

        var orderId = Guid.NewGuid();
        await channel.Writer.WriteAsync(
            new ShipmentJob(orderId, DateTimeOffset.UtcNow.AddMilliseconds(50)),
            cts.Token);

        await Task.Delay(300, cts.Token);
        await cts.CancelAsync();

        try
        {
            await executeTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when the CancellationToken is cancelled.
        }

        await orderService.Received(1).ShipOrderAsync(orderId, Arg.Any<CancellationToken>());
    }

    private sealed class FakeServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly IOrderService _orderService;

        public FakeServiceProvider(IOrderService orderService)
        {
            _orderService = orderService;
            ServiceProvider = this;
        }

        public IServiceProvider ServiceProvider { get; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IOrderService))
                return _orderService;
            if (serviceType == typeof(IServiceScopeFactory))
                return this;
            return null;
        }

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }
}
