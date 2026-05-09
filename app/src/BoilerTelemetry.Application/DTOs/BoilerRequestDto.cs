namespace BoilerTelemetry.Application.DTOs;

/// <summary>
/// DTO для создания нового котла.
/// </summary>
/// <param name="Name">Уникальное название котла.</param>
/// <param name="Location">Местоположение котла.</param>
/// <param name="TemperatureThreshold">
/// Порог температуры, при превышении которого фиксируется аномалия.
/// </param>
/// <param name="PressureThreshold">
/// Порог давления, при превышении которого фиксируется аномалия.
/// </param>
public record CreateBoilerDto(
    string Name,
    string Location,
    double TemperatureThreshold,
    double PressureThreshold
);

/// <summary>
/// DTO для частичного обновления данных котла.
/// Все поля являются необязательными.
/// </summary>
/// <param name="Name">Новое название котла.</param>
/// <param name="Location">Новое местоположение котла.</param>
/// <param name="TemperatureThreshold">Новый порог температуры.</param>
/// <param name="PressureThreshold">Новый порог давления.</param>
public record UpdateBoilerDto(
    string? Name,
    string? Location,
    double? TemperatureThreshold,
    double? PressureThreshold
);
