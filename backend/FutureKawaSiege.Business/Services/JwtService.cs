using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FutureKawaSiege.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FutureKawaSiege.Business.Services;

public class JwtService : Abstraction.IJwtService
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenLifetimeMinutes;
    private readonly int _refreshTokenLifetimeDays;

    public JwtService(IConfiguration configuration)
    {
        _issuer = configuration["Jwt:Issuer"] ?? "FutureKawaSiege";
        _audience = configuration["Jwt:Audience"] ?? "FutureKawaSiege";
        _accessTokenLifetimeMinutes = int.Parse(configuration["Jwt:AccessTokenLifetimeMinutes"] ?? "15");
        _refreshTokenLifetimeDays = int.Parse(configuration["Jwt:RefreshTokenLifetimeDays"] ?? "7");

        var secret = configuration["Jwt:Secret"]
            ?? configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException(
                "JWT secret not configured. Set Jwt:Secret in appsettings.json or JWT_SECRET environment variable.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(User user)
    {
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", user.Role.ToString()),
        };

        var claimsList = new List<Claim>(claims);

        if (user.WarehouseId.HasValue)
            claimsList.Add(new Claim("warehouse_id", user.WarehouseId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claimsList,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenLifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc/>
    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            return tokenHandler.ValidateToken(token, GetTokenValidationParameters(), out _);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public TokenValidationParameters GetTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    }
}
