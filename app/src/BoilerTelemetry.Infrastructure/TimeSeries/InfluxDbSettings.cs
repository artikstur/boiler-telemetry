namespace BoilerTelemetry.Infrastructure.TimeSeries;

public class InfluxDbSettings
{
    public string Url { get; set; } = "http://localhost:8086";
    public string Token { get; set; } = string.Empty;
    public string Org { get; set; } = string.Empty;
    public string Bucket { get; set; } = "telemetry";
}
