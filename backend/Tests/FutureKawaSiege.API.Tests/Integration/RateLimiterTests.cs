using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace FutureKawaSiege.API.Tests.Integration;

/// <summary>
/// Verifies the behavior of the rate limiter configured on /api/auth/login.
///
/// Each test builds its own <see cref="WebApplicationFactory{TEntryPoint}"/> (not shared
/// via IClassFixture) so the in-memory FixedWindowLimiter counters always start at zero.
/// Sharing a factory across tests would make results depend on execution order.
/// </summary>
public class RateLimiterTests
{
    private const int TestPermitLimit = 10;
    private const int TestWindowSeconds = 60;

    /// <summary>
    /// Builds a factory with a low, deterministic "login" limit, overriding the
    /// permissive defaults from appsettings.Testing.json (100 req/60s), which would
    /// otherwise make it impractical to reach a 429 in a fast integration test.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactoryWithLowLoginLimit()
    {
        const string permitLimitVar = "RateLimiting__Login__PermitLimit";
        const string windowSecondsVar = "RateLimiting__Login__WindowSeconds";

        Environment.SetEnvironmentVariable(permitLimitVar, TestPermitLimit.ToString());
        Environment.SetEnvironmentVariable(windowSecondsVar, TestWindowSeconds.ToString());

        try
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

            // Force the host to actually build now, while the env vars are still set.
            // AddApiRateLimiting bakes the values into local ints at this point, so
            // it's safe to clear the env vars right after.
            _ = factory.Services;

            return factory;
        }
        finally
        {
            // Restore immediately so this doesn't leak into other tests running
            Environment.SetEnvironmentVariable(permitLimitVar, null);
            Environment.SetEnvironmentVariable(windowSecondsVar, null);
        }
    }

    private static async Task SeedUserAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(password),
            Role = "Admin",
            Country = "FR",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PostLogin_Should_Return429_When_ExceedingPermitLimit_Within_Window()
    {
        // Arrange: change LoginLimit for less consuming test, attack tries to login several times
        using var factory = CreateFactoryWithLowLoginLimit();
        using var client = factory.CreateClient();

        var request = new LoginRequest("nobody@futurekawa.com", "WrongPassword123");

        for (var attempt = 1; attempt <= TestPermitLimit; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // Act: the attacker attempts one more time (too much) within the same window 
        var blockedResponse = await client.PostAsJsonAsync("/api/auth/login", request);

        // Assert: attack should be blocked
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
    }

    [Fact]
    public async Task PostLogin_Should_Not_Block_LegitimateUser_When_LimitExhausted_By_AnotherClient()
    {
        // This test expresses the EXPECTED behavior, not the current one: a user who
        // never made a failed attempt themselves should never be locked out because
        // someone else exhausted a shared quota.
        //
        // As long as the "login" limiter stays a single, non-partitioned
        // FixedWindowLimiter (see code review), this test is expected to FAIL — one
        // client can currently exhaust the whole quota and lock out every other
        // user trying to log in, which is a self-inflicted denial of service, not
        // just brute-force protection. It should turn green once the limiter is
        // partitioned per client (e.g. by IP, or by IP + email).
        using var factory = CreateFactoryWithLowLoginLimit();
        using var client = factory.CreateClient();

        const string victimEmail = "victim@futurekawa.com";
        const string victimPassword = "ValidPass123";
        await SeedUserAsync(factory, victimEmail, victimPassword);

        var attackerRequest = new LoginRequest("attacker@futurekawa.com", "GuessedPassword");

        // A single client consumes the entire shared quota with invalid credentials.
        for (var attempt = 1; attempt <= TestPermitLimit; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login", attackerRequest);
        }

        // Act: legitimate user with correct credentials tries to connect
        var victimRequest = new LoginRequest(victimEmail, victimPassword);
        var victimResponse = await client.PostAsJsonAsync("/api/auth/login", victimRequest);

        // Assert: legitimate user should not be blocked 
        Assert.Equal(HttpStatusCode.OK, victimResponse.StatusCode);
    }
}