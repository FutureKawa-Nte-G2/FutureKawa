using System.Net;
using System.Net.Http.Json;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FutureKawaSiege.API.Tests.Integration;

public class AlertsControllerSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AlertsControllerSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Should_Return401_When_NoApiKeyIsConfigured()
    {
        // Arrange : LocalApi:ApiKey vide -> le middleware doit rejeter la requête (fail-closed)
        await using var factoryWithoutApiKey = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalApi:ApiKey"] = ""
                });
            });
        });

        using var client = factoryWithoutApiKey.CreateClient();

        Guid warehouseId;
        using (var scope = factoryWithoutApiKey.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var country = new Country
            {
                Id = Guid.NewGuid(),
                Code = "BR",
                Name = "Brésil",
                NominalTemp = 29.0m,
                ToleranceTemp = 3.0m,
                NominalHumidity = 55.0m,
                ToleranceHumidity = 2.0m
            };
            var warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                CountryId = country.Id,
                Name = "Attacker Target",
                Reference = "WH-ATK"
            };

            db.AddRange(country, warehouse);
            await db.SaveChangesAsync();

            warehouseId = warehouse.Id;
        }

        var forgedAlert = new CreateAlertRequest
        {
            WarehouseId = warehouseId,
            Type = "temperature",
            MeasuredAt = DateTime.UtcNow
        };

        // Act : aucun header X-Api-Key
        var response = await client.PostAsJsonAsync("/api/alerts", forgedAlert);

        // Assert : la requête doit être rejetée
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var verifyScope = factoryWithoutApiKey.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var forgedAlertPersisted = verifyDb.Alerts.Any(a => a.WarehouseId == warehouseId);
        Assert.False(forgedAlertPersisted, "A fake alert was sent without authentication.");
    }
}
