using System.Text.Json;
using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace BoilerTelemetry.Application.Services;

public class BoilerService
{
    private readonly IBoilerRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<BoilerService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string AllBoilersCacheKey = "boilers:all";
    private static string BoilerCacheKey(Guid id) => $"boilers:{id}";

    public BoilerService(IBoilerRepository repository, IDistributedCache cache, ILogger<BoilerService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BoilerResponseDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var key = BoilerCacheKey(id);
        var cached = await _cache.GetStringAsync(key, ct);
        if (cached is not null)
        {
            _logger.LogInformation("Cache HIT for boiler {BoilerId}", id);
            return JsonSerializer.Deserialize<BoilerResponseDto>(cached, JsonOpts);
        }

        _logger.LogInformation("Cache MISS for boiler {BoilerId}, fetching from DB", id);
        var boiler = await _repository.GetByIdAsync(id, ct);
        if (boiler is null) return null;

        var dto = ToDto(boiler);
        await _cache.SetStringAsync(key, JsonSerializer.Serialize(dto, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) }, ct);
        return dto;
    }

    public async Task<IReadOnlyList<BoilerResponseDto>> GetAllAsync(CancellationToken ct)
    {
        var cached = await _cache.GetStringAsync(AllBoilersCacheKey, ct);
        if (cached is not null)
        {
            _logger.LogInformation("Cache HIT for all boilers");
            return JsonSerializer.Deserialize<List<BoilerResponseDto>>(cached, JsonOpts) ?? [];
        }

        _logger.LogInformation("Cache MISS for all boilers, fetching from DB");
        var boilers = await _repository.GetAllAsync(ct);
        var dtos = boilers.Select(ToDto).ToList();
        await _cache.SetStringAsync(AllBoilersCacheKey, JsonSerializer.Serialize(dtos, JsonOpts),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, ct);
        return dtos;
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
        await _cache.RemoveAsync(AllBoilersCacheKey, ct);
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
        await Task.WhenAll(
            _cache.RemoveAsync(BoilerCacheKey(id), ct),
            _cache.RemoveAsync(AllBoilersCacheKey, ct));
        return ToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var boiler = await _repository.GetByIdAsync(id, ct);
        if (boiler is null) return false;

        await _repository.DeleteAsync(id, ct);
        await Task.WhenAll(
            _cache.RemoveAsync(BoilerCacheKey(id), ct),
            _cache.RemoveAsync(AllBoilersCacheKey, ct));
        return true;
    }

    private static BoilerResponseDto ToDto(Boiler b) => new(
        b.Id, b.Name, b.Location,
        b.TemperatureThreshold, b.PressureThreshold,
        b.IsActive, b.CreatedAt, b.UpdatedAt
    );
}
