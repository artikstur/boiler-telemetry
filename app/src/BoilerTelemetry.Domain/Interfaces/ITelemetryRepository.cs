using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.Domain.Interfaces;

public interface ITelemetryRepository
{
    Task WriteAsync(TelemetryReading reading, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetryReading>> QueryAsync(Guid boilerId, DateTime from, DateTime to, CancellationToken ct = default);
}
