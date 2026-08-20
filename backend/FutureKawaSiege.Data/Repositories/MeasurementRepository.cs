using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Data.Repositories;

public class MeasurementRepository : IMeasurementRepository
{
    private readonly AppDbContext _context;

    public MeasurementRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Measurement>> GetByWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        return await _context.Measurements
            .Where(m => m.WarehouseId == warehouseId)
            .Include(m => m.Warehouse)
            .AsNoTracking()
            .OrderByDescending(m => m.MeasDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Measurement>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Measurements
            .Include(m => m.Warehouse)
            .AsNoTracking()
            .OrderByDescending(m => m.MeasDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Measurement?> GetExistingAsync(Guid warehouseId, DateOnly measDate, CancellationToken cancellationToken = default)
    {
        return await _context.Measurements
            .FirstOrDefaultAsync(m => m.WarehouseId == warehouseId && m.MeasDate == measDate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(Measurement measurement, CancellationToken cancellationToken = default)
    {
        await _context.Measurements.AddAsync(measurement, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}