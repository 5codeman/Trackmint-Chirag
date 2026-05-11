using Microsoft.Extensions.Logging;
using PersonalFinanceTracker.Application.Abstractions;
using TrackMint.Contracts.Events;

namespace PersonalFinanceTracker.Infrastructure.Messaging;

public sealed class NoOpIntegrationEventPublisher(ILogger<NoOpIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEvent integrationEvent, string routingKey, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Integration event skipped by no-op publisher. EventType={EventType} RoutingKey={RoutingKey} EventId={EventId}",
            integrationEvent.EventType,
            routingKey,
            integrationEvent.EventId);

        return Task.CompletedTask;
    }
}
