using System.Security.Claims;
using FutureKawaSiege.Business.Services.Abstraction;
using FutureKawaSiege.Commons.Exceptions.Services;
using FutureKawaSiege.Commons.Extensions;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;
using FutureKawaSiege.Data.Repositories;

namespace FutureKawaSiege.Business.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly IRefreshTokenService _refreshTokenService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        IRefreshTokenService refreshTokenService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _refreshTokenService = refreshTokenService;
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid credentials.");

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenRaw = await _refreshTokenService.CreateAndStoreAsync(user.Id, cancellationToken);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshTokenRaw,
            User: MapToUserResponse(user));
    }

    /// <inheritdoc/>
    public async Task<LoginResponse> RefreshAsync(string refreshTokenRaw, CancellationToken cancellationToken = default)
    {
        var (newRawToken, _, user) = await _refreshTokenService.ValidateAndRotateAsync(refreshTokenRaw, cancellationToken);

        var accessToken = _jwtService.GenerateAccessToken(user);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: newRawToken,
            User: MapToUserResponse(user));
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(string refreshTokenRaw, CancellationToken cancellationToken = default)
    {
        await _refreshTokenService.RevokeAsync(refreshTokenRaw, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<UserResponse> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var userResponse = new UserResponse(
            Id: principal.GetUserId(),
            Email: principal.GetEmail(),
            Role: principal.GetRole(),
            Country: principal.GetCountry(),
            WarehouseId: principal.GetWarehouseId());

        return Task.FromResult(userResponse);
    }

    private static UserResponse MapToUserResponse(Data.Entities.User user) =>
        new(
            Id: user.Id,
            Email: user.Email,
            Role: user.Role,
            Country: user.Country,
            WarehouseId: user.WarehouseId);
}
