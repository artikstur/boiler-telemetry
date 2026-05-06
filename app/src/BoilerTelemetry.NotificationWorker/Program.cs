using BoilerTelemetry.NotificationWorker;
using BoilerTelemetry.NotificationWorker.Persistence;
using BoilerTelemetry.NotificationWorker.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Elasticsearch;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "notification-worker")
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

builder.Services.Configure<NotificationWorkerSettings>(
    builder.Configuration.GetSection("NotificationWorker"));

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgreSQL")));

builder.Services.AddScoped<INotificationSender, LogNotificationSender>();
builder.Services.AddHostedService<NotificationProcessingWorker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Ensure database schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BoilerTelemetry.NotificationWorker.Persistence.NotificationDbContext>();
    db.Database.EnsureCreated();
}

app.MapHealthChecks("/health");
app.Run();
