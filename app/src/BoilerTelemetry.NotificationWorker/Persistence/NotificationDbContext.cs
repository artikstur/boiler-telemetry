using BoilerTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoilerTelemetry.NotificationWorker.Persistence;

public class NotificationDbContext : DbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).HasColumnName("id");
            e.Property(n => n.AnomalyEventId).HasColumnName("anomaly_event_id");
            e.Property(n => n.BoilerId).HasColumnName("boiler_id");
            e.Property(n => n.Channel).HasColumnName("channel").HasMaxLength(50);
            e.Property(n => n.Message).HasColumnName("message");
            e.Property(n => n.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(n => n.CreatedAt).HasColumnName("created_at");
        });
    }
}
