using System.Text.Json;
using TrackMint.Contracts.Events;
using TrackMint.NotificationService.Messaging;

namespace TrackMint.NotificationService.Tests;

public sealed class NotificationEventMapperTests
{
    [Fact]
    public void FromMessage_ShouldMapUserRegisteredEventToWelcomeNotification()
    {
        var userId = Guid.NewGuid();
        var integrationEvent = new UserRegisteredEvent
        {
            UserId = userId,
            Email = "chirag@example.com",
            DisplayName = "Chirag Raj"
        };

        var notification = NotificationEventMapper.FromMessage(
            "auth.user.registered",
            JsonSerializer.Serialize(integrationEvent));

        Assert.NotNull(notification);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal("welcome", notification.Type);
        Assert.Contains("Chirag Raj", notification.Message);
    }

    [Fact]
    public void FromMessage_ShouldMapBudgetThresholdEventToBudgetAlert()
    {
        var userId = Guid.NewGuid();
        var integrationEvent = new BudgetThresholdCrossedEvent
        {
            UserId = userId,
            BudgetId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            BudgetAmount = 10000,
            ActualSpend = 8500,
            UtilizationPercent = 85
        };

        var notification = NotificationEventMapper.FromMessage(
            "finance.budget.threshold_crossed",
            JsonSerializer.Serialize(integrationEvent));

        Assert.NotNull(notification);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal("budget_alert", notification.Type);
        Assert.Contains("85", notification.Message);
    }

    [Fact]
    public void FromMessage_ShouldMapGoalCompletedEventToGoalNotification()
    {
        var userId = Guid.NewGuid();
        var integrationEvent = new GoalCompletedEvent
        {
            UserId = userId,
            GoalId = Guid.NewGuid(),
            GoalName = "Emergency Fund",
            TargetAmount = 50000
        };

        var notification = NotificationEventMapper.FromMessage(
            "finance.goal.completed",
            JsonSerializer.Serialize(integrationEvent));

        Assert.NotNull(notification);
        Assert.Equal("goal_completed", notification.Type);
        Assert.Contains("Emergency Fund", notification.Message);
    }

    [Fact]
    public void FromMessage_ShouldIgnoreUnknownRoutingKey()
    {
        var notification = NotificationEventMapper.FromMessage("unknown.event", "{}");

        Assert.Null(notification);
    }
}
