namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-009 Slice 4B: Atomic offline achievement unlocking tests.
/// Tests authoritative evaluation, transactional atomicity, idempotency, reset fencing,
/// and crash rollback for Sector 10, Boss Slayer, and First Upgrade using real temporary SQLite databases.
/// </summary>
public sealed class SqliteGameplayStoreAchievementUnlockTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreAchievementUnlockTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_Unlock_" + Guid.NewGuid().ToString("N"));
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
            // Best effort temp cleanup
        }
    }

    private string GetTempDbPath(string name) =>
        Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");

    private async Task<string> CreateFreshStoreDbAsync(string name)
    {
        var dbPath = GetTempDbPath(name);
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }
        return dbPath;
    }

    private static async Task ExecuteSqlAsync(string dbPath, string sql, Action<SqliteCommand>? configureParams = null)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        configureParams?.Invoke(cmd);
        await cmd.ExecuteNonQueryAsync();
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        string factId = "2+2",
        bool isCorrect = true,
        bool isEligible = true,
        int latencyMs = 500,
        long epoch = 0,
        int effectiveDamage = 1,
        int boundCriticalRank = 0,
        int boundOverdriveRank = 0)
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: latencyMs,
            resetEpoch: epoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage,
            boundCriticalStrikeRank: boundCriticalRank,
            boundOverdriveRank: boundOverdriveRank);
    }

    private static async Task SetProgressionSpAndLevelAsync(
        string dbPath,
        int level = 10,
        long cumulativeXp = 1000,
        int earnedSp = 10,
        int spentSp = 0,
        int availableSp = 10)
    {
        await ExecuteSqlAsync(dbPath, @"
            UPDATE gameplay_progression
            SET cumulative_xp = @xp,
                player_level = @lvl,
                earned_sp = @earned,
                spent_sp = @spent,
                available_sp = @avail
            WHERE id = 1;", cmd =>
        {
            cmd.Parameters.AddWithValue("@xp", cumulativeXp);
            cmd.Parameters.AddWithValue("@lvl", level);
            cmd.Parameters.AddWithValue("@earned", earnedSp);
            cmd.Parameters.AddWithValue("@spent", spentSp);
            cmd.Parameters.AddWithValue("@avail", availableSp);
        });
    }

    private static async Task SetSectorAndOpponentAsync(
        string dbPath,
        int sector,
        int opponentIndex,
        int opponentHp,
        int playerHp = 10,
        int executedAttacks = 0)
    {
        await ExecuteSqlAsync(dbPath, @"
            UPDATE gameplay_run_state
            SET sector = @sector,
                opponent_index = @oppIdx,
                opponent_current_hp = @oppHp,
                player_current_hp = @playerHp,
                opponent_executed_attacks = @attacks,
                opponent_encounter_provenance = 'Authoritative'
            WHERE id = 1;", cmd =>
        {
            cmd.Parameters.AddWithValue("@sector", sector);
            cmd.Parameters.AddWithValue("@oppIdx", opponentIndex);
            cmd.Parameters.AddWithValue("@oppHp", opponentHp);
            cmd.Parameters.AddWithValue("@playerHp", playerHp);
            cmd.Parameters.AddWithValue("@attacks", executedAttacks);
        });
    }

    // =========================================================================
    // SECTION 1: SECTOR 10 ACHIEVEMENT
    // =========================================================================

    [Fact]
    public async Task Sector10_ConfirmedCrossingFromSector9To10_UnlocksSector10()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_crossing");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(9);

        // Put player in Sector 9 at the boss with 1 HP remaining
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec10-win", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsOpponentDefeated);
        Assert.True(receipt.TransitionResult.IsSectorCompleted);
        Assert.False(receipt.TransitionResult.IsGameOver);
        Assert.Equal(10, receipt.TransitionResult.NextState.Sector);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.Contains(achievements, a => a.AchievementId == CyberDefenseAchievementIds.Sector10);
        Assert.Contains(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task Sector10_Sector9NonCompletingAttempt_DoesNotUnlockSector10()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_noncompleting");
        // Normal opponent 0 in sector 9 (max HP 2)
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: 0, opponentHp: 2);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec9-hit", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.False(receipt.TransitionResult.IsSectorCompleted);
        Assert.Equal(9, receipt.TransitionResult.NextState.Sector);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.DoesNotContain(achievements, a => a.AchievementId == CyberDefenseAchievementIds.Sector10);
    }

    [Fact]
    public async Task Sector10_BossWinInSector1_DoesNotUnlockSector10()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_sec1_boss");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec1-boss-win", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsSectorCompleted);
        Assert.Equal(2, receipt.TransitionResult.NextState.Sector);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.DoesNotContain(achievements, a => a.AchievementId == CyberDefenseAchievementIds.Sector10);
        Assert.Contains(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task Sector10_ExistingHighSectorWithoutCrossing_DoesNotUnlockSector10()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_high_sector_no_cross");
        // State already in Sector 10 (normal opponent 0 max HP 3)
        await SetSectorAndOpponentAsync(dbPath, sector: 10, opponentIndex: 0, opponentHp: 3);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec10-hit", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.DoesNotContain(achievements, a => a.AchievementId == CyberDefenseAchievementIds.Sector10);
    }

    [Fact]
    public async Task Sector10_DuplicateSubmissionId_DoesNotReUnlockOrAlterTimestamp()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_duplicate");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(9);
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec10-dup", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements1 = await reader.GetUnlockedAchievementsAsync();
        var originalUnlock = achievements1.Single(a => a.AchievementId == CyberDefenseAchievementIds.Sector10);

        // Replaying the exact same submission
        var duplicateReceipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(duplicateReceipt);

        var achievements2 = await reader.GetUnlockedAchievementsAsync();
        var secondUnlock = achievements2.Single(a => a.AchievementId == CyberDefenseAchievementIds.Sector10);

        Assert.Equal(originalUnlock.UnlockedAtUtc, secondUnlock.UnlockedAtUtc);
    }

    [Fact]
    public async Task Sector10_StaleEpoch_FailsClosedWithoutUnlocking()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_stale_epoch");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(9);
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Intent with mismatched epoch 99
        var intent = CreateIntent("sub-sec10-stale", epoch: 99, effectiveDamage: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task Sector10_CalmModeSuppression_DoesNotUnlockSector10()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_calm_mode");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(9);
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-sec10-calm", isCorrect: true, isEligible: false, effectiveDamage: 1);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task Sector10_StoreReopen_RetainsUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("sec10_reopen");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(9);
        await SetSectorAndOpponentAsync(dbPath, sector: 9, opponentIndex: bossIndex, opponentHp: 1);

        DateTimeOffset unlockedAt;
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = CreateIntent("sub-sec10-reopen", isCorrect: true, effectiveDamage: 1);
            await store.StagePendingIntentAsync(intent);
            await store.ApplyAttemptTransactionAsync(intent);

            var reader = (ICyberDefenseAchievementReader)store;
            var list = await reader.GetUnlockedAchievementsAsync();
            unlockedAt = list.Single(a => a.AchievementId == CyberDefenseAchievementIds.Sector10).UnlockedAtUtc;
        }

        using (var reopened = new SqliteGameplayStore(dbPath))
        {
            await reopened.InitializeAsync();
            var reader = (ICyberDefenseAchievementReader)reopened;
            var list = await reader.GetUnlockedAchievementsAsync();

            var record = Assert.Single(list, a => a.AchievementId == CyberDefenseAchievementIds.Sector10);
            Assert.Equal(unlockedAt, record.UnlockedAtUtc);
            Assert.Equal(0, record.ResetEpoch);
        }
    }

    // =========================================================================
    // SECTION 2: BOSS SLAYER ACHIEVEMENT
    // =========================================================================

    [Fact]
    public async Task BossSlayer_ConfirmedBossDefeat_UnlocksBossSlayer()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_defeat");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1, executedAttacks: 3);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-boss-defeat", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsOpponentDefeated);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Contains(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task BossSlayer_ZeroEnemyAttacksFastWin_UnlocksBossSlayer()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_zero_attacks");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        // Boss has executed 0 attacks when defeated
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1, executedAttacks: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-boss-fast", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsOpponentDefeated);
        Assert.Equal(1, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent); // Learner attack count

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Contains(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task BossSlayer_BossEncounteredButNotDefeated_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_not_defeated");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 10);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-boss-hit-only", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.False(receipt.TransitionResult.IsOpponentDefeated);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.DoesNotContain(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task BossSlayer_NormalOpponentDefeated_DoesNotUnlockBossSlayer()
    {
        var dbPath = await CreateFreshStoreDbAsync("normal_opponent_defeat");
        // Opponent 0 is Normal
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: 0, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-normal-win", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsOpponentDefeated);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.DoesNotContain(achievements, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
    }

    [Fact]
    public async Task BossSlayer_DuplicateReceipt_DoesNotReUnlockOrAlterTimestamp()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_duplicate");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-boss-dup", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements1 = await reader.GetUnlockedAchievementsAsync();
        var originalUnlock = achievements1.Single(a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);

        // Duplicate replay
        await store.ApplyAttemptTransactionAsync(intent);

        var achievements2 = await reader.GetUnlockedAchievementsAsync();
        var secondUnlock = achievements2.Single(a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);

        Assert.Equal(originalUnlock.UnlockedAtUtc, secondUnlock.UnlockedAtUtc);
    }

    [Fact]
    public async Task BossSlayer_StaleEpoch_FailsClosed()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_stale_epoch");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-boss-stale", epoch: 5, effectiveDamage: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task BossSlayer_FailedApplication_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_failed_app");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Effective damage 99 exceeds entitlement for rank 0 (max is 1)
        var intent = CreateIntent("sub-boss-cheat", isCorrect: true, effectiveDamage: 99);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task BossSlayer_StoreReopen_RetainsUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("boss_reopen");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        DateTimeOffset unlockedAt;
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = CreateIntent("sub-boss-reopen", isCorrect: true, effectiveDamage: 1);
            await store.StagePendingIntentAsync(intent);
            await store.ApplyAttemptTransactionAsync(intent);

            var reader = (ICyberDefenseAchievementReader)store;
            var list = await reader.GetUnlockedAchievementsAsync();
            unlockedAt = list.Single(a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer).UnlockedAtUtc;
        }

        using (var reopened = new SqliteGameplayStore(dbPath))
        {
            await reopened.InitializeAsync();
            var reader = (ICyberDefenseAchievementReader)reopened;
            var list = await reader.GetUnlockedAchievementsAsync();

            var record = Assert.Single(list, a => a.AchievementId == CyberDefenseAchievementIds.BossSlayer);
            Assert.Equal(unlockedAt, record.UnlockedAtUtc);
        }
    }

    // =========================================================================
    // SECTION 3: FIRST UPGRADE ACHIEVEMENT
    // =========================================================================

    [Fact]
    public async Task FirstUpgrade_AttackPurchase_UnlocksFirstUpgrade()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_attack");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var progression = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, progression.AttackRank);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(achievements);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
        Assert.Equal(0, record.ResetEpoch);
    }

    [Fact]
    public async Task FirstUpgrade_FirewallPurchase_UnlocksFirstUpgrade()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_firewall");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var progression = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, progression.FirewallRank);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(achievements);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
    }

    [Fact]
    public async Task FirstUpgrade_CriticalStrikePurchase_UnlocksFirstUpgrade()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_crit");
        await SetProgressionSpAndLevelAsync(dbPath, level: 25, earnedSp: 25, availableSp: 25);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var snapshot = await store.PurchaseCriticalStrikeUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, snapshot.CriticalStrikeRank);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(achievements);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
    }

    [Fact]
    public async Task FirstUpgrade_OverdrivePurchase_UnlocksFirstUpgrade()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_overdrive");
        await SetProgressionSpAndLevelAsync(dbPath, level: 25, earnedSp: 25, availableSp: 25);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var snapshot = await store.PurchaseOverdriveUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, snapshot.OverdriveRank);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(achievements);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
    }

    [Fact]
    public async Task FirstUpgrade_InsufficientSp_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_no_sp");
        // Available SP is 0
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 0, availableSp: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseAttackUpgradeAsync(0, 0));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task FirstUpgrade_InsufficientPlayerLevel_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_low_level");
        // Level 1, Critical Strike Rank 1 requires Level 2
        await SetProgressionSpAndLevelAsync(dbPath, level: 1, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseCriticalStrikeUpgradeAsync(0, 0));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task FirstUpgrade_StaleExpectedRank_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_stale_rank");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Stale expected rank 1 when actual rank is 0
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseAttackUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task FirstUpgrade_StaleResetEpoch_DoesNotUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_stale_epoch");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Stale expected epoch 1 when actual epoch is 0
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 1));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task FirstUpgrade_ConcurrentDuplicatePurchaseAttempts_OnlyOneSucceedsAndUnlocks()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_concurrent");
        // Exactly 1 SP available, so only 1 rank-1 purchase can succeed
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 1, availableSp: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var t1 = Task.Run(() => store.PurchaseAttackUpgradeAsync(0, 0));
        var t2 = Task.Run(() => store.PurchaseAttackUpgradeAsync(0, 0));

        var results = await Task.WhenAll(
            t1.ContinueWith(t => (Success: !t.IsFaulted, Exception: t.Exception?.InnerException)),
            t2.ContinueWith(t => (Success: !t.IsFaulted, Exception: t.Exception?.InnerException)));

        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Equal(1, results.Count(r => !r.Success && r.Exception is InvalidOperationException));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(achievements);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
    }

    [Fact]
    public async Task FirstUpgrade_RepeatedPurchasesFromMultipleFamilies_YieldsSingleRowAndPreservesOriginalTimestamp()
    {
        var dbPath = await CreateFreshStoreDbAsync("upg_multi_family");
        await SetProgressionSpAndLevelAsync(dbPath, level: 25, earnedSp: 30, availableSp: 30);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. First purchase: Attack
        await store.PurchaseAttackUpgradeAsync(0, 0);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements1 = await reader.GetUnlockedAchievementsAsync();
        var firstRecord = Assert.Single(achievements1);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, firstRecord.AchievementId);
        var initialUnlockTime = firstRecord.UnlockedAtUtc;

        // Small delay to ensure any potential timestamp overwrite would differ
        await Task.Delay(15);

        // 2. Second purchase: Firewall
        await store.PurchaseFirewallUpgradeAsync(0, 0);

        var achievements2 = await reader.GetUnlockedAchievementsAsync();
        var secondRecord = Assert.Single(achievements2);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, secondRecord.AchievementId);
        Assert.Equal(initialUnlockTime, secondRecord.UnlockedAtUtc);

        // 3. Third purchase: Critical Strike
        await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);

        var achievements3 = await reader.GetUnlockedAchievementsAsync();
        var thirdRecord = Assert.Single(achievements3);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, thirdRecord.AchievementId);
        Assert.Equal(initialUnlockTime, thirdRecord.UnlockedAtUtc);

        // 4. Fourth purchase: Overdrive
        await store.PurchaseOverdriveUpgradeAsync(0, 0);

        var achievements4 = await reader.GetUnlockedAchievementsAsync();
        var fourthRecord = Assert.Single(achievements4);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, fourthRecord.AchievementId);
        Assert.Equal(initialUnlockTime, fourthRecord.UnlockedAtUtc);
    }

    // =========================================================================
    // SECTION 4: TRANSACTION INTEGRITY & RESET
    // =========================================================================

    [Fact]
    public async Task TransactionIntegrity_DuplicateAchievementInsert_RemainsIdempotent()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_idempotent");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // First purchase unlocks
        await store.PurchaseAttackUpgradeAsync(0, 0);
        // Second purchase of attack rank 2
        await store.PurchaseAttackUpgradeAsync(1, 0);

        var reader = (ICyberDefenseAchievementReader)store;
        var list = await reader.GetUnlockedAchievementsAsync();
        Assert.Single(list, a => a.AchievementId == CyberDefenseAchievementIds.FirstUpgrade);
    }

    [Fact]
    public async Task TransactionIntegrity_NoBroadConstraintSuppression_ThrowsOnCheckViolation()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_check_violation");

        // Verify that local_achievements table enforces check constraint and does not suppress it
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        // Negative reset epoch violates CHECK(reset_epoch >= 0)
        cmd.CommandText = "INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc) VALUES ('first_upgrade', -1, '2026-10-29T00:00:00.0000000+00:00');";

        await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task TransactionIntegrity_AchievementWriteFailure_RollsBackReceiptAndRunState()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_rollback_receipt");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        // Inject trigger to abort achievement insert
        await ExecuteSqlAsync(dbPath, @"
            CREATE TRIGGER abort_achieve_insert
            BEFORE INSERT ON local_achievements
            BEGIN
                SELECT RAISE(ABORT, 'Simulated achievement write failure');
            END;");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-fail-rollback", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        // Applying attempt must fail because achievement insertion is aborted
        await Assert.ThrowsAsync<SqliteException>(() => store.ApplyAttemptTransactionAsync(intent));

        // Verify that receipt was NOT committed to the ledger
        var receipt = await store.GetReceiptAsync("sub-fail-rollback");
        Assert.Null(receipt);

        // Verify that run state was NOT updated (boss still has 1 HP)
        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);

        // Verify that no achievement was recorded
        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task TransactionIntegrity_ReceiptFailure_DoesNotUnlockAchievement()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_rollback_receipt_fail");
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetSectorAndOpponentAsync(dbPath, sector: 1, opponentIndex: bossIndex, opponentHp: 1);

        // Inject trigger to abort receipt insertion
        await ExecuteSqlAsync(dbPath, @"
            CREATE TRIGGER abort_receipt_insert
            BEFORE INSERT ON gameplay_receipt_ledger
            BEGIN
                SELECT RAISE(ABORT, 'Simulated receipt insert failure');
            END;");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-receipt-abort", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        await Assert.ThrowsAsync<SqliteException>(() => store.ApplyAttemptTransactionAsync(intent));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task TransactionIntegrity_AchievementWriteFailure_RollsBackPurchaseAndProgression()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_rollback_purchase");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        // Inject trigger to abort achievement insert
        await ExecuteSqlAsync(dbPath, @"
            CREATE TRIGGER abort_achieve_purchase_insert
            BEFORE INSERT ON local_achievements
            BEGIN
                SELECT RAISE(ABORT, 'Simulated achievement write failure during purchase');
            END;");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() => store.PurchaseAttackUpgradeAsync(0, 0));

        // Progression must be intact (attack rank remains 0, available SP remains 5)
        var snapshot = await store.GetAttackUpgradeSnapshotAsync();
        Assert.Equal(0, snapshot.CurrentAttackRank);
        Assert.Equal(5, snapshot.AvailableSkillPoints);

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task TransactionIntegrity_FailedPurchase_DoesNotUnlockAchievement()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_failed_purchase_no_unlock");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 0, availableSp: 0);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseAttackUpgradeAsync(0, 0));

        var reader = (ICyberDefenseAchievementReader)store;
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);
    }

    [Fact]
    public async Task TransactionIntegrity_UnlockDoesNotAlterXpSpOrRevision()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_no_side_effects");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, cumulativeXp: 500, earnedSp: 5, spentSp: 0, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var progressionBefore = await store.PurchaseAttackUpgradeAsync(0, 0);

        // Verify economy invariants
        Assert.Equal(5, progressionBefore.EarnedSkillPoints);
        Assert.Equal(1, progressionBefore.SpentSkillPoints);
        Assert.Equal(4, progressionBefore.AvailableSkillPoints);
        Assert.Equal(2, progressionBefore.StoreRevision); // Revision incremented exactly once by purchase
    }

    [Fact]
    public async Task TransactionIntegrity_PendingReset_BlocksUnlock()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_pending_reset");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Stage a pending reset
        await store.BeginOrGetResetIntentAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PurchaseAttackUpgradeAsync(0, 0));

        var reader = (ICyberDefenseAchievementReader)store;
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
    }

    [Fact]
    public async Task TransactionIntegrity_FullLocalReset_ClearsAllAchievements()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_full_reset");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Unlock first_upgrade
        await store.PurchaseAttackUpgradeAsync(0, 0);

        var reader = (ICyberDefenseAchievementReader)store;
        var beforeReset = await reader.GetUnlockedAchievementsAsync();
        Assert.Single(beforeReset);

        // Stage and execute Full Local Reset to epoch 1
        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(1);
        await store.ClearResetIntentAsync(1);

        var afterReset = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(afterReset);
    }

    [Fact]
    public async Task TransactionIntegrity_PendingResetStartupRecovery_ClearsUnlocks()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_startup_recovery");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await store.PurchaseAttackUpgradeAsync(0, 0);
            await store.BeginOrGetResetIntentAsync();
            // Simulate crash while reset intent is pending
        }

        // On reopen and executing the pending reset
        using (var recoveredStore = new SqliteGameplayStore(dbPath))
        {
            await recoveredStore.InitializeAsync();
            // A pending reset is present; executing it clears the store
            await recoveredStore.ResetGameplayStateAsync(1);
            await recoveredStore.ClearResetIntentAsync(1);

            var reader = (ICyberDefenseAchievementReader)recoveredStore;
            var achievements = await reader.GetUnlockedAchievementsAsync();
            Assert.Empty(achievements);
        }
    }

    [Fact]
    public async Task TransactionIntegrity_SubsequentNewEpochEvents_CanUnlockAgain()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_new_epoch_unlock");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Epoch 0 unlock
        await store.PurchaseAttackUpgradeAsync(0, 0);

        // Reset to epoch 1
        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(1);
        await store.ClearResetIntentAsync(1);

        // Grant SP in epoch 1
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        // Epoch 1 unlock
        await store.PurchaseAttackUpgradeAsync(0, 1);

        var reader = (ICyberDefenseAchievementReader)store;
        var list = await reader.GetUnlockedAchievementsAsync();
        var record = Assert.Single(list);
        Assert.Equal(CyberDefenseAchievementIds.FirstUpgrade, record.AchievementId);
        Assert.Equal(1, record.ResetEpoch);
    }

    [Fact]
    public async Task TransactionIntegrity_NoCrossEpochLeakage()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_no_cross_epoch");

        // Insert a synthetic row with epoch 2 while current epoch in progression is 0
        await ExecuteSqlAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 2, '2026-10-29T00:00:00.0000000+00:00');");

        using var store = new SqliteGameplayStore(dbPath);
        // Startup validation rejects cross epoch row as corrupt
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
    }

    [Fact]
    public async Task TransactionIntegrity_CanonicalUtcTimestampPersistence()
    {
        var dbPath = await CreateFreshStoreDbAsync("tx_utc_canonical");
        await SetProgressionSpAndLevelAsync(dbPath, level: 5, earnedSp: 5, availableSp: 5);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.PurchaseAttackUpgradeAsync(0, 0);

        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unlocked_at_utc FROM local_achievements WHERE achievement_id = 'first_upgrade';";
        var rawTimestamp = (string)(await cmd.ExecuteScalarAsync())!;

        Assert.NotNull(rawTimestamp);
        Assert.Equal(33, rawTimestamp.Length);
        Assert.EndsWith("+00:00", rawTimestamp, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.TryParseExact(
            rawTimestamp,
            "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed));
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }
}
