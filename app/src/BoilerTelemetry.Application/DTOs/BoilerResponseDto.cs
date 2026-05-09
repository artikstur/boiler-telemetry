namespace BoilerTelemetry.Application.DTOs;

/// <summary>
/// DTO с информацией о котле.
/// </summary>
/// <param name="Id">Идентификатор котла.</param>
/// <param name="Name">Название котла.</param>
/// <param name="Location">Местоположение котла.</param>
/// <param name="TemperatureThreshold">
/// Порог температуры для обнаружения аномалий.
/// </param>
/// <param name="PressureThreshold">
/// Порог давления для обнаружения аномалий.
/// </param>
/// <param name="IsActive">
/// Признак активности котла.
/// </param>
/// <param name="CreatedAt">
/// Дата и время создания котла.
/// </param>
/// <param name="UpdatedAt">
/// Дата и время последнего обновления котла.
/// </param>
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
