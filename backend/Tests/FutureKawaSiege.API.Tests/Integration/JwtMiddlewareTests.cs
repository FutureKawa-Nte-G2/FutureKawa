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

/// <summary>
/// Verifies that JWT validation works locally with the shared secret key
/// (no network call to any external identity provider).
/// </summary>
public class JwtMiddlewareTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public JwtMiddlewareTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LocalValidation_Should_ValidateToken_WithoutNetworkCall()
    {
        // Arrange: create a user and get a JWT from the API
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "keytest@futurekawa.com",
            PasswordHash = hasher.Hash("KeyTest123"),
            Role = "Admin",
            Country = "FR",
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("keytest@futurekawa.com", "KeyTest123"));
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var jwt = loginBody!.Data!.AccessToken;

        // Act: validate the JWT locally by calling /me
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt);
        var response = await client.GetAsync("/api/auth/me");

        // Assert: token is valid without any external call
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidSignature_Should_Return401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
