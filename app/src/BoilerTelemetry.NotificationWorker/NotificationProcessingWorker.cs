using System.Text.Json;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.NotificationWorker.Persistence;
using BoilerTelemetry.NotificationWorker.Services;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BoilerTelemetry.NotificationWorker;

public class NotificationProcessingWorker : BackgroundService
{
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

                var anomaly = JsonSerializer.Deserialize<AnomalyEvent>(result.Message.Value, JsonOptions);
                if (anomaly is null) continue;

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
