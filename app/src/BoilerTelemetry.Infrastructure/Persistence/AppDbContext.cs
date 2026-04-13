using BoilerTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BoilerTelemetry.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<Boiler> Boilers => Set<Boiler>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
