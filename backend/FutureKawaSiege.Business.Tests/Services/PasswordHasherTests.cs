using FutureKawaSiege.Business.Services;

namespace FutureKawaSiege.Business.Tests.Services;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_Should_ReturnNonEmptyString_When_ValidPassword()
    {
        var hash = _hasher.Hash("TestPassword123");
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Hash_Should_ProduceUniqueHashPerCall_When_SamePassword()
    {
        var hash1 = _hasher.Hash("SamePassword");
        var hash2 = _hasher.Hash("SamePassword");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_Should_ReturnTrue_When_CorrectPassword()
    {
        var hash = _hasher.Hash("MySecret123");
        Assert.True(_hasher.Verify("MySecret123", hash));
    }

    [Fact]
    public void Verify_Should_ReturnFalse_When_WrongPassword()
    {
        var hash = _hasher.Hash("MySecret123");
        Assert.False(_hasher.Verify("WrongPassword", hash));
    }
}
