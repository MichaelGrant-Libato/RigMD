using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Persistence;

public interface IPerformanceRecordRepository
{
    Task AddAsync(PerformanceRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PerformanceRecord>> GetAsync(string clientId, CancellationToken cancellationToken = default);
}
