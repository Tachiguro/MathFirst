namespace MathFirst.Application.Gameplay;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Application boundary coordinating durable combat intent staging, post-learner-commit
/// combat consumption, startup recovery, and authoritative HUD ViewModel projection.
/// Guarantees that gameplay failures never compromise or block mathematical learning.
/// </summary>
public interface ICyberDefenseCombatCoordinator
{
    CyberDefenseHudViewModel CurrentViewModel { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        CancellationToken cancellationToken = default);
    Task<CyberDefenseConsumptionResult?> ConsumeCommittedAttemptAsync(
        string submissionId,
        CancellationToken cancellationToken = default);
    Task RefreshStateAsync(CancellationToken cancellationToken = default);
    void InvalidateState();
}
