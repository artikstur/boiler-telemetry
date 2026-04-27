using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.NotificationWorker.Services;

public interface INotificationSender
{
    Task SendAsync(AnomalyEvent anomaly, CancellationToken ct = default);
}
