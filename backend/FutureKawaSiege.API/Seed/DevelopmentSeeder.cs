using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.API.Seed;

/// <summary>
/// Seeds minimal reference data (test user, countries, warehouses) needed to exercise
/// the API locally without a real local API or manual SQL inserts.
/// Invoked via the `--seed` command-line argument; only runs in Development/Testing.
/// Idempotent: safe to run multiple times, existing records are left untouched.
/// </summary>
public class DevelopmentSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DevelopmentSeeder> _logger;

    private static readonly (string Code, string Name, decimal NominalTemp, decimal ToleranceTemp, decimal NominalHumidity, decimal ToleranceHumidity)[] CountryDefinitions =
    {
        ("BR", "Brésil",   29.0m, 3.0m, 55.0m, 2.0m),
        ("EC", "Équateur", 31.0m, 3.0m, 60.0m, 2.0m),
        ("CO", "Colombie", 26.0m, 3.0m, 80.0m, 2.0m),
    };

    public DevelopmentSeeder(AppDbContext db, IPasswordHasher hasher, ILogger<DevelopmentSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await SeedTestUserAsync();
        var countries = await SeedCountriesAsync();
        await SeedWarehousesAsync(countries);

        _logger.LogInformation(
            "Seed completed: 1 user, {CountryCount} countries, {WarehouseCount} warehouses.",
            countries.Count, await _db.Warehouses.CountAsync());
    }

    private async Task SeedTestUserAsync()
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == "test@futurekawa.com");
        if (existing is not null)
        {
            _logger.LogInformation("User already exists: test@futurekawa.com / TestPass123");
            return;
        }

        _db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "test@futurekawa.com",
            PasswordHash = _hasher.Hash("TestPass123"),
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        _logger.LogInformation("Seed user created: test@futurekawa.com / TestPass123");
    }

    private async Task<List<Country>> SeedCountriesAsync()
    {
        var countries = new List<Country>();

        foreach (var def in CountryDefinitions)
        {
            var country = await _db.Countries.FirstOrDefaultAsync(c => c.Code == def.Code);
            if (country is null)
            {
                country = new Country
                {
                    Id = Guid.NewGuid(),
                    Name = def.Name,
                    Code = def.Code,
                    NominalTemp = def.NominalTemp,
                    ToleranceTemp = def.ToleranceTemp,
                    NominalHumidity = def.NominalHumidity,
                    ToleranceHumidity = def.ToleranceHumidity,
                };
                _db.Countries.Add(country);
                _logger.LogInformation("Seed country created: {Name} ({Code})", country.Name, country.Code);
            }
            countries.Add(country);
        }

        await _db.SaveChangesAsync();
        return countries;
    }

    private async Task SeedWarehousesAsync(List<Country> countries)
    {
        foreach (var country in countries)
        {
            var reference = $"WH-{country.Code}";
            var existing = await _db.Warehouses.FirstOrDefaultAsync(w => w.Reference == reference);
            if (existing is not null)
                continue;

            _db.Warehouses.Add(new Warehouse
            {
                Id = Guid.NewGuid(),
                CountryId = country.Id,
                Name = $"Entrepôt {country.Name}",
                Reference = reference,
            });
            _logger.LogInformation("Seed warehouse created: {Reference}", reference);
        }

        await _db.SaveChangesAsync();
    }
}