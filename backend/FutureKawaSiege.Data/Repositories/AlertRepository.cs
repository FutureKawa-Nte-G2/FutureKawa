using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Data.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly AppDbContext _context;

    public AlertRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        await _context.Alerts.AddAsync(alert, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Alert>> GetActiveByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        return await _context.Alerts
            .AsNoTracking()
            .Where(a => a.WarehouseId == warehouseId && a.Status == AlertStatus.Active)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsActiveAsync(Guid warehouseId, AlertType type, CancellationToken cancellationToken = default)
    {
        return await _context.Alerts
            .AsNoTracking()
            .AnyAsync(a => a.WarehouseId == warehouseId && a.Type == type && a.Status == AlertStatus.Active, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Alerts
            .Include(a => a.Warehouse)
                .ThenInclude(w => w.Country)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        _context.Alerts.Update(alert);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
