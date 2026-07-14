using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using Moq;

namespace FutureKawaSiege.Business.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock = new();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(
            _userRepoMock.Object,
            _passwordHasherMock.Object,
            _jwtServiceMock.Object,
            _refreshTokenServiceMock.Object);
    }

    private static User CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@futurekawa.com",
        PasswordHash = "hashed_password",
        Role = "Admin",
        Country = "FR",
        WarehouseId = Guid.NewGuid(),
    };

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginResponse_When_CredentialsAreValid()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.Verify("password", user.PasswordHash)).Returns(true);
        _jwtServiceMock.Setup(j => j.GenerateAccessToken(user)).Returns("access_token");
        _refreshTokenServiceMock.Setup(r => r.CreateAndStoreAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("refresh_token");

        var result = await _authService.LoginAsync(new LoginRequest(user.Email, "password"));

        Assert.Equal("access_token", result.AccessToken);
        Assert.Equal("refresh_token", result.RefreshToken);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.Email, result.User.Email);
    }

    [Fact]
    public async Task LoginAsync_Should_ThrowAuthenticationException_When_UserNotFound()
    {
        _userRepoMock.Setup(r => r.GetByEmailAsync("unknown@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.LoginAsync(new LoginRequest("unknown@test.com", "password")));
    }

    [Fact]
    public async Task LoginAsync_Should_ThrowAuthenticationException_When_PasswordIsWrong()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.Verify("wrong", user.PasswordHash)).Returns(false);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.LoginAsync(new LoginRequest(user.Email, "wrong")));
    }

    [Fact]
    public async Task RefreshAsync_Should_ReturnNewTokens_When_RefreshTokenIsValid()
    {
        var user = CreateTestUser();
        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = "new_hash",
            CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7),
        };

        _refreshTokenServiceMock
            .Setup(r => r.ValidateAndRotateAsync("valid_refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("new_raw_refresh", newRefreshToken, user));
        _jwtServiceMock.Setup(j => j.GenerateAccessToken(user)).Returns("new_access_token");

        var result = await _authService.RefreshAsync("valid_refresh");

        Assert.Equal("new_access_token", result.AccessToken);
        Assert.Equal("new_raw_refresh", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_Should_ThrowAuthenticationException_When_RefreshTokenIsRevoked()
    {
        _refreshTokenServiceMock
            .Setup(r => r.ValidateAndRotateAsync("revoked", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException("Invalid or expired refresh token."));

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.RefreshAsync("revoked"));
    }

    [Fact]
    public async Task LogoutAsync_Should_CallRevokeOnce()
    {
        await _authService.LogoutAsync("some_token");

        _refreshTokenServiceMock.Verify(
            r => r.RevokeAsync("some_token", It.IsAny<CancellationToken>()), Times.Once);
    }
}
