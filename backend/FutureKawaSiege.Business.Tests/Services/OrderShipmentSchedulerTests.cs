using System.Threading.Channels;
using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;

namespace FutureKawaSiege.Business.Tests.Services;

public class OrderShipmentSchedulerTests
{
    [Fact]
    public async Task ScheduleShipmentAsync_Should_WriteJobToChannel_WithExpectedDelay()
    {
        var channel = Channel.CreateUnbounded<ShipmentJob>();
        IOrderShipmentScheduler scheduler = new OrderShipmentScheduler(channel);
        var orderId = Guid.NewGuid();

        await scheduler.ScheduleShipmentAsync(orderId, TimeSpan.FromSeconds(5));

        Assert.True(channel.Reader.TryRead(out var job));
        Assert.Equal(orderId, job.OrderId);
        Assert.True(job.ExecuteAt > DateTimeOffset.UtcNow.AddSeconds(4));
        Assert.True(job.ExecuteAt <= DateTimeOffset.UtcNow.AddSeconds(6));
    }
}
