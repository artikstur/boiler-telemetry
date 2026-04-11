using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;

namespace BoilerTelemetry.Application.Services;

public class BoilerService
{
    private readonly IBoilerRepository _repository;

    public BoilerService(IBoilerRepository repository)
    {
        _repository = repository;
    }

    public async Task<BoilerResponseDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var boiler = await _repository.GetByIdAsync(id, ct);
        return boiler is null ? null : ToDto(boiler);
    }

    public async Task<IReadOnlyList<BoilerResponseDto>> GetAllAsync(CancellationToken ct)
    {
        var boilers = await _repository.GetAllAsync(ct);
        return boilers.Select(ToDto).ToList();
    }

    public async Task<BoilerResponseDto> CreateAsync(CreateBoilerDto dto, CancellationToken ct)
    {
        if (await _repository.ExistsByNameAsync(dto.Name, ct))
            throw new InvalidOperationException($"Boiler with name '{dto.Name}' already exists");

        var boiler = new Boiler
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Location = dto.Location,
            TemperatureThreshold = dto.TemperatureThreshold,
            PressureThreshold = dto.PressureThreshold
        };

        var created = await _repository.CreateAsync(boiler, ct);
        return ToDto(created);
    }

    public async Task<BoilerResponseDto?> UpdateAsync(Guid id, UpdateBoilerDto dto, CancellationToken ct)
    {
        var boiler = await _repository.GetByIdAsync(id, ct);
        if (boiler is null) return null;

        if (dto.Name is not null) boiler.Name = dto.Name;
        if (dto.Location is not null) boiler.Location = dto.Location;
        if (dto.TemperatureThreshold.HasValue) boiler.TemperatureThreshold = dto.TemperatureThreshold.Value;
        if (dto.PressureThreshold.HasValue) boiler.PressureThreshold = dto.PressureThreshold.Value;
        boiler.UpdatedAt = DateTime.UtcNow;

        var updated = await _repository.UpdateAsync(boiler, ct);
        return ToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var boiler = await _repository.GetByIdAsync(id, ct);
        if (boiler is null) return false;

        await _repository.DeleteAsync(id, ct);
        return true;
    }

    private static BoilerResponseDto ToDto(Boiler b) => new(
        b.Id, b.Name, b.Location,
        b.TemperatureThreshold, b.PressureThreshold,
        b.IsActive, b.CreatedAt, b.UpdatedAt
    );
}
