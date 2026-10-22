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
    Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default);
    Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default);
    event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
    Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default);
    Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default);
    event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;
    Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default);
    Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default);
    event Action<CyberDefenseCombatModifierSnapshot>? CombatModifierSnapshotChanged;
    Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default);
    Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default);
    Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default);
    Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default);
    Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default);
    Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default);
    Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default);
    Task ClearPendingIntentAsync(string submissionId, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
        ClearPendingIntentAsync(submissionId, cancellationToken);
    Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default);
    Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default);
    Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default);
    Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default);
    Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
