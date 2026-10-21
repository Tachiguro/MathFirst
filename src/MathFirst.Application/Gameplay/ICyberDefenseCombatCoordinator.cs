namespace MathFirst.Application.Gameplay;

using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;

/// <summary>
/// Application boundary coordinating durable combat intent staging, post-learner-commit
/// combat consumption, startup recovery, and authoritative HUD ViewModel projection.
/// Guarantees that gameplay failures never compromise or block mathematical learning.
/// </summary>
public interface ICyberDefenseCombatCoordinator
{
    CyberDefenseHudViewModel CurrentViewModel { get; }
    CyberDefenseReceiptRecord? RecoveredPresentationReceipt => null;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        CancellationToken cancellationToken = default);
    Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        CancellationToken cancellationToken = default);
    Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank,
        CancellationToken cancellationToken = default);
    Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank,
        long? practicePosition,
        CancellationToken cancellationToken = default);
    Task<CyberDefenseQuestionBoundModifierRanks> GetOrBindCombatModifierRanksAsync(
        long questionKey,
        CancellationToken cancellationToken = default);
    CyberDefenseQuestionBoundModifierRanks GetBoundCombatModifierRanksOrDefault(
        long questionKey);
    Task<int> GetOrBindEffectiveAttackDamageAsync(
        long questionKey,
        CancellationToken cancellationToken = default);
    int GetBoundEffectiveAttackDamageOrDefault(
        long questionKey,
        int fallback = 1);
    Task<CyberDefenseFirewallActivationResult?> ActivateQuestionBoundFirewallCapacityAsync(
        long questionKey,
        CancellationToken cancellationToken = default);
    Task AwaitQuestionBoundFirewallActivationAsync(
        long questionKey,
        CancellationToken cancellationToken = default);
    Task<CyberDefenseConsumptionResult?> ConsumeCommittedAttemptAsync(
        string submissionId,
        CancellationToken cancellationToken = default);
    Task RefreshStateAsync(CancellationToken cancellationToken = default);
    void InvalidateState();
}
