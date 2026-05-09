namespace BoilerTelemetry.Application.DTOs;

/// <summary>
/// DTO с телеметрическими данными котла.
/// </summary>
/// <param name="BoilerId">
/// Идентификатор котла.
/// </param>
/// <param name="Temperature">
/// Значение температуры.
/// </param>
/// <param name="Pressure">
/// Значение давления.
/// </param>
/// <param name="Timestamp">
/// Время получения показаний.
/// </param>
public record TelemetryRequestDto(
    Guid BoilerId,
    double Temperature,
    double Pressure,
    DateTime Timestamp
);
