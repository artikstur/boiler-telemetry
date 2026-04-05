using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.Domain.Interfaces;

public interface ITelemetryPublisher
{
    Task PublishAsync(TelemetryReading reading, CancellationToken ct = default);
}
