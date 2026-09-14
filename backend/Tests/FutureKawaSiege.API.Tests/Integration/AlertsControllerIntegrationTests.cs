using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace FutureKawaSiege.API.Tests.Integration;

// Shares a collection with RateLimiterTests: that class briefly overrides the
// "alerts_ingest" permit limit via a process-wide env var, which would otherwise be
// able to race this class's shared factory if both ran in parallel (see the
// collection's doc comment in RateLimiterTests.cs).
[Collection("RateLimiterSensitive")]
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

    private async Task<string> GetAccessTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        const string email = "alerts-test@futurekawa.com";

        var user = db.Users.FirstOrDefault(u => u.Email == email);
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = hasher.Hash("TestPass123"),
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "TestPass123"));
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginBody!.Data!.AccessToken;
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

        await db.SaveChangesAsync();

        // Store reference for tests
        _testWarehouseReference = warehouse.Reference;
    }

    private string _testWarehouseReference = null!;

    [Fact]
    public async Task Create_Should_Return401_When_NoApiKey()
    {
        // Arrange
        await SeedTestDataAsync();
        var request = new CreateAlertRequest
        {
            WarehouseReference = _testWarehouseReference,
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
            WarehouseReference = _testWarehouseReference,
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

        var request = new CreateAlertRequest
        {
            WarehouseReference = _testWarehouseReference,
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
    public async Task Create_Should_Return404_When_WarehouseReferenceUnknown()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-DOES-NOT-EXIST",
            Type = "temperature"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return400_When_InvalidType()
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseReference = _testWarehouseReference,
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
    [InlineData("condition")]
    [InlineData("CONDITION")]
    [InlineData("expiration")]
    [InlineData("EXPIRATION")]
    public async Task Create_Should_Accept_ValidAlertTypes(string type)
    {
        // Arrange
        await SeedTestDataAsync();
        AddApiKeyHeader();

        var request = new CreateAlertRequest
        {
            WarehouseReference = _testWarehouseReference,
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

        // Create a fresh warehouse to guarantee no pre-existing alert
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var freshWarehouseRef = $"WH-IDEM-{Guid.NewGuid().ToString()[..8]}";
        var freshWarehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = freshWarehouseRef,
            Reference = freshWarehouseRef
        };
        db.Warehouses.Add(freshWarehouse);
        await db.SaveChangesAsync();

        var request = new CreateAlertRequest
        {
            WarehouseReference = freshWarehouseRef,
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

    [Fact]
    public async Task Resolve_Should_Return401_When_NoJwt()
    {
        // Act
        var response = await _client.PatchAsync($"/api/alerts/{Guid.NewGuid()}/resolve", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_Should_Return404_When_AlertDoesNotExist()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.PatchAsync($"/api/alerts/{Guid.NewGuid()}/resolve", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_Should_Return200_And_MarkAlertResolved()
    {
        // Arrange
        await SeedTestDataAsync();
        Guid alertId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var warehouse = db.Warehouses.First(w => w.Reference == _testWarehouseReference);
            var alert = new Alert
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                Type = AlertType.Condition,
                Status = AlertStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            db.Alerts.Add(alert);
            await db.SaveChangesAsync();
            alertId = alert.Id;
        }

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.PatchAsync($"/api/alerts/{alertId}/resolve", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolved = verifyDb.Alerts.First(a => a.Id == alertId);
        Assert.Equal(AlertStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task GetAll_Should_Return401_When_NoJwt()
    {
        // Act
        var response = await _client.GetAsync("/api/alerts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAll_Should_Return400_WithInvalidPagination(int page, int pageSize)
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts?page={page}&pageSize={pageSize}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Should_Return400_WithInvalidStatus()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/alerts?status=not_a_status");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Should_Return200_WithValidAuth()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/alerts?page=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AlertListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
    }

    [Fact]
    public async Task GetAll_Should_Return200_And_FilterByWarehouseAndStatus()
    {
        // Arrange: fresh warehouse (isolated from other tests' data) with one active
        // and one resolved alert, so the status filter is exercised against known data.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-LIST-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-LIST-{Guid.NewGuid().ToString()[..8]}"
        };
        db.Warehouses.Add(warehouse);
        var activeAlert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var resolvedAlert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Humidity,
            Status = AlertStatus.Resolved,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ResolvedAt = DateTime.UtcNow
        };
        db.Alerts.AddRange(activeAlert, resolvedAlert);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts?warehouseId={warehouse.Id}&status=active");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AlertListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        var returned = Assert.Single(body.Data!.Alerts, a => a.Id == activeAlert.Id);
        Assert.Equal("active", returned.Status);
        Assert.DoesNotContain(body.Data.Alerts, a => a.Id == resolvedAlert.Id);
    }

    [Fact]
    public async Task GetAll_Should_ReturnMostRecentFirst()
    {
        // Arrange: fresh warehouse with two alerts created at different times, so the
        // sort order is exercised against known data.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-SORT-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-SORT-{Guid.NewGuid().ToString()[..8]}"
        };
        db.Warehouses.Add(warehouse);
        var older = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        };
        var newer = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Humidity,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Alerts.AddRange(older, newer);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts?warehouseId={warehouse.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AlertListResponseDto>>();
        Assert.NotNull(body);
        var ids = body!.Data!.Alerts.Select(a => a.Id).ToList();
        Assert.Equal(new[] { newer.Id, older.Id }, ids);
    }

    [Fact]
    public async Task GetBatches_Should_Return401_When_NoJwt()
    {
        // Act
        var response = await _client.GetAsync($"/api/alerts/{Guid.NewGuid()}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBatches_Should_Return404_When_AlertDoesNotExist()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts/{Guid.NewGuid()}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBatches_Should_Return200_And_IncludeBatch_OverlappingAlertPeriod()
    {
        // Arrange: fresh warehouse/farm, an alert active from -3d to -1d, and a batch
        // stored at -2d (inside the alert's active period), still in stock.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-BATCHES-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-BATCHES-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Fazenda Teste",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Resolved,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            ResolvedAt = DateTime.UtcNow.AddDays(-1)
        };
        var overlappingBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = "BATCH-OVERLAP-001",
            StoredAt = DateTime.UtcNow.AddDays(-2), // inside [-3d, -1d]
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        db.AddRange(warehouse, farm, alert, overlappingBatch);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts/{alert.Id}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<AlertBatchDto>>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        var returned = Assert.Single(body.Data!, b => b.Id == overlappingBatch.Id);
        Assert.Equal("BATCH-OVERLAP-001", returned.BatchRef);
        Assert.Equal("Fazenda Teste", returned.FarmName);
        Assert.Equal("A", returned.QualityGrade);
    }

    [Fact]
    public async Task GetBatches_Should_ExcludeBatch_StoredAfterAlertResolved()
    {
        // Arrange: alert resolved at -1d, batch stored "now" (after resolution) -> no overlap.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-NOOVERLAP-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-NOOVERLAP-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Fazenda Teste",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Resolved,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            ResolvedAt = DateTime.UtcNow.AddDays(-1)
        };
        var laterBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = "BATCH-LATER-001",
            StoredAt = DateTime.UtcNow, // after the alert was resolved
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.B,
            Status = BatchStatus.Stored
        };
        db.AddRange(warehouse, farm, alert, laterBatch);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts/{alert.Id}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<AlertBatchDto>>>();
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Data!, b => b.Id == laterBatch.Id);
    }

    [Fact]
    public async Task GetBatches_Should_ExcludeBatch_ShippedBeforeAlertCreated()
    {
        // Arrange: alert created at -1d (still active), batch shipped at -5d -> no overlap.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-SHIPPED-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-SHIPPED-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Fazenda Teste",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var oldShippedBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = "BATCH-OLD-SHIPPED",
            StoredAt = DateTime.UtcNow.AddDays(-10),
            ShippedAt = DateTime.UtcNow.AddDays(-5), // shipped before the alert was created
            QualityGrade = BatchQualityGrade.C,
            Status = BatchStatus.Shipped
        };
        db.AddRange(warehouse, farm, alert, oldShippedBatch);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts/{alert.Id}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<AlertBatchDto>>>();
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Data!, b => b.Id == oldShippedBatch.Id);
    }

    [Fact]
    public async Task GetBatches_Should_IncludeBatch_StillInStock_When_AlertStillActive()
    {
        // Arrange: alert still active (no ResolvedAt), batch stored well before and
        // still in stock (no ShippedAt) -> both open-ended, must overlap.
        await SeedTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.First(c => c.Code == "BR");
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-OPEN-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-OPEN-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Fazenda Teste",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Humidity,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        var openBatch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = "BATCH-OPEN-001",
            StoredAt = DateTime.UtcNow.AddDays(-30),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        db.AddRange(warehouse, farm, alert, openBatch);
        await db.SaveChangesAsync();

        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/alerts/{alert.Id}/batches");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<AlertBatchDto>>>();
        Assert.NotNull(body);
        Assert.Contains(body.Data!, b => b.Id == openBatch.Id);
    }
}
