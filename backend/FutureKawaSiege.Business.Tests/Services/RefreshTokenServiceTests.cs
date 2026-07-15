using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Moq;

namespace FutureKawaSiege.Business.Tests.Services;

public class RefreshTokenServiceTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepoMock = new();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly RefreshTokenService _refreshTokenService;

    public RefreshTokenServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenLifetimeDays"] = "7",
            })
            .Build();

        _refreshTokenService = new RefreshTokenService(
            _refreshTokenRepoMock.Object,
            _jwtServiceMock.Object,
            config);
    }

    [Fact]
    public async Task ValidateAndRotateAsync_Should_ThrowAuthenticationException_When_TokenIsExpired_But_NotRevoked()
    {
        // Assert : create expired token
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = "expired_hash",
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // expired yesterday
            CreatedAt = DateTime.UtcNow.AddDays(-8),
        };

        // Act
        _refreshTokenRepoMock
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        // Assert
        await Assert.ThrowsAsync<AuthenticationException>(
            () => _refreshTokenService.ValidateAndRotateAsync("raw_refresh_token"));
    }
}