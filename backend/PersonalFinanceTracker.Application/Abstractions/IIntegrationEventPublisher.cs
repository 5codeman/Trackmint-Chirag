using TrackMint.Contracts.Events;

namespace PersonalFinanceTracker.Application.Abstractions;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, string routingKey, CancellationToken cancellationToken);
}
