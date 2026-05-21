using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackMint.AuthService.Domain;
using TrackMint.AuthService.Dtos;
using TrackMint.AuthService.Exceptions;
using TrackMint.AuthService.Messaging;
using TrackMint.AuthService.Persistence;
using TrackMint.AuthService.Security;
using TrackMint.Contracts.Events;

namespace TrackMint.AuthService.Services;

public sealed class AuthService(
    AuthDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IIntegrationEventPublisher eventPublisher,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstBlank(request.Email, "Email");
        ValidationGuard.AgainstBlank(request.DisplayName, "Display name");
        ValidationGuard.AgainstInvalidPassword(request.Password);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (exists)
        {
            throw new ValidationException("Email is already registered.");
        }

        var user = new User
        {
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password)
        };

        await dbContext.Users.AddAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishAsync(new UserRegisteredEvent
        {
            EventType = "auth.user.registered",
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName
        }, "auth.user.registered", cancellationToken);

        return await IssueTokensAsync(user, null, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstBlank(request.Email, "Email");
        ValidationGuard.AgainstBlank(request.Password, "Password");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken)
            ?? throw new UnauthorizedRequestException("Invalid email or password.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedRequestException("Invalid email or password.");
        }

        return await IssueTokensAsync(user, null, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstBlank(request.RefreshToken, "Refresh token");

        var hashedToken = tokenService.HashToken(request.RefreshToken);
        var refreshToken = await dbContext.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hashedToken, cancellationToken)
            ?? throw new UnauthorizedRequestException("Refresh token is invalid.");

        if (!refreshToken.IsActive || refreshToken.User is null)
        {
            throw new UnauthorizedRequestException("Refresh token has expired.");
        }

        refreshToken.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(refreshToken.User, refreshToken, cancellationToken);
    }

    public async Task<ForgotPasswordResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstBlank(request.Email, "Email");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return new ForgotPasswordResponse { Message = "If the account exists, a reset token has been generated." };
        }

        var token = tokenService.GeneratePasswordResetToken();
        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.PasswordResetMinutes)
        };

        await dbContext.PasswordResetTokens.AddAsync(resetToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ForgotPasswordResponse
        {
            Message = "Reset token generated. Use the returned token to reset the password.",
            ResetToken = token
        };
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ValidationGuard.AgainstBlank(request.Token, "Reset token");
        ValidationGuard.AgainstInvalidPassword(request.Password);

        var tokenHash = tokenService.HashToken(request.Token);
        var resetToken = await dbContext.PasswordResetTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken)
            ?? throw new ValidationException("Reset token is invalid.");

        if (!resetToken.IsActive || resetToken.User is null)
        {
            throw new ValidationException("Reset token has expired.");
        }

        resetToken.User.PasswordHash = passwordHasher.Hash(request.Password);
        resetToken.UsedAt = DateTime.UtcNow;

        foreach (var refreshToken in dbContext.RefreshTokens.Where(x => x.UserId == resetToken.UserId && x.RevokedAt == null))
        {
            refreshToken.RevokedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, RefreshToken? replacedToken, CancellationToken cancellationToken)
    {
        var accessToken = tokenService.GenerateAccessToken(user.Id, user.Email, user.DisplayName);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var hashedRefreshToken = tokenService.HashToken(rawRefreshToken);

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hashedRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays)
        };

        if (replacedToken is not null)
        {
            replacedToken.ReplacedByTokenHash = hashedRefreshToken;
        }

        await dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken
        };
    }
}
