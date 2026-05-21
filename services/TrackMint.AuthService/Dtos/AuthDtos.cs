namespace TrackMint.AuthService.Dtos;

public sealed class RegisterRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string DisplayName { get; init; }
}

public sealed class LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}

public sealed class ForgotPasswordRequest
{
    public required string Email { get; init; }
}

public sealed class ResetPasswordRequest
{
    public required string Token { get; init; }
    public required string Password { get; init; }
}

public sealed class AuthResponse
{
    public required Guid UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}

public sealed class ForgotPasswordResponse
{
    public required string Message { get; init; }
    public string? ResetToken { get; init; }
}
