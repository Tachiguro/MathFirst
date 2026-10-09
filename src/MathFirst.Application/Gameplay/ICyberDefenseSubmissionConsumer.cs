namespace MathFirst.Application.Gameplay;

using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;

public interface ICyberDefenseSubmissionConsumer
{
    Task StageIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default);
    Task<CyberDefenseConsumptionResult> ConsumeAttemptAsync(string submissionId, CancellationToken cancellationToken = default);
    Task<CyberDefenseRecoveryResult> RecoverPendingIntentsAsync(CancellationToken cancellationToken = default);
}
