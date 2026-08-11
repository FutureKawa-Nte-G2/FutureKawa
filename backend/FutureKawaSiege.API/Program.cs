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
// 
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (args.Contains("--seed"))
{
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("--seed is only allowed in Development or Testing.");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<FutureKawaSiege.Business.Services.Abstraction.IPasswordHasher>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == "test@futurekawa.com");
    if (existing is not null)
    {
        logger.LogInformation("User already exists: test@futurekawa.com / TestPass123");
        return;
    }

    var user = new FutureKawaSiege.Data.Entities.User
    {
        Id = Guid.NewGuid(),
        Email = "test@futurekawa.com",
        PasswordHash = hasher.Hash("TestPass123"),
        Role = FutureKawaSiege.Data.Entities.UserRole.Admin,
        CreatedAt = DateTime.UtcNow,
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    logger.LogInformation("Seed user created:");
    logger.LogInformation("  Email:    test@futurekawa.com");
    logger.LogInformation("  Password: TestPass123");
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();

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

app.MapControllers();

app.Run();

/// <summary>
/// Exposed for integration testing via WebApplicationFactory.
/// </summary>
public partial class Program { }
