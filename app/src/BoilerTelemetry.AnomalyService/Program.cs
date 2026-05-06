using BoilerTelemetry.AnomalyService;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "anomaly-service")
        .WriteTo.Console(new CompactJsonFormatter());

    var esUrl = ctx.Configuration["Elasticsearch:Url"];
    if (!string.IsNullOrEmpty(esUrl))
        cfg.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUrl))
        {
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv8,
            IndexFormat = "boiler-telemetry-{0:yyyy.MM.dd}",
            BatchAction = ElasticOpType.Create,
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog
        });
});

builder.Services.Configure<AnomalyServiceSettings>(
    builder.Configuration.GetSection("AnomalyService"));

builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "redis:6379");
builder.Services.AddHttpClient("CrudApi", client =>
    client.BaseAddress = new Uri(builder.Configuration["CrudApiBaseUrl"] ?? "http://localhost:8080"));

builder.Services.AddHostedService<AnomalyDetectionWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
