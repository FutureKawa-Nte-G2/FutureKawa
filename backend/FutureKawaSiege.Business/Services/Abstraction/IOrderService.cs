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
    /// Updates the status of an order. If the new status is "Shipped",
    /// notifies Odoo via JSON-RPC.
    /// </summary>
    /// <param name="id">The internal GUID of the order.</param>
    /// <param name="dto">The status update request.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The updated order response DTO, or <c>null</c> if not found.</returns>
    Task<OrderResponseDto?> UpdateOrderStatusAsync(Guid id, OrderStatusUpdateDto dto, CancellationToken cancellationToken = default);
}