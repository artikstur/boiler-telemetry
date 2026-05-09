using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BoilerTelemetry.Api.Controllers;

/// <summary>
/// API для управления котлами.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class BoilersController : ControllerBase
{
    private readonly BoilerService _service;
    private readonly IValidator<CreateBoilerDto> _createValidator;

    public BoilersController(BoilerService service, IValidator<CreateBoilerDto> createValidator)
    {
        _service = service;
        _createValidator = createValidator;
    }

    /// <summary>
    /// Получить список всех котлов.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Список котлов.</returns>
    /// <response code="200">Список успешно получен.</response>
    [ProducesResponseType(typeof(IEnumerable<BoilerResponseDto>), StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var boilers = await _service.GetAllAsync(ct);
        return Ok(boilers);
    }

    /// <summary>
    /// Получить котёл по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор котла.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Информация о котле.</returns>
    /// <response code="200">Котёл найден.</response>
    /// <response code="404">Котёл не найден.</response>
    [ProducesResponseType(typeof(BoilerResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var boiler = await _service.GetByIdAsync(id, ct);

        return boiler is null
            ? NotFound(new { error = "boiler not found" })
            : Ok(boiler);
    }

    /// <summary>
    /// Создать новый котёл.
    /// </summary>
    /// <param name="dto">Данные для создания котла.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Созданный котёл.</returns>
    /// <remarks>
    /// Требования:
    /// - Name должен быть уникальным
    /// - TemperatureThreshold должен быть больше 0
    /// - PressureThreshold должен быть больше 0
    /// </remarks>
    /// <response code="201">Котёл успешно создан.</response>
    /// <response code="400">Ошибка валидации.</response>
    /// <response code="409">Котёл с таким именем уже существует.</response>
    [ProducesResponseType(typeof(BoilerResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBoilerDto dto,
        CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(dto, ct);

        if (!validation.IsValid)
            throw new ValidationException(validation.Errors);

        var created = await _service.CreateAsync(dto, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            created);
    }

    /// <summary>
    /// Обновить данные котла.
    /// </summary>
    /// <param name="id">Идентификатор котла.</param>
    /// <param name="dto">Обновляемые поля.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Обновлённый котёл.</returns>
    /// <remarks>
    /// Все поля являются необязательными.
    /// Обновляются только переданные значения.
    /// </remarks>
    /// <response code="200">Котёл успешно обновлён.</response>
    /// <response code="404">Котёл не найден.</response>
    [ProducesResponseType(typeof(BoilerResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBoilerDto dto,
        CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(id, dto, ct);

        return updated is null
            ? NotFound(new { error = "boiler not found" })
            : Ok(updated);
    }

    /// <summary>
    /// Удалить котёл.
    /// </summary>
    /// <param name="id">Идентификатор котла.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат удаления.</returns>
    /// <response code="200">Котёл успешно удалён.</response>
    /// <response code="404">Котёл не найден.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, ct);

        return deleted
            ? Ok()
            : NotFound(new { error = "boiler not found" });
    }
}