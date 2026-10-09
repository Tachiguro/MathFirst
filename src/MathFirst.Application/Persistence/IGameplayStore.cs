namespace MathFirst.Application.Persistence;

using MathFirst.Domain.CyberDefense;

public interface IGameplayStore : IDisposable
{
    string StoragePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default);
    Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default);
    Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
