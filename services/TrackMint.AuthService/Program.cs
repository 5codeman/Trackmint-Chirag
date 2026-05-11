using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.AuthService",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.MapGet("/api/auth/service-info", (HttpContext httpContext) => Results.Ok(new
{
    service = "Auth Service",
    owns = new[] { "Users", "RefreshTokens", "PasswordResetTokens" },
    plannedEndpoints = new[] { "register", "login", "refresh", "forgot-password", "reset-password" },
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

app.Run();
