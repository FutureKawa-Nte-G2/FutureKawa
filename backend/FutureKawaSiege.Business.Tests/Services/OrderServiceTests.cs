using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class OrderServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IOdooIntegrationService _odooIntegrationService = Substitute.For<IOdooIntegrationService>();
    private readonly IOrderShipmentScheduler _shipmentScheduler = Substitute.For<IOrderShipmentScheduler>();
    private readonly OrderService _orderService;

    public OrderServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _orderService = new OrderService(
            _orderRepository,
            _odooIntegrationService,
            _shipmentScheduler,
            _context,
            Substitute.For<ILogger<OrderService>>());
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ShipOrderAsync_Should_MarkOrderAndBatchesAsShipped_When_OrderIsConfirmed()
    {
        var order = CreateConfirmedOrder();
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        await _orderService.ShipOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.All(order.Batches, b => Assert.Equal(BatchStatus.Shipped, b.Status));
        Assert.All(order.Batches, b => Assert.True(b.ShippedAt.HasValue));
        await _orderRepository.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
        await _odooIntegrationService.DidNotReceive()
            .NotifyOrderShippedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrderAsync_Should_NotifyOdoo_When_OrderHasOdooOrderId()
    {
        var order = CreateConfirmedOrder(odooOrderId: 42);
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _odooIntegrationService.NotifyOrderShippedAsync(42, Arg.Any<CancellationToken>()).Returns(true);

        await _orderService.ShipOrderAsync(order.Id);

        await _odooIntegrationService.Received(1).NotifyOrderShippedAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrderAsync_Should_Skip_When_OrderAlreadyShipped()
    {
        var order = CreateConfirmedOrder();
        order.Status = OrderStatus.Shipped;
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        await _orderService.ShipOrderAsync(order.Id);

        await _orderRepository.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _odooIntegrationService.DidNotReceive()
            .NotifyOrderShippedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrderAsync_Should_Skip_When_OrderNotFound()
    {
        _orderRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        await _orderService.ShipOrderAsync(Guid.NewGuid());

        await _orderRepository.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveOrderFromOdooAsync_Should_ScheduleShipment_When_OrderIsNew()
    {
        var dto = new OdooOrderWebhookDto
        {
            OrderId = 123,
            Client = "Client",
            OrderDate = DateTime.UtcNow.ToString("O"),
            Lines = [new OdooOrderLineDto { Product = "Coffee", Quantity = 10 }],
            BatchReferences = ["BATCH-001"],
        };

        _orderRepository.GetByOdooOrderIdAsync(123, Arg.Any<CancellationToken>())
            .Returns((Order?)null);
        _orderRepository.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _orderService.ReceiveOrderFromOdooAsync(dto);

        await _shipmentScheduler.Received(1).ScheduleShipmentAsync(
            Arg.Any<Guid>(),
            TimeSpan.FromSeconds(60),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReceiveOrderFromOdooAsync_Should_AssociateOldestBatch_And_CreateNewBatchForFifoPool_When_StockExists()
    {
        var country = new Country
        {
            Id = Guid.NewGuid(),
            Name = "Colombia",
            Code = "CO",
            NominalTemp = 19,
            ToleranceTemp = 2,
            NominalHumidity = 62,
            ToleranceHumidity = 5,
        };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "WH", Reference = "WH-CO", CountryId = country.Id };
        var farm = new Farm { Id = Guid.NewGuid(), Name = "Farm", Reference = "FM-CO", CountryId = country.Id };
        var oldestBatch = new Batch
        {
            Id = Guid.NewGuid(),
            Reference = "OLD-BATCH",
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            StoredAt = DateTime.UtcNow.Date.AddDays(-30),
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored,
        };
        var newerBatch = new Batch
        {
            Id = Guid.NewGuid(),
            Reference = "NEWER-BATCH",
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            StoredAt = DateTime.UtcNow.Date.AddDays(-5),
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored,
        };

        _context.Countries.Add(country);
        _context.Warehouses.Add(warehouse);
        _context.Farms.Add(farm);
        _context.Batches.AddRange(oldestBatch, newerBatch);
        await _context.SaveChangesAsync();

        var dto = new OdooOrderWebhookDto
        {
            OrderId = 456,
            Client = "Client",
            OrderDate = DateTime.UtcNow.ToString("O"),
            Country = "CO",
            Lines = [new OdooOrderLineDto { Product = "Coffee", Quantity = 10 }],
            BatchReferences = ["REPLENISH-BATCH"],
        };

        _orderRepository.GetByOdooOrderIdAsync(456, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        Order? capturedOrder = null;
        _orderRepository.AddAsync(Arg.Do<Order>(o => capturedOrder = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _orderService.ReceiveOrderFromOdooAsync(dto);

        Assert.NotNull(capturedOrder);
        var assignedBatch = Assert.Single(capturedOrder!.Batches);
        Assert.Equal("OLD-BATCH", assignedBatch.Reference);

        var replenishmentBatch = await _context.Batches
            .SingleAsync(b => b.Reference == "REPLENISH-BATCH");
        Assert.DoesNotContain(replenishmentBatch, capturedOrder.Batches);
    }

    [Fact]
    public async Task ReceiveOrderFromOdooAsync_Should_SkipBatch_When_AlreadyAssociatedWithAnotherOrder()
    {
        var allocatedBatch = new Batch
        {
            Id = Guid.NewGuid(),
            Reference = "ALLOCATED-OLD-BATCH",
            WarehouseId = Guid.NewGuid(),
            FarmId = Guid.NewGuid(),
            StoredAt = DateTime.UtcNow.Date.AddDays(-30),
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored,
        };
        var availableBatch = new Batch
        {
            Id = Guid.NewGuid(),
            Reference = "AVAILABLE-BATCH",
            WarehouseId = Guid.NewGuid(),
            FarmId = Guid.NewGuid(),
            StoredAt = DateTime.UtcNow.Date.AddDays(-10),
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored,
        };
        var existingOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderReference = "ODOO-1",
            ClientName = "Existing Client",
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            Batches = [allocatedBatch],
        };

        _context.Batches.AddRange(allocatedBatch, availableBatch);
        _context.Orders.Add(existingOrder);
        await _context.SaveChangesAsync();

        var dto = new OdooOrderWebhookDto
        {
            OrderId = 999,
            Client = "Client",
            OrderDate = DateTime.UtcNow.ToString("O"),
            Lines = [new OdooOrderLineDto { Product = "Coffee", Quantity = 10 }],
            BatchReferences = ["REPLENISH-BATCH-2"],
        };

        _orderRepository.GetByOdooOrderIdAsync(999, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        Order? capturedOrder = null;
        _orderRepository.AddAsync(Arg.Do<Order>(o => capturedOrder = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _orderService.ReceiveOrderFromOdooAsync(dto);

        Assert.NotNull(capturedOrder);
        var assignedBatch = Assert.Single(capturedOrder!.Batches);
        Assert.Equal("AVAILABLE-BATCH", assignedBatch.Reference);
    }

    [Fact]
    public async Task ReceiveOrderFromOdooAsync_Should_UseNewlyCreatedBatch_When_NoStockExists()
    {
        var dto = new OdooOrderWebhookDto
        {
            OrderId = 789,
            Client = "Client",
            OrderDate = DateTime.UtcNow.ToString("O"),
            Lines = [new OdooOrderLineDto { Product = "Coffee", Quantity = 10 }],
            BatchReferences = ["FIRST-BATCH"],
        };

        _orderRepository.GetByOdooOrderIdAsync(789, Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        Order? capturedOrder = null;
        _orderRepository.AddAsync(Arg.Do<Order>(o => capturedOrder = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _orderService.ReceiveOrderFromOdooAsync(dto);

        Assert.NotNull(capturedOrder);
        var assignedBatch = Assert.Single(capturedOrder!.Batches);
        Assert.Equal("FIRST-BATCH", assignedBatch.Reference);
    }

    private static Order CreateConfirmedOrder(int? odooOrderId = null)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            OdooOrderId = odooOrderId,
            OrderReference = "ODOO-123",
            ClientName = "Client",
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow,
            Batches =
            [
                new Batch
                {
                    Id = Guid.NewGuid(),
                    Reference = "BATCH-001",
                    Status = BatchStatus.Stored,
                    StoredAt = DateTime.UtcNow.Date,
                    QualityGrade = BatchQualityGrade.A,
                    WarehouseId = Guid.NewGuid(),
                    FarmId = Guid.NewGuid(),
                }
            ],
        };
    }
}
