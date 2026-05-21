namespace TrackMint.Contracts.Messaging;

public sealed record EventEnvelope<TEvent>
{
    public required TEvent Event { get; init; }
    public required string Exchange { get; init; }
    public required string RoutingKey { get; init; }
}
