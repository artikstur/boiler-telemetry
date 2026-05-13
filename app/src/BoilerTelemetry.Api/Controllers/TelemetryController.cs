using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BoilerTelemetry.Api.Controllers;

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
