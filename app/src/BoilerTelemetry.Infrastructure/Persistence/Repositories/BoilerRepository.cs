using BoilerTelemetry.Domain.Entities;
using BoilerTelemetry.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BoilerTelemetry.Infrastructure.Persistence.Repositories;

public class BoilerRepository : IBoilerRepository
{
    private readonly AppDbContext _db;

    public BoilerRepository(AppDbContext db) => _db = db;

    public async Task<Boiler?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Boilers.FindAsync([id], ct);

    public async Task<IReadOnlyList<Boiler>> GetAllAsync(CancellationToken ct)
        => await _db.Boilers.AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct);

    public async Task<Boiler> CreateAsync(Boiler boiler, CancellationToken ct)
    {
        _db.Boilers.Add(boiler);
        await _db.SaveChangesAsync(ct);
        return boiler;
    }

    public async Task<Boiler> UpdateAsync(Boiler boiler, CancellationToken ct)
    {
        _db.Boilers.Update(boiler);
        await _db.SaveChangesAsync(ct);
        return boiler;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var boiler = await _db.Boilers.FindAsync([id], ct);
        if (boiler is not null)
        {
            _db.Boilers.Remove(boiler);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct)
        => await _db.Boilers.AnyAsync(b => b.Name == name, ct);
}
