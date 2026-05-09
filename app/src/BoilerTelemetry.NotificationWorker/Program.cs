using BoilerTelemetry.NotificationWorker;
using BoilerTelemetry.NotificationWorker.Persistence;
using BoilerTelemetry.NotificationWorker.Services;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenSearch;

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["Otel:ServiceName"] ?? "boiler-telemetry-notification";

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
            .AddSource("BoilerTelemetry.NotificationWorker")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

builder.Services.Configure<NotificationWorkerSettings>(
    builder.Configuration.GetSection("NotificationWorker"));

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

builder.Services.AddScoped<INotificationSender, LogNotificationSender>();
builder.Services.AddHostedService<NotificationProcessingWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Создаём таблицу notifications. EnsureCreated() пропускает создание,
// если в БД уже есть _какие-то_ таблицы (от другого DbContext, например API),
// поэтому используем явный idempotent SQL.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoilerTelemetry.NotificationWorker.Persistence.NotificationDbContext>();
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS notifications (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            anomaly_event_id UUID NOT NULL,
            boiler_id UUID NOT NULL,
            channel VARCHAR(50) NOT NULL,
            message TEXT NOT NULL,
            status VARCHAR(20) NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );");
}

app.MapHealthChecks("/health");
app.Run();
