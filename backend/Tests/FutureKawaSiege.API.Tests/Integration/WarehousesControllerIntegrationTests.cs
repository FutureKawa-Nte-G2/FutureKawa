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

public class WarehousesControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    public WarehousesControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }
    private async Task<string> GetAccessTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
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
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "TestPass123"));
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginBody!.Data!.AccessToken;
    }
    /// <summary>
    /// Seeds a country (created once, reused across calls) and a fresh, uniquely-named
    /// warehouse under it, so tests can assert against known data instead of whatever
    /// happens to already be in the shared test database.
    /// </summary>
    private async Task<(Guid WarehouseId, string WarehouseName)> SeedWarehouseAsync(string countryCode, string countryName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var country = db.Countries.FirstOrDefault(c => c.Code == countryCode);
        if (country == null)
        {
            country = new Country
            {
                Id = Guid.NewGuid(),
                Code = countryCode,
                Name = countryName,
                NominalTemp = 28.0m,
                ToleranceTemp = 3.0m,
                NominalHumidity = 55.0m,
                ToleranceHumidity = 2.0m
            };
            db.Countries.Add(country);
        }
        var warehouseName = $"WH-TEST-{Guid.NewGuid().ToString()[..8]}";
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            CountryId = country.Id,
            Name = warehouseName,
            Reference = warehouseName
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        return (warehouse.Id, warehouseName);
    }
    [Fact]
    public async Task GetAll_Should_Return401_When_NoAuth()
    {
        var response = await _client.GetAsync("/api/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task GetAll_Should_Return200_WithValidAuth()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/warehouses");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WarehouseListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.NotNull(body.Data.Warehouses);
    }
    [Fact]
    public async Task GetAll_Should_FilterByCountryCode()
    {
        // Arrange: one warehouse in BR, one in EC, so the filter is exercised against
        // known data instead of relying on whatever the shared test DB already has.
        var (brWarehouseId, _) = await SeedWarehouseAsync("BR", "Brésil");
        var (ecWarehouseId, _) = await SeedWarehouseAsync("EC", "Équateur");
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/warehouses?country=BR");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WarehouseListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        // The filter must actually return something, and it must be the right thing:
        // the BR warehouse is present, the EC warehouse is not, and every returned
        // item is tagged BR.
        Assert.NotEmpty(body.Data!.Warehouses);
        Assert.Contains(body.Data.Warehouses, w => w.Id == brWarehouseId);
        Assert.DoesNotContain(body.Data.Warehouses, w => w.Id == ecWarehouseId);
        Assert.All(body.Data.Warehouses, w => Assert.Equal("BR", w.CountryCode));
    }
    [Fact]
    public async Task GetAll_Should_ReturnWarehouses_WithIdNameAndCountryCode()
    {
        // Arrange: guarantee at least one warehouse exists so the assertions below
        // are actually exercised instead of vacuously passing on an empty list.
        await SeedWarehouseAsync("BR", "Brésil");
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Act
        var response = await _client.GetAsync("/api/warehouses");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WarehouseListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotEmpty(body.Data!.Warehouses);
        foreach (var warehouse in body.Data.Warehouses)
        {
            Assert.NotEqual(Guid.Empty, warehouse.Id);
            Assert.False(string.IsNullOrEmpty(warehouse.Name));
            Assert.False(string.IsNullOrEmpty(warehouse.CountryCode));
        }
    }
}