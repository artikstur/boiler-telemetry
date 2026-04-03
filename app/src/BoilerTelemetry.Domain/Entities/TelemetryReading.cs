namespace BoilerTelemetry.Domain.Entities;

public class TelemetryReading
{
    public Guid BoilerId { get; set; }
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public DateTime Timestamp { get; set; }
}
