using System.Security.Cryptography;
using System.Text;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Configuration;

namespace FutureKawaSiege.Business.Services;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly int _refreshTokenLifetimeDays;

    public RefreshTokenService(
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IConfiguration configuration)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
        _refreshTokenLifetimeDays = int.Parse(configuration["Jwt:RefreshTokenLifetimeDays"] ?? "7");
    }

    /// <inheritdoc/>
    public async Task<string> CreateAndStoreAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rawToken = _jwtService.GenerateRefreshToken();
        var tokenHash = ComputeSha256Hash(rawToken);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenLifetimeDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false,
        };

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);

        return rawToken;
    }

    /// <inheritdoc/>
    public async Task<(string NewRawToken, RefreshToken RefreshToken, User User)> ValidateAndRotateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeSha256Hash(rawToken);

        var existing = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (existing is null || existing.IsRevoked || existing.ExpiresAt < DateTime.UtcNow)
            throw new AuthenticationException(
                "Invalid or expired refresh token.");

        // Generate new token
        var newRawToken = _jwtService.GenerateRefreshToken();
        var newTokenHash = ComputeSha256Hash(newRawToken);

        // Revoke old, link to new
        await _refreshTokenRepository.RevokeAsync(existing, newTokenHash, cancellationToken);

        // Create new
        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existing.UserId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenLifetimeDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false,
        };

        await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);

        return (newRawToken, newRefreshToken, existing.User);
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeSha256Hash(rawToken);
        var token = await _refreshTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (token is not null && !token.IsRevoked)
            await _refreshTokenRepository.RevokeAsync(token, cancellationToken: cancellationToken);
    }

    private static string ComputeSha256Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToBase64String(bytes);
    }
}
