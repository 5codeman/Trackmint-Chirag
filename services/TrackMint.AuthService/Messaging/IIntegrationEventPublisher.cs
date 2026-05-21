using TrackMint.Contracts.Events;

namespace TrackMint.AuthService.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, string routingKey, CancellationToken cancellationToken);
}
