namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreFirewallPurchaseTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreFirewallPurchaseTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_FirewallPurchase_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath(string name = "gameplay_firewall_purchase_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static async Task SetProgressionAsync(
        SqliteGameplayStore store,
        int playerLevel,
        int earnedSp,
        int spentSp,
        int availableSp,
        int attackRank = 0,
        int firewallRank = 0,
        int isAssistanceActive = 0,
        long cumulativeXp = 0,
        long storeRevision = 1,
        long resetEpoch = 0)
    {
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET player_level = @lvl,
                    earned_sp = @earned,
                    spent_sp = @spent,
                    available_sp = @available,
                    attack_rank = @atk,
                    firewall_rank = @fw,
                    is_beginner_assistance_active = @assist,
                    cumulative_xp = @xp,
                    store_revision = @rev,
                    reset_epoch = @epoch,
                    updated_at = @now
                WHERE id = 1;
            ";
            cmd.Parameters.AddWithValue("@lvl", playerLevel);
            cmd.Parameters.AddWithValue("@earned", earnedSp);
            cmd.Parameters.AddWithValue("@spent", spentSp);
            cmd.Parameters.AddWithValue("@available", availableSp);
            cmd.Parameters.AddWithValue("@atk", attackRank);
            cmd.Parameters.AddWithValue("@fw", firewallRank);
            cmd.Parameters.AddWithValue("@assist", isAssistanceActive);
            cmd.Parameters.AddWithValue("@xp", cumulativeXp);
            cmd.Parameters.AddWithValue("@rev", storeRevision);
            cmd.Parameters.AddWithValue("@epoch", resetEpoch);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        });
    }

    private static async Task SetPendingResetAsync(SqliteGameplayStore store)
    {
        await store.BeginOrGetResetIntentAsync();
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank0To1_AtLevel5_Costs3Sp_Succeeds()
    {
        var dbPath = GetTempDbPath("rank0_to_1");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 5, 5 earned SP, 0 spent, 5 available
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        var progression = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        Assert.Equal(1, progression.FirewallRank);
        Assert.Equal(3, progression.SpentSkillPoints);
        Assert.Equal(2, progression.AvailableSkillPoints);
        Assert.Equal(5, progression.EarnedSkillPoints);
        Assert.Equal(2, progression.StoreRevision); // Incremented from 1 to 2
        Assert.Equal(5, progression.PlayerLevel);

        // Verify persisted state matches
        var loaded = await store.GetProgressionAsync();
        Assert.Equal(1, loaded.FirewallRank);
        Assert.Equal(3, loaded.SpentSkillPoints);
        Assert.Equal(2, loaded.AvailableSkillPoints);
        Assert.Equal(5, loaded.EarnedSkillPoints);
        Assert.Equal(2, loaded.StoreRevision);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank1To2_AtLevel20_Costs8Sp_Succeeds()
    {
        var dbPath = GetTempDbPath("rank1_to_2");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 20, 15 earned SP, 3 spent (from rank 1), 12 available, rank 1
        await SetProgressionAsync(store, playerLevel: 20, earnedSp: 15, spentSp: 3, availableSp: 12, firewallRank: 1, storeRevision: 5);

        var progression = await store.PurchaseFirewallUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);

        Assert.Equal(2, progression.FirewallRank);
        Assert.Equal(11, progression.SpentSkillPoints); // 3 + 8 = 11
        Assert.Equal(4, progression.AvailableSkillPoints); // 12 - 8 = 4
        Assert.Equal(15, progression.EarnedSkillPoints);
        Assert.Equal(6, progression.StoreRevision);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(2, loaded.FirewallRank);
        Assert.Equal(11, loaded.SpentSkillPoints);
        Assert.Equal(4, loaded.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank2To3_AtLevel50_Costs15Sp_Succeeds()
    {
        var dbPath = GetTempDbPath("rank2_to_3");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 50, 30 earned SP, 11 spent, 19 available, rank 2
        await SetProgressionAsync(store, playerLevel: 50, earnedSp: 30, spentSp: 11, availableSp: 19, firewallRank: 2, storeRevision: 10);

        var progression = await store.PurchaseFirewallUpgradeAsync(expectedRank: 2, expectedResetEpoch: 0);

        Assert.Equal(3, progression.FirewallRank);
        Assert.Equal(26, progression.SpentSkillPoints); // 11 + 15 = 26
        Assert.Equal(4, progression.AvailableSkillPoints); // 19 - 15 = 4
        Assert.Equal(30, progression.EarnedSkillPoints);
        Assert.Equal(11, progression.StoreRevision);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(3, loaded.FirewallRank);
        Assert.Equal(26, loaded.SpentSkillPoints);
        Assert.Equal(4, loaded.AvailableSkillPoints);
    }

    [Theory]
    [InlineData(4, 0)]  // Level 4 trying to buy rank 1 (unlock 5)
    [InlineData(19, 1)] // Level 19 trying to buy rank 2 (unlock 20)
    [InlineData(49, 2)] // Level 49 trying to buy rank 3 (unlock 50)
    public async Task PurchaseFirewallUpgradeAsync_LevelBelowGate_RejectedWithoutMutation(
        int playerLevel,
        int currentRank)
    {
        var dbPath = GetTempDbPath($"gate_reject_lvl{playerLevel}");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        int available = 50;
        await SetProgressionAsync(store, playerLevel: playerLevel, earnedSp: 50, spentSp: 0, availableSp: available, firewallRank: currentRank);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: currentRank, expectedResetEpoch: 0));
        Assert.Contains("level", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify progression is completely unmutated
        var loaded = await store.GetProgressionAsync();
        Assert.Equal(currentRank, loaded.FirewallRank);
        Assert.Equal(available, loaded.AvailableSkillPoints);
        Assert.Equal(0, loaded.SpentSkillPoints);
        Assert.Equal(50, loaded.EarnedSkillPoints);
    }

    [Theory]
    [InlineData(5, 0, 2)]   // Rank 1 costs 3 SP, player has only 2 SP
    [InlineData(20, 1, 7)]  // Rank 2 costs 8 SP, player has only 7 SP
    [InlineData(50, 2, 14)] // Rank 3 costs 15 SP, player has only 14 SP
    public async Task PurchaseFirewallUpgradeAsync_InsufficientSp_RejectedWithoutMutation(
        int playerLevel,
        int currentRank,
        int availableSp)
    {
        var dbPath = GetTempDbPath($"sp_reject_rank{currentRank}");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: playerLevel, earnedSp: availableSp, spentSp: 0, availableSp: availableSp, firewallRank: currentRank);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: currentRank, expectedResetEpoch: 0));
        Assert.Contains("skill points", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(currentRank, loaded.FirewallRank);
        Assert.Equal(availableSp, loaded.AvailableSkillPoints);
        Assert.Equal(0, loaded.SpentSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_ExactlyZeroRemainingSp_Accepted()
    {
        var dbPath = GetTempDbPath("exact_zero_sp");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Exactly 3 SP available for rank 1 purchase
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 3, spentSp: 0, availableSp: 3, firewallRank: 0);

        var result = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        Assert.Equal(1, result.FirewallRank);
        Assert.Equal(0, result.AvailableSkillPoints);
        Assert.Equal(3, result.SpentSkillPoints);
        Assert.Equal(3, result.EarnedSkillPoints);
        Assert.Equal(result.EarnedSkillPoints, result.SpentSkillPoints + result.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_NegativeArguments_ThrowsArgumentOutOfRangeException()
    {
        var dbPath = GetTempDbPath("neg_args");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: -1, expectedResetEpoch: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: -1));
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank3Maximum_Rejected()
    {
        var dbPath = GetTempDbPath("max_rank_reject");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 60, earnedSp: 50, spentSp: 26, availableSp: 24, firewallRank: 3);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: 3, expectedResetEpoch: 0));
        Assert.Contains("maximum", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(3, loaded.FirewallRank);
        Assert.Equal(24, loaded.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_StaleRank_Rejected()
    {
        var dbPath = GetTempDbPath("stale_rank");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Current rank is 1, but caller claims expectedRank is 0
        await SetProgressionAsync(store, playerLevel: 20, earnedSp: 20, spentSp: 3, availableSp: 17, firewallRank: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));
        Assert.Contains("rank", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(1, loaded.FirewallRank);
        Assert.Equal(17, loaded.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_StaleResetEpoch_Rejected()
    {
        var dbPath = GetTempDbPath("stale_epoch");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0, resetEpoch: 0);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 99));
        Assert.Contains("epoch", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(0, loaded.FirewallRank);
        Assert.Equal(5, loaded.AvailableSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_PendingReset_Rejected()
    {
        var dbPath = GetTempDbPath("pending_reset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);
        await SetPendingResetAsync(store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));
        Assert.Contains("reset", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(0, loaded.FirewallRank);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_ConcurrentPurchases_OnlyOneSucceeds()
    {
        var dbPath = GetTempDbPath("concurrency_firewall");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 5, 6 SP available (enough for exactly one rank 1 purchase of 3 SP)
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 6, spentSp: 0, availableSp: 6, firewallRank: 0);

        // Fire two concurrent purchase calls with expectedRank 0
        var task1 = store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        var task2 = store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Error: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Error: t.Exception?.InnerException)));

        int successCount = 0;
        int failureCount = 0;
        foreach (var r in results)
        {
            if (r.Success) successCount++;
            else failureCount++;
        }

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);

        var loaded = await store.GetProgressionAsync();
        Assert.Equal(1, loaded.FirewallRank);
        Assert.Equal(3, loaded.SpentSkillPoints);
        Assert.Equal(3, loaded.AvailableSkillPoints);
        Assert.Equal(6, loaded.EarnedSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_SpConservation_StrictlyMaintained()
    {
        var dbPath = GetTempDbPath("sp_conservation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 50, earned 30 SP, spent 0, available 30
        await SetProgressionAsync(store, playerLevel: 50, earnedSp: 30, spentSp: 0, availableSp: 30, firewallRank: 0);

        var r1 = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(r1.EarnedSkillPoints, checked(r1.SpentSkillPoints + r1.AvailableSkillPoints));

        var r2 = await store.PurchaseFirewallUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);
        Assert.Equal(r2.EarnedSkillPoints, checked(r2.SpentSkillPoints + r2.AvailableSkillPoints));

        var r3 = await store.PurchaseFirewallUpgradeAsync(expectedRank: 2, expectedResetEpoch: 0);
        Assert.Equal(r3.EarnedSkillPoints, checked(r3.SpentSkillPoints + r3.AvailableSkillPoints));

        Assert.Equal(3, r3.FirewallRank);
        Assert.Equal(26, r3.SpentSkillPoints); // 3 + 8 + 15
        Assert.Equal(4, r3.AvailableSkillPoints); // 30 - 26
        Assert.Equal(30, r3.EarnedSkillPoints);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_StoreRevision_IncrementsMonotonically()
    {
        var dbPath = GetTempDbPath("store_revision_increment");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 50, earnedSp: 30, spentSp: 0, availableSp: 30, firewallRank: 0, storeRevision: 42);

        var r1 = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(43, r1.StoreRevision);

        var r2 = await store.PurchaseFirewallUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);
        Assert.Equal(44, r2.StoreRevision);
    }

    [Fact]
    public async Task ColdRestart_PreservesPurchasedFirewallRankAndAccounting()
    {
        var dbPath = GetTempDbPath("cold_restart_firewall");

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            await SetProgressionAsync(store1, playerLevel: 25, earnedSp: 20, spentSp: 0, availableSp: 20, attackRank: 3, firewallRank: 0, cumulativeXp: 1200);

            var bought1 = await store1.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
            Assert.Equal(1, bought1.FirewallRank);

            var bought2 = await store1.PurchaseFirewallUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);
            Assert.Equal(2, bought2.FirewallRank);
            await store1.CloseAsync();
        }

        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var prog = await store2.GetProgressionAsync();

            Assert.Equal(2, prog.FirewallRank);
            Assert.Equal(11, prog.SpentSkillPoints); // 3 + 8
            Assert.Equal(9, prog.AvailableSkillPoints);  // 20 - 11
            Assert.Equal(20, prog.EarnedSkillPoints);
            Assert.Equal(3, prog.AttackRank);
            Assert.Equal(25, prog.PlayerLevel);
            Assert.Equal(1200, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_DirectDatabasePersistence_MatchesProgressionRecord()
    {
        var dbPath = GetTempDbPath("db_persistence");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 10, spentSp: 0, availableSp: 10, firewallRank: 0);
        var record = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT firewall_rank, spent_sp, available_sp, store_revision FROM gameplay_progression WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(record.FirewallRank, reader.GetInt32(0));
        Assert.Equal(record.SpentSkillPoints, reader.GetInt32(1));
        Assert.Equal(record.AvailableSkillPoints, reader.GetInt32(2));
        Assert.Equal(record.StoreRevision, reader.GetInt64(3));
    }

    [Fact]
    public async Task Snapshot_CorrectlyReflectsEligibilityCostsAndUnlockState()
    {
        var dbPath = GetTempDbPath("snapshot_correctness");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 1, 0 SP, rank 0: level locked, insufficient SP
        await SetProgressionAsync(store, playerLevel: 1, earnedSp: 0, spentSp: 0, availableSp: 0, firewallRank: 0);
        var s1 = await store.GetFirewallUpgradeSnapshotAsync();
        Assert.Equal(0, s1.CurrentFirewallRank);
        Assert.Equal(1, s1.NextFirewallRank);
        Assert.Equal(5, s1.RequiredPlayerLevel);
        Assert.Equal(3, s1.NextRankCost);
        Assert.False(s1.IsLevelUnlocked);
        Assert.False(s1.HasSufficientSkillPoints);
        Assert.False(s1.CanPurchaseNextRank);

        // Level 5, 2 SP, rank 0: level unlocked, insufficient SP
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 2, spentSp: 0, availableSp: 2, firewallRank: 0);
        var s2 = await store.GetFirewallUpgradeSnapshotAsync();
        Assert.True(s2.IsLevelUnlocked);
        Assert.False(s2.HasSufficientSkillPoints);
        Assert.False(s2.CanPurchaseNextRank);

        // Level 5, 3 SP, rank 0: level unlocked, sufficient SP -> can purchase!
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 3, spentSp: 0, availableSp: 3, firewallRank: 0);
        var s3 = await store.GetFirewallUpgradeSnapshotAsync();
        Assert.True(s3.IsLevelUnlocked);
        Assert.True(s3.HasSufficientSkillPoints);
        Assert.True(s3.CanPurchaseNextRank);

        // Rank 3 (Max): no next rank, cannot purchase
        await SetProgressionAsync(store, playerLevel: 60, earnedSp: 50, spentSp: 26, availableSp: 24, firewallRank: 3);
        var s4 = await store.GetFirewallUpgradeSnapshotAsync();
        Assert.Equal(3, s4.CurrentFirewallRank);
        Assert.Null(s4.NextFirewallRank);
        Assert.Null(s4.RequiredPlayerLevel);
        Assert.Null(s4.NextRankCost);
        Assert.False(s4.CanPurchaseNextRank);
    }

    [Fact]
    public async Task ExistingAttackRank_AndAttackPurchases_PreservedAcrossFirewallPurchases()
    {
        var dbPath = GetTempDbPath("attack_firewall_interop");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Level 10, earned 20 SP, attack rank 2 (cost 1+2=3 SP spent), available 17 SP, firewall rank 0
        await SetProgressionAsync(store, playerLevel: 10, earnedSp: 20, spentSp: 3, availableSp: 17, attackRank: 2, firewallRank: 0);

        // Purchase Firewall Rank 1 (cost 3 SP)
        var fwProg = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, fwProg.FirewallRank);
        Assert.Equal(2, fwProg.AttackRank); // Preserved!
        Assert.Equal(6, fwProg.SpentSkillPoints); // 3 (attack) + 3 (firewall)
        Assert.Equal(14, fwProg.AvailableSkillPoints);

        // Purchase Attack Rank 3 (cost 2 SP under log2 policy: 1 + Log2(3) = 2)
        var atkProg = await store.PurchaseAttackUpgradeAsync(expectedRank: 2, expectedResetEpoch: 0);
        Assert.Equal(3, atkProg.AttackRank);
        Assert.Equal(1, atkProg.FirewallRank); // Preserved!
        Assert.Equal(8, atkProg.SpentSkillPoints); // 6 + 2
        Assert.Equal(12, atkProg.AvailableSkillPoints); // 14 - 2
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_DoesNotModifyActiveRunState()
    {
        var dbPath = GetTempDbPath("no_run_state_mod");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = await store.GetRunStateAsync();
        Assert.Equal(0, initialRun.ShieldCapacity);
        Assert.Equal(0, initialRun.ActiveShields);
        Assert.Equal(0, initialRun.ShieldRechargeUnits);

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5, firewallRank: 0);

        // Purchase Firewall Rank 1
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Authoritative run state must remain completely unchanged in Slice 4
        var postPurchaseRun = await store.GetRunStateAsync();
        Assert.Equal(0, postPurchaseRun.ShieldCapacity);
        Assert.Equal(0, postPurchaseRun.ActiveShields);
        Assert.Equal(0, postPurchaseRun.ShieldRechargeUnits);
        Assert.Equal(initialRun.PlayerCurrentHp, postPurchaseRun.PlayerCurrentHp);
        Assert.Equal(initialRun.CurrentOpponent.CurrentHp, postPurchaseRun.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task PurchaseFirewallUpgradeAsync_Rank1Purchase_DeactivatesBeginnerAssistanceFlag()
    {
        var dbPath = GetTempDbPath("beginner_assist_deactivation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Setup with is_beginner_assistance_active = 1
        await SetProgressionAsync(
            store,
            playerLevel: 5,
            earnedSp: 5,
            spentSp: 0,
            availableSp: 5,
            firewallRank: 0,
            isAssistanceActive: 1);

        // Purchase Firewall Rank 1
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Verify in database that is_beginner_assistance_active was reset to 0
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_beginner_assistance_active FROM gameplay_progression WHERE id = 1;";
        var flag = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, flag);
    }
}
