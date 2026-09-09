using FutureKawaSiege.Data.Entities;

namespace FutureKawaSiege.Data.Repositories;

public interface IUserRepository
{
    /// <summary>
    /// Retrieves a user by their email address (unique, case-insensitive match).
    /// </summary>
    /// <param name="email">The email address to search for.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The <see cref="User"/> entity if found; otherwise <c>null</c>.</returns>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing user entity in the data store.
    /// </summary>
    /// <param name="user">The user entity with modified fields.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
