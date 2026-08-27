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
            TimeSpan.FromSeconds(5),
            Arg.Any<CancellationToken>());
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
