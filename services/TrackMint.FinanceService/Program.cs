using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PersonalFinanceTracker.Api.Middleware;
using PersonalFinanceTracker.Domain.Entities;
using PersonalFinanceTracker.Infrastructure;
using PersonalFinanceTracker.Infrastructure.Persistence;
using PersonalFinanceTracker.Infrastructure.Security;
using PersonalFinanceTracker.Application.Abstractions;
using TrackMint.FinanceService.Messaging;
using TrackMint.Contracts.Events;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IIntegrationEventPublisher, RabbitMqIntegrationEventPublisher>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
    });

builder.Services.AddAuthorization();

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

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("frontend");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        await EnsureUserProjectionAsync(context);
    }

    await next();
});
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.FinanceService",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.MapGet("/api/finance/service-info", (HttpContext httpContext) => Results.Ok(new
{
    service = "Finance Service",
    owns = new[]
    {
        "Accounts",
        "AccountMembers",
        "Categories",
        "Transactions",
        "Budgets",
        "Goals",
        "RecurringTransactions",
        "Rules",
        "AuditLogs"
    },
    publishes = new[]
    {
        nameof(TransactionCreatedEvent),
        nameof(TransactionUpdatedEvent),
        nameof(TransactionDeletedEvent),
        nameof(BudgetThresholdCrossedEvent),
        nameof(GoalCompletedEvent),
        nameof(RecurringTransactionGeneratedEvent)
    },
    storage = "finance_db",
    identityProjection = "Finance stores user id/email/display name as a local projection from JWT claims.",
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

app.MapControllers();

await app.Services.InitializeDatabaseAsync();

app.Run();

static async Task EnsureUserProjectionAsync(HttpContext context)
{
    var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");

    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return;
    }

    var email = context.User.FindFirstValue(ClaimTypes.Email)
        ?? context.User.FindFirstValue("email")
        ?? $"user-{userId:N}@trackmint.local";
    var displayName = context.User.FindFirstValue("displayName") ?? email;

    var dbContext = context.RequestServices.GetRequiredService<ApplicationDbContext>();
    var exists = await dbContext.Users.AnyAsync(x => x.Id == userId, context.RequestAborted);
    if (exists)
    {
        return;
    }

    await dbContext.Users.AddAsync(new User
    {
        Id = userId,
        Email = email,
        DisplayName = displayName,
        PasswordHash = "owned-by-auth-service"
    }, context.RequestAborted);
    await dbContext.SaveChangesAsync(context.RequestAborted);
}
