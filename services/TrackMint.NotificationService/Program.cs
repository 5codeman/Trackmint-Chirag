using TrackMint.Contracts.Events;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.NotificationService",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.MapGet("/api/notifications/service-info", (HttpContext httpContext) => Results.Ok(new
{
    service = "Notification Service",
    owns = new[] { "Notifications" },
    consumes = new[]
    {
        nameof(BudgetThresholdCrossedEvent),
        nameof(GoalCompletedEvent),
        nameof(RecurringTransactionGeneratedEvent)
    },
    plannedChannels = new[] { "in-app" },
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

app.Run();
