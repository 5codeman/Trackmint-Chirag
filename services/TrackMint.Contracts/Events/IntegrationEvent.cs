namespace TrackMint.Contracts.Events;

public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required string EventType { get; init; }
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public required Guid UserId { get; init; }
    public string? CorrelationId { get; init; }
}
