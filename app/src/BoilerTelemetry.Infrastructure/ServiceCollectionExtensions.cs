using BoilerTelemetry.Domain.Interfaces;
using BoilerTelemetry.Infrastructure.Messaging;
using BoilerTelemetry.Infrastructure.Persistence;
using BoilerTelemetry.Infrastructure.Persistence.Repositories;
using BoilerTelemetry.Infrastructure.TimeSeries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BoilerTelemetry.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // PostgreSQL
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("PostgreSQL")));

        services.AddScoped<IBoilerRepository, BoilerRepository>();

        // InfluxDB
        services.Configure<InfluxDbSettings>(configuration.GetSection("InfluxDB"));
        services.AddSingleton<ITelemetryRepository, InfluxTelemetryRepository>();

        // Kafka
        services.Configure<KafkaSettings>(configuration.GetSection("Kafka"));
        services.AddSingleton<ITelemetryPublisher, KafkaTelemetryPublisher>();

        return services;
    }
}
