namespace FutureKawaSiege.Business.Services;

public class PasswordHasher : Abstraction.IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <inheritdoc/>
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <inheritdoc/>
    public bool Verify(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
