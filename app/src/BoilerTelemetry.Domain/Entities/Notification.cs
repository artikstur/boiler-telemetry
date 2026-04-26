namespace BoilerTelemetry.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnomalyEventId { get; set; }
    public Guid BoilerId { get; set; }
    public string Channel { get; set; } = "Log";
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = "sent";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
