using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.Data.Repositories;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly AppDbContext _context;

    public WarehouseRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Warehouse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .Include(w => w.Country)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Warehouse>> GetByCountryCodeAsync(string countryCode, CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .Include(w => w.Country)
            .Where(w => w.Country.Code == countryCode)
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Warehouse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Warehouses
            .AsNoTracking()
            .Include(w => w.Country)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }
}
