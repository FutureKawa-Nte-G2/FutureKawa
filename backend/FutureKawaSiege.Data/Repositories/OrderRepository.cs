using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Data.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Lines)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Farm)
            .ThenInclude(f => f.Country)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Warehouse)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Lines)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Farm)
            .ThenInclude(f => f.Country)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Warehouse)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Order?> GetByOdooOrderIdAsync(int odooOrderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Lines)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Farm)
            .ThenInclude(f => f.Country)
            .Include(o => o.Batches)
            .ThenInclude(b => b.Warehouse)
            .FirstOrDefaultAsync(o => o.OdooOrderId == odooOrderId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        order.UpdatedAt = DateTime.UtcNow;
        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
    }
}