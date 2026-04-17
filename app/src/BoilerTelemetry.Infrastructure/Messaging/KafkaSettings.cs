namespace BoilerTelemetry.Infrastructure.Messaging;

public class KafkaSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "telemetry-events";
}
