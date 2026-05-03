using BoilerTelemetry.AnomalyService;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

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
