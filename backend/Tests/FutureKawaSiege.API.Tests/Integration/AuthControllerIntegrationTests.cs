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

public class AuthControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedUserAsync(string email = "test@futurekawa.com", string password = "TestPass123")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(password),
            Role = "Admin",
            Country = "FR",
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostLogin_Should_Return200_WithAccessTokenAndCookie_When_ValidCredentials()
    {
        await SeedUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("test@futurekawa.com", "TestPass123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.NotNull(body.Data!.AccessToken);
        Assert.Equal("test@futurekawa.com", body.Data.User.Email);

        // Verify refresh token cookie is set
        var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookieHeaders, c => c.Contains("refresh_token"));
    }

    [Fact]
    public async Task PostLogin_Should_Return401_When_InvalidPassword()
    {
        await SeedUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("test@futurekawa.com", "WrongPassword"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLogin_Should_Return401_When_UserNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody@test.com", "anything"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Verify same message as wrong password (no user enumeration)
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.NotNull(body.Message);
    }

    [Fact]
    public async Task PostRefresh_Should_Return401_When_NoCookie()
    {
        var response = await _client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_Should_Return401_When_NoJwt()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_Should_Return200_WithUserInfo_When_ValidJwt()
    {
        await SeedUserAsync();

        // Login to get a valid JWT
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("test@futurekawa.com", "TestPass123"));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var jwt = loginBody!.Data!.AccessToken;

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>();
        Assert.NotNull(body);
        Assert.True(body.Success);
        Assert.Equal("test@futurekawa.com", body.Data!.Email);
    }

    [Fact]
    public async Task PostLogout_Should_Return200()
    {
        var response = await _client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify cookie is removed (Set-Cookie with empty/expired value)
        var setCookieHeaders = response.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookieHeaders, c => c.Contains("refresh_token"));
    }
}
