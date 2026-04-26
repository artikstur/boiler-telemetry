namespace BoilerTelemetry.Domain.Entities;

public class AnomalyEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BoilerId { get; set; }
    public string AnomalyType { get; set; } = string.Empty;
    public double ActualValue { get; set; }
    public double Threshold { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
