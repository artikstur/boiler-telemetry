namespace BoilerTelemetry.NotificationWorker;

public class NotificationWorkerSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroup { get; set; } = "notification-worker-group";
    public string InputTopic { get; set; } = "anomaly-events";
}
