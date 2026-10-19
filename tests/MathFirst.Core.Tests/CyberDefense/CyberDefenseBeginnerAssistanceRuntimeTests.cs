namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class CyberDefenseBeginnerAssistanceRuntimeTests : IDisposable
{
    private readonly string _testDirectory;

    public CyberDefenseBeginnerAssistanceRuntimeTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_AssistanceRuntime_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "gameplay_assistance_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        bool isCorrect = true,
        bool isEligible = true,
        int effectiveDamage = 1,
        long resetEpoch = 0,
        string factId = "add:1+1")
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 250,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage);
    }

    private static async Task SetProgressionAsync(
        SqliteGameplayStore store,
        int playerLevel = 1,
        int earnedSp = 0,
        int spentSp = 0,
        int availableSp = 0,
        int attackRank = 0,
        int firewallRank = 0,
        int consecutiveEarlyDefeats = 0,
        int isAssistanceActive = 0,
        long cumulativeXp = 0,
        long storeRevision = 1,
        long resetEpoch = 0,
        int? activatedFirewallRank = null)
    {
        long resolvedXp = cumulativeXp > 0
            ? cumulativeXp
            : CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel);

        int resolvedActivatedRank = activatedFirewallRank ?? firewallRank;

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
                    consecutive_early_defeats = @defeats,
                    is_beginner_assistance_active = @assist,
                    activated_firewall_rank = @actFw,
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
            cmd.Parameters.AddWithValue("@defeats", consecutiveEarlyDefeats);
            cmd.Parameters.AddWithValue("@assist", isAssistanceActive);
            cmd.Parameters.AddWithValue("@actFw", resolvedActivatedRank);
            cmd.Parameters.AddWithValue("@xp", resolvedXp);
            cmd.Parameters.AddWithValue("@rev", storeRevision);
            cmd.Parameters.AddWithValue("@epoch", resetEpoch);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        });
    }

    private static async Task SetRunStateAsync(
        SqliteGameplayStore store,
        int sector = 1,
        int opponentIndex = 0,
        int? opponentHp = null,
        int playerHp = 100,
        int capacity = 0,
        int active = 0,
        int recharge = 0,
        string provenance = "Authoritative")
    {
        int resolvedOpponentHp = opponentHp ?? CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET sector = @sector,
                    opponent_index = @oppIdx,
                    opponent_current_hp = @oppHp,
                    player_current_hp = @playerHp,
                    opponent_encounter_provenance = @prov,
                    shield_capacity = @cap,
                    active_shields = @active,
                    shield_recharge_units = @recharge,
                    updated_at = @now
                WHERE id = 1;
            ";
            cmd.Parameters.AddWithValue("@sector", sector);
            cmd.Parameters.AddWithValue("@oppIdx", opponentIndex);
            cmd.Parameters.AddWithValue("@oppHp", resolvedOpponentHp);
            cmd.Parameters.AddWithValue("@playerHp", playerHp);
            cmd.Parameters.AddWithValue("@prov", provenance);
            cmd.Parameters.AddWithValue("@cap", capacity);
            cmd.Parameters.AddWithValue("@active", active);
            cmd.Parameters.AddWithValue("@recharge", recharge);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        });
    }

    private static async Task<(int ConsecutiveEarlyDefeats, bool IsAssistanceActive)> GetAssistanceStateAsync(SqliteGameplayStore store)
    {
        int consecutive = 0;
        bool isActive = false;
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT consecutive_early_defeats, is_beginner_assistance_active FROM gameplay_progression WHERE id = 1;";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                consecutive = reader.GetInt32(0);
                isActive = reader.GetInt32(1) == 1;
            }
        });
        return (consecutive, isActive);
    }

    // =========================================================================
    // A. TRIGGER BEHAVIOR
    // =========================================================================

    [Fact]
    public async Task QualifyingEarlyDefeat_FirstAndSecond_IncrementsCounter_DoesNotActivate()
    {
        var dbPath = GetTempDbPath("trigger_first_second");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Fresh player: 0 defeats, 0 assistance
        var (c0, a0) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, c0);
        Assert.False(a0);

        // First qualifying early defeat in Sector 1 (player HP = 1 <= enemy incoming 4)
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 1, capacity: 0, active: 0, recharge: 0);
        var receipt1 = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-defeat-1", isCorrect: false));
        Assert.True(receipt1.TransitionResult!.IsGameOver);

        var (c1, a1) = await GetAssistanceStateAsync(store);
        Assert.Equal(1, c1);
        Assert.False(a1);
        var runState1 = await store.GetRunStateAsync();
        Assert.Equal(0, runState1.ShieldCapacity);
        Assert.Equal(0, runState1.ActiveShields);

        // Second qualifying early defeat in Sector 1
        await SetRunStateAsync(store, sector: 1, opponentIndex: 1, playerHp: 1, capacity: 0, active: 0, recharge: 0);
        var receipt2 = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-defeat-2", isCorrect: false));
        Assert.True(receipt2.TransitionResult!.IsGameOver);

        var (c2, a2) = await GetAssistanceStateAsync(store);
        Assert.Equal(2, c2);
        Assert.False(a2);
        var runState2 = await store.GetRunStateAsync();
        Assert.Equal(0, runState2.ShieldCapacity);
        Assert.Equal(0, runState2.ActiveShields);
    }

    [Fact]
    public async Task QualifyingEarlyDefeat_Third_ActivatesAssistance_AndGrantsOneEmergencyShield()
    {
        var dbPath = GetTempDbPath("trigger_third");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 2 prior early defeats
        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2, isAssistanceActive: 0);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 2, playerHp: 1, capacity: 0, active: 0, recharge: 0);

        // Third qualifying defeat
        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-defeat-3", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsGameOver);

        // Assistance must be activated
        var (c3, a3) = await GetAssistanceStateAsync(store);
        Assert.Equal(3, c3);
        Assert.True(a3);

        // Run state must be rebooted into Sector 1 with exactly one charged Emergency Shield
        var runState = await store.GetRunStateAsync();
        Assert.Equal(1, runState.Sector);
        Assert.Equal(0, runState.OpponentIndex);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, runState.PlayerCurrentHp);
        Assert.Equal(1, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);

        // Receipt reflects the rebooted next shield state
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(1, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ThirdDefeatActivation_DoesNotGrantPermanentFirewallRank_NorExtraXpOrSp()
    {
        var dbPath = GetTempDbPath("trigger_no_unearned_gain");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 1, earnedSp: 0, spentSp: 0, availableSp: 0,
            attackRank: 0, firewallRank: 0, consecutiveEarlyDefeats: 2, isAssistanceActive: 0, cumulativeXp: 0);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 1, capacity: 0, active: 0, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-defeat-gain-check", isCorrect: false));

        var prog = await store.GetProgressionAsync();
        // Permanent firewall rank remains 0
        Assert.Equal(0, prog.FirewallRank);
        // Only standard incorrect attempt XP (1 XP)
        Assert.Equal(1, receipt.AwardedXp);
        Assert.Equal(1, prog.CumulativeXp);
        Assert.Equal(1, prog.PlayerLevel);
        Assert.Equal(0, prog.EarnedSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);
    }

    // =========================================================================
    // B. ELIGIBILITY
    // =========================================================================

    [Fact]
    public async Task Sector1EarlyGameOver_Qualifies()
    {
        var dbPath = GetTempDbPath("eligibility_sector1");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunStateAsync(store, sector: 1, opponentIndex: 3, playerHp: 1);
        await store.ApplyAttemptTransactionAsync(CreateIntent("sub-s1-qualifies", isCorrect: false));

        var (count, _) = await GetAssistanceStateAsync(store);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Sector1BossGameOver_DoesNotQualify_ResetsCounter()
    {
        var dbPath = GetTempDbPath("eligibility_boss_gameover");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 2 prior early defeats
        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2);
        // OpponentIndex 5 is Boss in Sector 1 (NormalOpponentCount = 5, so BossIndex = 5)
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, bossIndex);
        await SetRunStateAsync(store, sector: 1, opponentIndex: bossIndex, opponentHp: bossMaxHp, playerHp: 1);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-boss-defeat", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsGameOver);

        // Counter must be reset to 0 because boss reached/game-over at boss does not qualify
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, count);
        Assert.False(isActive);
    }

    [Fact]
    public async Task ReachingSector1Boss_ResetsConsecutiveDefeatCounter()
    {
        var dbPath = GetTempDbPath("eligibility_reach_boss");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 2 prior early defeats
        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2);
        // OpponentIndex 4 is the last Normal opponent in Sector 1. When defeated, player reaches Boss (Index 5).
        await SetRunStateAsync(store, sector: 1, opponentIndex: 4, opponentHp: 1, playerHp: 100);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-reach-boss", isCorrect: true, effectiveDamage: 1));
        Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
        Assert.Equal(OpponentKind.Boss, receipt.TransitionResult.NextState.CurrentOpponent.Kind);

        // Counter must reset to 0 upon reaching the boss
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, count);
        Assert.False(isActive);
    }

    [Fact]
    public async Task Sector2GameOver_DoesNotQualify_ResetsCounter()
    {
        var dbPath = GetTempDbPath("eligibility_sector2");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2);
        await SetRunStateAsync(store, sector: 2, opponentIndex: 0, playerHp: 1);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-s2-gameover", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsGameOver);

        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, count);
        Assert.False(isActive);
    }

    [Fact]
    public async Task FirewallRank1_PreventsAssistanceTrigger()
    {
        var dbPath = GetTempDbPath("eligibility_firewall_rank_blocks");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Permanent Firewall Rank 1 owned
        await SetProgressionAsync(store, firewallRank: 1, consecutiveEarlyDefeats: 2);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 1, capacity: 1, active: 0, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-fw1-defeat", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsGameOver);

        // Assistance must NOT activate with Firewall Rank 1
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.False(isActive);
    }

    [Fact]
    public async Task NonconsecutiveDefeats_DoNotAccumulateAcrossBossReach()
    {
        var dbPath = GetTempDbPath("eligibility_streak_broken");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 2 early defeats
        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2);

        // Defeat opponent 4 to reach Sector 1 Boss
        await SetRunStateAsync(store, sector: 1, opponentIndex: 4, opponentHp: 1, playerHp: 100);
        await store.ApplyAttemptTransactionAsync(CreateIntent("sub-break-streak-boss", isCorrect: true, effectiveDamage: 1));

        var (cAfterBoss, _) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, cAfterBoss);

        // Now die at the boss
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, bossIndex);
        await SetRunStateAsync(store, sector: 1, opponentIndex: bossIndex, opponentHp: bossHp, playerHp: 1);
        await store.ApplyAttemptTransactionAsync(CreateIntent("sub-boss-defeat", isCorrect: false));

        var (cAfterBossDeath, _) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, cAfterBossDeath);

        // Next run: 1 early defeat in Sector 1
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 1);
        await store.ApplyAttemptTransactionAsync(CreateIntent("sub-fresh-defeat-1", isCorrect: false));

        var (cFresh, aFresh) = await GetAssistanceStateAsync(store);
        Assert.Equal(1, cFresh);
        Assert.False(aFresh);
    }

    // =========================================================================
    // C. SHIELD BEHAVIOR
    // =========================================================================

    [Fact]
    public async Task EmergencyShield_IncorrectAnswer_AbsorbsFullDamage_DecrementsActiveShield()
    {
        var dbPath = GetTempDbPath("shield_absorption");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Assistance active with 1 charged shield
        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 1, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-absorb", isCorrect: false));

        // Damage absorbed completely
        Assert.True(receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(4, receipt.TransitionResult.AbsorbedEnemyDamage); // Sector 1 Normal enemy damage is 4
        Assert.Equal(0, receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);

        // Active shield decremented to 0, +1 recharge unit added
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(100, runState.PlayerCurrentHp);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(1, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task EmergencyShield_CorrectAnswer_AddsTwoRechargeUnits()
    {
        var dbPath = GetTempDbPath("shield_recharge_correct");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 0, recharge: 5);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-recharge-correct", isCorrect: true));

        // Correct gives +2 recharge units: 5 + 2 = 7
        Assert.Equal(7, receipt.TransitionResult!.NextState.ShieldRechargeUnits);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(7, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task EmergencyShield_IncorrectAnswer_WithoutShield_AddsOneRechargeUnit()
    {
        var dbPath = GetTempDbPath("shield_recharge_incorrect");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 0, recharge: 5);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-recharge-incorrect", isCorrect: false));

        // Incorrect gives +1 recharge unit: 5 + 1 = 6; took 4 damage: 100 - 4 = 96
        Assert.False(receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(96, receipt.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(6, receipt.TransitionResult.NextState.ShieldRechargeUnits);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
    }

    [Fact]
    public async Task EmergencyShield_Recharge_RestoresShieldAtThreshold_AndCapsAtCapacity1()
    {
        var dbPath = GetTempDbPath("shield_restore_threshold");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        // At 29 recharge units, a correct answer adds +2 -> 31 >= 30: restores shield!
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 0, recharge: 29);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-restore", isCorrect: true));

        // Capacity is 1, so restored shields = 1 == Capacity -> recharge units reset to 0 (cannot exceed capacity)
        Assert.Equal(1, receipt.TransitionResult!.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task GameOverWhileAssistanceActive_PreservesAssistanceAndRestartsWithEmergencyShield()
    {
        var dbPath = GetTempDbPath("gameover_while_active");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        // Depleted shield, low HP -> game over
        await SetRunStateAsync(store, sector: 1, opponentIndex: 1, playerHp: 1, capacity: 1, active: 0, recharge: 10);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-gameover-active", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsGameOver);

        // Assistance remains active
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.True(isActive);
        Assert.Equal(3, count);

        // Next run state starts fresh with 1 charged Emergency Shield
        var runState = await store.GetRunStateAsync();
        Assert.Equal(1, runState.Sector);
        Assert.Equal(0, runState.OpponentIndex);
        Assert.Equal(100, runState.PlayerCurrentHp);
        Assert.Equal(1, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }

    // =========================================================================
    // D. RETIREMENT
    // =========================================================================

    [Fact]
    public async Task ClearingSector1_RetiresAssistance_AndResetsCounter()
    {
        var dbPath = GetTempDbPath("retire_on_clear");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        // Fighting Sector 1 Boss (index 5) with 1 HP remaining
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: bossIndex, opponentHp: 1, playerHp: 100, capacity: 1, active: 1, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-clear-s1", isCorrect: true, effectiveDamage: 1));
        Assert.True(receipt.TransitionResult!.IsSectorCompleted);
        Assert.Equal(2, receipt.TransitionResult.NextState.Sector);

        // Assistance must be deactivated, defeat counter reset
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, count);
        Assert.False(isActive);
    }

    [Fact]
    public async Task ClearingSector1_WithFirewallRank0_RemovesUnearnedTemporaryShield()
    {
        var dbPath = GetTempDbPath("retire_removes_shield");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, firewallRank: 0, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: bossIndex, opponentHp: 1, playerHp: 100, capacity: 1, active: 1, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-clear-s1-unearned", isCorrect: true, effectiveDamage: 1));
        Assert.True(receipt.TransitionResult!.IsSectorCompleted);

        // Run state entering Sector 2 must NOT retain unearned temporary shield
        Assert.Equal(2, receipt.TransitionResult.NextState.Sector);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.Sector);
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ClearingSector1_WithFirewallRank1_PreservesLegitimatePermanentShield()
    {
        var dbPath = GetTempDbPath("retire_preserves_permanent");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Player owns permanent Firewall Rank 1 (purchased earlier)
        await SetProgressionAsync(store, firewallRank: 1, consecutiveEarlyDefeats: 0, isAssistanceActive: 0);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: bossIndex, opponentHp: 1, playerHp: 100, capacity: 1, active: 1, recharge: 0);

        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-clear-s1-permanent", isCorrect: true, effectiveDamage: 1));
        Assert.True(receipt.TransitionResult!.IsSectorCompleted);

        // Sector 2 retains legitimate permanent shield
        Assert.Equal(2, receipt.TransitionResult.NextState.Sector);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(1, receipt.TransitionResult.NextState.ActiveShields);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.Sector);
        Assert.Equal(1, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
    }

    [Fact]
    public async Task PurchasingFirewallRank1_RetiresAssistanceImmediately()
    {
        var dbPath = GetTempDbPath("purchase_retires_assistance");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5,
            firewallRank: 0, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);

        // Purchase Firewall Rank 1
        var updated = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, updated.FirewallRank);

        // is_beginner_assistance_active is reset to 0
        var (_, isActive) = await GetAssistanceStateAsync(store);
        Assert.False(isActive);
    }

    [Fact]
    public async Task PurchasingFirewallRank1_PreservesInFlightQuestionSemantics_AndActivatesAtNextBoundary()
    {
        var dbPath = GetTempDbPath("purchase_handoff");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Assistance active with 1 shield; player at Level 5 with 5 SP
        await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 0, availableSp: 5,
            firewallRank: 0, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 1, recharge: 0);

        // Mid-question purchase of Firewall Rank 1
        await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // In-flight question Q1 is evaluated using existing run-state shield semantics:
        // An incorrect answer consumes the temporary shield
        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-q1", isCorrect: false));
        Assert.True(receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        // At next-question presentation boundary (Q2), activation runs with target permanent capacity 1
        var activation = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.Applied, activation.Status);
        // The former temporary slot is replaced with charged permanent Rank 1 capacity
        Assert.Equal(1, activation.RunState!.ShieldCapacity);
        Assert.Equal(1, activation.RunState.ActiveShields);
        Assert.Equal(0, activation.RunState.ShieldRechargeUnits);

        // Repeated activation at Q2 boundary is idempotent and does not refill shields
        var repeatedActivation = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.AlreadyCurrent, repeatedActivation.Status);
    }

    [Fact]
    public async Task ColdRestartDuringHandoff_DoesNotDuplicateNorLoseEntitlement()
    {
        var dbPath = GetTempDbPath("cold_restart_handoff");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await SetProgressionAsync(store, playerLevel: 5, earnedSp: 5, spentSp: 3, availableSp: 2,
                firewallRank: 1, consecutiveEarlyDefeats: 3, isAssistanceActive: 0);
            await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 1, active: 1, recharge: 0);
        }

        // Simulate cold restart: fresh store instance
        using (var freshStore = new SqliteGameplayStore(dbPath))
        {
            await freshStore.InitializeAsync();
            var prog = await freshStore.GetProgressionAsync();
            var runState = await freshStore.GetRunStateAsync();

            Assert.Equal(1, prog.FirewallRank);
            Assert.Equal(1, runState.ShieldCapacity);
            Assert.Equal(1, runState.ActiveShields);

            // Activate at question boundary
            var activation = await freshStore.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
            Assert.Equal(1, activation.RunState!.ShieldCapacity);
            Assert.Equal(1, activation.RunState.ActiveShields);
        }
    }

    // =========================================================================
    // E. PERSISTENCE & RECEIPTS
    // =========================================================================

    [Fact]
    public async Task ColdRestart_RetainsAssistanceStateAndShieldState()
    {
        var dbPath = GetTempDbPath("cold_restart_state");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
            await SetRunStateAsync(store, sector: 1, opponentIndex: 2, playerHp: 80, capacity: 1, active: 0, recharge: 15);
        }

        using (var freshStore = new SqliteGameplayStore(dbPath))
        {
            await freshStore.InitializeAsync();
            var (count, isActive) = await GetAssistanceStateAsync(freshStore);
            Assert.Equal(3, count);
            Assert.True(isActive);

            var runState = await freshStore.GetRunStateAsync();
            Assert.Equal(1, runState.ShieldCapacity);
            Assert.Equal(0, runState.ActiveShields);
            Assert.Equal(15, runState.ShieldRechargeUnits);
        }
    }

    [Fact]
    public async Task DuplicateReceiptReplay_DoesNotRecountDefeatNorReactivateAssistance()
    {
        var dbPath = GetTempDbPath("idempotent_replay");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 1, capacity: 0, active: 0, recharge: 0);
        var intent = CreateIntent("sub-idempotent-defeat", isCorrect: false);

        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        var (c1, _) = await GetAssistanceStateAsync(store);
        Assert.Equal(1, c1);

        // Replay same submission ID
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);

        // Counter must NOT increment again
        var (c2, _) = await GetAssistanceStateAsync(store);
        Assert.Equal(1, c2);
    }

    [Fact]
    public async Task FullLocalReset_ClearsConsecutiveDefeatsAndAssistanceFlag()
    {
        var dbPath = GetTempDbPath("reset_clears_assistance");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 3, isAssistanceActive: 1);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 2, playerHp: 80, capacity: 1, active: 1, recharge: 0);

        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: 1);
        await store.ClearResetIntentAsync(targetEpoch: 1);

        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(0, count);
        Assert.False(isActive);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
    }

    [Fact]
    public async Task FailedTransaction_RollsBackAllProgressionAndRunStateChanges()
    {
        var dbPath = GetTempDbPath("transaction_rollback");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2, isAssistanceActive: 0);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 100, capacity: 0, active: 0, recharge: 0);

        // Attempt with wrong reset epoch -> transaction must fail and roll back
        var invalidIntent = CreateIntent("sub-fail-epoch", isCorrect: false, resetEpoch: 999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(invalidIntent));

        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(2, count);
        Assert.False(isActive);
    }

    // =========================================================================
    // F. REGRESSION ISOLATION
    // =========================================================================

    [Fact]
    public async Task AttackPurchase_UnaffectedByAssistance()
    {
        var dbPath = GetTempDbPath("attack_purchase_unaffected");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, playerLevel: 2, earnedSp: 1, spentSp: 0, availableSp: 1,
            attackRank: 0, firewallRank: 0, consecutiveEarlyDefeats: 2, isAssistanceActive: 0);

        var updated = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, updated.AttackRank);

        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(2, count);
        Assert.False(isActive);
    }

    [Fact]
    public async Task CalmModeSuppressed_HasNoEffectOnAssistanceOrDefeatCountOrShields()
    {
        var dbPath = GetTempDbPath("calm_mode_isolated");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetProgressionAsync(store, consecutiveEarlyDefeats: 2, isAssistanceActive: 0);
        await SetRunStateAsync(store, sector: 1, opponentIndex: 0, playerHp: 5, capacity: 0, active: 0, recharge: 0);

        // Ineligible intent (Calm Mode)
        var calmIntent = CreateIntent("sub-calm", isCorrect: false, isEligible: false);
        var receipt = await store.ApplyAttemptTransactionAsync(calmIntent);

        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);

        // Counter, assistance flag, and run state must NOT be mutated
        var (count, isActive) = await GetAssistanceStateAsync(store);
        Assert.Equal(2, count);
        Assert.False(isActive);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(5, runState.PlayerCurrentHp);
    }

    [Fact]
    public async Task Sector1BossClear_WithMidQuestionRank1Purchase_RemovesTemporaryShieldCapacity()
    {
        var dbPath = GetTempDbPath("boss_clear_mid_purchase");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Player has temporary emergency shield and enough SP to buy Rank 1
        await SetProgressionAsync(
            store,
            playerLevel: 5,
            earnedSp: 5,
            spentSp: 0,
            availableSp: 5,
            firewallRank: 0,
            isAssistanceActive: 1);

        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        await SetRunStateAsync(
            store,
            sector: 1,
            opponentIndex: bossIndex,
            opponentHp: 1,
            playerHp: 100,
            capacity: 1,
            active: 1,
            recharge: 0);

        // Player purchases Rank 1 mid-question during boss fight
        var purchasedProg = await store.PurchaseFirewallUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        Assert.Equal(1, purchasedProg.FirewallRank);
        Assert.Equal(0, purchasedProg.ActivatedFirewallRank);

        // Defeat Sector 1 Boss
        var receipt = await store.ApplyAttemptTransactionAsync(CreateIntent("sub-boss-defeat", isCorrect: true, effectiveDamage: 1));
        Assert.True(receipt.TransitionResult!.IsSectorCompleted);
        Assert.Equal(2, receipt.TransitionResult.NextState.Sector);

        // Because activated_firewall_rank was 0 when clearing Sector 1, temporary shield must be removed!
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.Sector);
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }
}
