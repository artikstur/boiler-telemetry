using System.Diagnostics;
using System.Text.Json;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;
using BoilerTelemetry.Domain.Tracing;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoilerTelemetry.Infrastructure.Messaging;

public class KafkaTelemetryPublisher : ITelemetryPublisher, IDisposable
{
    public static readonly ActivitySource ActivitySource = new("BoilerTelemetry.Api.Kafka");

    private readonly IProducer<string, string> _producer;
    private readonly KafkaSettings _settings;
    private readonly ILogger<KafkaTelemetryPublisher> _logger;

    public KafkaTelemetryPublisher(
        IOptions<KafkaSettings> settings,
        ILogger<KafkaTelemetryPublisher> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            Acks = Acks.All,
            LingerMs = 5,
            BatchNumMessages = 10000,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(TelemetryReading reading, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity(
            $"kafka publish {_settings.Topic}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination", _settings.Topic);
        activity?.SetTag("boiler.id", reading.BoilerId.ToString());

        var message = new Message<string, string>
        {
            Key = reading.BoilerId.ToString(),
            Value = JsonSerializer.Serialize(reading, JsonSerializerOptions),
            Headers = new Headers()
        };
        // Инжектим текущий trace context в Kafka headers — потребитель его подхватит.
        KafkaTracePropagation.Inject(Activity.Current, message.Headers);

        var dr = await _producer.ProduceAsync(_settings.Topic, message, ct);
        activity?.SetTag("messaging.kafka.partition", dr.Partition.Value);
        activity?.SetTag("messaging.kafka.offset", dr.Offset.Value);
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public void Dispose() => _producer.Dispose();
}
