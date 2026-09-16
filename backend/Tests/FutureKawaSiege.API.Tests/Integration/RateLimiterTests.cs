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
/// Tests here mutate process-wide environment variables to override rate limiter
/// permit limits (see <see cref="CreateFactoryWithLowLoginLimit"/> for why: config
/// is read before the host's ConfigureAppConfiguration hooks apply). They share this
/// collection with <see cref="AlertsControllerIntegrationTests"/>, whose shared
/// factory sends many requests through the same "alerts_ingest" limiter these tests
/// briefly lower — without serializing the two, xUnit's default parallel-by-class
/// execution could race the env var against that factory's one-time build.
/// </summary>
[CollectionDefinition("RateLimiterSensitive")]
public class RateLimiterSensitiveCollection;

/// <summary>
/// Verifies the behavior of the rate limiter configured on /api/auth/login.
///
/// Each test builds its own <see cref="WebApplicationFactory{TEntryPoint}"/> (not shared
/// via IClassFixture) so the in-memory FixedWindowLimiter counters always start at zero.
/// Sharing a factory across tests would make results depend on execution order.
/// </summary>
[Collection("RateLimiterSensitive")]
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
            Role = UserRole.Admin,
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

    /// <summary>
    /// Builds a factory with a low, deterministic "alerts_ingest" limit and a single
    /// configured country key, overriding the permissive appsettings.Testing.json defaults.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactoryWithLowAlertsLimit()
    {
        const string permitLimitVar = "RateLimiting__AlertsIngest__PermitLimit";
        const string windowSecondsVar = "RateLimiting__AlertsIngest__WindowSeconds";
        const string apiKeyVar = "LocalApi__Countries__BR__ApiKey";

        Environment.SetEnvironmentVariable(permitLimitVar, TestPermitLimit.ToString());
        Environment.SetEnvironmentVariable(windowSecondsVar, TestWindowSeconds.ToString());
        Environment.SetEnvironmentVariable(apiKeyVar, "rate-limit-test-key");

        try
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

            _ = factory.Services;

            return factory;
        }
        finally
        {
            Environment.SetEnvironmentVariable(permitLimitVar, null);
            Environment.SetEnvironmentVariable(windowSecondsVar, null);
            Environment.SetEnvironmentVariable(apiKeyVar, null);
        }
    }

    [Fact]
    public async Task PostAlerts_Should_Return429_When_ExceedingPermitLimit_Within_Window()
    {
        // Arrange
        using var factory = CreateFactoryWithLowAlertsLimit();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "rate-limit-test-key");

        var request = new CreateAlertRequest
        {
            WarehouseReference = "WH-DOES-NOT-EXIST",
            Type = "temperature"
        };

        for (var attempt = 1; attempt <= TestPermitLimit; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/alerts", request);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // Act: one more push within the same window, beyond the permit limit
        var blockedResponse = await client.PostAsJsonAsync("/api/alerts", request);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
    }

    [Fact]
    public async Task PostAlerts_Should_NotBlock_DifferentApiKey_When_OneKeyExceedsLimit()
    {
        // Arrange: the rate limiter partitions by API key, so a compromised or abused
        // key for one country must not exhaust the budget of every other country.
        const string permitLimitVar = "RateLimiting__AlertsIngest__PermitLimit";
        const string windowSecondsVar = "RateLimiting__AlertsIngest__WindowSeconds";
        const string brKeyVar = "LocalApi__Countries__BR__ApiKey";
        const string coKeyVar = "LocalApi__Countries__CO__ApiKey";

        Environment.SetEnvironmentVariable(permitLimitVar, TestPermitLimit.ToString());
        Environment.SetEnvironmentVariable(windowSecondsVar, TestWindowSeconds.ToString());
        Environment.SetEnvironmentVariable(brKeyVar, "br-rate-limit-key");
        Environment.SetEnvironmentVariable(coKeyVar, "co-rate-limit-key");

        WebApplicationFactory<Program> factory;
        try
        {
            factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
            _ = factory.Services;
        }
        finally
        {
            Environment.SetEnvironmentVariable(permitLimitVar, null);
            Environment.SetEnvironmentVariable(windowSecondsVar, null);
            Environment.SetEnvironmentVariable(brKeyVar, null);
            Environment.SetEnvironmentVariable(coKeyVar, null);
        }

        using (factory)
        {
            using var brClient = factory.CreateClient();
            brClient.DefaultRequestHeaders.Add("X-Api-Key", "br-rate-limit-key");
            using var coClient = factory.CreateClient();
            coClient.DefaultRequestHeaders.Add("X-Api-Key", "co-rate-limit-key");

            var request = new CreateAlertRequest
            {
                WarehouseReference = "WH-DOES-NOT-EXIST",
                Type = "temperature"
            };

            // Exhaust BR's budget
            for (var attempt = 1; attempt <= TestPermitLimit; attempt++)
            {
                await brClient.PostAsJsonAsync("/api/alerts", request);
            }
            var brBlockedResponse = await brClient.PostAsJsonAsync("/api/alerts", request);
            Assert.Equal(HttpStatusCode.TooManyRequests, brBlockedResponse.StatusCode);

            // Act: CO, a different key, should be unaffected
            var coResponse = await coClient.PostAsJsonAsync("/api/alerts", request);

            // Assert
            Assert.NotEqual(HttpStatusCode.TooManyRequests, coResponse.StatusCode);
        }
    }
}