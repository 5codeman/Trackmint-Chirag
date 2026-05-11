using TrackMint.Contracts.Events;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.InsightsService",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.MapGet("/api/insights/service-info", (HttpContext httpContext) => Results.Ok(new
{
    service = "Insights Service",
    owns = new[] { "DashboardReadModels", "ReportReadModels", "ForecastReadModels" },
    consumes = new[]
    {
        nameof(TransactionCreatedEvent),
        nameof(TransactionUpdatedEvent),
        nameof(TransactionDeletedEvent),
        nameof(BudgetThresholdCrossedEvent),
        nameof(GoalCompletedEvent)
    },
    plannedEndpoints = new[] { "dashboard", "reports", "forecast", "health-score", "insight-cards" },
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

app.Run();
