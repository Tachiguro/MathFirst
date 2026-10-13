namespace MathFirst.Application.Gameplay;

using System;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Authoritative application-level ViewModel projecting Cyber Defense player progression,
/// skill points, and Attack rank upgrades to the Upgrade Terminal presentation layer.
/// Enforces safe single-writer purchase execution with double-click rejection, optimistic
/// rank and reset-epoch fencing, and automatic state refresh.
/// </summary>
public sealed class UpgradeTerminalViewModel : IDisposable
{
    private readonly IGameplayStore _gameplayStore;
    private readonly object _stateLock = new();
    private int _isPurchasingFlag;
    private bool _isDisposed;
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
    public bool CanPurchase => Status.CanPurchase && !IsBusy && !IsDisposed;
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
    public bool CanPurchaseFirewall => Status.CanPurchaseFirewall && !IsBusy && !IsDisposed;
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
        lock (_stateLock)
        {
            if (_isDisposed) return _status;
        }

        var progression = await _gameplayStore.GetProgressionAsync(cancellationToken).ConfigureAwait(false);

        UpgradeTerminalStatus? statusToPublish = null;
        UpgradeTerminalStatus currentStatus;

        lock (_stateLock)
        {
            if (_isDisposed) return _status;

            var candidate = UpgradeTerminalStatus.FromProgression(progression);
            if (TryApplyStatus_NoLock(candidate, out var applied))
            {
                statusToPublish = applied;
            }
            currentStatus = _status;
        }

        if (statusToPublish != null)
        {
            StatusChanged?.Invoke(statusToPublish);
            StateChanged?.Invoke();
        }

        return currentStatus;
    }

    public async Task<UpgradePurchaseResult> PurchaseUpgradeAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return UpgradePurchaseResult.Disposed(_status);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return UpgradePurchaseResult.Cancelled(Status);
        }

        // Rapid repeated click / double-click race defense:
        // Reject immediately if another purchase is already executing.
        if (Interlocked.CompareExchange(ref _isPurchasingFlag, 1, 0) != 0)
        {
            return UpgradePurchaseResult.Busy(Status);
        }

        bool busyEntryNotified = false;
        try
        {
            lock (_stateLock)
            {
                if (_isDisposed)
                {
                    return UpgradePurchaseResult.Disposed(_status);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return UpgradePurchaseResult.Cancelled(Status);
            }

            // Notify subscribers that we entered busy state
            StateChanged?.Invoke();
            busyEntryNotified = true;

            var current = Status;

            // Fail-closed checks before hitting the store
            if (current.IsMaxRank)
            {
                return UpgradePurchaseResult.MaxRankReached(current);
            }

            if (!current.CanAfford)
            {
                return UpgradePurchaseResult.InsufficientSkillPoints(current);
            }

            try
            {
                var newProgression = await _gameplayStore.PurchaseAttackUpgradeAsync(
                    current.CurrentAttackRank,
                    current.ResetEpoch,
                    cancellationToken).ConfigureAwait(false);

                var committedStatus = UpgradeTerminalStatus.FromProgression(newProgression);
                UpgradeTerminalStatus? statusToPublish = null;
                bool wasDisposed;

                lock (_stateLock)
                {
                    wasDisposed = _isDisposed;
                    if (!wasDisposed)
                    {
                        if (TryApplyStatus_NoLock(committedStatus, out var applied))
                        {
                            statusToPublish = applied;
                        }
                    }
                }

                if (!wasDisposed && statusToPublish != null)
                {
                    StatusChanged?.Invoke(statusToPublish);
                    StateChanged?.Invoke();
                }

                return UpgradePurchaseResult.Success(committedStatus);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return UpgradePurchaseResult.Cancelled(Status);
            }
            catch (ObjectDisposedException)
            {
                return UpgradePurchaseResult.Disposed(Status);
            }
            catch (InvalidOperationException ex)
            {
                // Concurrency conflict, insufficient SP, or store rejection:
                // Refresh status to synchronize with authoritative store
                await TryRefreshCoreAsync().ConfigureAwait(false);
                var refreshed = Status;

                if (ex.Message.Contains("Insufficient available skill points", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.InsufficientSkillPoints(refreshed, ex.Message);
                }

                if (ex.Message.Contains("Expected attack rank", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("Concurrency conflict", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.StaleRank(refreshed, ex.Message);
                }

                if (ex.Message.Contains("reset epoch", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("reset is pending", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.StaleResetEpoch(refreshed, ex.Message);
                }

                if (ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("overflow", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("exceeds maximum representable", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.MaxRankReached(refreshed, ex.Message);
                }

                return UpgradePurchaseResult.Error(ex.Message, ex, refreshed);
            }
            catch (Exception ex)
            {
                await TryRefreshCoreAsync().ConfigureAwait(false);
                return UpgradePurchaseResult.Error(ex.Message, ex, Status);
            }
        }
        finally
        {
            Volatile.Write(ref _isPurchasingFlag, 0);

            bool shouldNotifyRelease;
            lock (_stateLock)
            {
                shouldNotifyRelease = !_isDisposed && busyEntryNotified;
            }

            if (shouldNotifyRelease)
            {
                StateChanged?.Invoke();
            }
        }
    }

    public async Task<UpgradePurchaseResult> PurchaseFirewallUpgradeAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return UpgradePurchaseResult.Disposed(_status);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return UpgradePurchaseResult.Cancelled(Status);
        }

        if (Interlocked.CompareExchange(ref _isPurchasingFlag, 1, 0) != 0)
        {
            return UpgradePurchaseResult.Busy(Status);
        }

        bool busyEntryNotified = false;
        try
        {
            lock (_stateLock)
            {
                if (_isDisposed)
                {
                    return UpgradePurchaseResult.Disposed(_status);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return UpgradePurchaseResult.Cancelled(Status);
            }

            StateChanged?.Invoke();
            busyEntryNotified = true;

            var current = Status;

            if (current.IsFirewallMaxRank)
            {
                return UpgradePurchaseResult.MaxRankReached(current, "Maximum firewall rank reached.");
            }

            if (!current.IsFirewallLevelUnlocked)
            {
                return UpgradePurchaseResult.LockedLevel(
                    current,
                    $"Firewall rank {current.NextFirewallRank} requires player level {current.NextRequiredLevel}.");
            }

            if (!current.HasSufficientFirewallSkillPoints)
            {
                return UpgradePurchaseResult.InsufficientSkillPoints(
                    current,
                    "Insufficient skill points for firewall upgrade.");
            }

            try
            {
                var newProgression = await _gameplayStore.PurchaseFirewallUpgradeAsync(
                    current.CurrentFirewallRank,
                    current.ResetEpoch,
                    cancellationToken).ConfigureAwait(false);

                var committedStatus = UpgradeTerminalStatus.FromProgression(newProgression);
                UpgradeTerminalStatus? statusToPublish = null;
                bool wasDisposed;

                lock (_stateLock)
                {
                    wasDisposed = _isDisposed;
                    if (!wasDisposed)
                    {
                        if (TryApplyStatus_NoLock(committedStatus, out var applied))
                        {
                            statusToPublish = applied;
                        }
                    }
                }

                if (!wasDisposed && statusToPublish != null)
                {
                    StatusChanged?.Invoke(statusToPublish);
                    StateChanged?.Invoke();
                }

                return UpgradePurchaseResult.Success(committedStatus);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return UpgradePurchaseResult.Cancelled(Status);
            }
            catch (ObjectDisposedException)
            {
                return UpgradePurchaseResult.Disposed(Status);
            }
            catch (InvalidOperationException ex)
            {
                await TryRefreshCoreAsync().ConfigureAwait(false);
                var refreshed = Status;

                if (ex.Message.Contains("level", StringComparison.OrdinalIgnoreCase) &&
                    (ex.Message.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
                     ex.Message.Contains("requires", StringComparison.OrdinalIgnoreCase)))
                {
                    return UpgradePurchaseResult.LockedLevel(refreshed, ex.Message);
                }

                if (ex.Message.Contains("Insufficient available skill points", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.InsufficientSkillPoints(refreshed, ex.Message);
                }

                if (ex.Message.Contains("Expected firewall rank", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("Concurrency conflict", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.StaleRank(refreshed, ex.Message);
                }

                if (ex.Message.Contains("reset epoch", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("reset is pending", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.StaleResetEpoch(refreshed, ex.Message);
                }

                if (ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("already reached maximum rank", StringComparison.OrdinalIgnoreCase))
                {
                    return UpgradePurchaseResult.MaxRankReached(refreshed, ex.Message);
                }

                return UpgradePurchaseResult.Error(ex.Message, ex, refreshed);
            }
            catch (Exception ex)
            {
                await TryRefreshCoreAsync().ConfigureAwait(false);
                return UpgradePurchaseResult.Error(ex.Message, ex, Status);
            }
        }
        finally
        {
            Volatile.Write(ref _isPurchasingFlag, 0);

            bool shouldNotifyRelease;
            lock (_stateLock)
            {
                shouldNotifyRelease = !_isDisposed && busyEntryNotified;
            }

            if (shouldNotifyRelease)
            {
                StateChanged?.Invoke();
            }
        }
    }

    public Task<UpgradePurchaseResult> PurchaseFirewallAsync(CancellationToken cancellationToken = default) =>
        PurchaseFirewallUpgradeAsync(cancellationToken);

    private async Task TryRefreshCoreAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best effort refresh; ignore secondary failure
        }
    }

    private readonly object _refreshLock = new();
    private Task? _inFlightRefreshTask;
    private bool _queuedRefreshNeeded;

    private void TriggerAuthoritativeReconciliation()
    {
        lock (_refreshLock)
        {
            lock (_stateLock)
            {
                if (_isDisposed) return;
            }

            if (_inFlightRefreshTask != null && !_inFlightRefreshTask.IsCompleted)
            {
                _queuedRefreshNeeded = true;
                return;
            }

            _inFlightRefreshTask = RunReconciliationLoopAsync();
        }
    }

    private async Task RunReconciliationLoopAsync()
    {
        while (true)
        {
            lock (_refreshLock)
            {
                _queuedRefreshNeeded = false;
            }

            try
            {
                await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best effort refresh; ignore secondary failure
            }

            lock (_refreshLock)
            {
                bool isDisposed;
                lock (_stateLock)
                {
                    isDisposed = _isDisposed;
                }

                if (isDisposed || !_queuedRefreshNeeded)
                {
                    _inFlightRefreshTask = null;
                    break;
                }
            }
        }
    }

    private void OnAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot snapshot)
    {
        UpgradeTerminalStatus? statusToPublish = null;
        bool shouldReconcileAuthoritativeStore = false;

        lock (_stateLock)
        {
            if (_isDisposed) return;

            if (snapshot.ResetEpoch < _status.ResetEpoch)
            {
                return;
            }
            if (snapshot.ResetEpoch == _status.ResetEpoch && snapshot.StoreRevision < _status.StoreRevision)
            {
                return;
            }

            long cumulativeXp;
            if (snapshot.ResetEpoch > _status.ResetEpoch)
            {
                // Reset epoch advanced: previous epoch's XP must never be retained
                cumulativeXp = 0L;
                shouldReconcileAuthoritativeStore = true;
            }
            else if (snapshot.PlayerLevel != _status.PlayerLevel)
            {
                cumulativeXp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(snapshot.PlayerLevel);
                shouldReconcileAuthoritativeStore = true;
            }
            else
            {
                cumulativeXp = _status.CumulativeXp;
            }

            var candidate = UpgradeTerminalStatus.FromSnapshot(
                snapshot,
                cumulativeXp,
                currentFirewallRank: _status.CurrentFirewallRank,
                activatedFirewallRank: _status.ActivatedFirewallRank);

            if (TryApplyStatus_NoLock(candidate, out var applied))
            {
                statusToPublish = applied;
            }
        }

        if (statusToPublish != null)
        {
            StatusChanged?.Invoke(statusToPublish);
            StateChanged?.Invoke();
        }

        if (shouldReconcileAuthoritativeStore)
        {
            TriggerAuthoritativeReconciliation();
        }
    }

    private void OnFirewallUpgradeSnapshotChanged(CyberDefenseFirewallUpgradeSnapshot snapshot)
    {
        UpgradeTerminalStatus? statusToPublish = null;
        bool shouldReconcileAuthoritativeStore = false;

        lock (_stateLock)
        {
            if (_isDisposed) return;

            if (snapshot.ResetEpoch < _status.ResetEpoch)
            {
                return;
            }
            if (snapshot.ResetEpoch == _status.ResetEpoch && snapshot.StoreRevision < _status.StoreRevision)
            {
                return;
            }

            long cumulativeXp;
            if (snapshot.ResetEpoch > _status.ResetEpoch)
            {
                // Reset epoch advanced: previous epoch's XP must never be retained
                cumulativeXp = 0L;
                shouldReconcileAuthoritativeStore = true;
            }
            else if (snapshot.PlayerLevel != _status.PlayerLevel)
            {
                cumulativeXp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(snapshot.PlayerLevel);
                shouldReconcileAuthoritativeStore = true;
            }
            else
            {
                cumulativeXp = _status.CumulativeXp;
            }

            var candidate = UpgradeTerminalStatus.FromFirewallSnapshot(
                snapshot,
                cumulativeXp,
                currentAttackRank: _status.CurrentAttackRank,
                activatedFirewallRank: _status.ActivatedFirewallRank);

            if (TryApplyStatus_NoLock(candidate, out var applied))
            {
                statusToPublish = applied;
            }
        }

        if (statusToPublish != null)
        {
            StatusChanged?.Invoke(statusToPublish);
            StateChanged?.Invoke();
        }

        if (shouldReconcileAuthoritativeStore)
        {
            TriggerAuthoritativeReconciliation();
        }
    }

    private bool TryApplyStatus_NoLock(UpgradeTerminalStatus candidate, out UpgradeTerminalStatus appliedStatus)
    {
        appliedStatus = _status;
        if (_isDisposed)
        {
            return false;
        }

        // Monotonic reset epoch fencing: drop older epochs
        if (candidate.ResetEpoch < _status.ResetEpoch)
        {
            return false;
        }

        if (candidate.ResetEpoch == _status.ResetEpoch)
        {
            // Monotonic store revision fencing: drop older revisions in same epoch
            if (candidate.StoreRevision < _status.StoreRevision)
            {
                return false;
            }

            // Duplicate suppression: if revision and all status properties are identical, skip
            if (candidate.StoreRevision == _status.StoreRevision && candidate == _status)
            {
                return false;
            }
        }

        _status = candidate;
        appliedStatus = _status;
        return true;
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
        _gameplayStore.AttackUpgradeSnapshotChanged -= OnAttackUpgradeSnapshotChanged;
        _gameplayStore.FirewallUpgradeSnapshotChanged -= OnFirewallUpgradeSnapshotChanged;
        StatusChanged = null;
        StateChanged = null;
    }
}
