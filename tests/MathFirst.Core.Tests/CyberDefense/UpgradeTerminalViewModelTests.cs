namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class UpgradeTerminalViewModelTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_UpgradeTerminal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteGameplayStore CreateStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");
        return new SqliteGameplayStore(dbPath);
    }

    private static async Task SetProgressionAsync(
        string dbPath,
        long cumulativeXp,
        int playerLevel,
        int earnedSp,
        int spentSp,
        int availableSp,
        int attackRank,
        long resetEpoch = 0,
        int firewallRank = 0,
        int activatedFirewallRank = 0)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE gameplay_progression
            SET cumulative_xp = @xp,
                player_level = @level,
                earned_sp = @earned,
                spent_sp = @spent,
                available_sp = @available,
                attack_rank = @rank,
                reset_epoch = @resetEpoch,
                firewall_rank = @firewallRank,
                activated_firewall_rank = @activatedFirewallRank
            WHERE id = 1;
        ";
        cmd.Parameters.AddWithValue("@xp", cumulativeXp);
        cmd.Parameters.AddWithValue("@level", playerLevel);
        cmd.Parameters.AddWithValue("@earned", earnedSp);
        cmd.Parameters.AddWithValue("@spent", spentSp);
        cmd.Parameters.AddWithValue("@available", availableSp);
        cmd.Parameters.AddWithValue("@rank", attackRank);
        cmd.Parameters.AddWithValue("@resetEpoch", resetEpoch);
        cmd.Parameters.AddWithValue("@firewallRank", firewallRank);
        cmd.Parameters.AddWithValue("@activatedFirewallRank", activatedFirewallRank);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    // =========================================================================
    // 1. Construction and Initial Projection
    // =========================================================================

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new UpgradeTerminalViewModel(null!));
    }

    [Fact]
    public void InitialState_ProjectsAuthoritativeDefaults()
    {
        using var store = CreateStore();
        using var vm = new UpgradeTerminalViewModel(store);

        Assert.Equal(1, vm.PlayerLevel);
        Assert.Equal(0L, vm.CumulativeXp);
        Assert.Equal(0L, vm.XpIntoCurrentLevel);
        Assert.True(vm.XpRequiredForNextLevel > 0);
        Assert.Equal(0, vm.AvailableSkillPoints);
        Assert.Equal(0, vm.CurrentAttackRank);
        Assert.Equal(1, vm.CurrentBaseDamage);
        Assert.Equal(1, vm.NextAttackRank);
        Assert.Equal(2, vm.NextBaseDamage);
        Assert.Equal(1, vm.NextRankCost);
        Assert.False(vm.HasSufficientSkillPoints);
        Assert.False(vm.CanAfford);
        Assert.False(vm.IsMaxRank);
        Assert.False(vm.CanPurchase);
        Assert.Equal(0L, vm.ResetEpoch);
        Assert.Equal(1L, vm.StoreRevision);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsDisposed);
    }

    [Fact]
    public async Task InitializeAsync_LoadsAuthoritativeProgression()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Level 3 requires XP in [183, 314) per CyberDefenseLevelPolicy
        // Delta XP for level 1 = 80, level 2 = 104, sum = 184
        long testXp = 250L;
        int level = CyberDefenseLevelPolicy.GetLevel(testXp);
        Assert.Equal(3, level);
        int earnedSp = level - 1; // 2 SP
        await SetProgressionAsync(store.StoragePath, testXp, level, earnedSp, 0, earnedSp, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(3, vm.PlayerLevel);
        Assert.Equal(250L, vm.CumulativeXp);
        Assert.Equal(2, vm.AvailableSkillPoints);
        Assert.Equal(0, vm.CurrentAttackRank);
        Assert.Equal(1, vm.CurrentBaseDamage);
        Assert.Equal(1, vm.NextAttackRank);
        Assert.Equal(1, vm.NextRankCost); // Rank 1 costs 1 SP
        Assert.True(vm.CanAfford);
        Assert.True(vm.CanPurchase);
    }

    // =========================================================================
    // 2. Successful Purchase and Progression Refresh
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_WhenSufficientFunds_IncrementsRankAndDeductsCost()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        bool statusChangedFired = false;
        bool stateChangedFired = false;
        vm.StatusChanged += _ => statusChangedFired = true;
        vm.StateChanged += () => stateChangedFired = true;

        var result = await vm.PurchaseUpgradeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result.Status);
        Assert.NotNull(result.UpdatedStatus);
        Assert.Equal(1, result.UpdatedStatus.CurrentAttackRank);

        // ViewModel projections updated
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(2, vm.CurrentBaseDamage);
        Assert.Equal(2, vm.NextAttackRank);
        Assert.Equal(3, vm.NextBaseDamage);
        Assert.Equal(2, vm.NextRankCost); // Rank 2 costs 2 SP
        Assert.Equal(3, vm.AvailableSkillPoints); // 4 - 1 = 3 SP
        Assert.True(vm.CanAfford); // 3 >= 2
        Assert.True(vm.CanPurchase);

        Assert.True(statusChangedFired);
        Assert.True(stateChangedFired);

        // Authoritative store updated
        var storeProg = await store.GetProgressionAsync();
        Assert.Equal(1, storeProg.AttackRank);
        Assert.Equal(3, storeProg.AvailableSkillPoints);
        Assert.Equal(1, storeProg.SpentSkillPoints);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_SequentialPurchases_AllowedWhenFundsRemain()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        // 5 SP available, rank 0
        await SetProgressionAsync(store.StoragePath, 1000L, 6, 5, 0, 5, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // 1st purchase: Rank 0 -> 1 (cost 1 SP, remaining 4 SP)
        var res1 = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, res1.Status);
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(4, vm.AvailableSkillPoints);

        // 2nd purchase: Rank 1 -> 2 (cost 2 SP, remaining 2 SP)
        var res2 = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, res2.Status);
        Assert.Equal(2, vm.CurrentAttackRank);
        Assert.Equal(2, vm.AvailableSkillPoints);

        // 3rd purchase: Rank 2 -> 3 (cost 2 SP, remaining 0 SP)
        var res3 = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, res3.Status);
        Assert.Equal(3, vm.CurrentAttackRank);
        Assert.Equal(0, vm.AvailableSkillPoints);
        Assert.False(vm.CanAfford);

        // 4th purchase attempt: Rank 3 -> 4 costs 3 SP, but 0 available
        var res4 = await vm.PurchaseUpgradeAsync();
        Assert.False(res4.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.InsufficientSkillPoints, res4.Status);
        Assert.Equal(3, vm.CurrentAttackRank);
        Assert.Equal(0, vm.AvailableSkillPoints);
    }

    // =========================================================================
    // 3. Concurrency Safety: Rapid Repeated Clicks (Double-Click Race)
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_RapidRepeatedClicks_RejectsSecondClickWithBusy()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        // Task 1 will pause inside store purchase
        var task1 = vm.PurchaseUpgradeAsync();

        // Wait until task 1 is inside store purchase
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Task 2 arrives concurrently while Task 1 is still in progress
        var task2 = vm.PurchaseUpgradeAsync();
        var result2 = await task2;

        // Second click rejected immediately as Busy
        Assert.False(result2.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Busy, result2.Status);

        // Release task 1
        delayedStore.ReleasePurchase();
        var result1 = await task1;

        Assert.True(result1.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result1.Status);

        // Exactly one purchase was executed in the store
        Assert.Equal(1, delayedStore.PurchaseCallCount);
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(4, vm.AvailableSkillPoints);
    }

    // =========================================================================
    // 4. Insufficient Funds Handling
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_InsufficientFunds_PreCheckReturnsInsufficientSkillPoints()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 0L, 1, 0, 0, 0, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.InsufficientSkillPoints, result.Status);
        Assert.Equal(0, vm.CurrentAttackRank);
        Assert.Equal(0, vm.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_InsufficientFunds_StoreRejectionReturnsInsufficientSkillPoints()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 0L, 1, 1, 0, 1, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // Drain SP directly in store behind VM's back
        await SetProgressionAsync(store.StoragePath, 0L, 1, 0, 0, 0, 0);

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.InsufficientSkillPoints, result.Status);
    }

    // =========================================================================
    // 5. Optimistic Concurrency: Stale Rank and Stale Reset Epoch
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_StaleRank_ReturnsStaleRankAndRefreshes()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 1000L, 6, 5, 0, 5, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // Advance store rank to 1 behind VM's back
        await SetProgressionAsync(store.StoragePath, 1000L, 6, 5, 1, 4, 1);

        // VM still expects rank 0
        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.StaleRank, result.Status);

        // State refreshed to current store rank 1
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(4, vm.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_StaleResetEpoch_ReturnsStaleResetEpochAndRefreshes()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 1000L, 6, 5, 0, 5, 0, resetEpoch: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // Advance reset epoch directly in store behind VM's back
        await SetProgressionAsync(store.StoragePath, 1000L, 6, 5, 0, 5, 0, resetEpoch: 1);

        // VM still expects epoch 0
        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.StaleResetEpoch, result.Status);

        // VM refreshed to epoch 1
        Assert.Equal(1L, vm.ResetEpoch);
    }

    // =========================================================================
    // 6. Max Rank Reached
    // =========================================================================

    [Fact]
    public void MaxRank_CannotPurchase_AndPurchaseReturnsMaxRankReached()
    {
        var maxStatus = new UpgradeTerminalStatus(
            playerLevel: 100,
            cumulativeXp: 100000L,
            xpIntoCurrentLevel: 0L,
            xpRequiredForNextLevel: 1000,
            remainingXpToNextLevel: 1000,
            progressRatio: 0.0,
            levelProgress: CyberDefenseLevelPolicy.CalculateProgress(100000L),
            availableSkillPoints: 1000,
            earnedSkillPoints: 1000,
            spentSkillPoints: 0,
            currentAttackRank: CyberDefenseAttackUpgradeSnapshot.MaxRepresentableAttackRank,
            nextAttackRank: null,
            currentBaseDamage: CyberDefenseAttackUpgradeSnapshot.MaxRepresentableAttackRank + 1,
            nextBaseDamage: null,
            nextRankCost: null,
            hasSufficientSkillPoints: false,
            resetEpoch: 0,
            storeRevision: 1);

        Assert.True(maxStatus.IsMaxRank);
        Assert.False(maxStatus.CanPurchase);

        var purchaseResult = UpgradePurchaseResult.MaxRankReached(maxStatus);
        Assert.False(purchaseResult.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.MaxRankReached, purchaseResult.Status);
    }

    // =========================================================================
    // 7. Cancellation and Disposal
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_CancelledToken_ReturnsCancelled()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await vm.PurchaseUpgradeAsync(cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_WhenDisposed_ReturnsDisposed()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        vm.Dispose();
        Assert.True(vm.IsDisposed);
        Assert.False(vm.CanPurchase);

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Disposed, result.Status);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromStoreSnapshotEvents()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        int eventCount = 0;
        vm.StatusChanged += _ => eventCount++;

        vm.Dispose();

        // Mutate store progression and trigger purchase
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);
        await store.PurchaseAttackUpgradeAsync(0, 0);

        // Disposed VM must not react to store events
        Assert.Equal(0, eventCount);
    }

    // =========================================================================
    // 8. Refresh and External Store Notifications
    // =========================================================================

    [Fact]
    public async Task RefreshAsync_UpdatesViewModelFromStore()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(0, vm.AvailableSkillPoints);

        // Update store behind VM's back
        await SetProgressionAsync(store.StoragePath, 200L, 2, 1, 0, 1, 0);

        await vm.RefreshAsync();

        Assert.Equal(2, vm.PlayerLevel);
        Assert.Equal(200L, vm.CumulativeXp);
        Assert.Equal(1, vm.AvailableSkillPoints);
        Assert.True(vm.CanPurchase);
    }

    [Fact]
    public async Task StoreAttackUpgradeSnapshotChanged_UpdatesViewModelStatus()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(0, vm.CurrentAttackRank);

        // Perform purchase directly on store (simulating another coordinator or test)
        await store.PurchaseAttackUpgradeAsync(0, 0);

        // VM subscribed to AttackUpgradeSnapshotChanged
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(2, vm.CurrentBaseDamage);
        Assert.Equal(3, vm.AvailableSkillPoints);
    }

    // =========================================================================
    // 9. Error Handling
    // =========================================================================

    [Fact]
    public async Task PurchaseUpgradeAsync_UnexpectedStoreError_ReturnsErrorWithoutFalseSuccess()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0);

        var failingStore = new FailingUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(failingStore);
        await vm.InitializeAsync();

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Error, result.Status);
        Assert.Contains("Simulated store failure", result.ErrorMessage);
    }

    // =========================================================================
    // 10. Damage Binding Semantics: Upgrades Apply to Future Questions Only
    // =========================================================================

    [Fact]
    public async Task DamageBinding_UpgradesApplyToFutureQuestionsOnly()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);

        var coordinator = new CyberDefenseCombatCoordinator(store);
        await coordinator.InitializeAsync();

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // 1. Question 1 presented before upgrade -> binds baseline damage (1)
        int q1Damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(1001L);
        Assert.Equal(1, q1Damage);

        // 2. Player purchases upgrade in terminal -> damage becomes 2
        var purchaseResult = await vm.PurchaseUpgradeAsync();
        Assert.True(purchaseResult.IsSuccess);
        Assert.Equal(2, vm.CurrentBaseDamage);

        // 3. Question 1 still retains original bound damage 1!
        int q1RetainedDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(1001L);
        Assert.Equal(1, q1RetainedDamage);

        // 4. Future Question 2 presented after upgrade -> binds upgraded damage 2!
        int q2Damage = await coordinator.GetOrBindEffectiveAttackDamageAsync(1002L);
        Assert.Equal(2, q2Damage);
    }

    // =========================================================================
    // 11. Additional Contract and Edge-Case Coverage
    // =========================================================================

    [Fact]
    public void UpgradeTerminalStatus_FromProgression_CalculatesAllPropertiesAccurately()
    {
        // 500 XP -> Level 5 (cumulative threshold 447, next 612)
        // delta XP for level 5 = 165, xp into level = 53, remaining = 112
        var prog = new CyberDefenseProgressionRecord(
            storeRevision: 10,
            resetEpoch: 2,
            cumulativeXp: 500L,
            playerLevel: 5,
            earnedSkillPoints: 4,
            spentSkillPoints: 1,
            availableSkillPoints: 3,
            attackRank: 1,
            economyVersion: "ECONOMY_V1",
            updatedAt: DateTimeOffset.UtcNow);

        var status = UpgradeTerminalStatus.FromProgression(prog);

        Assert.Equal(5, status.PlayerLevel);
        Assert.Equal(500L, status.CumulativeXp);
        var expectedProgress = CyberDefenseLevelPolicy.CalculateProgress(500L);
        Assert.Equal(expectedProgress.XpIntoCurrentLevel, status.XpIntoCurrentLevel);
        Assert.Equal(expectedProgress.XpRequiredForNextLevel, status.XpRequiredForNextLevel);
        Assert.Equal(expectedProgress.RemainingXpToNextLevel, status.RemainingXpToNextLevel);
        Assert.Equal(expectedProgress.ProgressRatio, status.ProgressRatio);
        Assert.Equal(3, status.AvailableSkillPoints);
        Assert.Equal(4, status.EarnedSkillPoints);
        Assert.Equal(1, status.SpentSkillPoints);
        Assert.Equal(1, status.CurrentAttackRank);
        Assert.Equal(2, status.NextAttackRank);
        Assert.Equal(2, status.CurrentBaseDamage);
        Assert.Equal(3, status.NextBaseDamage);
        Assert.Equal(2, status.NextRankCost); // Rank 2 cost = 2 SP
        Assert.True(status.HasSufficientSkillPoints);
        Assert.True(status.CanAfford);
        Assert.False(status.IsMaxRank);
        Assert.True(status.CanPurchase);
        Assert.Equal(2L, status.ResetEpoch);
        Assert.Equal(10L, status.StoreRevision);
    }

    [Fact]
    public void UpgradeTerminalStatus_FromSnapshot_MapsCorrectly()
    {
        var snap = new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: 2,
            nextAttackRank: 3,
            currentBaseDamage: 3,
            nextBaseDamage: 4,
            nextRankCost: 2,
            availableSkillPoints: 1,
            hasSufficientSkillPoints: false,
            playerLevel: 3,
            resetEpoch: 1,
            storeRevision: 5);

        var status = UpgradeTerminalStatus.FromSnapshot(snap, cumulativeXp: 200L);

        Assert.Equal(3, status.PlayerLevel);
        Assert.Equal(200L, status.CumulativeXp);
        Assert.Equal(1, status.AvailableSkillPoints);
        Assert.Equal(2, status.CurrentAttackRank);
        Assert.Equal(3, status.NextAttackRank);
        Assert.Equal(3, status.CurrentBaseDamage);
        Assert.Equal(4, status.NextBaseDamage);
        Assert.Equal(2, status.NextRankCost);
        Assert.False(status.HasSufficientSkillPoints);
        Assert.False(status.CanAfford);
        Assert.False(status.CanPurchase);
        Assert.Equal(1L, status.ResetEpoch);
        Assert.Equal(5L, status.StoreRevision);
    }

    [Fact]
    public void UpgradePurchaseResult_PropertiesAndAccessors_ExposeExpectedValues()
    {
        var status = UpgradeTerminalStatus.Initial();
        var success = UpgradePurchaseResult.Success(status);
        Assert.True(success.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, success.Status);
        Assert.Same(status, success.StatusSnapshot);
        Assert.Same(status, success.UpdatedStatus);
        Assert.Null(success.ErrorMessage);
        Assert.Null(success.Exception);

        var busy = UpgradePurchaseResult.Busy(status);
        Assert.False(busy.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Busy, busy.Status);

        var ex = new InvalidOperationException("Fail");
        var error = UpgradePurchaseResult.Error("Error message", ex, status);
        Assert.False(error.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Error, error.Status);
        Assert.Equal("Error message", error.ErrorMessage);
        Assert.Same(ex, error.Exception);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_StoreThrowsResetPending_ReturnsStaleResetEpoch()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0);

        var throwingStore = new CustomThrowingStore(realStore, new InvalidOperationException("Cannot purchase attack upgrade while a reset is pending."));
        using var vm = new UpgradeTerminalViewModel(throwingStore);
        await vm.InitializeAsync();

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.StaleResetEpoch, result.Status);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_StoreThrowsObjectDisposedException_ReturnsDisposed()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0);

        var throwingStore = new CustomThrowingStore(realStore, new ObjectDisposedException("Store"));
        using var vm = new UpgradeTerminalViewModel(throwingStore);
        await vm.InitializeAsync();

        var result = await vm.PurchaseUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Disposed, result.Status);
    }

    // =========================================================================
    // Test Doubles
    // =========================================================================

    private sealed class ControllableDelayedUpgradeStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;
        private readonly TaskCompletionSource<bool> _purchaseRelease = new();
        public TaskCompletionSource<bool> PurchaseEnteredSignal { get; } = new();
        public int PurchaseCallCount { get; private set; }

        public ControllableDelayedUpgradeStore(IGameplayStore inner)
        {
            _inner = inner;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => _inner.GetProgressionAsync(cancellationToken);
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;

        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);

        public async Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            PurchaseCallCount++;
            PurchaseEnteredSignal.TrySetResult(true);
            await _purchaseRelease.Task;
            return await _inner.PurchaseAttackUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        }

        public async Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            PurchaseCallCount++;
            PurchaseEnteredSignal.TrySetResult(true);
            await _purchaseRelease.Task;
            return await _inner.PurchaseFirewallUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        }

        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);

        public void ReleasePurchase() => _purchaseRelease.TrySetResult(true);
        public void ReleasePurchaseWithException(Exception ex) => _purchaseRelease.TrySetException(ex);

        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class FailingUpgradeStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;

        public FailingUpgradeStore(IGameplayStore inner)
        {
            _inner = inner;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => _inner.GetProgressionAsync(cancellationToken);
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;

        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);

        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated store failure: disk I/O error");
        }

        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated store failure: disk I/O error");
        }

        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);

        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class CustomThrowingStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;
        private readonly Exception _exceptionToThrow;

        public CustomThrowingStore(IGameplayStore inner, Exception exceptionToThrow)
        {
            _inner = inner;
            _exceptionToThrow = exceptionToThrow;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => _inner.GetProgressionAsync(cancellationToken);
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;

        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);

        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            throw _exceptionToThrow;
        }

        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            throw _exceptionToThrow;
        }

        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);

        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class ControllableDelayedProgressionStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;
        private readonly TaskCompletionSource<bool> _progressionRelease = new();
        public TaskCompletionSource<bool> ProgressionEnteredSignal { get; } = new();
        public bool DelayNextProgression { get; set; }

        public ControllableDelayedProgressionStore(IGameplayStore inner)
        {
            _inner = inner;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);

        public async Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default)
        {
            if (DelayNextProgression)
            {
                ProgressionEnteredSignal.TrySetResult(true);
                await _progressionRelease.Task;
            }
            return await _inner.GetProgressionAsync(cancellationToken);
        }

        public void ReleaseProgression() => _progressionRelease.TrySetResult(true);

        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;
        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.PurchaseAttackUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.PurchaseFirewallUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class ConfigurableProgressionStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;
        public CyberDefenseProgressionRecord NextRecord { get; set; }

        public ConfigurableProgressionStore(IGameplayStore inner, CyberDefenseProgressionRecord initialRecord)
        {
            _inner = inner;
            NextRecord = initialRecord;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => Task.FromResult(NextRecord);
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;
        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.PurchaseAttackUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.PurchaseFirewallUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class ActionAfterPurchaseStore : IGameplayStore
    {
        private readonly IGameplayStore _inner;
        private readonly Action _onCommitted;

        public ActionAfterPurchaseStore(IGameplayStore inner, Action onCommitted)
        {
            _inner = inner;
            _onCommitted = onCommitted;
            _inner.AttackUpgradeSnapshotChanged += snapshot => AttackUpgradeSnapshotChanged?.Invoke(snapshot);
            _inner.FirewallUpgradeSnapshotChanged += snapshot => FirewallUpgradeSnapshotChanged?.Invoke(snapshot);
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => _inner.GetRunStateAsync(cancellationToken);
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => _inner.GetResetEpochAsync(cancellationToken);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => _inner.GetStoreRevisionAsync(cancellationToken);
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => _inner.GetProgressionAsync(cancellationToken);
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetAttackUpgradeSnapshotAsync(cancellationToken);
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;
        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.GetFirewallUpgradeSnapshotAsync(cancellationToken);

        public async Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            var result = await _inner.PurchaseAttackUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
            _onCommitted();
            return result;
        }

        public async Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        {
            var result = await _inner.PurchaseFirewallUpgradeAsync(expectedRank, expectedResetEpoch, cancellationToken);
            _onCommitted();
            return result;
        }

        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            _inner.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity, expectedResetEpoch, cancellationToken);

        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetReceiptAsync(submissionId, cancellationToken);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.StagePendingIntentAsync(intent, cancellationToken);
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.GetPendingIntentAsync(submissionId, cancellationToken);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => _inner.GetPendingIntentsAsync(resetEpoch, cancellationToken);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => _inner.ClearPendingIntentAsync(submissionId, cancellationToken);
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => _inner.ApplyAttemptTransactionAsync(intent, cancellationToken);
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.GetResetIntentAsync(cancellationToken);
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => _inner.BeginOrGetResetIntentAsync(cancellationToken);
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ResetGameplayStateAsync(targetEpoch, cancellationToken);
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => _inner.ClearResetIntentAsync(targetEpoch, cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    // =========================================================================
    // 12. Review Remediation Regression Tests (MF-CYBER-005 Slice 6A)
    // =========================================================================

    [Fact]
    public async Task ResetFromNonzeroXpAndHigherLevel_ReconcilesStatusConsistently()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        // Level 5, 500 XP, 4 earned SP
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0, resetEpoch: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(5, vm.PlayerLevel);
        Assert.Equal(500L, vm.CumulativeXp);
        Assert.Equal(0L, vm.ResetEpoch);

        // External reset occurs in store
        var intent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(intent.TargetEpoch);
        await store.ClearResetIntentAsync(intent.TargetEpoch);

        Assert.Equal(1L, vm.ResetEpoch);
        Assert.Equal(1, vm.PlayerLevel);
        Assert.Equal(0L, vm.CumulativeXp);
        Assert.Equal(0L, vm.XpIntoCurrentLevel);
        Assert.Equal(1, vm.LevelProgress.Level);
        Assert.Equal(0, vm.AvailableSkillPoints);
        Assert.Equal(0, vm.CurrentAttackRank);
    }

    [Fact]
    public async Task RefreshAsync_StartedBeforeNewerPurchase_DoesNotRollBackViewModelState()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedProgressionStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        Assert.Equal(0, vm.CurrentAttackRank);

        // Start RefreshAsync which will pause in GetProgressionAsync
        delayedStore.DelayNextProgression = true;
        var refreshTask = vm.RefreshAsync();
        await delayedStore.ProgressionEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // While refresh is paused in flight, perform a purchase directly on realStore
        await realStore.PurchaseAttackUpgradeAsync(0, 0);

        // The snapshot event updates the VM to rank 1, revision 2
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.True(vm.StoreRevision >= 2);

        // Release the stale refresh response (which was read before purchase committed)
        delayedStore.ReleaseProgression();
        var refreshedStatus = await refreshTask;

        // Fencing must prevent stale refresh from rolling back rank 1 to rank 0
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(1, refreshedStatus.CurrentAttackRank);
        Assert.True(vm.StoreRevision >= 2);
    }

    [Fact]
    public async Task RefreshAsync_StartedBeforeGameplayReset_DoesNotRollBackResetEpoch()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0, resetEpoch: 0);

        var delayedStore = new ControllableDelayedProgressionStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        Assert.Equal(0L, vm.ResetEpoch);
        Assert.Equal(5, vm.PlayerLevel);

        // Start RefreshAsync which will pause in GetProgressionAsync
        delayedStore.DelayNextProgression = true;
        var refreshTask = vm.RefreshAsync();
        await delayedStore.ProgressionEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // While refresh is paused, an external reset occurs
        var intent = await realStore.BeginOrGetResetIntentAsync();
        await realStore.ResetGameplayStateAsync(intent.TargetEpoch);
        await realStore.ClearResetIntentAsync(intent.TargetEpoch);

        // VM observes reset via snapshot event
        Assert.Equal(1L, vm.ResetEpoch);
        Assert.Equal(1, vm.PlayerLevel);

        // Release the stale refresh response (from epoch 0)
        delayedStore.ReleaseProgression();
        var refreshedStatus = await refreshTask;

        // Fencing must prevent stale epoch 0 response from rolling back epoch 1
        Assert.Equal(1L, vm.ResetEpoch);
        Assert.Equal(1L, refreshedStatus.ResetEpoch);
        Assert.Equal(1, vm.PlayerLevel);
        Assert.Equal(0L, vm.CumulativeXp);
    }

    [Fact]
    public async Task RefreshAsync_StaleRevisionAndStaleResetEpoch_IgnoredWithoutMutationOrEvents()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 1, 4, 1, resetEpoch: 2);

        var currentRecord = new CyberDefenseProgressionRecord(
            storeRevision: 10,
            resetEpoch: 2,
            cumulativeXp: 1000L,
            playerLevel: 6,
            earnedSkillPoints: 5,
            spentSkillPoints: 1,
            availableSkillPoints: 4,
            attackRank: 1,
            economyVersion: "ECONOMY_V1",
            updatedAt: DateTimeOffset.UtcNow);

        var testStore = new ConfigurableProgressionStore(realStore, currentRecord);
        using var vm = new UpgradeTerminalViewModel(testStore);
        await vm.InitializeAsync();

        Assert.Equal(2L, vm.ResetEpoch);
        Assert.Equal(10L, vm.StoreRevision);

        int statusChangedCount = 0;
        vm.StatusChanged += _ => statusChangedCount++;

        // 1. Stale older epoch (epoch 1 < 2)
        var staleEpochRecord = new CyberDefenseProgressionRecord(
            storeRevision: 20,
            resetEpoch: 1,
            cumulativeXp: 1000L,
            playerLevel: 6,
            earnedSkillPoints: 5,
            spentSkillPoints: 1,
            availableSkillPoints: 4,
            attackRank: 1,
            economyVersion: "ECONOMY_V1",
            updatedAt: DateTimeOffset.UtcNow);
        testStore.NextRecord = staleEpochRecord;

        var result1 = await vm.RefreshAsync();
        Assert.Equal(2L, vm.ResetEpoch);
        Assert.Equal(10L, vm.StoreRevision);
        Assert.Equal(0, statusChangedCount);

        // 2. Stale older revision in same epoch (revision 5 < 10 in epoch 2)
        var staleRevisionRecord = new CyberDefenseProgressionRecord(
            storeRevision: 5,
            resetEpoch: 2,
            cumulativeXp: 1000L,
            playerLevel: 6,
            earnedSkillPoints: 5,
            spentSkillPoints: 1,
            availableSkillPoints: 4,
            attackRank: 1,
            economyVersion: "ECONOMY_V1",
            updatedAt: DateTimeOffset.UtcNow);
        testStore.NextRecord = staleRevisionRecord;

        var result2 = await vm.RefreshAsync();
        Assert.Equal(2L, vm.ResetEpoch);
        Assert.Equal(10L, vm.StoreRevision);
        Assert.Equal(0, statusChangedCount);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_PublishesStatusChangedExactlyOncePerRevision()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var statusEvents = new List<UpgradeTerminalStatus>();
        vm.StatusChanged += s => statusEvents.Add(s);

        var purchaseResult = await vm.PurchaseUpgradeAsync();

        Assert.True(purchaseResult.IsSuccess);
        var singleEvent = Assert.Single(statusEvents);
        Assert.Equal(1, singleEvent.CurrentAttackRank);
        Assert.Equal(vm.StoreRevision, singleEvent.StoreRevision);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_NotifiesBusyEntryAndBusyExitTransitions()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        var observedStates = new List<(bool IsBusy, bool CanPurchase)>();
        vm.StateChanged += () => observedStates.Add((vm.IsBusy, vm.CanPurchase));

        var purchaseTask = vm.PurchaseUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // While in flight, subscriber must observe busy state
        Assert.Contains(observedStates, s => s.IsBusy && !s.CanPurchase);

        delayedStore.ReleasePurchase();
        var result = await purchaseTask;

        Assert.True(result.IsSuccess);
        // After completion, subscriber must observe released gate
        Assert.True(observedStates.Count >= 2);
        var finalState = observedStates[^1];
        Assert.False(finalState.IsBusy);
        Assert.True(finalState.CanPurchase);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_DisposalWhilePurchaseInFlight_SuppressesCallbacksAndReturnsDisposed()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        int postDisposalCallbacks = 0;
        vm.StatusChanged += _ => { if (vm.IsDisposed) postDisposalCallbacks++; };
        vm.StateChanged += () => { if (vm.IsDisposed) postDisposalCallbacks++; };

        var purchaseTask = vm.PurchaseUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Dispose VM while purchase is in flight
        vm.Dispose();
        Assert.True(vm.IsDisposed);

        // Abort store purchase due to disposal
        delayedStore.ReleasePurchaseWithException(new ObjectDisposedException("IGameplayStore"));
        var result = await purchaseTask;

        Assert.Equal(UpgradePurchaseStatus.Disposed, result.Status);
        Assert.Equal(0, postDisposalCallbacks);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_StoreTransactionCommittedBeforeDisposal_ReturnsSuccessWithoutPostDisposalCallbacks()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 500L, 5, 4, 0, 4, 0);

        UpgradeTerminalViewModel? vmRef = null;
        var interceptingStore = new ActionAfterPurchaseStore(realStore, () =>
        {
            // Store transaction has committed in SQLite; dispose VM right before it processes the result
            vmRef?.Dispose();
        });

        using var vm = new UpgradeTerminalViewModel(interceptingStore);
        vmRef = vm;
        await vm.InitializeAsync();

        int postDisposalCallbacks = 0;
        vm.StatusChanged += _ => { if (vm.IsDisposed) postDisposalCallbacks++; };
        vm.StateChanged += () => { if (vm.IsDisposed) postDisposalCallbacks++; };

        var result = await vm.PurchaseUpgradeAsync();

        // Must represent committed transaction as success, not rolled back
        Assert.True(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result.Status);
        Assert.NotNull(result.UpdatedStatus);
        Assert.Equal(1, result.UpdatedStatus.CurrentAttackRank);

        // Store verification: SQLite transaction committed
        var storeProg = await realStore.GetProgressionAsync();
        Assert.Equal(1, storeProg.AttackRank);
        Assert.Equal(3, storeProg.AvailableSkillPoints);

        // No callbacks after disposal
        Assert.Equal(0, postDisposalCallbacks);
    }

    [Fact]
    public async Task Disposal_SuppressesAllCallbacksAndMutations()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        await SetProgressionAsync(store.StoragePath, 500L, 5, 4, 0, 4, 0);

        var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        int callbackCount = 0;
        vm.StatusChanged += _ => callbackCount++;
        vm.StateChanged += () => callbackCount++;

        vm.Dispose();

        // Attempts to refresh, purchase, or handle store events must be suppressed
        await vm.RefreshAsync();
        var purchaseResult = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Disposed, purchaseResult.Status);

        // Direct store purchase should not trigger VM events
        await store.PurchaseAttackUpgradeAsync(0, 0);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_RapidDoubleClickAndSequentialPurchases_ExecutesCleanly()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        // 5 SP available (enough for rank 1 cost 1 and rank 2 cost 2 = 3 SP total)
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        // Click 1 starts and pauses in store
        var click1 = vm.PurchaseUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Click 2 arrives concurrently -> rejected as Busy
        var click2 = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Busy, click2.Status);

        // Release click 1 -> succeeds
        delayedStore.ReleasePurchase();
        var res1 = await click1;
        Assert.Equal(UpgradePurchaseStatus.Success, res1.Status);
        Assert.Equal(1, vm.CurrentAttackRank);

        // Click 3 initiates sequentially after gate release -> succeeds
        var click3 = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, click3.Status);
        Assert.Equal(2, vm.CurrentAttackRank);
        Assert.Equal(2, vm.AvailableSkillPoints); // 5 - 1 - 2 = 2 SP
    }

    [Fact]
    public async Task PurchaseUpgradeAsync_ExceptionAndCancellationCleanup_ReleasesBusyGate()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();
        await SetProgressionAsync(realStore.StoragePath, 1000L, 6, 5, 0, 5, 0);

        var failingStore = new FailingUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(failingStore);
        await vm.InitializeAsync();

        bool gateReleasedAfterException = false;
        vm.StateChanged += () =>
        {
            if (!vm.IsBusy) gateReleasedAfterException = true;
        };

        var errResult = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Error, errResult.Status);
        Assert.False(vm.IsBusy);
        Assert.True(gateReleasedAfterException);

        // Next attempt with cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelResult = await vm.PurchaseUpgradeAsync(cts.Token);
        Assert.Equal(UpgradePurchaseStatus.Cancelled, cancelResult.Status);
        Assert.False(vm.IsBusy);
    }

    // =========================================================================
    // 16. Slice 6A: Firewall Contracts and LockedLevel RED Demonstrations
    // =========================================================================

    [Fact]
    public void UpgradePurchaseStatus_DefinesLockedLevel()
    {
        Assert.True(Enum.TryParse<UpgradePurchaseStatus>("LockedLevel", out _), "UpgradePurchaseStatus must define LockedLevel.");
    }

    [Fact]
    public void UpgradePurchaseResult_LockedLevelFactory_AvailableAndValid()
    {
        var factory = typeof(UpgradePurchaseResult).GetMethod("LockedLevel", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(factory);
    }

    [Fact]
    public void UpgradeTerminalStatus_ExposesFirewallProperties()
    {
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("CurrentFirewallRank"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("ActivatedFirewallRank"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("NextFirewallRank"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("NextRequiredLevel"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("NextFirewallCost"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("CurrentPermanentCapacity"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("NextPermanentCapacity"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("IsFirewallLevelUnlocked"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("HasSufficientFirewallSkillPoints"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("IsFirewallMaxRank"));
        Assert.NotNull(typeof(UpgradeTerminalStatus).GetProperty("CanPurchaseFirewall"));
    }

    [Fact]
    public void UpgradeTerminalViewModel_ExposesFirewallPurchaseCommand()
    {
        var method = typeof(UpgradeTerminalViewModel).GetMethod("PurchaseFirewallUpgradeAsync");
        Assert.NotNull(method);
    }

    // =========================================================================
    // 17. Slice 6A: Firewall Status Projections and Policy Alignment
    // =========================================================================

    [Theory]
    [InlineData(1, 0, 0, false, false, false, 5, 3, 0, 1)]     // Level 1 / Rank 0: locked, cannot afford
    [InlineData(5, 3, 0, true, true, true, 5, 3, 0, 1)]        // Level 5 / Rank 0 with 3 SP: purchasable
    [InlineData(5, 2, 0, true, false, false, 5, 3, 0, 1)]       // Level 5 / Rank 0 without 3 SP: insufficient
    [InlineData(19, 10, 1, false, true, false, 20, 8, 1, 2)]    // Level 19 / Rank 1: locked, cannot purchase
    [InlineData(20, 8, 1, true, true, true, 20, 8, 1, 2)]       // Level 20 / Rank 1 with 8 SP: purchasable
    [InlineData(49, 20, 2, false, true, false, 50, 15, 2, 3)]   // Level 49 / Rank 2: locked, cannot purchase
    [InlineData(50, 15, 2, true, true, true, 50, 15, 2, 3)]     // Level 50 / Rank 2 with 15 SP: purchasable
    public async Task UpgradeTerminalViewModel_FirewallStatusProjections_MatchPolicy(
        int playerLevel,
        int availableSp,
        int firewallRank,
        bool expectedLevelUnlocked,
        bool expectedHasSufficientSp,
        bool expectedCanPurchase,
        int expectedNextReqLevel,
        int expectedNextCost,
        int expectedCurrentCapacity,
        int expectedNextCapacity)
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long cumulativeXp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel);
        int earnedSp = Math.Max(availableSp, playerLevel - 1);
        int spentSp = earnedSp - availableSp;

        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: cumulativeXp,
            playerLevel: playerLevel,
            earnedSp: earnedSp,
            spentSp: spentSp,
            availableSp: availableSp,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: firewallRank,
            activatedFirewallRank: firewallRank);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(firewallRank, vm.CurrentFirewallRank);
        Assert.Equal(firewallRank, vm.ActivatedFirewallRank);
        Assert.Equal(expectedNextReqLevel, vm.NextRequiredLevel);
        Assert.Equal(expectedNextCost, vm.NextFirewallCost);
        Assert.Equal(expectedCurrentCapacity, vm.CurrentPermanentCapacity);
        Assert.Equal(expectedNextCapacity, vm.NextPermanentCapacity);
        Assert.Equal(expectedLevelUnlocked, vm.IsFirewallLevelUnlocked);
        Assert.Equal(expectedHasSufficientSp, vm.HasSufficientFirewallSkillPoints);
        Assert.False(vm.IsFirewallMaxRank);
        Assert.Equal(expectedCanPurchase, vm.CanPurchaseFirewall);
    }

    [Fact]
    public async Task UpgradeTerminalViewModel_FirewallStatus_Rank3_MaxRankReached()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(50);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 50,
            earnedSp: 49,
            spentSp: 26,
            availableSp: 23,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 3,
            activatedFirewallRank: 3);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(3, vm.CurrentFirewallRank);
        Assert.Equal(3, vm.ActivatedFirewallRank);
        Assert.Null(vm.NextFirewallRank);
        Assert.Null(vm.NextFirewallCost);
        Assert.Equal(3, vm.CurrentPermanentCapacity);
        Assert.Null(vm.NextPermanentCapacity);
        Assert.True(vm.IsFirewallMaxRank);
        Assert.False(vm.CanPurchaseFirewall);
    }

    [Fact]
    public async Task UpgradeTerminalViewModel_BeginnerAssistanceShield_DoesNotCountAsPurchasedRank()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        using (var connection = new SqliteConnection($"Data Source={store.StoragePath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET is_beginner_assistance_active = 1,
                    firewall_rank = 0,
                    activated_firewall_rank = 0
                WHERE id = 1;

                UPDATE gameplay_run_state
                SET shield_capacity = 1,
                    active_shields = 1
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        Assert.Equal(0, vm.CurrentFirewallRank);
        Assert.Equal(0, vm.CurrentPermanentCapacity);
        Assert.Equal(1, vm.NextFirewallRank);
        Assert.Equal(1, vm.NextPermanentCapacity);
        Assert.False(vm.IsFirewallMaxRank);
    }

    // =========================================================================
    // 18. Slice 6A: Result Contracts and Enum Preservation
    // =========================================================================

    [Fact]
    public void UpgradePurchaseStatus_PreservesExistingValuesAndAppendsLockedLevel()
    {
        Assert.Equal(0, (int)UpgradePurchaseStatus.Success);
        Assert.Equal(1, (int)UpgradePurchaseStatus.InsufficientSkillPoints);
        Assert.Equal(2, (int)UpgradePurchaseStatus.StaleRank);
        Assert.Equal(3, (int)UpgradePurchaseStatus.StaleResetEpoch);
        Assert.Equal(4, (int)UpgradePurchaseStatus.MaxRankReached);
        Assert.Equal(5, (int)UpgradePurchaseStatus.Busy);
        Assert.Equal(6, (int)UpgradePurchaseStatus.Disposed);
        Assert.Equal(7, (int)UpgradePurchaseStatus.Cancelled);
        Assert.Equal(8, (int)UpgradePurchaseStatus.Error);
        Assert.Equal(9, (int)UpgradePurchaseStatus.LockedLevel);
    }

    [Fact]
    public void UpgradePurchaseResult_LockedLevel_ReturnsExpectedContract()
    {
        var status = UpgradeTerminalStatus.Initial();
        var result = UpgradePurchaseResult.LockedLevel(status, "Player level insufficient.");

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.LockedLevel, result.Status);
        Assert.Same(status, result.StatusSnapshot);
        Assert.Equal("Player level insufficient.", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void UpgradePurchaseResult_ExistingAttackFactories_RemainUnchanged()
    {
        var status = UpgradeTerminalStatus.Initial();
        var success = UpgradePurchaseResult.Success(status);
        Assert.True(success.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, success.Status);

        var insufficient = UpgradePurchaseResult.InsufficientSkillPoints(status);
        Assert.False(insufficient.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.InsufficientSkillPoints, insufficient.Status);

        var maxRank = UpgradePurchaseResult.MaxRankReached(status);
        Assert.False(maxRank.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.MaxRankReached, maxRank.Status);

        var staleRank = UpgradePurchaseResult.StaleRank(status);
        Assert.False(staleRank.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.StaleRank, staleRank.Status);

        var staleEpoch = UpgradePurchaseResult.StaleResetEpoch(status);
        Assert.False(staleEpoch.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.StaleResetEpoch, staleEpoch.Status);

        var disposed = UpgradePurchaseResult.Disposed(status);
        Assert.False(disposed.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Disposed, disposed.Status);

        var busy = UpgradePurchaseResult.Busy(status);
        Assert.False(busy.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Busy, busy.Status);

        var cancelled = UpgradePurchaseResult.Cancelled(status);
        Assert.False(cancelled.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Cancelled, cancelled.Status);

        var ex = new InvalidOperationException("boom");
        var error = UpgradePurchaseResult.Error("boom", ex, status);
        Assert.False(error.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Error, error.Status);
        Assert.Same(ex, error.Exception);
    }

    // =========================================================================
    // 19. Slice 6A: Firewall Purchases and Deductions
    // =========================================================================

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank1_Deducts3SpAndUpdatesState()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 5,
            earnedSp: 5,
            spentSp: 0,
            availableSp: 5,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 0,
            activatedFirewallRank: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result.Status);
        Assert.Equal(1, vm.CurrentFirewallRank);
        Assert.Equal(1, vm.CurrentPermanentCapacity);
        Assert.Equal(2, vm.AvailableSkillPoints);
        Assert.Equal(2, vm.NextFirewallRank);
        Assert.Equal(20, vm.NextRequiredLevel);
        Assert.Equal(8, vm.NextFirewallCost);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank2_Deducts8SpAndUpdatesState()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(20);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 20,
            earnedSp: 19,
            spentSp: 3,
            availableSp: 16,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 1,
            activatedFirewallRank: 1);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result.Status);
        Assert.Equal(2, vm.CurrentFirewallRank);
        Assert.Equal(2, vm.CurrentPermanentCapacity);
        Assert.Equal(8, vm.AvailableSkillPoints);
        Assert.Equal(3, vm.NextFirewallRank);
        Assert.Equal(50, vm.NextRequiredLevel);
        Assert.Equal(15, vm.NextFirewallCost);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank3_Deducts15SpAndReachesMax()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(50);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 50,
            earnedSp: 49,
            spentSp: 11,
            availableSp: 38,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 2,
            activatedFirewallRank: 2);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Success, result.Status);
        Assert.Equal(3, vm.CurrentFirewallRank);
        Assert.Equal(3, vm.CurrentPermanentCapacity);
        Assert.Equal(23, vm.AvailableSkillPoints);
        Assert.True(vm.IsFirewallMaxRank);
        Assert.False(vm.CanPurchaseFirewall);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_LevelLocked_ReturnsLockedLevel()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: 0,
            playerLevel: 1,
            earnedSp: 10,
            spentSp: 0,
            availableSp: 10,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 0,
            activatedFirewallRank: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.LockedLevel, result.Status);
        Assert.Equal(0, vm.CurrentFirewallRank);
        Assert.Equal(10, vm.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_InsufficientSkillPoints_ReturnsInsufficientSkillPoints()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 5,
            earnedSp: 4,
            spentSp: 2,
            availableSp: 2,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 0,
            activatedFirewallRank: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.InsufficientSkillPoints, result.Status);
        Assert.Equal(0, vm.CurrentFirewallRank);
        Assert.Equal(2, vm.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_MaxRankReached_ReturnsMaxRankReached()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(50);
        await SetProgressionAsync(
            store.StoragePath,
            cumulativeXp: xp,
            playerLevel: 50,
            earnedSp: 49,
            spentSp: 26,
            availableSp: 23,
            attackRank: 0,
            resetEpoch: 0,
            firewallRank: 3,
            activatedFirewallRank: 3);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.MaxRankReached, result.Status);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Disposed_ReturnsDisposed()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        vm.Dispose();

        var result = await vm.PurchaseFirewallUpgradeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Disposed, result.Status);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Cancelled_ReturnsCancelledAndReleasesGate()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(store.StoragePath, xp, 5, 5, 0, 5, 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await vm.PurchaseFirewallUpgradeAsync(cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpgradePurchaseStatus.Cancelled, result.Status);
        Assert.False(vm.IsBusy);
    }

    // =========================================================================
    // 20. Slice 6A: Shared Concurrency Gate and Cross-Skill Collisions
    // =========================================================================

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_RapidDoubleClick_ReturnsBusy()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(realStore.StoragePath, xp, 5, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        var click1 = vm.PurchaseFirewallUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var click2 = await vm.PurchaseFirewallUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Busy, click2.Status);

        delayedStore.ReleasePurchase();
        var res1 = await click1;
        Assert.Equal(UpgradePurchaseStatus.Success, res1.Status);
        Assert.Equal(1, vm.CurrentFirewallRank);
    }

    [Fact]
    public async Task Purchase_CrossSkillCollision_AttackWhileFirewallBusy_ReturnsBusy()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(realStore.StoragePath, xp, 5, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        var firewallClick = vm.PurchaseFirewallUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var attackClick = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Busy, attackClick.Status);

        delayedStore.ReleasePurchase();
        var firewallRes = await firewallClick;
        Assert.Equal(UpgradePurchaseStatus.Success, firewallRes.Status);
        Assert.Equal(1, vm.CurrentFirewallRank);
    }

    [Fact]
    public async Task Purchase_CrossSkillCollision_FirewallWhileAttackBusy_ReturnsBusy()
    {
        using var realStore = CreateStore();
        await realStore.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(realStore.StoragePath, xp, 5, 5, 0, 5, 0);

        var delayedStore = new ControllableDelayedUpgradeStore(realStore);
        using var vm = new UpgradeTerminalViewModel(delayedStore);
        await vm.InitializeAsync();

        var attackClick = vm.PurchaseUpgradeAsync();
        await delayedStore.PurchaseEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firewallClick = await vm.PurchaseFirewallUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Busy, firewallClick.Status);

        delayedStore.ReleasePurchase();
        var attackRes = await attackClick;
        Assert.Equal(UpgradePurchaseStatus.Success, attackRes.Status);
        Assert.Equal(1, vm.CurrentAttackRank);
    }

    // =========================================================================
    // 21. Slice 6A: Reactive State Reconciliation in Both Event Delivery Orders
    // =========================================================================

    [Fact]
    public async Task ReactiveSnapshots_DeliveryOrderAttackThenFirewall_PreservesBothSkills()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(store.StoragePath, xp, 5, 5, 0, 5, 0, firewallRank: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        await store.PurchaseAttackUpgradeAsync(0, 0);
        await store.PurchaseFirewallUpgradeAsync(0, 0);

        await Task.Delay(50);
        await vm.RefreshAsync();

        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(1, vm.CurrentFirewallRank);
        Assert.Equal(1, vm.AvailableSkillPoints);
    }

    [Fact]
    public async Task ReactiveSnapshots_DeliveryOrderFirewallThenAttack_PreservesBothSkills()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        long xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5);
        await SetProgressionAsync(store.StoragePath, xp, 5, 5, 0, 5, 0, firewallRank: 0);

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        await store.PurchaseFirewallUpgradeAsync(0, 0);
        await store.PurchaseAttackUpgradeAsync(0, 0);

        await Task.Delay(50);
        await vm.RefreshAsync();

        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(1, vm.CurrentFirewallRank);
        Assert.Equal(1, vm.AvailableSkillPoints);
    }
}
