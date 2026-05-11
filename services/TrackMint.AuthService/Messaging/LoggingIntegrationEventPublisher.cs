using TrackMint.Contracts.Events;

namespace TrackMint.AuthService.Messaging;

public sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public Task PublishAsync(IntegrationEvent integrationEvent, string routingKey, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Integration event published. EventType={EventType} RoutingKey={RoutingKey} EventId={EventId} UserId={UserId} CorrelationId={CorrelationId}",
            integrationEvent.EventType,
            routingKey,
            integrationEvent.EventId,
            integrationEvent.UserId,
            integrationEvent.CorrelationId);

        return Task.CompletedTask;
    }
}
