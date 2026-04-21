using BoilerTelemetry.Api.Middleware;
using BoilerTelemetry.Application.Services;
using BoilerTelemetry.Application.Validators;
using BoilerTelemetry.Infrastructure;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure (PostgreSQL, InfluxDB, Kafka)
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

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
