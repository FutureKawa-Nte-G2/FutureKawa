using System.Security.Claims;
using FutureKawaSiege.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace FutureKawaSiege.Business.Services.Abstraction;

public interface IJwtService
{
    /// <summary>
    /// Generates a short-lived JWT access token for the specified user.
    /// </summary>
    /// <param name="user">The <see cref="User"/> entity to issue the token for.</param>
    /// <returns>A signed JWT access token string.</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Generates a cryptographically random refresh token string (opaque, not a JWT).
    /// </summary>
    /// <returns>A base64-encoded random refresh token.</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates a non-expired JWT access token and returns the embedded claims principal.
    /// </summary>
    /// <param name="token">The JWT access token string.</param>
    /// <returns>A <see cref="ClaimsPrincipal"/> if valid; otherwise <c>null</c>.</returns>
    ClaimsPrincipal? ValidateAccessToken(string token);

    /// <summary>
    /// Returns the <see cref="TokenValidationParameters"/> used to validate JWT tokens issued by this service.
    /// </summary>
    /// <returns>The configured token validation parameters (issuer, audience, signing key, etc.).</returns>
    TokenValidationParameters GetTokenValidationParameters();
}
