using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FutureKawaSiege.API.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Add test configuration for LocalApi:Countries (per-country API keys)
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                ["LocalApi:Countries:BR:ApiKey"] = "local-api-shared-key",
                ["LocalApi:Countries:BR:BaseUrl"] = "http://127.0.0.1:8000"
            };
            config.AddInMemoryCollection(testConfig);
        });
    }
}
