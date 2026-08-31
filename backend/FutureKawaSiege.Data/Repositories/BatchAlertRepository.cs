using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Data.Repositories;

public class BatchAlertRepository : IBatchAlertRepository
{
    private readonly AppDbContext _context;

    public BatchAlertRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddAsync(BatchAlert alert, CancellationToken cancellationToken = default)
    {
        await _context.BatchAlerts.AddAsync(alert, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<BatchAlert>> GetActiveByBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        return await _context.BatchAlerts
            .AsNoTracking()
            .Where(ba => ba.BatchId == batchId && ba.Status == AlertStatus.Active)
            .OrderByDescending(ba => ba.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsActiveAsync(Guid batchId, AlertType type, CancellationToken cancellationToken = default)
    {
        return await _context.BatchAlerts
            .AsNoTracking()
            .AnyAsync(ba => ba.BatchId == batchId && ba.Type == type && ba.Status == AlertStatus.Active, cancellationToken);
    }
}
