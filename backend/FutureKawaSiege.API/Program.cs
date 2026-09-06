using FluentValidation;
using FluentValidation.AspNetCore;
using FutureKawaSiege.API.Helpers;
using FutureKawaSiege.Business.Helpers;
using FutureKawaSiege.Business.Validators;
using FutureKawaSiege.Data;
using FutureKawaSiege.Data.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase("TestDb"));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
}

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IMeasurementRepository, MeasurementRepository>();
builder.Services.AddScoped<IBatchRepository, BatchRepository>();
builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<IWarehouseRepository, WarehouseRepository>();
builder.Services.AddScoped<IAlertRepository, AlertRepository>();

builder.Services.AddBusinessServices();

builder.Services.AddApiRateLimiting(builder.Configuration);

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException(
        "JWT secret not configured. Set Jwt:Secret in appsettings.json or JWT_SECRET environment variable.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "FutureKawaSiege",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "FutureKawaSiege",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // JWT is sent via Authorization header (standard)
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// The frontend runs on its own origin and sends credentials, so the browser
// requires the exact origin to be echoed back: a wildcard is rejected.
const string FrontendCorsPolicy = "frontend";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddHealthChecks();
//
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Applies pending EF migrations then exits, so the container orchestrator can
// sequence "database ready -> schema migrated -> API starts" instead of having
// the API race the migration on every boot.
if (args.Contains("--migrate"))
{
    if (app.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("--migrate is meaningless in Testing (in-memory provider).");

    using var migrationScope = app.Services.CreateScope();
    await migrationScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}

if (args.Contains("--seed"))
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("--seed is only allowed in Development or Testing.");

    using var scope = app.Services.CreateScope();
    var seeder = ActivatorUtilities.CreateInstance<FutureKawaSiege.API.Seed.DevelopmentSeeder>(scope.ServiceProvider);
    await seeder.RunAsync();
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

// Before the rate limiter on purpose: a short-circuited 429 that carries no
// CORS header reaches the browser as an opaque "CORS error" instead of the
// real status, which is what the login limiter produces after 5 attempts.
app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Middleware: validate Odoo webhook token for /api/integration/odoo/* endpoints
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/integration/odoo"))
    {
        var configToken = builder.Configuration["Odoo:WebhookToken"];
        var headerToken = context.Request.Headers["X-Webhook-Token"].FirstOrDefault();

        if (!string.IsNullOrEmpty(configToken) && headerToken == configToken)
        {
            context.Items["OdooWebhookToken"] = configToken;
        }
    }

    await next();
});

// Middleware: validate API key for /api/alerts endpoint (local warehouse API)
// Note: Rejects the request when LocalApi:ApiKey is not configured (fail-closed).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/alerts") && context.Request.Method == "POST")
    {
        var configKey = builder.Configuration["LocalApi:ApiKey"];
        var headerKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();

        // Reject when no key is configured (fail-closed) or when the header does not match
        if (string.IsNullOrEmpty(configKey) || headerKey != configKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: invalid or missing X-Api-Key.");
            return;
        }
    }

    await next();
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

/// <summary>
/// Exposed for integration testing via WebApplicationFactory.
/// </summary>
public partial class Program { }
