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
    private readonly Dictionary<long, int> _boundQuestionFirewallRank = new();
    private readonly Dictionary<long, Task<int>> _inFlightQuestionBindings = new();
    private readonly Dictionary<long, Task<CyberDefenseFirewallActivationResult?>> _inFlightQuestionActivations = new();
    private long _lastActivatedQuestionKey = -1;
    private long _deferredActivationQuestionKey = -1;
    private long _activeGeneration = 1;
    private long _activeResetEpoch = -1;
    private CyberDefenseAttackUpgradeSnapshot? _authoritativeSnapshot;
    private CyberDefenseFirewallUpgradeSnapshot? _authoritativeFirewallSnapshot;
    private readonly Dictionary<long, CyberDefenseQuestionBoundModifierRanks> _boundQuestionModifierRanks = new();
    private CyberDefenseCombatModifierSnapshot? _authoritativeModifierSnapshot;
    private long _lowestRetainedModifierQuestionKey = -1;
    private bool _modifierWarmupInFlight;
    private bool _disposed;
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
            _gameplayStore.FirewallUpgradeSnapshotChanged += OnFirewallUpgradeSnapshotChanged;
            _gameplayStore.CombatModifierSnapshotChanged += OnCombatModifierSnapshotChanged;
        }
    }

    // Caller holds _stateLock. Everything scoped to a reset epoch is dropped together; the modifier
    // cache belongs to the old epoch too and must be re-established by an authoritative snapshot.
    private void ResetEpochScopedState()
    {
        _boundQuestionDamage.Clear();
        _boundQuestionFirewallRank.Clear();
        _boundQuestionModifierRanks.Clear();
        _inFlightQuestionBindings.Clear();
        _inFlightQuestionActivations.Clear();
        _lastActivatedQuestionKey = -1;
        _deferredActivationQuestionKey = -1;
        _lowestRetainedQuestionKey = -1;
        _lowestRetainedModifierQuestionKey = -1;
        _authoritativeModifierSnapshot = null;
    }

    private bool TryApplyAuthoritativeModifierSnapshot(CyberDefenseCombatModifierSnapshot snapshot)
    {
        // 1. Monotonic reset epoch fence: stale epochs never apply.
        if (_activeResetEpoch >= 0 && snapshot.ResetEpoch < _activeResetEpoch)
        {
            return false;
        }

        // 2. Epoch transition: invalidate every old binding; old callbacks can never restore them.
        if (_activeResetEpoch >= 0 && snapshot.ResetEpoch > _activeResetEpoch)
        {
            _activeResetEpoch = snapshot.ResetEpoch;
            ResetEpochScopedState();
            _authoritativeModifierSnapshot = snapshot;
            return true;
        }

        if (_activeResetEpoch < 0)
        {
            _activeResetEpoch = snapshot.ResetEpoch;
        }

        // 3. Monotonic revision fence within the epoch: delayed reads/callbacks never regress the cache.
        if (_authoritativeModifierSnapshot != null && snapshot.ResetEpoch == _authoritativeModifierSnapshot.ResetEpoch)
        {
            if (snapshot.StoreRevision <= _authoritativeModifierSnapshot.StoreRevision)
            {
                return snapshot.StoreRevision == _authoritativeModifierSnapshot.StoreRevision;
            }
        }

        _authoritativeModifierSnapshot = snapshot;
        return true;
    }

    private void OnCombatModifierSnapshotChanged(CyberDefenseCombatModifierSnapshot snapshot)
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            // Only the cache moves. Existing question bindings are never rewritten by an event.
            TryApplyAuthoritativeModifierSnapshot(snapshot);
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
            ResetEpochScopedState();
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

    private bool TryApplyAuthoritativeFirewallSnapshot(CyberDefenseFirewallUpgradeSnapshot snapshot)
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
            ResetEpochScopedState();
            _authoritativeFirewallSnapshot = snapshot;
            return true;
        }

        if (_activeResetEpoch < 0)
        {
            _activeResetEpoch = snapshot.ResetEpoch;
        }

        // 3. Monotonic revision fence within the same epoch
        if (_authoritativeFirewallSnapshot != null && snapshot.ResetEpoch == _authoritativeFirewallSnapshot.ResetEpoch)
        {
            if (snapshot.StoreRevision < _authoritativeFirewallSnapshot.StoreRevision)
            {
                return false;
            }

            if (snapshot.StoreRevision == _authoritativeFirewallSnapshot.StoreRevision)
            {
                return true;
            }
        }

        _authoritativeFirewallSnapshot = snapshot;
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

    private void OnFirewallUpgradeSnapshotChanged(CyberDefenseFirewallUpgradeSnapshot snapshot)
    {
        lock (_stateLock)
        {
            int prePurchaseRank = _authoritativeFirewallSnapshot?.CurrentFirewallRank ?? 0;

            // 1. For any question currently in flight whose activation hasn't bound a target yet,
            // bind immediately to the pre-purchase authoritative rank so mid-flight purchases
            // can never retroactively upgrade this question's entitlement!
            if (_inFlightQuestionActivations.Count > 0)
            {
                foreach (var inFlightKey in _inFlightQuestionActivations.Keys)
                {
                    if (!_boundQuestionFirewallRank.ContainsKey(inFlightKey))
                    {
                        _boundQuestionFirewallRank[inFlightKey] = prePurchaseRank;
                    }
                }
            }

            // 2. Monotonic reset epoch and revision fence
            TryApplyAuthoritativeFirewallSnapshot(snapshot);
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
            await _gameplayStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
            CyberDefenseReceiptRecord? latestReceipt = null;
            if (_consumer != null)
            {
                var recoveryResult = await _consumer.RecoverPendingIntentsAsync(cancellationToken).ConfigureAwait(false);
                if (recoveryResult.Receipts.Count > 0)
                {
                    latestReceipt = recoveryResult.Receipts[^1];
                }
            }

            var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await _gameplayStore.GetAttackUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var firewallSnapshot = await _gameplayStore.GetFirewallUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var modifierSnapshot = await TryReadModifierSnapshotAsync(cancellationToken).ConfigureAwait(false);


            lock (_stateLock)
            {
                if (latestReceipt != null)
                {
                    _feedbackRevision++;
                }

                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, latestReceipt, _feedbackRevision);
                TryApplyAuthoritativeSnapshot(snapshot);
                TryApplyAuthoritativeFirewallSnapshot(firewallSnapshot);
                if (modifierSnapshot != null && !_disposed)
                {
                    TryApplyAuthoritativeModifierSnapshot(modifierSnapshot);
                }
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

    public Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        CancellationToken cancellationToken = default)
    {
        return StageIntentAsync(
            submissionId,
            factId,
            isCorrect,
            latencyMs,
            isEligibleAtSubmission,
            effectiveAttackDamage,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            cancellationToken);
    }

    public Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank,
        CancellationToken cancellationToken = default)
    {
        return StageIntentAsync(
            submissionId,
            factId,
            isCorrect,
            latencyMs,
            isEligibleAtSubmission,
            effectiveAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank,
            practicePosition: null,
            cancellationToken);
    }

    public async Task StageIntentAsync(
        string submissionId,
        string factId,
        bool isCorrect,
        long latencyMs,
        bool isEligibleAtSubmission,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank,
        long? practicePosition,
        CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || _consumer == null || string.IsNullOrWhiteSpace(submissionId) || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var resetEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var intent = new CyberDefensePendingIntentRecord(
                submissionId: submissionId,
                factId: factId,
                isCorrect: isCorrect,
                isEligible: isEligibleAtSubmission,
                responseLatencyMs: latencyMs,
                resetEpoch: resetEpoch,
                createdAt: DateTimeOffset.UtcNow,
                effectiveAttackDamage: Math.Max(1, effectiveAttackDamage),
                boundCriticalStrikeRank: boundCriticalStrikeRank,
                boundOverdriveRank: boundOverdriveRank,
                practicePosition: practicePosition);

            await _consumer.StageIntentAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Learning-first failure isolation: Staging failure must not prevent learner progress commit.
        }
    }

    public Task<CyberDefenseQuestionBoundModifierRanks> GetOrBindCombatModifierRanksAsync(
        long questionKey,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<CyberDefenseQuestionBoundModifierRanks>(cancellationToken);
        }

        if (questionKey < 0)
        {
            return Task.FromResult(CyberDefenseQuestionBoundModifierRanks.Zero);
        }

        long warmupGeneration = -1;
        CyberDefenseQuestionBoundModifierRanks bound;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return Task.FromResult(CyberDefenseQuestionBoundModifierRanks.Zero);
            }

            _activeQuestionKey = questionKey;

            // 1. Immutable once bound.
            if (_boundQuestionModifierRanks.TryGetValue(questionKey, out var existing))
            {
                return Task.FromResult(existing);
            }

            // 2. An evicted historical question must never be rebound at newer purchase ranks.
            if (_lowestRetainedModifierQuestionKey >= 0 && questionKey < _lowestRetainedModifierQuestionKey)
            {
                return Task.FromResult(CyberDefenseQuestionBoundModifierRanks.Zero);
            }

            // 3. Coherent cached snapshot of the active epoch: bind synchronously at the presentation boundary.
            if (_authoritativeModifierSnapshot != null &&
                (_activeResetEpoch < 0 || _authoritativeModifierSnapshot.ResetEpoch == _activeResetEpoch))
            {
                bound = new CyberDefenseQuestionBoundModifierRanks(
                    _authoritativeModifierSnapshot.CriticalStrikeRank,
                    _authoritativeModifierSnapshot.OverdriveRank);
            }
            else
            {
                // 4. Cold/uncertain cache: conservative 0/0 now. A warm-up only benefits FUTURE questions.
                bound = CyberDefenseQuestionBoundModifierRanks.Zero;
                if (_gameplayStore != null && !_modifierWarmupInFlight)
                {
                    _modifierWarmupInFlight = true;
                    warmupGeneration = _activeGeneration;
                }
            }

            _boundQuestionModifierRanks[questionKey] = bound;
            TrimBoundQuestionModifierRanksHistory();
        }

        if (warmupGeneration >= 0)
        {
            _ = WarmModifierSnapshotAsync(warmupGeneration);
        }

        return Task.FromResult(bound);
    }

    public CyberDefenseQuestionBoundModifierRanks GetBoundCombatModifierRanksOrDefault(long questionKey)
    {
        if (questionKey < 0)
        {
            return CyberDefenseQuestionBoundModifierRanks.Zero;
        }

        lock (_stateLock)
        {
            return _boundQuestionModifierRanks.TryGetValue(questionKey, out var ranks)
                ? ranks
                : CyberDefenseQuestionBoundModifierRanks.Zero;
        }
    }

    private async Task WarmModifierSnapshotAsync(long taskGeneration)
    {
        CyberDefenseCombatModifierSnapshot? snapshot = null;
        try
        {
            snapshot = await _gameplayStore!.GetCombatModifierSnapshotAsync().ConfigureAwait(false);
        }
        catch
        {
            // Failure isolation: a failed warm-up leaves the cache cold; questions stay conservative.
        }

        lock (_stateLock)
        {
            if (taskGeneration != _activeGeneration)
            {
                // InvalidateState already cleared the in-flight flag for the newer generation.
                return;
            }

            _modifierWarmupInFlight = false;
            if (snapshot != null && !_disposed)
            {
                // Affects only questions presented after this point; bound questions are never touched.
                TryApplyAuthoritativeModifierSnapshot(snapshot);
            }
        }
    }

    private async Task<CyberDefenseCombatModifierSnapshot?> TryReadModifierSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _gameplayStore!.GetCombatModifierSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A pending reset or unreadable modifier state keeps the cache cold; Attack/Firewall paths are unaffected.
            return null;
        }
    }

    private void TrimBoundQuestionModifierRanksHistory()
    {
        if (_boundQuestionModifierRanks.Count > MaxQuestionDamageHistory)
        {
            var keysToRemove = _boundQuestionModifierRanks.Keys
                .Where(k => k != _activeQuestionKey)
                .OrderBy(k => k)
                .Take(_boundQuestionModifierRanks.Count - MaxQuestionDamageHistory)
                .ToList();

            foreach (var k in keysToRemove)
            {
                _boundQuestionModifierRanks.Remove(k);
                if (_lowestRetainedModifierQuestionKey < 0 || k >= _lowestRetainedModifierQuestionKey)
                {
                    _lowestRetainedModifierQuestionKey = k + 1;
                }
            }
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

    public int GetBoundFirewallRankOrDefault(
        long questionKey,
        int fallback = 0)
    {
        if (questionKey < 0)
        {
            return fallback;
        }

        lock (_stateLock)
        {
            if (_boundQuestionFirewallRank.TryGetValue(questionKey, out var rank))
            {
                return rank;
            }

            return fallback;
        }
    }

    public Task<CyberDefenseFirewallActivationResult?> ActivateQuestionBoundFirewallCapacityAsync(
        long questionKey,
        CancellationToken cancellationToken = default)
    {
        if (questionKey < 0 || _gameplayStore == null)
        {
            return Task.FromResult<CyberDefenseFirewallActivationResult?>(null);
        }

        Task<CyberDefenseFirewallActivationResult?>? inFlightTask;
        lock (_stateLock)
        {
            _activeQuestionKey = questionKey;

            // 1. If already activated for this question key or an older question, do not activate again.
            if (_lastActivatedQuestionKey >= 0 && questionKey <= _lastActivatedQuestionKey)
            {
                return Task.FromResult<CyberDefenseFirewallActivationResult?>(null);
            }

            // 2. Synchronous presentation-time binding (BLK-01 / DEC-02):
            // The question's Firewall entitlement MUST be bound synchronously before any async await.
            int boundTarget;
            if (_boundQuestionFirewallRank.TryGetValue(questionKey, out var existingRank))
            {
                boundTarget = existingRank;
            }
            else
            {
                if (_lowestRetainedQuestionKey >= 0 && questionKey < _lowestRetainedQuestionKey)
                {
                    boundTarget = 0;
                }
                else if (_authoritativeFirewallSnapshot != null &&
                         (_activeResetEpoch < 0 || _authoritativeFirewallSnapshot.ResetEpoch == _activeResetEpoch))
                {
                    boundTarget = _authoritativeFirewallSnapshot.CurrentFirewallRank;
                }
                else
                {
                    // Fail closed to safe rank 0 when no trustworthy snapshot is cached at presentation
                    boundTarget = 0;
                }

                _boundQuestionFirewallRank[questionKey] = boundTarget;
                TrimBoundQuestionFirewallRankHistory();
            }

            // 3. If activation for this questionKey is already in flight, return the existing task.
            if (!_inFlightQuestionActivations.TryGetValue(questionKey, out inFlightTask))
            {
                inFlightTask = PerformQuestionActivationAsync(questionKey, boundTarget, _activeGeneration, _activeResetEpoch);
                _inFlightQuestionActivations[questionKey] = inFlightTask;
            }
        }

        return AwaitActivationAsync(inFlightTask, cancellationToken);
    }

    public Task AwaitQuestionBoundFirewallActivationAsync(
        long questionKey,
        CancellationToken cancellationToken = default)
    {
        if (questionKey < 0)
        {
            return Task.CompletedTask;
        }

        Task<CyberDefenseFirewallActivationResult?>? inFlightTask;
        lock (_stateLock)
        {
            _inFlightQuestionActivations.TryGetValue(questionKey, out inFlightTask);
        }

        if (inFlightTask == null)
        {
            return Task.CompletedTask;
        }

        return AwaitActivationAsync(inFlightTask, cancellationToken);
    }

    private async Task<CyberDefenseFirewallActivationResult?> PerformQuestionActivationAsync(
        long questionKey,
        int boundTarget,
        long taskGeneration,
        long taskResetEpoch)
    {
        CyberDefenseFirewallActivationResult? result = null;
        try
        {
            long currentEpoch = taskResetEpoch;
            if (currentEpoch < 0 && _gameplayStore != null)
            {
                currentEpoch = await _gameplayStore.GetResetEpochAsync().ConfigureAwait(false);
            }

            if (_gameplayStore != null && currentEpoch >= 0)
            {
                // Refresh cached authoritative snapshot if missing, to warm cache for subsequent questions (4.G).
                // This MUST NOT increase the already-bound entitlement for questionKey (4.H).
                bool shouldFetchSnapshot = false;
                lock (_stateLock)
                {
                    if (_authoritativeFirewallSnapshot == null)
                    {
                        shouldFetchSnapshot = true;
                    }
                }

                if (shouldFetchSnapshot)
                {
                    try
                    {
                        var snapshot = await _gameplayStore.GetFirewallUpgradeSnapshotAsync().ConfigureAwait(false);
                        lock (_stateLock)
                        {
                            TryApplyAuthoritativeFirewallSnapshot(snapshot);
                        }
                    }
                    catch
                    {
                        // Failure isolation: Snapshot warming failure must not block activation
                    }
                }

                result = await _gameplayStore.ActivatePendingFirewallCapacityAsync(
                    boundTarget,
                    currentEpoch).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Learning-first failure isolation: Activation failure must never crash or block learning.
            result = CyberDefenseFirewallActivationResult.Failed(ex.Message);
        }

        lock (_stateLock)
        {
            _inFlightQuestionActivations.Remove(questionKey);

            // Generation fence: stale generation callback dropped
            if (taskGeneration != _activeGeneration)
            {
                return result;
            }

            // Epoch fence
            if (_activeResetEpoch >= 0 && taskResetEpoch >= 0 && taskResetEpoch != _activeResetEpoch)
            {
                return result;
            }

            if (result != null)
            {
                switch (result.Status)
                {
                    case CyberDefenseFirewallActivationStatus.Applied:
                    case CyberDefenseFirewallActivationStatus.AlreadyCurrent:
                        if (_deferredActivationQuestionKey == questionKey)
                        {
                            _deferredActivationQuestionKey = -1;
                        }

                        if (questionKey > _lastActivatedQuestionKey)
                        {
                            _lastActivatedQuestionKey = questionKey;
                        }

                        if (result.RunState != null)
                        {
                            _currentViewModel = CyberDefenseHudViewModel.FromRunState(result.RunState, null, _feedbackRevision);
                        }
                        break;

                    case CyberDefenseFirewallActivationStatus.DeferredPendingIntent:
                        _deferredActivationQuestionKey = questionKey;
                        break;

                    case CyberDefenseFirewallActivationStatus.RejectedResetPending:
                    case CyberDefenseFirewallActivationStatus.RejectedEpochMismatch:
                    case CyberDefenseFirewallActivationStatus.Failed:
                        if (_deferredActivationQuestionKey == questionKey)
                        {
                            _deferredActivationQuestionKey = -1;
                        }
                        break;
                }
            }

            return result;
        }
    }

    private async Task TryRetryDeferredActivationAsync(CancellationToken cancellationToken)
    {
        if (_gameplayStore == null)
        {
            return;
        }

        long retryKey;
        int boundTarget;
        long currentEpoch;
        lock (_stateLock)
        {
            retryKey = _deferredActivationQuestionKey;
            if (retryKey < 0)
            {
                return;
            }

            if (_lastActivatedQuestionKey >= retryKey)
            {
                _deferredActivationQuestionKey = -1;
                return;
            }

            if (!_boundQuestionFirewallRank.TryGetValue(retryKey, out boundTarget))
            {
                _deferredActivationQuestionKey = -1;
                return;
            }

            currentEpoch = _activeResetEpoch;
        }

        try
        {
            if (currentEpoch < 0)
            {
                currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
            }

            var retryResult = await _gameplayStore.ActivatePendingFirewallCapacityAsync(
                boundTarget,
                currentEpoch,
                cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                switch (retryResult.Status)
                {
                    case CyberDefenseFirewallActivationStatus.Applied:
                    case CyberDefenseFirewallActivationStatus.AlreadyCurrent:
                        _deferredActivationQuestionKey = -1;
                        if (retryKey > _lastActivatedQuestionKey)
                        {
                            _lastActivatedQuestionKey = retryKey;
                        }

                        if (retryResult.RunState != null)
                        {
                            _currentViewModel = CyberDefenseHudViewModel.FromRunState(
                                retryResult.RunState,
                                null,
                                _feedbackRevision);
                        }
                        break;

                    case CyberDefenseFirewallActivationStatus.DeferredPendingIntent:
                        // Preserve original bound entitlement and retry eligibility
                        break;

                    case CyberDefenseFirewallActivationStatus.RejectedResetPending:
                    case CyberDefenseFirewallActivationStatus.RejectedEpochMismatch:
                    case CyberDefenseFirewallActivationStatus.Failed:
                    default:
                        _deferredActivationQuestionKey = -1;
                        break;
                }
            }
        }
        catch
        {
            // Learning-first failure isolation: Retry failure must not crash or bubble up,
            // and must not leave retry state permanently stuck.
            lock (_stateLock)
            {
                _deferredActivationQuestionKey = -1;
            }
        }
    }

    private void TrimBoundQuestionFirewallRankHistory()
    {
        if (_boundQuestionFirewallRank.Count > MaxQuestionDamageHistory)
        {
            var keysToRemove = _boundQuestionFirewallRank.Keys
                .Where(k => k != _activeQuestionKey && k != _deferredActivationQuestionKey)
                .OrderBy(k => k)
                .Take(_boundQuestionFirewallRank.Count - MaxQuestionDamageHistory)
                .ToList();

            foreach (var k in keysToRemove)
            {
                _boundQuestionFirewallRank.Remove(k);
            }
        }
    }

    private static async Task<CyberDefenseFirewallActivationResult?> AwaitActivationAsync(Task<CyberDefenseFirewallActivationResult?> activationTask, CancellationToken cancellationToken)
    {
        try
        {
            return await activationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Learning-first failure isolation
            return null;
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

                await TryRetryDeferredActivationAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (result.Status == CyberDefenseConsumptionStatus.AlreadyConsumed && result.Receipt != null)
            {
                var runState = await _gameplayStore.GetRunStateAsync(cancellationToken).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
                }

                await TryRetryDeferredActivationAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (_gameplayStore == null || string.IsNullOrWhiteSpace(submissionId) || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resetIntent = await _gameplayStore.GetResetIntentAsync(cancellationToken).ConfigureAwait(false);
            if (resetIntent.IsPending)
            {
                return;
            }

            var currentEpoch = await _gameplayStore.GetResetEpochAsync(cancellationToken).ConfigureAwait(false);
            if (_consumer != null)
            {
                await _consumer.ClearAbandonedIntentAsync(submissionId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _gameplayStore.ClearPendingIntentAsync(submissionId, currentEpoch, cancellationToken).ConfigureAwait(false);
            }
            await TryRetryDeferredActivationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Learning-first failure isolation.
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ClearAbandonedIntentAsync(string submissionId, CancellationToken cancellationToken = default) =>
        ClearPendingIntentAsync(submissionId, cancellationToken);

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
            var firewallSnapshot = await _gameplayStore.GetFirewallUpgradeSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var modifierSnapshot = await TryReadModifierSnapshotAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                _currentViewModel = CyberDefenseHudViewModel.FromRunState(runState, null, _feedbackRevision);
                TryApplyAuthoritativeSnapshot(snapshot);
                TryApplyAuthoritativeFirewallSnapshot(firewallSnapshot);
                if (modifierSnapshot != null && !_disposed)
                {
                    TryApplyAuthoritativeModifierSnapshot(modifierSnapshot);
                }
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
            _boundQuestionFirewallRank.Clear();
            _boundQuestionModifierRanks.Clear();
            _inFlightQuestionBindings.Clear();
            _inFlightQuestionActivations.Clear();
            _lastActivatedQuestionKey = -1;
            _deferredActivationQuestionKey = -1;
            _activeQuestionKey = -1;
            _lowestRetainedQuestionKey = -1;
            _lowestRetainedModifierQuestionKey = -1;
            _modifierWarmupInFlight = false;
            _authoritativeSnapshot = null;
            _authoritativeFirewallSnapshot = null;
            _authoritativeModifierSnapshot = null;
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            _disposed = true;
            _activeGeneration++;
            _boundQuestionModifierRanks.Clear();
            _authoritativeModifierSnapshot = null;
            _modifierWarmupInFlight = false;
        }

        if (_gameplayStore != null)
        {
            _gameplayStore.AttackUpgradeSnapshotChanged -= OnAttackUpgradeSnapshotChanged;
            _gameplayStore.FirewallUpgradeSnapshotChanged -= OnFirewallUpgradeSnapshotChanged;
            _gameplayStore.CombatModifierSnapshotChanged -= OnCombatModifierSnapshotChanged;
        }
    }
}
