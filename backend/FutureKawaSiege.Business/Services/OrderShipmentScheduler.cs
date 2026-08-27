using System.Threading.Channels;
using FutureKawaSiege.Business.Services.Abstraction;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// In-memory implementation of <see cref="IOrderShipmentScheduler"/> that writes
/// shipment jobs to a <see cref="Channel{T}"/> consumed by <see cref="OrderShipmentBackgroundService"/>.
/// </summary>
public class OrderShipmentScheduler : IOrderShipmentScheduler
{
    private readonly Channel<ShipmentJob> _channel;

    public OrderShipmentScheduler(Channel<ShipmentJob> channel)
    {
        _channel = channel;
    }

    /// <inheritdoc/>
    public ValueTask ScheduleShipmentAsync(
        Guid orderId,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        var job = new ShipmentJob(orderId, DateTimeOffset.UtcNow.Add(delay));
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }
}
