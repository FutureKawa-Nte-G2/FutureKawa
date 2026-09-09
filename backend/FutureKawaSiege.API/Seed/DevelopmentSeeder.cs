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

    // Demo data for the batch measurement curves page (#35): just enough to
    // reach it from the FIFO list locally, without the MQTT/local-API
    // pipeline running. Brazil only — the fuller seed (all countries,
    // multiple farms/batches/orders) is tracked separately (issue #15).
    private const string DemoCountryCode = "BR";
    private const string DemoFarmReference = "FARM-BR-DEMO";
    private const string DemoBatchReference = "BR-DEMO-0001";
    private const int DemoMeasurementDays = 21;

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
        var warehouses = await SeedWarehousesAsync(countries);
        await SeedDemoBatchAndMeasurementsAsync(countries, warehouses);

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

    private async Task<List<Warehouse>> SeedWarehousesAsync(List<Country> countries)
    {
        var warehouses = new List<Warehouse>();

        foreach (var country in countries)
        {
            var reference = $"WH-{country.Code}";
            var existing = await _db.Warehouses.FirstOrDefaultAsync(w => w.Reference == reference);
            if (existing is null)
            {
                existing = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    CountryId = country.Id,
                    Name = $"Entrepôt {country.Name}",
                    Reference = reference,
                };
                _db.Warehouses.Add(existing);
                _logger.LogInformation("Seed warehouse created: {Reference}", reference);
            }
            warehouses.Add(existing);
        }

        await _db.SaveChangesAsync();
        return warehouses;
    }

    /// <summary>
    /// Seeds one farm, one in-stock batch, and ~3 weeks of daily measurements
    /// on the Brazil warehouse, so the FIFO list has a batch to click into and
    /// its "Relevés" (measurement curves) page has real data to plot. Values
    /// wobble deterministically (fixed random seed) around the country's
    /// nominal temperature/humidity, so a fresh DB always renders the same
    /// demo curve. Idempotent: skips whatever already exists.
    /// </summary>
    private async Task SeedDemoBatchAndMeasurementsAsync(List<Country> countries, List<Warehouse> warehouses)
    {
        var country = countries.FirstOrDefault(c => c.Code == DemoCountryCode);
        var warehouse = warehouses.FirstOrDefault(w => w.CountryId == country?.Id);
        if (country is null || warehouse is null)
        {
            return;
        }

        var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Reference == DemoFarmReference);
        if (farm is null)
        {
            farm = new Farm
            {
                Id = Guid.NewGuid(),
                CountryId = country.Id,
                Name = "Fazenda Boa Vista",
                Reference = DemoFarmReference,
            };
            _db.Farms.Add(farm);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seed farm created: {Reference}", DemoFarmReference);
        }

        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Reference == DemoBatchReference);
        if (batch is null)
        {
            batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                FarmId = farm.Id,
                Reference = DemoBatchReference,
                StoredAt = DateTime.UtcNow.AddDays(-(DemoMeasurementDays - 1)),
                ShippedAt = null,
                QualityGrade = BatchQualityGrade.A,
                Status = BatchStatus.Stored,
            };
            _db.Batches.Add(batch);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seed batch created: {Reference}", DemoBatchReference);
        }

        var existingDates = await _db.Measurements
            .Where(m => m.WarehouseId == warehouse.Id)
            .Select(m => m.MeasDate)
            .ToListAsync();

        var random = new Random(20260909);
        var addedCount = 0;

        for (var offset = DemoMeasurementDays - 1; offset >= 0; offset--)
        {
            var measDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-offset));
            if (existingDates.Contains(measDate))
            {
                continue;
            }

            var tempWobble = (decimal)(Math.Sin(offset * 0.6) * 1.5) + ((decimal)random.NextDouble() - 0.5m);
            var humidityWobble = (decimal)(Math.Cos(offset * 0.5) * 1.2) + ((decimal)random.NextDouble() - 0.5m);
            var avgTemp = Math.Round(country.NominalTemp + tempWobble, 1);
            var avgHumidity = Math.Round(country.NominalHumidity + humidityWobble, 1);

            _db.Measurements.Add(new Measurement
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                MeasDate = measDate,
                AvgMeasTemp = avgTemp,
                MinMeasTemp = Math.Round(avgTemp - 1.2m - (decimal)random.NextDouble(), 1),
                MaxMeasTemp = Math.Round(avgTemp + 1.2m + (decimal)random.NextDouble(), 1),
                AvgMeasHumidity = avgHumidity,
                MinMeasHumidity = Math.Round(avgHumidity - 1.0m - (decimal)random.NextDouble(), 1),
                MaxMeasHumidity = Math.Round(avgHumidity + 1.0m + (decimal)random.NextDouble(), 1),
            });
            addedCount++;
        }

        if (addedCount > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Seed measurements created: {Count} days for {Reference}.", addedCount, warehouse.Reference);
        }
    }
}