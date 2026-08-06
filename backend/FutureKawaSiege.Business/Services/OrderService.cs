using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FutureKawaSiege.Business.Services;

/// <summary>
/// Service for managing sales orders received from Odoo.
///
/// Responsibilities:
/// - Receive orders from the Odoo webhook and persist them
/// - List and retrieve orders for the API
/// - Update order status and notify Odoo when an order is shipped
/// </summary>
public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOdooIntegrationService _odooIntegrationService;
    private readonly IOrderShipmentScheduler _shipmentScheduler;
    private readonly AppDbContext _context;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IOdooIntegrationService odooIntegrationService,
        IOrderShipmentScheduler shipmentScheduler,
        AppDbContext context,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _odooIntegrationService = odooIntegrationService;
        _shipmentScheduler = shipmentScheduler;
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OrderResponseDto> ReceiveOrderFromOdooAsync(
        OdooOrderWebhookDto dto,
        CancellationToken cancellationToken = default)
    {
        // Check if the order already exists (idempotency)
        var existing = dto.OrderId > 0
            ? await _orderRepository.GetByOdooOrderIdAsync(dto.OrderId, cancellationToken)
            : null;

        if (existing is not null)
        {
            _logger.LogInformation(
                "Order from Odoo (OdooOrderId={OdooOrderId}) already exists, skipping",
                dto.OrderId);
            return MapToDto(existing);
        }

        // Parse the order date from ISO 8601
        DateTime orderDate = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.OrderDate) &&
            DateTime.TryParse(dto.OrderDate, out var parsed))
        {
            // PostgreSQL 'timestamp with time zone' requires Kind=Utc
            orderDate = parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OdooOrderId = dto.OrderId > 0 ? dto.OrderId : null,
            OrderReference = $"ODOO-{dto.OrderId}",
            OrderDate = orderDate,
            ClientName = dto.Client ?? "Unknown",
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            Lines = dto.Lines.Select(l => new OrderLine
            {
                Id = Guid.NewGuid(),
                ProductName = l.Product ?? "Unknown Product",
                Quantity = l.Quantity,
            }).ToList(),
        };

        order.Batches = await ResolveBatchesAsync(
            dto.BatchReferences,
            cancellationToken);

        await _orderRepository.AddAsync(order, cancellationToken);

        await _shipmentScheduler.ScheduleShipmentAsync(
            order.Id,
            TimeSpan.FromSeconds(5),
            cancellationToken);

        _logger.LogInformation(
            "Order received from Odoo: {OrderReference} (OdooOrderId={OdooOrderId}) with {BatchCount} batches",
            order.OrderReference,
            order.OdooOrderId,
            order.Batches.Count);

        _logger.LogInformation(
            "Order received from Odoo: {OrderReference} (OdooOrderId={OdooOrderId})",
            order.OrderReference,
            order.OdooOrderId);

        return MapToDto(order);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<OrderResponseDto>> GetOrdersAsync(
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetAllAsync(cancellationToken);
        return orders.Select(MapToDto);
    }

    /// <inheritdoc/>
    public async Task<OrderResponseDto?> GetOrderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);
        return order is null ? null : MapToDto(order);
    }

    /// <inheritdoc/> 
    public async Task ShipOrderAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Cannot ship order {OrderId}: order not found", id);
            return;
        }

        if (order.Status == OrderStatus.Shipped || order.Status == OrderStatus.Delivered)
        {
            _logger.LogInformation(
                "Order {OrderReference} is already {Status}; skipping shipment",
                order.OrderReference,
                order.Status);
            return;
        }

        order.Status = OrderStatus.Shipped;

        foreach (var batch in order.Batches)
        {
            batch.Status = BatchStatus.Shipped;
            if (!batch.ShippedAt.HasValue)
            {
                batch.ShippedAt = DateTime.UtcNow.Date;
            }
        }

        _logger.LogInformation(
            "Order {OrderReference} marked as shipped. Updated {BatchCount} associated batches.",
            order.OrderReference,
            order.Batches.Count);

        if (order.OdooOrderId is not null)
        {
            _logger.LogInformation(
                "Order {OrderReference} marked as shipped, notifying Odoo...",
                order.OrderReference);

            try
            {
                var success = await _odooIntegrationService.NotifyOrderShippedAsync(
                    order.OdooOrderId.Value,
                    cancellationToken);

                if (success)
                {
                    order.IntegrationErrorMessage = null;
                    _logger.LogInformation(
                        "Odoo successfully notified that order {OrderReference} was shipped",
                        order.OrderReference);
                }
                else
                {
                    order.IntegrationErrorMessage =
                        "Failed to notify Odoo of shipping status.";
                    _logger.LogWarning(
                        "Failed to notify Odoo that order {OrderReference} was shipped",
                        order.OrderReference);
                }
            }
            catch (Exception ex)
            {
                order.IntegrationErrorMessage = ex.Message;
                _logger.LogError(ex,
                    "Error notifying Odoo that order {OrderReference} was shipped",
                    order.OrderReference);
            }
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
    }

    /// <summary>
    /// Resolves existing batches by reference. Missing batches are created
    /// with a default warehouse and farm so the order flow remains functional.
    /// </summary>
    private async Task<List<Batch>> ResolveBatchesAsync(
        IEnumerable<string> batchReferences,
        CancellationToken cancellationToken)
    {
        var references = batchReferences
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();

        if (references.Count == 0)
            return [];

        var existingBatches = await _context.Batches
            .Where(b => references.Contains(b.Reference))
            .ToListAsync(cancellationToken);

        var existingReferences = existingBatches.Select(b => b.Reference).ToHashSet();
        var missingReferences = references.Where(r => !existingReferences.Contains(r)).ToList();

        if (missingReferences.Count > 0)
        {
            var defaults = await EnsureDefaultBatchDependenciesAsync(cancellationToken);

            foreach (var reference in missingReferences)
            {
                var batch = new Batch
                {
                    Reference = reference,
                    WarehouseId = defaults.WarehouseId,
                    FarmId = defaults.FarmId,
                    StoredAt = DateTime.UtcNow.Date,
                    QualityGrade = BatchQualityGrade.A,
                    Status = BatchStatus.Stored,
                };

                _context.Batches.Add(batch);
                existingBatches.Add(batch);
            }

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Created {Count} missing batches for order references: {References}",
                missingReferences.Count,
                string.Join(", ", missingReferences));
        }

        return existingBatches;
    }

    private async Task<(Guid WarehouseId, Guid FarmId)> EnsureDefaultBatchDependenciesAsync(
        CancellationToken cancellationToken)
    {
        var country = await _context.Countries.FirstOrDefaultAsync(cancellationToken)
            ?? await CreateDefaultCountryAsync(cancellationToken);

        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(cancellationToken)
            ?? await CreateDefaultWarehouseAsync(country.Id, cancellationToken);

        var farm = await _context.Farms.FirstOrDefaultAsync(cancellationToken)
            ?? await CreateDefaultFarmAsync(country.Id, cancellationToken);

        return (warehouse.Id, farm.Id);
    }

    private async Task<Country> CreateDefaultCountryAsync(CancellationToken cancellationToken)
    {
        var country = new Country
        {
            Name = "Default Country",
            Code = "DFLT",
            NominalTemp = 20,
            ToleranceTemp = 2,
            NominalHumidity = 60,
            ToleranceHumidity = 5,
        };

        _context.Countries.Add(country);
        await _context.SaveChangesAsync(cancellationToken);
        return country;
    }

    private async Task<Warehouse> CreateDefaultWarehouseAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var warehouse = new Warehouse
        {
            Name = "Default Warehouse",
            Reference = "WH-DEFAULT",
            CountryId = countryId,
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync(cancellationToken);
        return warehouse;
    }

    private async Task<Farm> CreateDefaultFarmAsync(
        Guid countryId,
        CancellationToken cancellationToken)
    {
        var farm = new Farm
        {
            Name = "Default Farm",
            Reference = "FM-DEFAULT",
            CountryId = countryId,
        };

        _context.Farms.Add(farm);
        await _context.SaveChangesAsync(cancellationToken);
        return farm;
    }

    /// <summary>
    /// Maps an <see cref="Order"/> entity to an <see cref="OrderResponseDto"/>.
    /// </summary>
    private static OrderResponseDto MapToDto(Order order) =>
        new()
        {
            Id = order.Id,
            OdooOrderId = order.OdooOrderId,
            OrderReference = order.OrderReference,
            OrderDate = order.OrderDate,
            ClientName = order.ClientName,
            Status = order.Status.ToString(),
            IntegrationErrorMessage = order.IntegrationErrorMessage,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Lines = order.Lines.Select(l => new OrderLineResponseDto
            {
                Id = l.Id,
                ProductName = l.ProductName,
                Quantity = l.Quantity,
            }),
            Batches = order.Batches.Select(b => new BatchDto
            {
                Id = b.Id,
                Reference = b.Reference,
                QualityGrade = b.QualityGrade.ToString(),
                Status = b.Status.ToString(),
                WarehouseName = b.Warehouse?.Name ?? string.Empty,
                FarmName = b.Farm?.Name ?? string.Empty,
                CountryName = b.Farm?.Country?.Name ?? string.Empty,
                StoredAt = b.StoredAt,
                ShippedAt = b.ShippedAt,
            }),
        };
}