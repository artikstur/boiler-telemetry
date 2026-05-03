using BoilerTelemetry.NotificationWorker;
using BoilerTelemetry.NotificationWorker.Persistence;
using BoilerTelemetry.NotificationWorker.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

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
