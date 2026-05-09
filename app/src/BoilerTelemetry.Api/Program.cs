using BoilerTelemetry.Api.Middleware;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Application.Validators;
using BoilerTelemetry.Infrastructure;
using FluentValidation;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenSearch;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

var serviceName = builder.Configuration["Otel:ServiceName"] ?? "boiler-telemetry-api";

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

// OpenTelemetry tracing → Jaeger via OTLP
var otlpEndpoint = builder.Configuration["Otel:Endpoint"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}

// Infrastructure (PostgreSQL, InfluxDB, Kafka, Redis)
builder.Services.AddInfrastructure(builder.Configuration);

// Application services
builder.Services.AddScoped<BoilerService>();
builder.Services.AddScoped<TelemetryService>();

// Validation
builder.Services.AddValidatorsFromAssemblyContaining<TelemetryRequestValidator>();

// API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Ensure database schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoilerTelemetry.Infrastructure.Persistence.AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
    {
        diagCtx.Set("RequestHost", httpCtx.Request.Host.Value ?? "");
        diagCtx.Set("RequestScheme", httpCtx.Request.Scheme);
        diagCtx.Set("UserAgent", httpCtx.Request.Headers["User-Agent"].ToString());
        diagCtx.Set("RemoteIp", httpCtx.Connection.RemoteIpAddress?.ToString() ?? "");
    };
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseHttpMetrics();   // длительность/количество запросов по маршрутам

app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics("/metrics");

app.Run();
