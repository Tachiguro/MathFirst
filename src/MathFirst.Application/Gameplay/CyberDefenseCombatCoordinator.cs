namespace MathFirst.Application.Gameplay;

using System;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Production implementation of ICyberDefenseCombatCoordinator connecting TrainingSession submissions
/// to SqliteGameplayStore and CyberDefenseSubmissionConsumer.
/// Enforces complete mathematical learning isolation: any gameplay persistence or consumption failure
/// is safely handled without disrupting mathematical progress.
/// </summary>
public sealed class CyberDefenseCombatCoordinator : ICyberDefenseCombatCoordinator
{
    private readonly IGameplayStore? _gameplayStore;
    private readonly ICyberDefenseSubmissionConsumer? _consumer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CyberDefenseHudViewModel _currentViewModel = CyberDefenseHudViewModel.Initial();
    private long _feedbackRevision;

    public CyberDefenseCombatCoordinator(
        IGameplayStore? gameplayStore = null,
        ICyberDefenseSubmissionConsumer? consumer = null)
    {
        _gameplayStore = gameplayStore;
        _consumer = consumer;
    }

    public CyberDefenseHudViewModel CurrentViewModel
    {
        get
        {
            lock (_gate)
            {
                return _currentViewModel;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || _consumer == null)
        {
            lock (_gate)
            {
                _currentViewModel = CyberDefenseHudViewModel.Initial();
            }
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _gameplayStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var recoveryResult = await _consumer.RecoverPendingIntentsAsync(cancellationToken).ConfigureAwait(false);
            var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);

            CyberDefenseReceiptRecord? latestReceipt = null;
            if (recoveryResult.Receipts.Count > 0)
            {
                latestReceipt = recoveryResult.Receipts[^1];
                _feedbackRevision++;
            }

            _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, latestReceipt, _feedbackRevision);
        }
        catch
        {
            // Learning-first failure isolation: If gameplay store is unavailable or reset is pending,
            // fall back to default run view without blocking application initialization.
            _currentViewModel = CyberDefenseHudViewModel.Initial();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || _consumer == null || string.IsNullOrWhiteSpace(submissionId))
        {
            return;
        }

        try
        {
            var resetEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
            var intent = new CyberDefensePendingIntentRecord(
                submissionId: submissionId,
                factId: factId,
                isCorrect: isCorrect,
                isEligible: isEligibleAtSubmission,
                responseLatencyMs: latencyMs,
                resetEpoch: resetEpoch,
                createdAt: DateTimeOffset.UtcNow);

            await _consumer.StageIntentAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Learning-first failure isolation: Staging failure must not prevent learner progress commit.
        }
    }

    public async Task<CyberDefenseConsumptionResult?> ConsumeCommittedAttemptAsync(
        string submissionId,
        CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || _consumer == null || string.IsNullOrWhiteSpace(submissionId))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _consumer.ConsumeAttemptAsync(submissionId, cancellationToken).ConfigureAwait(false);
            if (result.Status == CyberDefenseConsumptionStatus.Success && result.Receipt != null)
            {
                var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
                _feedbackRevision++;
                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, result.Receipt, _feedbackRevision);
            }
            else if (result.Status == CyberDefenseConsumptionStatus.AlreadyConsumed && result.Receipt != null)
            {
                var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
            }
            return result;
        }
        catch
        {
            // Learning-first failure isolation: Gameplay consumption failure must not alter mathematical state.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null)
        {
            lock (_gate)
            {
                _currentViewModel = CyberDefenseHudViewModel.Initial();
            }
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
            _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
        }
        catch
        {
            _currentViewModel = CyberDefenseHudViewModel.Initial();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InvalidateState()
    {
        lock (_gate)
        {
            _feedbackRevision = 0;
            _currentViewModel = CyberDefenseHudViewModel.Initial();
        }
    }
}
