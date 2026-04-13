using BoilerTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoilerTelemetry.Infrastructure.Persistence.Configurations;

public class BoilerConfiguration : IEntityTypeConfiguration<Boiler>
{
    public void Configure(EntityTypeBuilder<Boiler> builder)
    {
        builder.ToTable("boilers");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.HasIndex(b => b.Name).IsUnique();

        builder.Property(b => b.Location).HasColumnName("location").HasMaxLength(500).IsRequired();
        builder.Property(b => b.TemperatureThreshold).HasColumnName("temperature_threshold");
        builder.Property(b => b.PressureThreshold).HasColumnName("pressure_threshold");
        builder.Property(b => b.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");
    }
}
