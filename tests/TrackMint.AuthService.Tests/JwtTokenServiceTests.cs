using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using TrackMint.AuthService.Security;

namespace TrackMint.AuthService.Tests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_ShouldIncludeUserClaims()
    {
        var userId = Guid.NewGuid();
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "TrackMint.Tests",
            Audience = "TrackMint.Tests.Client",
            SigningKey = "trackmint-test-signing-key-with-enough-length"
        }));

        var token = service.GenerateAccessToken(userId, "chirag@example.com", "Chirag Raj");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("TrackMint.Tests", jwt.Issuer);
        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == userId.ToString());
        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Email && claim.Value == "chirag@example.com");
        Assert.Contains(jwt.Claims, claim => claim.Type == "displayName" && claim.Value == "Chirag Raj");
    }

    [Fact]
    public void HashToken_ShouldBeDeterministic()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions()));

        var firstHash = service.HashToken("refresh-token");
        var secondHash = service.HashToken("refresh-token");

        Assert.Equal(firstHash, secondHash);
        Assert.NotEqual("refresh-token", firstHash);
    }
}
