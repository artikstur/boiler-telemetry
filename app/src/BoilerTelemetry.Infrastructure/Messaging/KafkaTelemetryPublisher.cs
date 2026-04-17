using System.Text.Json;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoilerTelemetry.Infrastructure.Messaging;

public class KafkaTelemetryPublisher : ITelemetryPublisher, IDisposable
{
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
            Acks = Acks.Leader,
            LingerMs = 5,
            BatchNumMessages = 10000,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(TelemetryReading reading, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            Key = reading.BoilerId.ToString(),
            Value = JsonSerializer.Serialize(reading, JsonSerializerOptions)
        };

        await _producer.ProduceAsync(_settings.Topic, message, ct);
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public void Dispose() => _producer.Dispose();
}
