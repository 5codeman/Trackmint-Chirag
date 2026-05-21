using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TrackMint.Contracts.Events;

namespace TrackMint.AuthService.Messaging;

public sealed class RabbitMqIntegrationEventPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    private readonly RabbitMqOptions _options = options.Value;

    public Task PublishAsync(IntegrationEvent integrationEvent, string routingKey, CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                DispatchConsumersAsync = true
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.MessageId = integrationEvent.EventId.ToString();
            properties.Type = integrationEvent.EventType;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.ContentType = "application/json";

            channel.BasicPublish(_options.Exchange, routingKey, basicProperties: properties, body: body);

            logger.LogInformation(
                "Published RabbitMQ integration event. EventType={EventType} RoutingKey={RoutingKey} EventId={EventId}",
                integrationEvent.EventType,
                routingKey,
                integrationEvent.EventId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "RabbitMQ publish failed. Auth transaction remains committed. EventType={EventType} RoutingKey={RoutingKey}",
                integrationEvent.EventType,
                routingKey);
        }

        return Task.CompletedTask;
    }
}
