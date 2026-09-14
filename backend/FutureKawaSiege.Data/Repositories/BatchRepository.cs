using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;
namespace FutureKawaSiege.Data.Repositories;

public class BatchRepository : IBatchRepository
{
    private readonly AppDbContext _context;
    public BatchRepository(AppDbContext context)
    {
        _context = context;
    }
    /// <inheritdoc/>
    public async Task<(IEnumerable<Batch> Items, int TotalCount)> GetPagedAsync(
        string? countryCode,
        Guid? warehouseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Batches
            .AsNoTracking()
            .Where(b => b.ShippedAt == null) // Exclude shipped batches by default
            .Include(b => b.Warehouse)
                .ThenInclude(w => w.Country)
            .Include(b => b.Warehouse)
                .ThenInclude(w => w.Alerts.Where(a => a.Status == AlertStatus.Active))
            .Include(b => b.Farm)
            .AsQueryable();
        if (!string.IsNullOrEmpty(countryCode))
        {
            query = query.Where(b => b.Warehouse.Country.Code == countryCode);
        }
        if (warehouseId.HasValue)
        {
            query = query.Where(b => b.WarehouseId == warehouseId);
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.StoredAt) // FIFO: oldest first
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, totalCount);
    }
    /// <inheritdoc/>
    public async Task<Batch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Batches
            .AsNoTracking()
            .Include(b => b.Warehouse)
                .ThenInclude(w => w.Country)
            .Include(b => b.Warehouse)
                .ThenInclude(w => w.Alerts.Where(a => a.Status == AlertStatus.Active))
            .Include(b => b.Farm)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }
    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Batches
            .AsNoTracking()
            .AnyAsync(b => b.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Batch>> GetOverlappingWarehousePeriodAsync(
        Guid warehouseId,
        DateTime periodStart,
        DateTime? periodEnd,
        CancellationToken cancellationToken = default)
    {
        // An open-ended period (still ongoing, e.g. an active alert) overlaps
        // anything that started on or before "now or later" -> use MaxValue rather
        // than DateTime.UtcNow so a batch entering after this call still overlaps
        // a period that is genuinely still open at read time.
        var effectivePeriodEnd = periodEnd ?? DateTime.MaxValue;

        return await _context.Batches
            .AsNoTracking()
            .Include(b => b.Warehouse)
                .ThenInclude(w => w.Country)
            .Include(b => b.Farm)
            .Where(b => b.WarehouseId == warehouseId)
            .Where(b => b.StoredAt <= effectivePeriodEnd)
            .Where(b => b.ShippedAt == null || b.ShippedAt >= periodStart)
            .OrderBy(b => b.StoredAt)
            .ToListAsync(cancellationToken);
    }
}
