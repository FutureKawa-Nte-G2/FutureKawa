using System.Security.Claims;
using FutureKawaSiege.Commons.Models.API.Requests;
using FutureKawaSiege.Commons.Models.API.Responses;

namespace FutureKawaSiege.Business.Services.Abstraction;

public interface IAuthService
{
    /// <summary>
    /// Authenticates a user with the provided credentials and returns a JWT access token along with a refresh token.
    /// </summary>
    /// <param name="request">The login request containing email and password.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A <see cref="LoginResponse"/> containing access and refresh tokens, or throws if credentials are invalid.</returns>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an expired or active refresh token, rotates it, and returns a new pair of access + refresh tokens.
    /// </summary>
    /// <param name="refreshTokenRaw">The raw refresh token string provided by the client.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A <see cref="LoginResponse"/> containing the new token pair.</returns>
    Task<LoginResponse> RefreshAsync(string refreshTokenRaw, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the given refresh token so it can no longer be used to obtain new access tokens.
    /// </summary>
    /// <param name="refreshTokenRaw">The raw refresh token string to revoke.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task LogoutAsync(string refreshTokenRaw, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the profile of the currently authenticated user extracted from the JWT claims.
    /// </summary>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> from the HTTP context.</param>
    /// <returns>A <see cref="UserResponse"/> with the current user's information.</returns>
    Task<UserResponse> GetCurrentUserAsync(ClaimsPrincipal principal);
}
