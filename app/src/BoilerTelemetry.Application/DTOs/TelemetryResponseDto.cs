namespace BoilerTelemetry.Application.DTOs;

public record TelemetryResponseDto(
    Guid BoilerId,
    double Temperature,
    double Pressure,
    DateTime Timestamp
);
