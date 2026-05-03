using BoilerTelemetry.Api.Middleware;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Application.Validators;
using BoilerTelemetry.Infrastructure;
using FluentValidation;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

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
