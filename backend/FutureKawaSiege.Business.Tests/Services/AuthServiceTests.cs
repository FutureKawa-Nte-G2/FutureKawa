using FutureKawaSiege.Business.Services;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Data.Entities;
using FutureKawaSiege.Data.Repositories;
using NSubstitute;

namespace FutureKawaSiege.Business.Tests.Services;

public class AuthServiceTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwtService = Substitute.For<IJwtService>();
    private readonly IRefreshTokenService _refreshTokenService = Substitute.For<IRefreshTokenService>();
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(
            _userRepo,
            _passwordHasher,
            _jwtService,
            _refreshTokenService);
    }

    private static User CreateTestUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@futurekawa.com",
        PasswordHash = "hashed_password",
        Role = UserRole.Admin,
        WarehouseId = Guid.NewGuid(),
    };

    [Fact]
    public async Task LoginAsync_Should_ReturnLoginResponse_When_CredentialsAreValid()
    {
        var user = CreateTestUser();
        _userRepo.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.Verify("password", user.PasswordHash).Returns(true);
        _jwtService.GenerateAccessToken(user).Returns("access_token");
        _refreshTokenService.CreateAndStoreAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns("refresh_token");

        var result = await _authService.LoginAsync(new LoginRequest(user.Email, "password"));

        Assert.Equal("access_token", result.AccessToken);
        Assert.Equal("refresh_token", result.RefreshToken);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.Email, result.User.Email);
    }

    [Fact]
    public async Task LoginAsync_Should_ThrowAuthenticationException_When_UserNotFound()
    {
        _userRepo.GetByEmailAsync("unknown@test.com", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.LoginAsync(new LoginRequest("unknown@test.com", "password")));
    }

    [Fact]
    public async Task LoginAsync_Should_ThrowAuthenticationException_When_PasswordIsWrong()
    {
        var user = CreateTestUser();
        _userRepo.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.Verify("wrong", user.PasswordHash).Returns(false);

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

        _refreshTokenService
            .ValidateAndRotateAsync("valid_refresh", Arg.Any<CancellationToken>())
            .Returns(("new_raw_refresh", newRefreshToken, user));
        _jwtService.GenerateAccessToken(user).Returns("new_access_token");

        var result = await _authService.RefreshAsync("valid_refresh");

        Assert.Equal("new_access_token", result.AccessToken);
        Assert.Equal("new_raw_refresh", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_Should_ThrowAuthenticationException_When_RefreshTokenIsRevoked()
    {
        _refreshTokenService
            .ValidateAndRotateAsync("revoked", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<(string, RefreshToken, User)>(
                new AuthenticationException("Invalid or expired refresh token.")));

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.RefreshAsync("revoked"));
    }

    [Fact]
    public async Task LogoutAsync_Should_CallRevokeOnce()
    {
        await _authService.LogoutAsync("some_token");

        await _refreshTokenService.Received(1)
            .RevokeAsync("some_token", Arg.Any<CancellationToken>());
    }
}
