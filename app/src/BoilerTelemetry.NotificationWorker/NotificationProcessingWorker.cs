using System.Diagnostics;
using System.Text.Json;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Tracing;
using BoilerTelemetry.NotificationWorker.Persistence;
using BoilerTelemetry.NotificationWorker.Services;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Prometheus;

namespace BoilerTelemetry.NotificationWorker;

public class NotificationProcessingWorker : BackgroundService
{
    public static readonly ActivitySource ActivitySource = new("BoilerTelemetry.NotificationWorker");

    private static readonly Counter NotificationsSent = Metrics.CreateCounter(
        "boiler_notifications_sent_total",
        "Количество отправленных уведомлений",
        new CounterConfiguration { LabelNames = new[] { "channel", "status" } });

    private readonly NotificationWorkerSettings _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationProcessingWorker> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public NotificationProcessingWorker(
        IOptions<NotificationWorkerSettings> settings,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationProcessingWorker> logger)
    {
        _settings = settings.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Уступаем поток host startup'у, иначе блокирующий consumer.Consume()
        // не даст Kestrel открыть /health endpoint на 8080.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_settings.InputTopic);
        _logger.LogInformation("Notification Worker started, consuming from {Topic}", _settings.InputTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                if (result?.Message?.Value is null) continue;

                var parentContext = KafkaTracePropagation.Extract(result.Message.Headers);
                using var activity = ActivitySource.StartActivity(
                    $"kafka consume {_settings.InputTopic}",
                    ActivityKind.Consumer,
                    parentContext);
                activity?.SetTag("messaging.system", "kafka");
                activity?.SetTag("messaging.source", _settings.InputTopic);
                activity?.SetTag("messaging.kafka.partition", result.Partition.Value);
                activity?.SetTag("messaging.kafka.offset", result.Offset.Value);
                activity?.SetTag("messaging.consumer_group", _settings.ConsumerGroup);

                var anomaly = JsonSerializer.Deserialize<AnomalyEvent>(result.Message.Value, JsonOptions);
                if (anomaly is null) continue;
                activity?.SetTag("anomaly.type", anomaly.AnomalyType);
                activity?.SetTag("boiler.id", anomaly.BoilerId.ToString());

                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                await sender.SendAsync(anomaly, stoppingToken);

                var notification = new Notification
                {
                    AnomalyEventId = anomaly.Id,
                    BoilerId = anomaly.BoilerId,
                    Channel = "Log",
                    Message = $"{anomaly.AnomalyType}: value={anomaly.ActualValue}, threshold={anomaly.Threshold}",
                    Status = "sent"
                };

                db.Notifications.Add(notification);
                await db.SaveChangesAsync(stoppingToken);

                NotificationsSent.WithLabels(notification.Channel, notification.Status).Inc();
                consumer.Commit(result);
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error");
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown - token was cancelled
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification");
            }
        }

        consumer.Close();
    }
}
