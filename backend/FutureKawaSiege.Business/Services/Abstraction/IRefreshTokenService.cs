using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Business.Services.Abstraction;

public interface IRefreshTokenService
{
    /// <summary>
    /// Creates a new refresh token for the given user and persists it, returning the raw token string.
    /// </summary>
    /// <param name="userId">The unique identifier of the user for whom the token is issued.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The raw refresh token string to be sent to the client.</returns>
    Task<string> CreateAndStoreAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a raw refresh token, revokes the old one, and issues a new rotated token
    /// for the associated user.
    /// </summary>
    /// <param name="rawToken">The raw refresh token string provided by the client.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A tuple containing the new raw token string, the newly persisted <see cref="RefreshToken"/> entity,
    /// and the associated <see cref="User"/> entity. Throws if the token is invalid or expired.
    /// </returns>
    Task<(string NewRawToken, RefreshToken RefreshToken, User User)> ValidateAndRotateAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the refresh token identified by the raw token string, preventing further use.
    /// </summary>
    /// <param name="rawToken">The raw refresh token string to revoke.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default);
}
