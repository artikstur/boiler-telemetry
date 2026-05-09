using BoilerTelemetry.AnomalyService;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenSearch;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["Otel:ServiceName"] ?? "boiler-telemetry-anomaly";

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", serviceName)
        .WriteTo.Console(new CompactJsonFormatter());

    var openSearchUrl = ctx.Configuration["OpenSearch:Url"];
    if (!string.IsNullOrEmpty(openSearchUrl))
        cfg.WriteTo.OpenSearch(new OpenSearchSinkOptions(new Uri(openSearchUrl))
        {
            AutoRegisterTemplate = false,
            IndexFormat = "boiler-telemetry-{0:yyyy.MM.dd}",
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog
        });
});

var otlpEndpoint = builder.Configuration["Otel:Endpoint"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(t => t
            .AddSource("BoilerTelemetry.AnomalyService")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

builder.Services.Configure<AnomalyServiceSettings>(
    builder.Configuration.GetSection("AnomalyService"));

builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379");
builder.Services.AddHttpClient("CrudApi", client =>
    client.BaseAddress = new Uri(builder.Configuration["CrudApiBaseUrl"] ?? "http://localhost:8080"));

builder.Services.AddHostedService<AnomalyDetectionWorker>();
builder.Services.AddHealthChecks();

// Отдельный MetricServer на 9090 — слушает /metrics независимо от Kestrel.
// Это важно потому что блокирующий consumer.Consume() мешает Kestrel-у
// надёжно открывать порты.
var metricServer = new KestrelMetricServer(port: 9090);
metricServer.Start();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
