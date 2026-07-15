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
        });

        return services;
    }
}
