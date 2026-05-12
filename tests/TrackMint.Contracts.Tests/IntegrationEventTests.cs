using TrackMint.Contracts.Events;

namespace TrackMint.Contracts.Tests;

public sealed class IntegrationEventTests
{
    [Fact]
    public void FinanceEvents_ShouldExposeStableEventTypeNames()
    {
        Assert.Equal(nameof(TransactionCreatedEvent), new TransactionCreatedEvent
        {
            UserId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Type = "Expense",
            Amount = 100,
            TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow)
        }.EventType);

        Assert.Equal(nameof(BudgetThresholdCrossedEvent), new BudgetThresholdCrossedEvent
        {
            UserId = Guid.NewGuid(),
            BudgetId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            BudgetAmount = 1000,
            ActualSpend = 900,
            UtilizationPercent = 90
        }.EventType);

        Assert.Equal(nameof(GoalCompletedEvent), new GoalCompletedEvent
        {
            UserId = Guid.NewGuid(),
            GoalId = Guid.NewGuid(),
            GoalName = "Emergency Fund",
            TargetAmount = 50000
        }.EventType);
    }

    [Fact]
    public void UserRegisteredEvent_ShouldUseRoutingFriendlyEventType()
    {
        var integrationEvent = new UserRegisteredEvent
        {
            UserId = Guid.NewGuid(),
            Email = "chirag@example.com",
            DisplayName = "Chirag Raj"
        };

        Assert.Equal("auth.user.registered", integrationEvent.EventType);
    }
}
