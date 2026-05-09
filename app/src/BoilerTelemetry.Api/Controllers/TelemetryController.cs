using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BoilerTelemetry.Api.Controllers;

/// <summary>
/// API для приёма и получения телеметрии котлов.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryService _service;
    private readonly IValidator<TelemetryRequestDto> _validator;

    public TelemetryController(TelemetryService service, IValidator<TelemetryRequestDto> validator)
    {
        _service = service;
        _validator = validator;
    }

    /// <summary>
    /// Отправить телеметрию котла.
    /// </summary>
    /// <param name="dto">Показания датчиков.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <remarks>
    /// Ограничения:
    /// - Temperature: от -50 до 200
    /// - Pressure: от 0 до 50
    /// - Timestamp не может быть из будущего
    /// </remarks>
    /// <response code="202">Телеметрия успешно принята.</response>
    /// <response code="400">Ошибка валидации.</response>
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] TelemetryRequestDto dto, CancellationToken ct)
    {
        var validation = await _validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        await _service.IngestAsync(dto, ct);
        return Accepted(new { message = "accepted" });
    }

    /// <summary>
    /// Получить историю телеметрии котла за период.
    /// </summary>
    /// <param name="boilerId">Идентификатор котла.</param>
    /// <param name="from">Начало периода (UTC).</param>
    /// <param name="to">Конец периода (UTC).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Список показаний телеметрии.</returns>
    /// <response code="200">История успешно получена.</response>
    /// <response code="400">Некорректный диапазон дат.</response>
    [ProducesResponseType(typeof(IEnumerable<TelemetryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("{boilerId:guid}")]
    public async Task<IActionResult> GetHistory(
        Guid boilerId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (from == default || to == default)
            return BadRequest(new { error = "invalid date format for parameters 'from' and 'to', expected ISO 8601" });

        if (from >= to)
            return BadRequest(new { error = "'from' must be earlier than 'to'" });

        var readings = await _service.GetHistoryAsync(boilerId, from, to, ct);
        return Ok(readings);
    }
}
