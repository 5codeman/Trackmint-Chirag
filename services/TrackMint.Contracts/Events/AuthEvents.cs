namespace TrackMint.Contracts.Events;

public sealed record UserRegisteredEvent : IntegrationEvent
{
    public UserRegisteredEvent()
    {
        EventType = "auth.user.registered";
    }

    public required string Email { get; init; }
    public required string DisplayName { get; init; }
}
