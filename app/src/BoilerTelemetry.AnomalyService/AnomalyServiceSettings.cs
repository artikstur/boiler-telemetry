namespace BoilerTelemetry.AnomalyService;

public class AnomalyServiceSettings
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroup { get; set; } = "anomaly-service-group";
    public string InputTopic { get; set; } = "telemetry-events";
    public string OutputTopic { get; set; } = "anomaly-events";
}
