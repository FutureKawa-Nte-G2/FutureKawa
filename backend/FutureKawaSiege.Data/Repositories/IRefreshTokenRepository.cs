using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Retrieves a refresh token by its SHA-256 hash, including the associated user.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the raw refresh token.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The <see cref="RefreshToken"/> entity with its <see cref="User"/> loaded, or <c>null</c> if not found.</returns>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new refresh token entity.
    /// </summary>
    /// <param name="refreshToken">The refresh token entity to store.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an existing refresh token as revoked and optionally records the hash of the replacement token.
    /// </summary>
    /// <param name="refreshToken">The refresh token entity to revoke.</param>
    /// <param name="replacedByTokenHash">The SHA-256 hash of the new token that replaces this one, if rotating.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task RevokeAsync(RefreshToken refreshToken, string? replacedByTokenHash = null, CancellationToken cancellationToken = default);
}
