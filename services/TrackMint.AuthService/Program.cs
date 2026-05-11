using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrackMint.AuthService.Dtos;
using TrackMint.AuthService.Messaging;
using TrackMint.AuthService.Middleware;
using TrackMint.AuthService.Persistence;
using TrackMint.AuthService.Security;
using TrackMint.AuthService.Services;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContext<AuthDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5434;Database=auth_db;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, global::TrackMint.AuthService.Services.AuthService>();
builder.Services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth-sensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("frontend");
app.UseRateLimiter();

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
    endpoints = new[] { "register", "login", "refresh", "forgot-password", "reset-password" },
    storage = "auth_db",
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

var auth = app.MapGroup("/api/auth");

auth.MapPost("/register", (RegisterRequest request, IAuthService service, CancellationToken cancellationToken) =>
    service.RegisterAsync(request, cancellationToken))
    .RequireRateLimiting("auth-sensitive");

auth.MapPost("/login", (LoginRequest request, IAuthService service, CancellationToken cancellationToken) =>
    service.LoginAsync(request, cancellationToken))
    .RequireRateLimiting("auth-sensitive");

auth.MapPost("/refresh", (RefreshTokenRequest request, IAuthService service, CancellationToken cancellationToken) =>
    service.RefreshAsync(request, cancellationToken));

auth.MapPost("/forgot-password", (ForgotPasswordRequest request, IAuthService service, CancellationToken cancellationToken) =>
    service.ForgotPasswordAsync(request, cancellationToken))
    .RequireRateLimiting("auth-sensitive");

auth.MapPost("/reset-password", async (ResetPasswordRequest request, IAuthService service, CancellationToken cancellationToken) =>
{
    await service.ResetPasswordAsync(request, cancellationToken);
    return Results.NoContent();
}).RequireRateLimiting("auth-sensitive");

await app.Services.InitializeAuthDatabaseAsync();

app.Run();
