using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.Domain.Interfaces;

public interface IBoilerRepository
{
    Task<Boiler?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Boiler>> GetAllAsync(CancellationToken ct = default);
    Task<Boiler> CreateAsync(Boiler boiler, CancellationToken ct = default);
    Task<Boiler> UpdateAsync(Boiler boiler, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}
