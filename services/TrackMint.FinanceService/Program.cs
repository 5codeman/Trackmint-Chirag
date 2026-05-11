using TrackMint.Contracts.Events;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

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
    correlationId = httpContext.Request.Headers[Headers.CorrelationId].FirstOrDefault()
}));

app.Run();
