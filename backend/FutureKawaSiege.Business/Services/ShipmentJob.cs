namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Represents a delayed shipment job scheduled by <see cref="IOrderShipmentScheduler"/>.
/// </summary>
public sealed record ShipmentJob(Guid OrderId, DateTimeOffset ExecuteAt);
