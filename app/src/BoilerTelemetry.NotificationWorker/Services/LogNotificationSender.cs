using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.NotificationWorker.Services;

public class LogNotificationSender : INotificationSender
{
    private readonly ILogger<LogNotificationSender> _logger;

    public LogNotificationSender(ILogger<LogNotificationSender> logger) => _logger = logger;

    public Task SendAsync(AnomalyEvent anomaly, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[NOTIFICATION] Boiler {BoilerId}: {Type}, value={Value}, threshold={Threshold}",
            anomaly.BoilerId, anomaly.AnomalyType, anomaly.ActualValue, anomaly.Threshold);

        return Task.CompletedTask;
    }
}
