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
    private readonly ILocalBatchPushClient _batchPushClient;
    private readonly AppDbContext _context;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IOdooIntegrationService odooIntegrationService,
        IOrderShipmentScheduler shipmentScheduler,
        ILocalBatchPushClient batchPushClient,
        AppDbContext context,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _odooIntegrationService = odooIntegrationService;
        _shipmentScheduler = shipmentScheduler;
        _batchPushClient = batchPushClient;
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

        var countryId = await ResolveCountryIdAsync(dto.Country, cancellationToken);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OdooOrderId = dto.OrderId > 0 ? dto.OrderId : null,
            OrderReference = $"ODOO-{dto.OrderId}",
            OrderDate = orderDate,
            ClientName = dto.Client ?? "Unknown",
            CountryId = countryId,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            Lines = dto.Lines.Select(l => new OrderLine
            {
                Id = Guid.NewGuid(),
                ProductName = l.Product ?? "Unknown Product",
                Quantity = l.Quantity,
            }).ToList(),
        };

        var qualityGrade = ParseQualityGrade(dto.QualityGrade);

        order.Batches = await ResolveOrderBatchAsync(
            dto.BatchReferences,
            qualityGrade,
            countryId,
            cancellationToken);

        await _orderRepository.AddAsync(order, cancellationToken);

        await _shipmentScheduler.ScheduleShipmentAsync(
            order.Id,
            TimeSpan.FromSeconds(60),
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

            var countryCode = batch.Farm?.Country?.Code;
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                _logger.LogWarning(
                    "Cannot push shipment of batch {BatchReference}: no country resolved for its farm.",
                    batch.Reference);
            }
            else
            {
                try
                {
                    await _batchPushClient.PushShipmentAsync(
                        countryCode,
                        batch.Reference,
                        DateOnly.FromDateTime(batch.ShippedAt.Value),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error pushing shipment of batch {BatchReference} to country {CountryCode}",
                        batch.Reference, countryCode);
                }
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
    /// Implements FIFO batch allocation for a newly received order.
    ///
    /// Instead of creating a batch dedicated to the order, the order is
    /// associated with the oldest eligible batch currently in stock (FIFO —
    /// not yet shipped, not already tied to another order, matching this
    /// order's country and quality grade, not expired, and not in a
    /// warehouse under an active alert), while a new batch is created in
    /// parallel to replenish the stock pool for future orders. Missing
    /// batches are created with a default warehouse and farm so the order
    /// flow remains functional, and pushed to the country's local API.
    ///
    /// If no eligible batch is available (e.g. the very first order, empty
    /// stock, or every batch already allocated / ineligible), the newly
    /// created batch is used to fulfill the order instead.
    /// </summary>
    private async Task<List<Batch>> ResolveOrderBatchAsync(
        IEnumerable<string> batchReferences,
        BatchQualityGrade qualityGrade,
        Guid? countryId,
        CancellationToken cancellationToken)
    {
        // Oldest eligible batch currently in stock: not yet shipped, not
        // already tied to another order, matching this order's country and
        // quality grade, not expired (>365 days), and not in a warehouse
        // under an active alert (same eligibility rule as the previous
        // ResolveBatchesAsync / BatchService.ComputeStatus). Read before
        // this order's replenishment batch is created, so it never
        // allocates the batch to itself.
        var expirationThreshold = DateTime.UtcNow.AddDays(-365);

        var oldestBatch = await _context.Batches
            .Where(b => b.ShippedAt == null
                && !b.Orders.Any()
                && (countryId == null || b.Warehouse.CountryId == countryId)
                && b.QualityGrade == qualityGrade
                && b.StoredAt > expirationThreshold
                && !b.Warehouse.Alerts.Any(a => a.Status == AlertStatus.Active))
            .OrderBy(b => b.StoredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var newReferences = batchReferences
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .ToList();

        var newBatches = new List<Batch>();

        if (newReferences.Count > 0)
        {
            var existingReferences = await _context.Batches
                .Where(b => newReferences.Contains(b.Reference))
                .Select(b => b.Reference)
                .ToListAsync(cancellationToken);

            var missingReferences = newReferences
                .Where(r => !existingReferences.Contains(r))
                .ToList();

            if (missingReferences.Count > 0)
            {
                var defaults = await EnsureDefaultBatchDependenciesAsync(countryId, cancellationToken);
                var storedAt = DateTime.UtcNow.Date;

                foreach (var reference in missingReferences)
                {
                    var batch = new Batch
                    {
                        Reference = reference,
                        WarehouseId = defaults.WarehouseId,
                        FarmId = defaults.FarmId,
                        StoredAt = storedAt,
                        QualityGrade = qualityGrade,
                        Status = BatchStatus.Stored,
                    };

                    _context.Batches.Add(batch);
                    newBatches.Add(batch);
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Created {Count} new batch(es) entering the FIFO stock pool: {References}",
                    missingReferences.Count,
                    string.Join(", ", missingReferences));

                foreach (var batch in newBatches)
                {
                    try
                    {
                        await _batchPushClient.PushBatchAsync(
                            defaults.CountryCode,
                            batch.Reference,
                            defaults.FarmReference,
                            defaults.WarehouseReference,
                            DateOnly.FromDateTime(storedAt),
                            qualityGrade,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error pushing batch {BatchReference} to country {CountryCode}",
                            batch.Reference, defaults.CountryCode);
                    }
                }
            }
        }

        if (oldestBatch is not null)
        {
            _logger.LogInformation(
                "Order associated with oldest eligible batch in stock (FIFO): {Reference} (stored on {StoredAt:yyyy-MM-dd})",
                oldestBatch.Reference,
                oldestBatch.StoredAt);
            return [oldestBatch];
        }

        // No eligible batch was available (empty stock, all batches already
        // allocated, or none matching country/quality/expiry/alert): fall
        // back to the batch(es) just created.
        return newBatches;
    }

    private static BatchQualityGrade ParseQualityGrade(string? grade)
    {
        return grade?.ToUpperInvariant() switch
        {
            "B" => BatchQualityGrade.B,
            "C" => BatchQualityGrade.C,
            _ => BatchQualityGrade.A,
        };
    }

    private async Task<(Guid WarehouseId, Guid FarmId, string WarehouseReference, string FarmReference, string CountryCode)>
        EnsureDefaultBatchDependenciesAsync(
            Guid? countryId,
            CancellationToken cancellationToken)
    {
        var country = countryId is not null
            ? await _context.Countries.FindAsync(new object?[] { countryId.Value }, cancellationToken)
                ?? await CreateDefaultCountryAsync(cancellationToken)
            : await _context.Countries.FirstOrDefaultAsync(cancellationToken)
                ?? await CreateDefaultCountryAsync(cancellationToken);

        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.CountryId == country.Id, cancellationToken)
            ?? await CreateDefaultWarehouseAsync(country.Id, cancellationToken);

        var farm = await _context.Farms
            .FirstOrDefaultAsync(f => f.CountryId == country.Id, cancellationToken)
            ?? await CreateDefaultFarmAsync(country.Id, cancellationToken);

        return (warehouse.Id, farm.Id, warehouse.Reference, farm.Reference, country.Code);
    }

    private async Task<Guid?> ResolveCountryIdAsync(
        string? countryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return null;

        var normalizedCode = countryCode.Trim().ToUpperInvariant();
        var country = await _context.Countries
            .FirstOrDefaultAsync(c => c.Code == normalizedCode, cancellationToken);

        if (country is not null)
            return country.Id;

        country = CreateCountryFromCode(normalizedCode);
        _context.Countries.Add(country);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created country {CountryName} ({CountryCode}) from Odoo order",
            country.Name,
            country.Code);

        return country.Id;
    }

    private static Country CreateCountryFromCode(string code) => code switch
    {
        "BR" => new Country
        {
            Name = "Brazil",
            Code = "BR",
            NominalTemp = 20,
            ToleranceTemp = 2,
            NominalHumidity = 60,
            ToleranceHumidity = 5,
        },
        "EC" => new Country
        {
            Name = "Ecuador",
            Code = "EC",
            NominalTemp = 18,
            ToleranceTemp = 2,
            NominalHumidity = 65,
            ToleranceHumidity = 5,
        },
        "CO" => new Country
        {
            Name = "Colombia",
            Code = "CO",
            NominalTemp = 19,
            ToleranceTemp = 2,
            NominalHumidity = 62,
            ToleranceHumidity = 5,
        },
        _ => new Country
        {
            Name = code,
            Code = code,
            NominalTemp = 20,
            ToleranceTemp = 2,
            NominalHumidity = 60,
            ToleranceHumidity = 5,
        },
    };

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
        var country = await _context.Countries.FindAsync(new object?[] { countryId }, cancellationToken)
            ?? throw new InvalidOperationException($"Country {countryId} not found.");

        var suffix = country.Code.ToUpperInvariant();
        var warehouse = new Warehouse
        {
            Name = $"Default Warehouse ({country.Name})",
            Reference = $"WH-{suffix}-DEFAULT",
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
        var country = await _context.Countries.FindAsync(new object?[] { countryId }, cancellationToken)
            ?? throw new InvalidOperationException($"Country {countryId} not found.");

        var suffix = country.Code.ToUpperInvariant();
        var farm = new Farm
        {
            Name = $"Default Farm ({country.Name})",
            Reference = $"FM-{suffix}-DEFAULT",
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
            Country = order.Country?.Name,
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