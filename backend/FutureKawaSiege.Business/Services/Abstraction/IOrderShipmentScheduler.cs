namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Schedules a delayed shipment for an order.
/// </summary>
public interface IOrderShipmentScheduler
{
    /// <summary>
    /// Schedules an order to be marked as shipped after the specified delay.
    /// </summary>
    /// <param name="orderId">The internal GUID of the order to ship.</param>
    /// <param name="delay">The time to wait before shipping the order.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    ValueTask ScheduleShipmentAsync(Guid orderId, TimeSpan delay, CancellationToken cancellationToken = default);
}
