using FluentValidation;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Business.Validators;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FutureKawaSiege.API.Controllers;

/// <summary>
/// Controller for receiving webhooks from the Odoo ERP.
///
/// The Odoo module sends an HTTP POST to this endpoint when a sales order
/// is confirmed. The payload is validated and persisted by the OrderService.
///
/// Authentication: a shared token in the X-Webhook-Token header (not JWT).
/// This is because Odoo does not have a JWT token — it uses a pre-shared
/// secret configured in both systems.
/// </summary>
[ApiController]
[Route("api/integration/odoo")]
public class IntegrationController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IValidator<OdooOrderWebhookDto> _validator;
    private readonly ILogger<IntegrationController> _logger;

    public IntegrationController(
        IOrderService orderService,
        IValidator<OdooOrderWebhookDto> validator,
        ILogger<IntegrationController> logger)
    {
        _orderService = orderService;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Receives a sales order from the Odoo ERP webhook.
    ///
    /// The Odoo module calls this endpoint when a sales order is confirmed
    /// (action_confirm). The payload contains the order details including
    /// coffee-specific fields (batch reference, quality grade, origin country).
    ///
    /// Authentication is via a shared token in the X-Webhook-Token header.
    /// </summary>
    [HttpPost("orders")]
    [EnableRateLimiting("odoo_webhook")]
    public async Task<ActionResult<ApiResponse<OrderResponseDto>>> ReceiveOrder(
        [FromBody] OdooOrderWebhookDto dto,
        CancellationToken cancellationToken)
    {
        // Validate the shared webhook token
        var expectedToken = Request.HttpContext.Items["OdooWebhookToken"] as string;
        if (string.IsNullOrEmpty(expectedToken))
        {
            _logger.LogWarning("Odoo webhook received without valid token");
            return Unauthorized(ApiResponse<OrderResponseDto>.Fail("Invalid webhook token."));
        }

        // Validate the payload
        var validationResult = await _validator.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage);
            return BadRequest(ApiResponse<OrderResponseDto>.Fail("Validation failed.", errors));
        }

        try
        {
            var response = await _orderService.ReceiveOrderFromOdooAsync(dto, cancellationToken);
            return Ok(ApiResponse<OrderResponseDto>.Ok(response, "Order received from Odoo."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Odoo webhook for order {OrderId}", dto.OrderId);
            return StatusCode(500, ApiResponse<OrderResponseDto>.Fail("Internal server error."));
        }
    }
}