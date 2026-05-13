using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;

namespace BoilerTelemetry.Application.Services;

public class TelemetryService
{
    private readonly ITelemetryRepository _telemetryRepository;
    private readonly ITelemetryPublisher _telemetryPublisher;

    public TelemetryService(
        ITelemetryRepository telemetryRepository,
        ITelemetryPublisher telemetryPublisher)
    {
        _telemetryRepository = telemetryRepository;
        _telemetryPublisher = telemetryPublisher;
    }

    public async Task IngestAsync(TelemetryRequestDto dto, CancellationToken ct)
    {
        var reading = new TelemetryReading
        {
            BoilerId = dto.BoilerId,
            Temperature = dto.Temperature,
            Pressure = dto.Pressure,
            Timestamp = dto.Timestamp
        };

        await Task.WhenAll(
            _telemetryRepository.WriteAsync(reading, ct),
            _telemetryPublisher.PublishAsync(reading, ct)
        );
    }

    public async Task<IReadOnlyList<TelemetryResponseDto>> GetHistoryAsync(
        Guid boilerId, DateTime from, DateTime to, CancellationToken ct)
    {
        var readings = await _telemetryRepository.QueryAsync(boilerId, from, to, ct);
        return readings
            .Select(r => new TelemetryResponseDto(r.BoilerId, r.Temperature, r.Pressure, r.Timestamp))
            .ToList();
    }
}
