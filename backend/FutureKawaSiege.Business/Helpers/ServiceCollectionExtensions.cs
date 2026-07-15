using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Business.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace FutureKawaSiege.Business.Helpers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        // Services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtService, JwtService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
