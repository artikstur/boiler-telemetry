using BoilerTelemetry.Api.Middleware;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Application.Validators;
using BoilerTelemetry.Infrastructure;
using FluentValidation;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Elasticsearch;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "api")
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

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
