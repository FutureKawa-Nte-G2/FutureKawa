namespace FutureKawaSiege.Business.Services.Abstraction;

public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a plain-text password using a cryptographically strong algorithm (e.g. BCrypt).
    /// </summary>
    /// <param name="password">The plain-text password to hash.</param>
    /// <returns>The salted hash string suitable for storage.</returns>
    string Hash(string password);

    /// <summary>
    /// Verifies a plain-text password against a previously stored hash.
    /// </summary>
    /// <param name="password">The plain-text password provided by the user.</param>
    /// <param name="hash">The stored hash to compare against.</param>
    /// <returns><c>true</c> if the password matches the hash; otherwise <c>false</c>.</returns>
    bool Verify(string password, string hash);
}
