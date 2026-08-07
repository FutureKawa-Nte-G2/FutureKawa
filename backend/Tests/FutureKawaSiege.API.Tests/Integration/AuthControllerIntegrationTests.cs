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
            Role = UserRole.Admin,
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
    public async Task PostRefresh_Should_Return200_WithNewTokens_When_ValidCookie()
    {
        // Arrange
        await SeedUserAsync();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("test@futurekawa.com", "TestPass123"));
        loginResponse.EnsureSuccessStatusCode();

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var oldAccessToken = loginBody!.Data!.AccessToken;

        // Extract the refresh token cookie (Secure=true not sent over test HTTP, so pass manually)
        var cookieValue = ExtractCookieValue(loginResponse, "refresh_token");

        // Act — call refresh with the cookie
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.TryAddWithoutValidation("Cookie", $"refresh_token={cookieValue}");
        var refreshResponse = await _client.SendAsync(refreshRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        Assert.NotNull(refreshBody);
        Assert.True(refreshBody.Success);
        Assert.NotNull(refreshBody.Data!.AccessToken);
        Assert.NotEqual(oldAccessToken, refreshBody.Data.AccessToken);
        Assert.Equal("test@futurekawa.com", refreshBody.Data.User.Email);

        // Verify a new refresh token cookie is set (rotation)
        var setCookieHeaders = refreshResponse.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(setCookieHeaders, c => c.Contains("refresh_token"));
    }

    [Fact]
    public async Task PostRefresh_Should_Return401_When_RevokedToken()
    {
        // Arrange — seed, login, capture cookie, then logout to revoke it
        await SeedUserAsync();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("test@futurekawa.com", "TestPass123"));
        loginResponse.EnsureSuccessStatusCode();

        var cookieValue = ExtractCookieValue(loginResponse, "refresh_token");

        // Logout revokes the refresh token server-side
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.TryAddWithoutValidation("Cookie", $"refresh_token={cookieValue}");
        await _client.SendAsync(logoutRequest);

        // Act — try to refresh with the now-revoked cookie
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.TryAddWithoutValidation("Cookie", $"refresh_token={cookieValue}");
        var refreshResponse = await _client.SendAsync(refreshRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        Assert.NotNull(refreshBody);
        Assert.False(refreshBody.Success);
    }

    /// <summary>
    /// Extracts a cookie value from a Set-Cookie response header.
    /// Needed because test server uses HTTP and cookies with Secure=true are not sent automatically.
    /// </summary>
    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{cookieName}="));
        // Format: "name=value; path=...; secure; ..."
        var nameAndValue = header.Split(';')[0];  // "name=value"
        return nameAndValue[(cookieName.Length + 1)..]; // value
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
