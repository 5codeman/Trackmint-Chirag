using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TrackMint.Contracts.Events;
using TrackMint.NotificationService.Domain;
using TrackMint.NotificationService.Persistence;

namespace TrackMint.NotificationService.Messaging;

public sealed class NotificationEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<NotificationEventConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IModel? _channel;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(() =>
        {
            _channel?.Close();
            _connection?.Close();
        });

        TryStartConsumer(stoppingToken);
        return Task.CompletedTask;
    }

    private void TryStartConsumer(CancellationToken stoppingToken)
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

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.ExchangeDeclare(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
            _channel.QueueDeclare(_options.QueueName, durable: true, exclusive: false, autoDelete: false);

            foreach (var routingKey in new[]
            {
                "auth.user.registered",
                "finance.budget.threshold_crossed",
                "finance.goal.completed",
                "finance.recurring.generated"
            })
            {
                _channel.QueueBind(_options.QueueName, _options.Exchange, routingKey);
            }

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (_, args) =>
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                await HandleMessageAsync(args, stoppingToken);
            };

            _channel.BasicConsume(_options.QueueName, autoAck: false, consumer);
            logger.LogInformation("Notification RabbitMQ consumer started. Queue={QueueName}", _options.QueueName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RabbitMQ consumer could not start. Notification API will still run.");
        }
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.ToArray());
            var notification = args.RoutingKey switch
            {
                "auth.user.registered" => CreateWelcomeNotification(json),
                "finance.budget.threshold_crossed" => CreateBudgetNotification(json),
                "finance.goal.completed" => CreateGoalNotification(json),
                "finance.recurring.generated" => CreateRecurringNotification(json),
                _ => null
            };

            if (notification is null)
            {
                _channel.BasicAck(args.DeliveryTag, multiple: false);
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

            var exists = await dbContext.Notifications.AnyAsync(
                x => x.UserId == notification.UserId &&
                     x.Type == notification.Type &&
                     x.Title == notification.Title &&
                     x.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-5),
                cancellationToken);

            if (!exists)
            {
                await dbContext.Notifications.AddAsync(notification, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            _channel.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process notification event. RoutingKey={RoutingKey}", args.RoutingKey);
            _channel.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static Notification? CreateWelcomeNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<UserRegisteredEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "welcome",
                Title = "Welcome to TrackMint",
                Message = $"Hi {integrationEvent.DisplayName}, your TrackMint account is ready."
            };
    }

    private static Notification? CreateBudgetNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<BudgetThresholdCrossedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "budget_alert",
                Title = "Budget threshold crossed",
                Message = $"You have used {integrationEvent.UtilizationPercent:N0}% of this budget."
            };
    }

    private static Notification? CreateGoalNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<GoalCompletedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "goal_completed",
                Title = "Goal completed",
                Message = $"You completed your goal: {integrationEvent.GoalName}."
            };
    }

    private static Notification? CreateRecurringNotification(string json)
    {
        var integrationEvent = JsonSerializer.Deserialize<RecurringTransactionGeneratedEvent>(json);
        return integrationEvent is null
            ? null
            : new Notification
            {
                UserId = integrationEvent.UserId,
                Type = "recurring_transaction",
                Title = "Recurring transaction created",
                Message = $"A recurring transaction was generated for {integrationEvent.RunDate:yyyy-MM-dd}."
            };
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
