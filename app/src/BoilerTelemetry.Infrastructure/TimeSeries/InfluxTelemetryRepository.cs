using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoilerTelemetry.Infrastructure.TimeSeries;

public class InfluxTelemetryRepository : ITelemetryRepository, IDisposable
{
    private readonly InfluxDBClient _client;
    private readonly InfluxDbSettings _settings;
    private readonly ILogger<InfluxTelemetryRepository> _logger;

    public InfluxTelemetryRepository(
        IOptions<InfluxDbSettings> settings,
        ILogger<InfluxTelemetryRepository> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = new InfluxDBClient(_settings.Url, _settings.Token);
    }

    public async Task WriteAsync(TelemetryReading reading, CancellationToken ct)
    {
        var point = PointData
            .Measurement("telemetry")
            .Tag("boiler_id", reading.BoilerId.ToString())
            .Field("temperature", reading.Temperature)
            .Field("pressure", reading.Pressure)
            .Timestamp(reading.Timestamp, WritePrecision.Ms);

        var writeApi = _client.GetWriteApiAsync();
        await writeApi.WritePointAsync(point, _settings.Bucket, _settings.Org, ct);
    }

    public async Task<IReadOnlyList<TelemetryReading>> QueryAsync(
        Guid boilerId, DateTime from, DateTime to, CancellationToken ct)
    {
        var flux = $"""
            from(bucket: "{_settings.Bucket}")
              |> range(start: {from:yyyy-MM-ddTHH:mm:ssZ}, stop: {to:yyyy-MM-ddTHH:mm:ssZ})
              |> filter(fn: (r) => r._measurement == "telemetry")
              |> filter(fn: (r) => r.boiler_id == "{boilerId}")
              |> pivot(rowKey: ["_time"], columnKey: ["_field"], valueColumn: "_value")
              |> sort(columns: ["_time"])
            """;

        var queryApi = _client.GetQueryApi();
        var tables = await queryApi.QueryAsync(flux, _settings.Org, ct);

        var results = new List<TelemetryReading>();
        foreach (var record in tables.SelectMany(t => t.Records))
        {
            results.Add(new TelemetryReading
            {
                BoilerId = boilerId,
                Temperature = Convert.ToDouble(record.GetValueByKey("temperature")),
                Pressure = Convert.ToDouble(record.GetValueByKey("pressure")),
                Timestamp = record.GetTime()!.Value.ToDateTimeUtc()
            });
        }

        return results;
    }

    public void Dispose() => _client.Dispose();
}
