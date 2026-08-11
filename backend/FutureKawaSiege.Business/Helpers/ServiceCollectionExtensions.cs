using System.Threading.Channels;
using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Business.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace FutureKawaSiege.Business.Helpers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Auth services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtService, JwtService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        // Order & Odoo integration services
        services.AddScoped<IOrderService, OrderService>();
        services.AddHttpClient<IOdooIntegrationService, OdooIntegrationService>();

        // Measurement sync services
        services.AddHttpClient<ILocalMeasurementApiService, LocalMeasurementApiService>();
        services.AddScoped<IMeasurementSyncService, MeasurementSyncService>();
        services.AddHostedService<MeasurementSyncBackgroundService>();

        // Delayed order shipment infrastructure
        services.AddSingleton(Channel.CreateUnbounded<ShipmentJob>());
        services.AddSingleton<IOrderShipmentScheduler, OrderShipmentScheduler>();
        services.AddHostedService<OrderShipmentBackgroundService>();

        return services;
    }
}
