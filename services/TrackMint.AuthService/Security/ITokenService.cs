namespace TrackMint.AuthService.Security;

public interface ITokenService
{
    string GenerateAccessToken(Guid userId, string email, string displayName);
    string GenerateRefreshToken();
    string GeneratePasswordResetToken();
    string HashToken(string token);
}
