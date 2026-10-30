namespace MathFirst.Application.Gameplay;

using System;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Authoritative application-level ViewModel projecting Cyber Defense player progression,
/// skill points, and all four upgrade families to the Upgrade Terminal presentation layer.
/// Enforces safe single-writer purchase execution with double-click rejection, optimistic
/// rank and reset-epoch fencing, and automatic state refresh.
/// </summary>
public sealed class UpgradeTerminalViewModel : IDisposable
{
    private readonly IGameplayStore _gameplayStore;
    private readonly object _stateLock = new();
    private int _isPurchasingFlag;
    private bool _isDisposed;
    private bool _isSynchronized;
    private bool _hasAuthoritativeStatus;
    private long _minimumEpoch;
    private long _minimumRevision = 1;
    private long _refreshRequest;
    private long _lastCompletedRefresh;
    private UpgradeTerminalStatus _status = UpgradeTerminalStatus.Initial();

    public UpgradeTerminalViewModel(IGameplayStore gameplayStore)
    {
        ArgumentNullException.ThrowIfNull(gameplayStore);
        _gameplayStore = gameplayStore;
        _gameplayStore.AttackUpgradeSnapshotChanged += OnAttackUpgradeSnapshotChanged;
        _gameplayStore.FirewallUpgradeSnapshotChanged += OnFirewallUpgradeSnapshotChanged;
    }

    public bool IsDisposed
    {
        get { lock (_stateLock) return _isDisposed; }
    }

    public bool IsBusy => Volatile.Read(ref _isPurchasingFlag) != 0;

    /// <summary>
    /// True once a consistent committed progression snapshot was accepted. Before that, <see cref="Status"/> holds
    /// placeholder values that presentation must not show as the player's level or XP. It stays true afterwards: a
    /// later failed read keeps the last coherent status instead of inventing a different one.
    /// </summary>
    public bool HasAuthoritativeStatus
    {
        get { lock (_stateLock) return _hasAuthoritativeStatus; }
    }

    public UpgradeTerminalStatus Status
    {
        get { lock (_stateLock) return _status; }
    }

    public int PlayerLevel => Status.PlayerLevel;
    public long CumulativeXp => Status.CumulativeXp;
    public long XpIntoCurrentLevel => Status.XpIntoCurrentLevel;
    public int XpRequiredForNextLevel => Status.XpRequiredForNextLevel;
    public int RemainingXpToNextLevel => Status.RemainingXpToNextLevel;
    public double ProgressRatio => Status.ProgressRatio;
    public PlayerLevelProgress LevelProgress => Status.LevelProgress;
    public int AvailableSkillPoints => Status.AvailableSkillPoints;
    public int CurrentAttackRank => Status.CurrentAttackRank;
    public int? NextAttackRank => Status.NextAttackRank;
    public int CurrentBaseDamage => Status.CurrentBaseDamage;
    public int? NextBaseDamage => Status.NextBaseDamage;
    public int? NextRankCost => Status.NextRankCost;
    public bool HasSufficientSkillPoints => Status.HasSufficientSkillPoints;
    public bool CanAfford => Status.CanAfford;
    public bool IsMaxRank => Status.IsMaxRank;
    public bool CanPurchase => Status.CanPurchase && IsSynchronized && !IsBusy && !IsDisposed;
    public int CurrentFirewallRank => Status.CurrentFirewallRank;
    public int ActivatedFirewallRank => Status.ActivatedFirewallRank;
    public int? NextFirewallRank => Status.NextFirewallRank;
    public int? NextRequiredLevel => Status.NextRequiredLevel;
    public int? NextFirewallRequiredLevel => Status.NextRequiredLevel;
    public int? NextFirewallCost => Status.NextFirewallCost;
    public int CurrentPermanentCapacity => Status.CurrentPermanentCapacity;
    public int CurrentShieldCapacity => Status.CurrentPermanentCapacity;
    public int? NextPermanentCapacity => Status.NextPermanentCapacity;
    public int? NextShieldCapacity => Status.NextPermanentCapacity;
    public bool IsFirewallLevelUnlocked => Status.IsFirewallLevelUnlocked;
    public bool HasSufficientFirewallSkillPoints => Status.HasSufficientFirewallSkillPoints;
    public bool HasSufficientFirewallSp => Status.HasSufficientFirewallSkillPoints;
    public bool IsFirewallMaxRank => Status.IsFirewallMaxRank;
    public bool CanPurchaseFirewall => Status.CanPurchaseFirewall && IsSynchronized && !IsBusy && !IsDisposed;
    public bool IsFirewallShieldLocked => Status.IsFirewallShieldLocked;
    public bool IsSynchronized { get { lock (_stateLock) return _isSynchronized; } }
    public int? CurrentCriticalStrikeRank => Status.CurrentCriticalStrikeRank;
    public int? NextCriticalStrikeRank => Status.NextCriticalStrikeRank;
    public int? NextCriticalStrikeRequiredLevel => Status.NextCriticalStrikeRequiredLevel;
    public int? NextCriticalStrikeCost => Status.NextCriticalStrikeCost;
    public bool IsCriticalStrikeLevelUnlocked => Status.IsCriticalStrikeLevelUnlocked;
    public bool HasSufficientCriticalStrikeSkillPoints => Status.HasSufficientCriticalStrikeSkillPoints;
    public bool IsCriticalStrikeMaxRank => Status.IsCriticalStrikeMaxRank;
    public bool CanPurchaseCriticalStrike => Status.CanPurchaseCriticalStrike && IsSynchronized && !IsBusy && !IsDisposed;
    public int? CurrentOverdriveRank => Status.CurrentOverdriveRank;
    public int? NextOverdriveRank => Status.NextOverdriveRank;
    public int? NextOverdriveRequiredLevel => Status.NextOverdriveRequiredLevel;
    public int? NextOverdriveCost => Status.NextOverdriveCost;
    public bool IsOverdriveLevelUnlocked => Status.IsOverdriveLevelUnlocked;
    public bool HasSufficientOverdriveSkillPoints => Status.HasSufficientOverdriveSkillPoints;
    public bool IsOverdriveMaxRank => Status.IsOverdriveMaxRank;
    public bool CanPurchaseOverdrive => Status.CanPurchaseOverdrive && IsSynchronized && !IsBusy && !IsDisposed;
    public long ResetEpoch => Status.ResetEpoch;
    public long StoreRevision => Status.StoreRevision;

    public event Action<UpgradeTerminalStatus>? StatusChanged;
    public event Action? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpgradeTerminalStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        long request;
        lock (_stateLock)
        {
            if (_isDisposed) return _status;
            request = ++_refreshRequest;
        }
        try
        {
            // Each snapshot validates progression/run/reset in one store transaction. Pair it
            // only with the identical progression revision; retries are deliberately bounded.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var progression = await _gameplayStore.GetProgressionAsync(cancellationToken).ConfigureAwait(false);
                var modifiers = await _gameplayStore.GetCombatModifierSnapshotAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (progression.ResetEpoch != modifiers.ResetEpoch || progression.StoreRevision != modifiers.StoreRevision)
                    continue;
                var candidate = UpgradeTerminalStatus.FromProgression(progression, modifiers);
                lock (_stateLock)
                {
                    if (_isDisposed) return _status;
                    if (request < _lastCompletedRefresh) return _status;
                    if (IsOlder(candidate.ResetEpoch, candidate.StoreRevision, _status.ResetEpoch, _status.StoreRevision))
                        return _status;
                    if (IsOlder(candidate.ResetEpoch, candidate.StoreRevision, _minimumEpoch, _minimumRevision))
                        continue;
                    bool synchronizationChanged = !_isSynchronized;
                    _isSynchronized = true;
                    _hasAuthoritativeStatus = true;
                    _lastCompletedRefresh = request;
                    if (candidate != _status)
                    {
                        _status = candidate;
                        NotifyStatusChanged(candidate);
                        NotifyStateChanged();
                    }
                    else if (synchronizationChanged) NotifyStateChanged();
                    return _status;
                }
            }
            throw new InvalidOperationException("Could not reconcile progression and modifier snapshot at a consistent revision and reset epoch.");
        }
        catch
        {
            lock (_stateLock)
            {
                if (request >= _lastCompletedRefresh)
                {
                    _lastCompletedRefresh = request;
                    bool wasSynchronized = _isSynchronized;
                    _isSynchronized = false;
                    if (wasSynchronized) NotifyStateChanged();
                }
            }
            throw;
        }
    }

    public Task<UpgradePurchaseResult> PurchaseUpgradeAsync(CancellationToken cancellationToken = default) =>
        PurchaseAsync(UpgradeKind.Attack, cancellationToken);

    public Task<UpgradePurchaseResult> PurchaseFirewallUpgradeAsync(CancellationToken cancellationToken = default) =>
        PurchaseAsync(UpgradeKind.Firewall, cancellationToken);

    public Task<UpgradePurchaseResult> PurchaseFirewallAsync(CancellationToken cancellationToken = default) =>
        PurchaseFirewallUpgradeAsync(cancellationToken);

    public Task<UpgradePurchaseResult> PurchaseCriticalStrikeUpgradeAsync(CancellationToken cancellationToken = default) =>
        PurchaseAsync(UpgradeKind.CriticalStrike, cancellationToken);

    public Task<UpgradePurchaseResult> PurchaseOverdriveUpgradeAsync(CancellationToken cancellationToken = default) =>
        PurchaseAsync(UpgradeKind.Overdrive, cancellationToken);

    private enum UpgradeKind { Attack, Firewall, CriticalStrike, Overdrive }

    private async Task<UpgradePurchaseResult> PurchaseAsync(UpgradeKind kind, CancellationToken cancellationToken)
    {
        lock (_stateLock) { if (_isDisposed) return UpgradePurchaseResult.Disposed(_status); }
        if (cancellationToken.IsCancellationRequested) return UpgradePurchaseResult.Cancelled(Status);
        if (Interlocked.CompareExchange(ref _isPurchasingFlag, 1, 0) != 0)
            return UpgradePurchaseResult.Busy(Status);

        try
        {
            lock (_stateLock) { if (_isDisposed) return UpgradePurchaseResult.Disposed(_status); }
            if (cancellationToken.IsCancellationRequested) return UpgradePurchaseResult.Cancelled(Status);
            NotifyStateChanged();
            if (!IsSynchronized)
            {
                try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                { return UpgradePurchaseResult.Cancelled(Status); }
                catch (Exception ex) { return UpgradePurchaseResult.Error(ex.Message, ex, Status); }
            }
            lock (_stateLock) { if (_isDisposed) return UpgradePurchaseResult.Disposed(_status); }
            if (cancellationToken.IsCancellationRequested) return UpgradePurchaseResult.Cancelled(Status);
            var current = Status;
            var rejection = CheckEligibility(current, kind);
            if (rejection != null) return rejection;

            CyberDefenseProgressionRecord? committedProgression = null;
            CyberDefenseCombatModifierSnapshot? committedModifiers = null;
            // Only the store call determines whether a purchase committed. A notification or
            // subsequent read failure must never turn a confirmed charge into a failed purchase.
            try
            {
                switch (kind)
                {
                    case UpgradeKind.Attack:
                        committedProgression = await _gameplayStore.PurchaseAttackUpgradeAsync(
                            current.CurrentAttackRank, current.ResetEpoch, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpgradeKind.Firewall:
                        committedProgression = await _gameplayStore.PurchaseFirewallUpgradeAsync(
                            current.CurrentFirewallRank, current.ResetEpoch, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpgradeKind.CriticalStrike:
                        committedModifiers = await _gameplayStore.PurchaseCriticalStrikeUpgradeAsync(
                            current.CurrentCriticalStrikeRank!.Value, current.ResetEpoch, cancellationToken).ConfigureAwait(false);
                        break;
                    case UpgradeKind.Overdrive:
                        committedModifiers = await _gameplayStore.PurchaseOverdriveUpgradeAsync(
                            current.CurrentOverdriveRank!.Value, current.ResetEpoch, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { return UpgradePurchaseResult.Cancelled(Status); }
            catch (ObjectDisposedException) { return UpgradePurchaseResult.Disposed(Status); }
            catch (Exception ex)
            {
                await TryRefreshCoreAsync().ConfigureAwait(false);
                return ClassifyRejection(ex);
            }

            long epoch = committedModifiers?.ResetEpoch ?? committedProgression!.ResetEpoch;
            long revision = committedModifiers?.StoreRevision ?? committedProgression!.StoreRevision;
            ObserveRevision(epoch, revision);
            try
            {
                // Completion of an already committed purchase is independent of caller cancellation.
                var refreshed = await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
                if (!IsDisposed && IsSynchronized) return UpgradePurchaseResult.Success(refreshed);
            }
            catch (Exception ex)
            {
                return new UpgradePurchaseResult(UpgradePurchaseStatus.Success, null,
                    "Purchase committed; terminal refresh is unavailable.", ex);
            }
            // Legacy Attack/Firewall callers can still inspect their committed progression.
            // Unknown modifier ranks remain null and are never published as a complete status.
            return new UpgradePurchaseResult(UpgradePurchaseStatus.Success,
                committedProgression is null ? null : UpgradeTerminalStatus.FromProgression(committedProgression),
                "Purchase committed; terminal status is unavailable.", null);
        }
        finally
        {
            Volatile.Write(ref _isPurchasingFlag, 0);
            lock (_stateLock) { if (!_isDisposed) NotifyStateChanged(); }
        }
    }

    private static UpgradePurchaseResult? CheckEligibility(UpgradeTerminalStatus status, UpgradeKind kind)
    {
        bool max = kind switch
        {
            UpgradeKind.Attack => status.IsMaxRank, UpgradeKind.Firewall => status.IsFirewallMaxRank,
            UpgradeKind.CriticalStrike => status.IsCriticalStrikeMaxRank, _ => status.IsOverdriveMaxRank
        };
        if (max) return UpgradePurchaseResult.MaxRankReached(status);
        bool unlocked = kind switch
        {
            UpgradeKind.Attack => true, UpgradeKind.Firewall => status.IsFirewallLevelUnlocked,
            UpgradeKind.CriticalStrike => status.IsCriticalStrikeLevelUnlocked, _ => status.IsOverdriveLevelUnlocked
        };
        if (!unlocked) return UpgradePurchaseResult.LockedLevel(status);
        bool affordable = kind switch
        {
            UpgradeKind.Attack => status.HasSufficientSkillPoints, UpgradeKind.Firewall => status.HasSufficientFirewallSkillPoints,
            UpgradeKind.CriticalStrike => status.HasSufficientCriticalStrikeSkillPoints, _ => status.HasSufficientOverdriveSkillPoints
        };
        return affordable ? null : UpgradePurchaseResult.InsufficientSkillPoints(status);
    }

    private UpgradePurchaseResult ClassifyRejection(Exception ex)
    {
        var status = Status;
        if (ex is not InvalidOperationException) return UpgradePurchaseResult.Error(ex.Message, ex, status);
        bool Contains(string value) => ex.Message.Contains(value, StringComparison.OrdinalIgnoreCase);
        if (Contains("reset epoch") || Contains("reset") && Contains("pending"))
            return UpgradePurchaseResult.StaleResetEpoch(status, ex.Message);
        if (Contains("Insufficient available skill points")) return UpgradePurchaseResult.InsufficientSkillPoints(status, ex.Message);
        if (Contains("level") && (Contains("insufficient") || Contains("requires")))
            return UpgradePurchaseResult.LockedLevel(status, ex.Message);
        if (Contains("Expected") && Contains("rank") || Contains("Concurrency conflict") || Contains("conflicted"))
            return UpgradePurchaseResult.StaleRank(status, ex.Message);
        if (Contains("maximum") || Contains("overflow")) return UpgradePurchaseResult.MaxRankReached(status, ex.Message);
        return UpgradePurchaseResult.Error(ex.Message, ex, status);
    }

    private async Task TryRefreshCoreAsync()
    {
        try { await RefreshAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* Keep the last coherent status; further purchases require reconciliation. */ }
    }

    private static bool IsOlder(long epoch, long revision, long otherEpoch, long otherRevision) =>
        epoch < otherEpoch || epoch == otherEpoch && revision < otherRevision;

    private void ObserveRevision(long epoch, long revision)
    {
        lock (_stateLock)
        {
            if (IsOlder(epoch, revision, _minimumEpoch, _minimumRevision)) return;
            _minimumEpoch = epoch;
            _minimumRevision = revision;
            if (IsOlder(_status.ResetEpoch, _status.StoreRevision, epoch, revision)) _isSynchronized = false;
        }
    }

    private readonly object _refreshLock = new();
    private bool _reconciliationRunning;
    private bool _reconciliationRequested;

    private void TriggerAuthoritativeReconciliation(long epoch, long revision)
    {
        lock (_stateLock)
        {
            if (_isDisposed || IsOlder(epoch, revision, _status.ResetEpoch, _status.StoreRevision)) return;
            ObserveRevision(epoch, revision);
        }
        lock (_refreshLock)
        {
            if (_reconciliationRunning) { _reconciliationRequested = true; return; }
            _reconciliationRunning = true;
        }
        _ = ReconcileEventsAsync();
    }

    private async Task ReconcileEventsAsync()
    {
        // Coalesce event bursts, with a strict bound even under continuous store activity.
        for (int pass = 0; pass < 3; pass++)
        {
            lock (_refreshLock) { _reconciliationRequested = false; }
            await TryRefreshCoreAsync().ConfigureAwait(false);
            lock (_refreshLock)
            {
                if (!_reconciliationRequested || pass == 2)
                {
                    _reconciliationRunning = false;
                    return;
                }
            }
        }
    }

    private void OnAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot snapshot) =>
        TriggerAuthoritativeReconciliation(snapshot.ResetEpoch, snapshot.StoreRevision);

    private void OnFirewallUpgradeSnapshotChanged(CyberDefenseFirewallUpgradeSnapshot snapshot) =>
        TriggerAuthoritativeReconciliation(snapshot.ResetEpoch, snapshot.StoreRevision);

    private void NotifyStatusChanged(UpgradeTerminalStatus status)
    {
        // Observer failures cannot change the outcome of a committed transaction.
        if (StatusChanged is not { } handlers) return;
        foreach (Action<UpgradeTerminalStatus> handler in handlers.GetInvocationList())
        {
            if (_isDisposed || !ReferenceEquals(status, _status)) return;
            try { handler(status); } catch { }
        }
    }

    private void NotifyStateChanged()
    {
        lock (_stateLock)
        {
            if (_isDisposed || StateChanged is not { } handlers) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                if (_isDisposed) return;
                try { handler(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _isSynchronized = false;
            _hasAuthoritativeStatus = false;
            _gameplayStore.AttackUpgradeSnapshotChanged -= OnAttackUpgradeSnapshotChanged;
            _gameplayStore.FirewallUpgradeSnapshotChanged -= OnFirewallUpgradeSnapshotChanged;
            StatusChanged = null;
            StateChanged = null;
        }
    }
}
