namespace BoilerTelemetry.Application.DTOs;

public record BoilerResponseDto(
    Guid Id,
    string Name,
    string Location,
    double TemperatureThreshold,
    double PressureThreshold,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
