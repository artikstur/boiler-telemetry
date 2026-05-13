namespace BoilerTelemetry.Application.DTOs;

public record TelemetryRequestDto(
    Guid BoilerId,
    double Temperature,
    double Pressure,
    DateTime Timestamp
);
