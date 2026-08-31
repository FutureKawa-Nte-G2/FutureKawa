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
        // Arrange
        var token = await GetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/warehouses?country=BR");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<WarehouseListResponseDto>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        
        // If there are warehouses returned, they should all have countryCode BR
        if (body.Data!.Warehouses.Any())
        {
            Assert.All(body.Data.Warehouses, w => Assert.Equal("BR", w.CountryCode));
        }
    }

    [Fact]
    public async Task GetAll_Should_ReturnWarehouses_WithIdNameAndCountryCode()
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
        
        foreach (var warehouse in body.Data!.Warehouses)
        {
            Assert.NotEqual(Guid.Empty, warehouse.Id);
            Assert.False(string.IsNullOrEmpty(warehouse.Name));
            Assert.False(string.IsNullOrEmpty(warehouse.CountryCode));
        }
    }
}
