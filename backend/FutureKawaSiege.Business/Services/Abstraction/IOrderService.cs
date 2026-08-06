using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

/// <summary>
/// Service for managing sales orders received from Odoo.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Receives an order from the Odoo webhook, persists it, and returns a response.
    /// </summary>
    /// <param name="dto">The webhook payload from Odoo.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The created order response.</returns>
    Task<OrderResponseDto> ReceiveOrderFromOdooAsync(OdooOrderWebhookDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all orders.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A list of order response DTOs.</returns>
    Task<IEnumerable<OrderResponseDto>> GetOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single order by its internal ID.
    /// </summary>
    /// <param name="id">The internal GUID of the order.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The order response DTO, or <c>null</c> if not found.</returns>
    Task<OrderResponseDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order as shipped, updates its associated batches, and
    /// notifies Odoo via JSON-RPC when applicable.
    /// </summary>
    /// <param name="id">The internal GUID of the order.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ShipOrderAsync(Guid id, CancellationToken cancellationToken = default);
}