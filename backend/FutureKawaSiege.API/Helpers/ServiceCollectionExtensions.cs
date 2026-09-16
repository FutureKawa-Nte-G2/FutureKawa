using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FutureKawaSiege.API.Helpers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var loginLimit = int.Parse(configuration["RateLimiting:Login:PermitLimit"] ?? "5");
        var loginWindow = int.Parse(configuration["RateLimiting:Login:WindowSeconds"] ?? "60");
        var refreshLimit = int.Parse(configuration["RateLimiting:Refresh:PermitLimit"] ?? "10");
        var refreshWindow = int.Parse(configuration["RateLimiting:Refresh:WindowSeconds"] ?? "60");
        var webhookLimit = int.Parse(configuration["RateLimiting:OdooWebhook:PermitLimit"] ?? "30");
        var webhookWindow = int.Parse(configuration["RateLimiting:OdooWebhook:WindowSeconds"] ?? "60");
        var alertsLimit = int.Parse(configuration["RateLimiting:AlertsIngest:PermitLimit"] ?? "60");
        var alertsWindow = int.Parse(configuration["RateLimiting:AlertsIngest:WindowSeconds"] ?? "60");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "Too many requests. Please try again later."
                });
                await context.HttpContext.Response.WriteAsync(json, cancellationToken);
            };

            options.AddFixedWindowLimiter("login", config =>
            {
                config.PermitLimit = loginLimit;
                config.Window = TimeSpan.FromSeconds(loginWindow);
                config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                config.QueueLimit = 0;
            });

            options.AddFixedWindowLimiter("refresh", config =>
            {
                config.PermitLimit = refreshLimit;
                config.Window = TimeSpan.FromSeconds(refreshWindow);
                config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                config.QueueLimit = 0;
            });

            options.AddFixedWindowLimiter("odoo_webhook", config =>
            {
                config.PermitLimit = webhookLimit;
                config.Window = TimeSpan.FromSeconds(webhookWindow);
                config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                config.QueueLimit = 0;
            });

            // Partitioned per API key (not a single shared window like the limiters
            // above): a compromised or abused key for one country must not exhaust
            // the budget shared by every other country's key.
            options.AddPolicy("alerts_ingest", context =>
            {
                var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(apiKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = alertsLimit,
                    Window = TimeSpan.FromSeconds(alertsWindow),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
