using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TrackMint.Contracts.Events;
using TrackMint.Contracts.Http;
using TrackMint.NotificationService.Domain;
using TrackMint.NotificationService.Dtos;
using TrackMint.NotificationService.Messaging;
using TrackMint.NotificationService.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5434;Database=notification_db;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddHostedService<NotificationEventConsumer>();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TrackMint";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TrackMint.Client";
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? "replace-this-in-production-with-a-long-random-key";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
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

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.NotificationService",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.MapGet("/api/notifications/service-info", (HttpContext httpContext) => Results.Ok(new
{
    service = "Notification Service",
    owns = new[] { "Notifications", "User notification read state" },
    consumes = new[]
    {
        nameof(BudgetThresholdCrossedEvent),
        nameof(GoalCompletedEvent),
        nameof(RecurringTransactionGeneratedEvent)
    },
    storage = "notification_db",
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

var notifications = app.MapGroup("/api/notifications").RequireAuthorization();

notifications.MapGet("/", async (NotificationDbContext dbContext, HttpContext context, CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(context);
    var items = await dbContext.Notifications
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(50)
        .Select(x => new NotificationResponse
        {
            Id = x.Id,
            UserId = x.UserId,
            Type = x.Type,
            Title = x.Title,
            Message = x.Message,
            IsRead = x.IsRead,
            CreatedAtUtc = x.CreatedAtUtc,
            ReadAtUtc = x.ReadAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(items);
});

notifications.MapPost("/", async (CreateNotificationRequest request, NotificationDbContext dbContext, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { message = "Title and message are required." });
    }

    var notification = new Notification
    {
        UserId = request.UserId,
        Type = string.IsNullOrWhiteSpace(request.Type) ? "general" : request.Type.Trim(),
        Title = request.Title.Trim(),
        Message = request.Message.Trim()
    };

    await dbContext.Notifications.AddAsync(notification, cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/notifications/{notification.Id}", ToResponse(notification));
});

notifications.MapPut("/{id:guid}/read", async (Guid id, NotificationDbContext dbContext, HttpContext context, CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(context);
    var notification = await dbContext.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
    if (notification is null)
    {
        return Results.NotFound(new { message = "Notification not found." });
    }

    notification.IsRead = true;
    notification.ReadAtUtc = DateTime.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToResponse(notification));
});

await app.Services.InitializeNotificationDatabaseAsync();

app.Run();

static Guid GetCurrentUserId(HttpContext context)
{
    var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");

    return Guid.TryParse(value, out var userId)
        ? userId
        : throw new UnauthorizedAccessException("User is not authenticated.");
}

static NotificationResponse ToResponse(Notification notification) => new()
{
    Id = notification.Id,
    UserId = notification.UserId,
    Type = notification.Type,
    Title = notification.Title,
    Message = notification.Message,
    IsRead = notification.IsRead,
    CreatedAtUtc = notification.CreatedAtUtc,
    ReadAtUtc = notification.ReadAtUtc
};
