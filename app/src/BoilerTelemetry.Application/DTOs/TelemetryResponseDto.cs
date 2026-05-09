namespace BoilerTelemetry.Application.DTOs;

/// <summary>
/// DTO ответа с телеметрическими данными.
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
/// Время регистрации показаний.
/// </param>
public record TelemetryResponseDto(
    Guid BoilerId,
    double Temperature,
    double Pressure,
    DateTime Timestamp
);
