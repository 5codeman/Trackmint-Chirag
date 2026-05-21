using System.Text.Json;
using TrackMint.Contracts.Events;
using TrackMint.NotificationService.Domain;

namespace TrackMint.NotificationService.Messaging;

public static class NotificationEventMapper
{
    public static Notification? FromMessage(string routingKey, string json) =>
        routingKey switch
        {
            "auth.user.registered" => CreateWelcomeNotification(json),
            "finance.budget.threshold_crossed" => CreateBudgetNotification(json),
            "finance.goal.completed" => CreateGoalNotification(json),
            "finance.recurring.generated" => CreateRecurringNotification(json),
            _ => null
        };

    private static Notification? CreateWelcomeNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<UserRegisteredEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "welcome",
                Title = "Welcome to TrackMint",
                Message = $"Hi {integrationEvent.DisplayName}, your TrackMint account is ready."
            };
    }

    private static Notification? CreateBudgetNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<BudgetThresholdCrossedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "budget_alert",
                Title = "Budget threshold crossed",
                Message = $"You have used {integrationEvent.UtilizationPercent:N0}% of this budget."
            };
    }

    private static Notification? CreateGoalNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<GoalCompletedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "goal_completed",
                Title = "Goal completed",
                Message = $"You completed your goal: {integrationEvent.GoalName}."
            };
    }

    private static Notification? CreateRecurringNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<RecurringTransactionGeneratedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "recurring_transaction",
                Title = "Recurring transaction created",
                Message = $"A recurring transaction was generated for {integrationEvent.RunDate:yyyy-MM-dd}."
            };
    }
}
