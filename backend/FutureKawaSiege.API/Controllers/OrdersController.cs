using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for managing sales orders.
///
/// Provides endpoints to list, retrieve, and update the status of orders
/// received from the Odoo ERP. All endpoints require JWT authentication.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService orderService,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }

    /// <summary>
    /// Returns all orders received from Odoo, sorted by date (newest first).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<OrderResponseDto>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetOrdersAsync(cancellationToken);
        return Ok(ApiResponse<IEnumerable<OrderResponseDto>>.Ok(orders));
    }

    /// <summary>
    /// Returns a single order by its internal ID, including its lines.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderResponseDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderResponseDto>.Fail("Order not found."));

        return Ok(ApiResponse<OrderResponseDto>.Ok(order));
    }

    /// <summary>
    /// Updates the status of an order.
    ///
    /// When the status is set to "Shipped", the backend automatically
    /// notifies the Odoo ERP via JSON-RPC (calls action_mark_shipped).
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<OrderResponseDto>>> UpdateStatus(
        Guid id,
        [FromBody] OrderStatusUpdateDto dto,
        CancellationToken cancellationToken)
    {
        var order = await _orderService.UpdateOrderStatusAsync(id, dto, cancellationToken);
        if (order is null)
            return NotFound(ApiResponse<OrderResponseDto>.Fail("Order not found."));

        return Ok(ApiResponse<OrderResponseDto>.Ok(order, "Order status updated."));
    }
}