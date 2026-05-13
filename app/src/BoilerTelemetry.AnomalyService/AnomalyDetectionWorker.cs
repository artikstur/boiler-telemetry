using System.Diagnostics;
using System.Text.Json;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Tracing;
using Confluent.Kafka;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Prometheus;

namespace BoilerTelemetry.AnomalyService;

public class AnomalyDetectionWorker : BackgroundService
{
    public static readonly ActivitySource ActivitySource = new("BoilerTelemetry.AnomalyService");

    private static readonly Counter AnomaliesDetected = Metrics.CreateCounter(
        "boiler_anomalies_detected_total",
        "Количество обнаруженных аномалий",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    private static readonly Counter TelemetryProcessed = Metrics.CreateCounter(
        "boiler_telemetry_processed_total",
        "Количество обработанных telemetry-событий");

    private readonly AnomalyServiceSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AnomalyDetectionWorker> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AnomalyDetectionWorker(
        IOptions<AnomalyServiceSettings> settings,
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        ILogger<AnomalyDetectionWorker> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            GroupId = _settings.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 5
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

        consumer.Subscribe(_settings.InputTopic);
        _logger.LogInformation("Anomaly Service started, consuming from {Topic}", _settings.InputTopic);

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

                var reading = JsonSerializer.Deserialize<TelemetryReading>(result.Message.Value, JsonOptions);
                if (reading is null) continue;
                activity?.SetTag("boiler.id", reading.BoilerId.ToString());

                var boiler = await GetBoilerAsync(reading.BoilerId, stoppingToken);
                if (boiler is null)
                {
                    _logger.LogWarning("Boiler {BoilerId} not found, skipping", reading.BoilerId);
                    continue;
                }

                TelemetryProcessed.Inc();

                var anomalies = AnomalyDetector.DetectAnomalies(reading, boiler);
                foreach (var anomaly in anomalies)
                {
                    using var publishActivity = ActivitySource.StartActivity(
                        $"kafka publish {_settings.OutputTopic}", ActivityKind.Producer);
                    publishActivity?.SetTag("messaging.system", "kafka");
                    publishActivity?.SetTag("messaging.destination", _settings.OutputTopic);
                    publishActivity?.SetTag("anomaly.type", anomaly.AnomalyType);

                    var message = new Message<string, string>
                    {
                        Key = anomaly.BoilerId.ToString(),
                        Value = JsonSerializer.Serialize(anomaly, JsonOptions),
                        Headers = new Headers()
                    };
                    KafkaTracePropagation.Inject(Activity.Current, message.Headers);

                    var dr = await producer.ProduceAsync(_settings.OutputTopic, message, stoppingToken);
                    publishActivity?.SetTag("messaging.kafka.partition", dr.Partition.Value);
                    publishActivity?.SetTag("messaging.kafka.offset", dr.Offset.Value);
                    AnomaliesDetected.WithLabels(anomaly.AnomalyType).Inc();
                    _logger.LogWarning("Anomaly detected: {Type} on boiler {BoilerId}, value={Value}, threshold={Threshold}",
                        anomaly.AnomalyType, anomaly.BoilerId, anomaly.ActualValue, anomaly.Threshold);
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error");
            }
        }

        consumer.Close();
    }

    private async Task<Boiler?> GetBoilerAsync(Guid boilerId, CancellationToken ct)
    {
        var cacheKey = $"boiler_{boilerId}";

        var cachedJson = await _cache.GetStringAsync(cacheKey, ct);
        if (cachedJson is not null)
            return JsonSerializer.Deserialize<Boiler>(cachedJson, ApiJsonOptions);

        try
        {
            var client = _httpClientFactory.CreateClient("CrudApi");
            var response = await client.GetAsync($"api/v1/boilers/{boilerId}", ct);
            if (!response.IsSuccessStatusCode) return null;

            var boiler = await response.Content.ReadFromJsonAsync<Boiler>(ApiJsonOptions, ct);
            if (boiler is not null)
            {
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                };
                await _cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(boiler, ApiJsonOptions),
                    cacheOptions,
                    ct);
            }

            return boiler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch boiler {BoilerId}", boilerId);
            return null;
        }
    }
}
