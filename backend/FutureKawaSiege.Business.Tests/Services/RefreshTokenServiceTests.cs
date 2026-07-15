using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class RefreshTokenServiceTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepoMock = Substitute.For<IRefreshTokenRepository>();
    private readonly IJwtService _jwtServiceMock = Substitute.For<IJwtService>();
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
            _refreshTokenRepoMock,
            _jwtServiceMock,
            config);
    }

    [Fact]
    public async Task ValidateAndRotateAsync_Should_ThrowAuthenticationException_When_TokenIsExpired_But_NotRevoked()
    {
        // Arrange : create expired token
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = "expired_hash",
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // expired yesterday
            CreatedAt = DateTime.UtcNow.AddDays(-8),
        };

        _refreshTokenRepoMock
            .GetByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expiredToken);

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(
            () => _refreshTokenService.ValidateAndRotateAsync("raw_refresh_token"));
    }
}