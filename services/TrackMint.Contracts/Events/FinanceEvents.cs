namespace TrackMint.Contracts.Events;

public sealed record BudgetThresholdCrossedEvent : IntegrationEvent
{
    public BudgetThresholdCrossedEvent()
    {
        EventType = nameof(BudgetThresholdCrossedEvent);
    }

    public required Guid BudgetId { get; init; }
    public required Guid CategoryId { get; init; }
    public required decimal BudgetAmount { get; init; }
    public required decimal ActualSpend { get; init; }
    public required decimal UtilizationPercent { get; init; }
}

public sealed record GoalCompletedEvent : IntegrationEvent
{
    public GoalCompletedEvent()
    {
        EventType = nameof(GoalCompletedEvent);
    }

    public required Guid GoalId { get; init; }
    public required string GoalName { get; init; }
    public required decimal TargetAmount { get; init; }
}

public sealed record RecurringTransactionGeneratedEvent : IntegrationEvent
{
    public RecurringTransactionGeneratedEvent()
    {
        EventType = nameof(RecurringTransactionGeneratedEvent);
    }

    public required Guid RecurringTransactionId { get; init; }
    public required Guid GeneratedTransactionId { get; init; }
    public required DateOnly RunDate { get; init; }
}
