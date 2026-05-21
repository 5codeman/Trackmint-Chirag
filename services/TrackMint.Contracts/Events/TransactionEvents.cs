namespace TrackMint.Contracts.Events;

public sealed record TransactionCreatedEvent : IntegrationEvent
{
    public TransactionCreatedEvent()
    {
        EventType = nameof(TransactionCreatedEvent);
    }

    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public Guid? CategoryId { get; init; }
    public required string Type { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly TransactionDate { get; init; }
}

public sealed record TransactionUpdatedEvent : IntegrationEvent
{
    public TransactionUpdatedEvent()
    {
        EventType = nameof(TransactionUpdatedEvent);
    }

    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public required string Type { get; init; }
    public required decimal Amount { get; init; }
}

public sealed record TransactionDeletedEvent : IntegrationEvent
{
    public TransactionDeletedEvent()
    {
        EventType = nameof(TransactionDeletedEvent);
    }

    public required Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
}
