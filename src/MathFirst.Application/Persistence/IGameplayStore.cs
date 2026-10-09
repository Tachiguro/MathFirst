namespace MathFirst.Application.Persistence;

using System.Collections.Generic;
using MathFirst.Domain.CyberDefense;

public interface IGameplayStore : IDisposable
{
    string StoragePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default);
    Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default);
    Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default);
    Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default);
    Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default);
    Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default);
    Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default);
    Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default);
    Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default);
    Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default);
    Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default);
    Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
