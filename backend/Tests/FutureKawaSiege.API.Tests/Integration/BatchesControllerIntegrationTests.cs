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

public class BatchesControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    public BatchesControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }
    private async Task<string> GetAccessTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        // Create test user if not exists
        var email = "test@futurekawa.com";
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
            db.SaveChanges();
        }
        // Login to get token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "TestPass123"));
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginBody!.Data!.AccessToken;
    }
    private async Task SeedBatchesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Batches.Any())
        {
            return;
        }
        var country = new Country
        {
            Id = Guid.NewGuid(),
            Code = "BR",
            Name = "Brazil",
            NominalTemp = 29.0m,
            ToleranceTemp = 3.0m,
            NominalHumidity = 55.0m,
            ToleranceHumidity = 2.0m
        };
        db.Countries.Add(country);
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Santos Warehouse",
            Reference = "WH-BR-SANTOS"
        };
        db.Warehouses.Add(warehouse);
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Fazenda Boa Vista",
            Reference = "FARM-BR-001"
        };
        db.Farms.Add(farm);
        db.Batches.AddRange(
            new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                FarmId = farm.Id,
                Reference = "BATCH-BR-001",
                StoredAt = DateTime.UtcNow.AddDays(-10),
                ShippedAt = null,
                QualityGrade = BatchQualityGrade.A,
                Status = BatchStatus.Stored
            },
            new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                FarmId = farm.Id,
                Reference = "BATCH-BR-002",
                StoredAt = DateTime.UtcNow.AddDays(-5),
                ShippedAt = null,
                QualityGrade = BatchQualityGrade.B,
                Status = BatchStatus.Stored
            },
            new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                FarmId = farm.Id,
                Reference = "BATCH-BR-003",
                StoredAt = DateTime.UtcNow.AddDays(-1),
                ShippedAt = null,
                QualityGrade = BatchQualityGrade.C,
                Status = BatchStatus.Stored
            });
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task GetAll_Should_Return401_When_NoAuth()
    {
        var response = await _client.GetAsync("/api/batches");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task GetAll_Should_Return200_WithValidAuth()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/batches?page=1&pageSize=10");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
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
        var response = await _client.GetAsync($"/api/batches?page={page}&pageSize={pageSize}");
        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task GetAll_Should_ReturnPaginatedResults()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/batches?page=1&pageSize=5");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal(1, body.Data!.Page);
        Assert.Equal(5, body.Data.PageSize);
    }
    [Fact]
    public async Task GetAll_Should_FilterByCountryCode()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/batches?country=BR&page=1&pageSize=10");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        // If there are batches, they should all have countryCode BR
        if (body.Data!.Batches.Any())
        {
            Assert.All(body.Data.Batches, b => Assert.Equal("BR", b.CountryCode));
        }
    }
    [Fact]
    public async Task GetAll_Should_ExcludeShippedBatches()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/batches?page=1&pageSize=100");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        // All returned batches should have null ShippedAt
        Assert.All(body.Data!.Batches, b => Assert.Null(b.ShippedAt));
    }
    [Fact]
    public async Task GetAll_Should_ReturnBatchesSortedByEnteredAt_Ascending()
    {
        // Arrange
        await SeedBatchesAsync();
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/batches?page=1&pageSize=10");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);

        var batches = body.Data!.Batches.ToList();
        Assert.NotEmpty(batches);
        // Verify FIFO order (oldest first)
        for (int i = 1; i < batches.Count; i++)
        {
            Assert.True(batches[i - 1].EnteredAt <= batches[i].EnteredAt,
                "Batches should be sorted by EnteredAt in ascending order (FIFO)");
        }
    }
    [Fact]
    public async Task GetAll_Should_ReturnAlertStatus_When_WarehouseHasActiveAlert()
    {
        // Arrange: a fresh warehouse (isolated from other tests' data) with an active
        // sensor alert, and a batch stored well within the 365-day window in it.
        // Exercises the real EF filtered Include (BatchRepository -> Warehouse.Alerts)
        // rather than the mocked repository used by BatchServiceTests.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = new Country
        {
            Id = Guid.NewGuid(),
            Code = "CO",
            Name = "Colombie",
            NominalTemp = 26.0m,
            ToleranceTemp = 3.0m,
            NominalHumidity = 80.0m,
            ToleranceHumidity = 2.0m
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-ALERT-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-ALERT-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Test Farm",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = $"BATCH-{Guid.NewGuid().ToString()[..8]}",
            StoredAt = DateTime.UtcNow.AddDays(-2),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.A,
            Status = BatchStatus.Stored
        };
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            Type = AlertType.Temperature,
            Status = AlertStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(country, warehouse, farm, batch, alert);
        await db.SaveChangesAsync();
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync($"/api/batches?warehouseId={warehouse.Id}&page=1&pageSize=10");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        var returnedBatch = Assert.Single(body.Data!.Batches, b => b.Id == batch.Id);
        Assert.Equal("alert", returnedBatch.Status);
    }

    [Fact]
    public async Task GetById_Should_Return401_When_NoAuth()
    {
        var response = await _client.GetAsync($"/api/batches/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Should_Return404_When_BatchDoesNotExist()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync($"/api/batches/{Guid.NewGuid()}");
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Should_Return200_WithMatchingBatch_When_Found()
    {
        // Arrange: fresh warehouse/farm/batch isolated from other tests' data,
        // used both to exercise the real EF Include (Warehouse/Country/Farm)
        // and to confirm the exact batch (not just any batch) is returned.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = new Country
        {
            Id = Guid.NewGuid(),
            Code = "EC",
            Name = "Équateur",
            NominalTemp = 27.0m,
            ToleranceTemp = 3.0m,
            NominalHumidity = 60.0m,
            ToleranceHumidity = 2.0m
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = $"WH-BYID-{Guid.NewGuid().ToString()[..8]}",
            Reference = $"WH-BYID-{Guid.NewGuid().ToString()[..8]}"
        };
        var farm = new Farm
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = "Test Farm",
            Reference = $"FARM-{Guid.NewGuid().ToString()[..8]}"
        };
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouse.Id,
            FarmId = farm.Id,
            Reference = "BATCH-BYID-001",
            StoredAt = DateTime.UtcNow.AddDays(-3),
            ShippedAt = null,
            QualityGrade = BatchQualityGrade.B,
            Status = BatchStatus.Stored
        };
        db.AddRange(country, warehouse, farm, batch);
        await db.SaveChangesAsync();
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync($"/api/batches/{batch.Id}");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<BatchListItemDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal(batch.Id, body.Data!.Id);
        Assert.Equal(warehouse.Id, body.Data.WarehouseId);
        Assert.Equal("EC", body.Data.CountryCode);
        Assert.Equal("BATCH-BYID-001", body.Data.BatchRef);
    }
}