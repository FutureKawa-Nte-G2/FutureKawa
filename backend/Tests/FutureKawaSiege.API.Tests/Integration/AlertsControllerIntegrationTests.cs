using System.Net;
using System.Net.Http.Json;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace FutureKawaSiege.API.Tests.Integration;

public class AlertsControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AlertsControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void AddApiKeyHeader()
    {
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", "local-api-shared-key");
    }

    private async Task SeedTestDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create country if not exists
        var country = db.Countries.FirstOrDefault(c => c.Code == "BR");
        if (country == null)
        {
            country = new Country
            {
                Id = Guid.NewGuid(),
                Code = "BR",
                Name = "Brésil",
                NominalTemp = 29.0m,
                ToleranceTemp = 3.0m,
                NominalHumidity = 55.0m,
                ToleranceHumidity = 2.0m
            };
            db.Countries.Add(country);
        }

        // Create warehouse if not exists
        var warehouse = db.Warehouses.FirstOrDefault(w => w.CountryId == country.Id);
        if (warehouse == null)
        {
            warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                CountryId = country.Id,
                Name = "Test Warehouse",
                Reference = "WH-TEST"
            };
            db.Warehouses.Add(warehouse);
        }

        // Create farm if not exists
        var farm = db.Farms.FirstOrDefault();
        if (farm == null)
        {
            farm = new Farm
            {
                Id = Guid.NewGuid(),
                CountryId = country.Id,
                Name = "Test Farm",
                Reference = "FARM-TEST"
            };
            db.Farms.Add(farm);
        }

        // Create batch if not exists
        var batch = db.Batches.FirstOrDefault(b => b.WarehouseId == warehouse.Id);
        if (batch == null)
        {
            batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                FarmId = farm.Id,
                Reference = "BATCH-TEST-001",
                StoredAt = DateTime.UtcNow.AddDays(-10),
                ShippedAt = null,
                QualityGrade = BatchQualityGrade.A,
                Status = BatchStatus.Stored
            };
            db.Batches.Add(batch);
        }

        await db.SaveChangesAsync();

        // Store IDs for tests
        _testWarehouseId = warehouse.Id;
        _testBatchId = batch.Id;
    }

    private Guid _testWarehouseId;
    private Guid _testBatchId;

    [Fact]
    public async Task Create_Should_Return401_When_NoApiKey()
    {
        // Arrange
        await SeedTestDataAsync();
        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = _testBatchId,
            Type = "temperature"
        };

        // Act - No X-Api-Key header
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return401_When_InvalidApiKey()
    {
        // Arrange
        await SeedTestDataAsync();
        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = _testBatchId,
            Type = "temperature"
        };

        // Act - Wrong API key
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return200_When_ValidRequest()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        // Generate unique batch ID to avoid idempotency conflicts
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = _testWarehouseId,
            FarmId = db.Farms.First().Id,
            Reference = $"BATCH-TEST-{Guid.NewGuid().ToString()[..8]}",
            StoredAt = DateTime.UtcNow.AddDays(-5),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        db.Batches.Add(newBatch);
        await db.SaveChangesAsync();

        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = newBatch.Id,
            Type = "temperature",
            MeasuredAt = DateTime.UtcNow
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
    }

    [Fact]
    public async Task Create_Should_Return400_When_WarehouseNotFound()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseId = Guid.NewGuid(), // Non-existent warehouse
            BatchId = _testBatchId,
            Type = "temperature"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return400_When_BatchNotFound()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = Guid.NewGuid(), // Non-existent batch
            Type = "temperature"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return400_When_InvalidType()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = _testBatchId,
            Type = "invalid_type"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("temperature")]
    [InlineData("humidity")]
    [InlineData("TEMPERATURE")]
    [InlineData("HUMIDITY")]
    public async Task Create_Should_Accept_ValidAlertTypes(string type)
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        // Create a new batch for each test to avoid idempotency
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = _testWarehouseId,
            FarmId = db.Farms.First().Id,
            Reference = $"BATCH-TEST-{Guid.NewGuid().ToString()[..8]}",
            StoredAt = DateTime.UtcNow.AddDays(-5),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        db.Batches.Add(newBatch);
        await db.SaveChangesAsync();

        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = newBatch.Id,
            Type = type
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_BeIdempotent_When_AlertAlreadyExists()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        // Create a new batch
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = _testWarehouseId,
            FarmId = db.Farms.First().Id,
            Reference = $"BATCH-TEST-{Guid.NewGuid().ToString()[..8]}",
            StoredAt = DateTime.UtcNow.AddDays(-5),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        db.Batches.Add(newBatch);
        await db.SaveChangesAsync();

        var request = new CreateAlertRequest
        {
            WarehouseId = _testWarehouseId,
            BatchId = newBatch.Id,
            Type = "temperature"
        };

        // Act - Create first alert
        var response1 = await _client.PostAsJsonAsync("/api/alerts", request);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Act - Try to create same alert again (should succeed due to idempotency)
        var response2 = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert - Both should return OK
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }
}
