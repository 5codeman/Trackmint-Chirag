using TrackMint.AuthService.Security;

namespace TrackMint.AuthService.Tests;

public sealed class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void Hash_ShouldVerifyOriginalPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.Hash("StrongPass123");

        Assert.True(hasher.Verify("StrongPass123", hash));
    }

    [Fact]
    public void Verify_ShouldRejectWrongPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("StrongPass123");

        Assert.False(hasher.Verify("WrongPass123", hash));
    }

    [Fact]
    public void Hash_ShouldUseUniqueSalt()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var firstHash = hasher.Hash("StrongPass123");
        var secondHash = hasher.Hash("StrongPass123");

        Assert.NotEqual(firstHash, secondHash);
        Assert.True(hasher.Verify("StrongPass123", firstHash));
        Assert.True(hasher.Verify("StrongPass123", secondHash));
    }
}
