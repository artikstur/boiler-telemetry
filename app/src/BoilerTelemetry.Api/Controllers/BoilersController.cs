using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BoilerTelemetry.Api.Controllers;

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

    [ProducesResponseType(typeof(IEnumerable<BoilerResponseDto>), StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var boilers = await _service.GetAllAsync(ct);
        return Ok(boilers);
    }

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