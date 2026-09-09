using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FutureKawaSiege.API.Seed;

/// <summary>
/// Seeds minimal reference and demo data (test user, countries, warehouses, farms,
/// batches, alerts, measurements, orders) needed to exercise the API locally without
/// a real local API or manual SQL inserts.
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


    private static readonly (string CountryCode, string Reference, string Name)[] WarehouseDefinitions =
    {
        ("BR", "WH-BR-SANTOS",  "Entrepôt Santos"),
        ("BR", "WH-BR-CERRADO", "Entrepôt Cerrado"),
        ("EC", "WH-EC",         "Entrepôt Équateur"),
        ("CO", "WH-CO",         "Entrepôt Colombie"),
    };

    private static readonly (string CountryCode, string Reference, string Name)[] FarmDefinitions =
    {
        ("BR", "FARM-BR-BOAVISTA",  "Fazenda Boa Vista"),
        ("BR", "FARM-BR-SERRAALTA", "Fazenda Serra Alta"),
        ("BR", "FARM-BR-RIOVERDE",  "Fazenda Rio Verde"),
    };


    private static readonly (string Reference, string WarehouseReference, string FarmName, BatchQualityGrade Quality, DateTime StoredAt, DateTime? ShippedAt, BatchStatus Status)[] BatchDefinitions =
    {
        ("BR-2025-0091", "WH-BR-SANTOS",  "Fazenda Boa Vista",  BatchQualityGrade.A, new DateTime(2025, 4, 10, 8, 0, 0, DateTimeKind.Utc),   null, BatchStatus.Stored),
        ("BR-2026-0014", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.A, new DateTime(2026, 5, 15, 9, 30, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0022", "WH-BR-CERRADO", "Fazenda Boa Vista",  BatchQualityGrade.A, new DateTime(2026, 6, 1, 14, 15, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0035", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.A, new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0041", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.B, new DateTime(2026, 7, 1, 7, 45, 0, DateTimeKind.Utc),   new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc), BatchStatus.Shipped),
        ("BR-2026-0048", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.B, new DateTime(2026, 7, 10, 11, 20, 0, DateTimeKind.Utc), null, BatchStatus.Stored),
        ("BR-2026-0052", "WH-BR-SANTOS",  "Fazenda Boa Vista",  BatchQualityGrade.A, new DateTime(2026, 7, 12, 8, 10, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0055", "WH-BR-CERRADO", "Fazenda Serra Alta", BatchQualityGrade.B, new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc),   null, BatchStatus.Stored),
        ("BR-2026-0058", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.A, new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc), null, BatchStatus.Stored),
        ("BR-2026-0061", "WH-BR-SANTOS",  "Fazenda Boa Vista",  BatchQualityGrade.B, new DateTime(2026, 7, 16, 11, 0, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0064", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.A, new DateTime(2026, 7, 17, 8, 45, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0067", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.B, new DateTime(2026, 7, 18, 9, 20, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0070", "WH-BR-CERRADO", "Fazenda Boa Vista",  BatchQualityGrade.A, new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0073", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.B, new DateTime(2026, 7, 20, 11, 15, 0, DateTimeKind.Utc), null, BatchStatus.Stored),
        ("BR-2026-0076", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.A, new DateTime(2026, 7, 21, 8, 30, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0079", "WH-BR-SANTOS",  "Fazenda Boa Vista",  BatchQualityGrade.B, new DateTime(2026, 7, 22, 9, 50, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0082", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.A, new DateTime(2026, 7, 23, 10, 40, 0, DateTimeKind.Utc), null, BatchStatus.Stored),
        ("BR-2026-0085", "WH-BR-SANTOS",  "Fazenda Rio Verde",  BatchQualityGrade.B, new DateTime(2026, 7, 24, 11, 5, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
        ("BR-2026-0088", "WH-BR-SANTOS",  "Fazenda Boa Vista",  BatchQualityGrade.A, new DateTime(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc),   null, BatchStatus.Stored),
        ("BR-2026-0091", "WH-BR-SANTOS",  "Fazenda Serra Alta", BatchQualityGrade.B, new DateTime(2026, 7, 26, 9, 15, 0, DateTimeKind.Utc),  null, BatchStatus.Stored),
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
        var warehouses = await SeedWarehousesAsync(countries);
        var farms = await SeedFarmsAsync(countries);
        var batches = await SeedBatchesAsync(farms, warehouses);
        await SeedAlertsAsync(warehouses);
        await SeedMeasurementsAsync(warehouses);
        await SeedOrdersAsync(countries, batches);

        _logger.LogInformation(
            "Seed completed: 1 user, {CountryCount} countries, {WarehouseCount} warehouses, {FarmCount} farms, {BatchCount} batches.",
            countries.Count, warehouses.Count, farms.Count, batches.Count);
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

    private async Task<Dictionary<string, Warehouse>> SeedWarehousesAsync(List<Country> countries)
    {
        var countriesByCode = countries.ToDictionary(c => c.Code);
        var warehousesByReference = new Dictionary<string, Warehouse>();

        foreach (var def in WarehouseDefinitions)
        {
            var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Reference == def.Reference);
            if (warehouse is null)
            {
                warehouse = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    CountryId = countriesByCode[def.CountryCode].Id,
                    Name = def.Name,
                    Reference = def.Reference,
                };
                _db.Warehouses.Add(warehouse);
                _logger.LogInformation("Seed warehouse created: {Reference}", def.Reference);
            }
            warehousesByReference[def.Reference] = warehouse;
        }

        await _db.SaveChangesAsync();
        return warehousesByReference;
    }

    private async Task<Dictionary<string, Farm>> SeedFarmsAsync(List<Country> countries)
    {
        var countriesByCode = countries.ToDictionary(c => c.Code);
        var farmsByName = new Dictionary<string, Farm>();

        foreach (var def in FarmDefinitions)
        {
            var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Name == def.Name);
            if (farm is null)
            {
                farm = new Farm
                {
                    Id = Guid.NewGuid(),
                    CountryId = countriesByCode[def.CountryCode].Id,
                    Name = def.Name,
                    Reference = def.Reference,
                };
                _db.Farms.Add(farm);
                _logger.LogInformation("Seed farm created: {Name}", def.Name);
            }
            farmsByName[def.Name] = farm;
        }

        await _db.SaveChangesAsync();
        return farmsByName;
    }

    private async Task<Dictionary<string, Batch>> SeedBatchesAsync(Dictionary<string, Farm> farms, Dictionary<string, Warehouse> warehouses)
    {
        var batchesByReference = new Dictionary<string, Batch>();

        foreach (var def in BatchDefinitions)
        {
            var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Reference == def.Reference);
            if (batch is null)
            {
                batch = new Batch
                {
                    Id = Guid.NewGuid(),
                    Reference = def.Reference,
                    WarehouseId = warehouses[def.WarehouseReference].Id,
                    FarmId = farms[def.FarmName].Id,
                    QualityGrade = def.Quality,
                    StoredAt = def.StoredAt,
                    ShippedAt = def.ShippedAt,
                    Status = def.Status,
                };
                _db.Batches.Add(batch);
                _logger.LogInformation("Seed batch created: {Reference}", def.Reference);
            }
            batchesByReference[def.Reference] = batch;
        }

        await _db.SaveChangesAsync();
        return batchesByReference;
    }

    private async Task SeedAlertsAsync(Dictionary<string, Warehouse> warehouses)
    {
        var cerrado = warehouses["WH-BR-CERRADO"];

        var exists = await _db.Alerts.AnyAsync(a => a.WarehouseId == cerrado.Id && a.Status == AlertStatus.Active);
        if (exists)
        {
            _logger.LogInformation("Seed alert already exists for {Reference}", cerrado.Reference);
            return;
        }

        var createdAt = new DateTime(2026, 7, 13, 6, 0, 0, DateTimeKind.Utc);
        _db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = cerrado.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = createdAt,
            MeasuredAt = createdAt,
            ResolvedAt = null,
        });
        await _db.SaveChangesAsync();
        _logger.LogInformation("Seed alert created: Cerrado, Temperature, Active");
    }

    private async Task SeedMeasurementsAsync(Dictionary<string, Warehouse> warehouses)
    {
        var santos = warehouses["WH-BR-SANTOS"];
        var cerrado = warehouses["WH-BR-CERRADO"];

        var measurements = new (Guid WarehouseId, DateOnly MeasDate, decimal AvgTemp, decimal MaxTemp, decimal MinTemp, decimal AvgHumidity, decimal MinHumidity, decimal MaxHumidity)[]
        {
            (santos.Id,  new DateOnly(2026, 7, 10), 28.5m, 29.8m, 27.1m, 54.0m, 52.5m, 55.5m),
            (santos.Id,  new DateOnly(2026, 7, 11), 28.9m, 30.1m, 27.5m, 55.2m, 53.0m, 56.0m),
            (santos.Id,  new DateOnly(2026, 7, 12), 29.2m, 30.4m, 27.9m, 54.7m, 52.9m, 55.8m),
            (cerrado.Id, new DateOnly(2026, 7, 10), 29.0m, 30.0m, 27.8m, 55.5m, 53.5m, 56.5m),
            (cerrado.Id, new DateOnly(2026, 7, 12), 30.8m, 32.4m, 29.6m, 57.0m, 55.0m, 58.5m),
            (cerrado.Id, new DateOnly(2026, 7, 13), 33.6m, 35.2m, 31.9m, 58.4m, 56.0m, 60.1m),
        };

        foreach (var m in measurements)
        {
            var exists = await _db.Measurements.AnyAsync(x => x.WarehouseId == m.WarehouseId && x.MeasDate == m.MeasDate);
            if (exists) continue;

            _db.Measurements.Add(new Measurement
            {
                Id = Guid.NewGuid(),
                WarehouseId = m.WarehouseId,
                MeasDate = m.MeasDate,
                AvgMeasTemp = m.AvgTemp,
                MaxMeasTemp = m.MaxTemp,
                MinMeasTemp = m.MinTemp,
                AvgMeasHumidity = m.AvgHumidity,
                MinMeasHumidity = m.MinHumidity,
                MaxMeasHumidity = m.MaxHumidity,
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Seed measurements ensured for Santos and Cerrado.");
    }

    private async Task SeedOrdersAsync(List<Country> countries, Dictionary<string, Batch> batches)
    {
        var brazil = countries.First(c => c.Code == "BR");

        var orderDefs = new (string Reference, OrderStatus Status, string ClientName, int? OdooOrderId, string? IntegrationError, string ProductName, decimal Quantity, string[] BatchRefs, DateTime OrderDate)[]
        {
            ("SO-BR-2026-0001", OrderStatus.Pending,   "Torréfaction Lyonnaise SARL", null, null, "Café vert Arabica", 1200m, Array.Empty<string>(),      new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc)),
            ("SO-BR-2026-0002", OrderStatus.Confirmed, "NordCafé Roasters GmbH",      4832, null, "Café vert Arabica", 500m,  new[] { "BR-2026-0058" },   new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc)),
            ("SO-BR-2026-0003", OrderStatus.Shipped,   "Green Bean Importers Ltd",    4791, null, "Café vert Robusta", 800m,  new[] { "BR-2026-0041" },   new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
            ("SO-BR-2026-0004", OrderStatus.Error,     "Café du Monde Trading",       null, "Odoo JSON-RPC error: database \"futurekawa\" does not exist", "Café vert Arabica", 300m, Array.Empty<string>(), new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)),
        };

        foreach (var def in orderDefs)
        {
            var exists = await _db.Orders.AnyAsync(o => o.OrderReference == def.Reference);
            if (exists) continue;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OdooOrderId = def.OdooOrderId,
                OrderReference = def.Reference,
                OrderDate = def.OrderDate,
                ClientName = def.ClientName,
                CountryId = brazil.Id,
                Status = def.Status,
                IntegrationErrorMessage = def.IntegrationError,
                CreatedAt = def.OrderDate,
                UpdatedAt = null,
                Lines = new List<OrderLine>
                {
                    new() { Id = Guid.NewGuid(), ProductName = def.ProductName, Quantity = def.Quantity },
                },
            };

            foreach (var reference in def.BatchRefs)
                order.Batches.Add(batches[reference]);

            _db.Orders.Add(order);
            _logger.LogInformation("Seed order created: {Reference} ({Status})", def.Reference, def.Status);
        }

        await _db.SaveChangesAsync();
    }
}