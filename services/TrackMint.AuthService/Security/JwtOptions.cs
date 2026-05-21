namespace TrackMint.AuthService.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TrackMint";
    public string Audience { get; set; } = "TrackMint.Client";
    public string SigningKey { get; set; } = "replace-this-in-production-with-a-long-random-key";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 7;
    public int PasswordResetMinutes { get; set; } = 15;
}
