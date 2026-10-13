namespace MathFirst.Application.Gameplay;

using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int MaxQuestionDamageHistory = 16;
    private readonly IGameplayStore? _gameplayStore;
    private readonly ICyberDefenseSubmissionConsumer? _consumer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<long, int> _boundQuestionDamage = new();
    private readonly Dictionary<long, Task<int>> _inFlightQuestionBindings = new();
    private long _activeGeneration = 1;
    private long _activeResetEpoch = -1;
    private CyberDefenseAttackUpgradeSnapshot? _authoritativeSnapshot;
    private long _activeQuestionKey = -1;
    private long _lowestRetainedQuestionKey = -1;
    private CyberDefenseHudViewModel _currentViewModel = CyberDefenseHudViewModel.Initial();
    private long _feedbackRevision;

    public CyberDefenseCombatCoordinator(
        IGameplayStore? gameplayStore = null,
        ICyberDefenseSubmissionConsumer? consumer = null)
    {
        _gameplayStore = gameplayStore;
        _consumer = consumer;
        if (_gameplayStore != null)
        {
            _gameplayStore.AttackUpgradeSnapshotChanged += OnAttackUpgradeSnapshotChanged;
        }
    }

    private bool TryApplyAuthoritativeSnapshot(CyberDefenseAttackUpgradeSnapshot snapshot)
    {
        // 1. Monotonic reset epoch fence: Discard stale snapshot from older epoch
        if (_activeResetEpoch >= 0 && snapshot.ResetEpoch < _activeResetEpoch)
        {
            return false;
        }

        // 2. Epoch transition: Clear old bindings when entering a newer epoch
        if (_activeResetEpoch >= 0 && snapshot.ResetEpoch > _activeResetEpoch)
        {
            _activeResetEpoch = snapshot.ResetEpoch;
            _boundQuestionDamage.Clear();
            _inFlightQuestionBindings.Clear();
            _lowestRetainedQuestionKey = -1;
            _authoritativeSnapshot = snapshot;
            return true;
        }

        if (_activeResetEpoch < 0)
        {
            _activeResetEpoch = snapshot.ResetEpoch;
        }

        // 3. Monotonic revision fence within the same epoch
        if (_authoritativeSnapshot != null && snapshot.ResetEpoch == _authoritativeSnapshot.ResetEpoch)
        {
            if (snapshot.StoreRevision < _authoritativeSnapshot.StoreRevision)
            {
                // Discard stale store revision: a delayed read or callback must never regress cache
                return false;
            }

            if (snapshot.StoreRevision == _authoritativeSnapshot.StoreRevision)
            {
                // Equal revision is deterministic; cache is already at this revision
                return true;
            }
        }

        _authoritativeSnapshot = snapshot;
        return true;
    }

    private void OnAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot snapshot)
    {
        lock (_stateLock)
        {
            // 1. Monotonic reset epoch and revision fence
            if (!TryApplyAuthoritativeSnapshot(snapshot))
            {
                return;
            }

            // 2. For any question currently in flight (dispatched before this purchase committed),
            // bind immediately to the conservative baseline entitlement (damage 1)
            // to prevent retroactive damage upgrade across multiple purchases.
            // (Replaces the unreliable CurrentAttackRank - 1 heuristic).
            if (_inFlightQuestionBindings.Count > 0)
            {
                const int baselineDamage = 1;
                foreach (var inFlightKey in _inFlightQuestionBindings.Keys)
                {
                    if (!_boundQuestionDamage.ContainsKey(inFlightKey))
                    {
                        _boundQuestionDamage[inFlightKey] = baselineDamage;
                    }
                }
            }
        }
    }

    public CyberDefenseHudViewModel CurrentViewModel
    {
        get
        {
            lock (_stateLock)
            {
                return _currentViewModel;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || _consumer == null)
        {
            lock (_stateLock)
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
            var snapshot = await _gameplayStore.GetAttackUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);

            CyberDefenseReceiptRecord? latestReceipt = null;
            if (recoveryResult.Receipts.Count > 0)
            {
                latestReceipt = recoveryResult.Receipts[^1];
            }

            lock (_stateLock)
            {
                if (latestReceipt != null)
                {
                    _feedbackRevision++;
                }

                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, latestReceipt, _feedbackRevision);
                TryApplyAuthoritativeSnapshot(snapshot);
            }
        }
        catch
        {
            // Learning-first failure isolation: If gameplay store is unavailable or reset is pending,
            // fall back to default run view without blocking application initialization.
            lock (_stateLock)
            {
                _currentViewModel = CyberDefenseHudViewModel.Initial();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        CancellationToken cancellationToken = default)
    {
        return StageIntentAsync(
            submissionId,
            factId,
            isCorrect,
            latencyMs,
            isEligibleAtSubmission,
            effectiveAttackDamage: 1,
            cancellationToken);
    }

    public async Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
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
                createdAt: DateTimeOffset.UtcNow,
                effectiveAttackDamage: Math.Max(1, effectiveAttackDamage));

            await _consumer.StageIntentAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Learning-first failure isolation: Staging failure must not prevent learner progress commit.
        }
    }

    public Task<int> GetOrBindEffectiveAttackDamageAsync(
        long questionKey,
        CancellationToken cancellationToken = default)
    {
        if (questionKey < 0)
        {
            return Task.FromResult(1);
        }

        Task<int>? inFlightTask;
        lock (_stateLock)
        {
            _activeQuestionKey = questionKey;

            // 1. If already bound, return existing binding immediately
            if (_boundQuestionDamage.TryGetValue(questionKey, out var existingDamage))
            {
                return Task.FromResult(existingDamage);
            }

            // 2. If this key is an evicted historical question, never rebind to a higher damage
            if (_lowestRetainedQuestionKey >= 0 && questionKey < _lowestRetainedQuestionKey)
            {
                return Task.FromResult(1);
            }

            // 3. If coherent authoritative snapshot is cached, bind synchronously at presentation boundary
            if (_authoritativeSnapshot != null)
            {
                int boundDamage = Math.Max(1, _authoritativeSnapshot.CurrentBaseDamage);
                _boundQuestionDamage[questionKey] = boundDamage;
                TrimBoundQuestionDamageHistory();
                return Task.FromResult(boundDamage);
            }

            // 4. Asynchronous capture when uninitialized
            if (!_inFlightQuestionBindings.TryGetValue(questionKey, out inFlightTask))
            {
                inFlightTask = CaptureAndBindDamageAsync(questionKey, _activeGeneration);
                _inFlightQuestionBindings[questionKey] = inFlightTask;
            }
        }

        return AwaitBindingAsync(inFlightTask, cancellationToken);
    }

    public int GetBoundEffectiveAttackDamageOrDefault(
        long questionKey,
        int fallback = 1)
    {
        if (questionKey < 0)
        {
            return fallback;
        }

        lock (_stateLock)
        {
            if (_boundQuestionDamage.TryGetValue(questionKey, out var damage))
            {
                return damage;
            }

            return fallback;
        }
    }

    private async Task<int> CaptureAndBindDamageAsync(long questionKey, long taskGeneration)
    {
        int boundDamage = 1;
        CyberDefenseAttackUpgradeSnapshot? snapshot = null;
        try
        {
            if (_gameplayStore != null)
            {
                snapshot = await _gameplayStore.GetAttackUpgradeSnapshotAsync().ConfigureAwait(false);
                boundDamage = Math.Max(1, snapshot.CurrentBaseDamage);
            }
        }
        catch
        {
            // Learning-first failure isolation: Fall back deterministically to damage 1.
            boundDamage = 1;
        }

        lock (_stateLock)
        {
            // 1. Generation fence: Stale callback from before InvalidateState is dropped completely
            if (taskGeneration != _activeGeneration)
            {
                return 1;
            }

            // 2. Monotonic reset epoch and revision fence
            if (snapshot != null)
            {
                if (!TryApplyAuthoritativeSnapshot(snapshot))
                {
                    // Snapshot was stale (older epoch or older revision).
                    // Do not use stale snapshot to upgrade question damage.
                    _inFlightQuestionBindings.Remove(questionKey);
                    if (_boundQuestionDamage.TryGetValue(questionKey, out var existingDmg))
                    {
                        return existingDmg;
                    }
                    return 1;
                }
            }

            // 3. Question binding: preserve pre-existing binding (e.g. from OnAttackUpgradeSnapshotChanged)
            if (!_boundQuestionDamage.TryGetValue(questionKey, out var existingDamage))
            {
                _boundQuestionDamage[questionKey] = boundDamage;
                TrimBoundQuestionDamageHistory();
            }
            else
            {
                boundDamage = existingDamage;
            }

            _inFlightQuestionBindings.Remove(questionKey);
            return boundDamage;
        }
    }

    private static async Task<int> AwaitBindingAsync(Task<int> bindingTask, CancellationToken cancellationToken)
    {
        try
        {
            return await bindingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Failure isolation
            return 1;
        }
    }

    private void TrimBoundQuestionDamageHistory()
    {
        if (_boundQuestionDamage.Count > MaxQuestionDamageHistory)
        {
            var keysToRemove = _boundQuestionDamage.Keys
                .Where(k => k != _activeQuestionKey)
                .OrderBy(k => k)
                .Take(_boundQuestionDamage.Count - MaxQuestionDamageHistory)
                .ToList();

            foreach (var k in keysToRemove)
            {
                _boundQuestionDamage.Remove(k);
                if (_lowestRetainedQuestionKey < 0 || k >= _lowestRetainedQuestionKey)
                {
                    _lowestRetainedQuestionKey = k + 1;
                }
            }
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
                lock (_stateLock)
                {
                    _feedbackRevision++;
                    _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, result.Receipt, _feedbackRevision);
                }
            }
            else if (result.Status == CyberDefenseConsumptionStatus.AlreadyConsumed && result.Receipt != null)
            {
                var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
                }
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
            lock (_stateLock)
            {
                _currentViewModel = CyberDefenseHudViewModel.Initial();
            }
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await _gameplayStore.GetAttackUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
                TryApplyAuthoritativeSnapshot(snapshot);
            }
        }
        catch
        {
            lock (_stateLock)
            {
                _currentViewModel = CyberDefenseHudViewModel.Initial();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InvalidateState()
    {
        lock (_stateLock)
        {
            _activeGeneration++;
            _feedbackRevision = 0;
            _currentViewModel = CyberDefenseHudViewModel.Initial();
            _boundQuestionDamage.Clear();
            _inFlightQuestionBindings.Clear();
            _activeQuestionKey = -1;
            _lowestRetainedQuestionKey = -1;
            _authoritativeSnapshot = null;
        }
    }

    public void Dispose()
    {
        if (_gameplayStore != null)
        {
            _gameplayStore.AttackUpgradeSnapshotChanged -= OnAttackUpgradeSnapshotChanged;
        }
    }
}
