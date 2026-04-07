namespace BoilerTelemetry.Application.DTOs;

public record CreateBoilerDto(
    string Name,
    string Location,
    double TemperatureThreshold,
    double PressureThreshold
);

public record UpdateBoilerDto(
    string? Name,
    string? Location,
    double? TemperatureThreshold,
    double? PressureThreshold
);
