namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

[Collection("Combat modifier purchase SQLite isolation")]
public sealed class SqliteGameplayStoreModifierReceiptIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirst_Slice14_ModifierCombat_" + Guid.NewGuid().ToString("N"));

    private string GameplayDbPath => Path.Combine(_directory, "gameplay.db");
    private string LearnerDbPath => Path.Combine(_directory, "learner.db");

    public SqliteGameplayStoreModifierReceiptIntegrationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp directory cleanup.
        }
    }

    private async Task<SqliteGameplayStore> OpenStoreAsync(string? path = null)
    {
        var store = new SqliteGameplayStore(path ?? GameplayDbPath);
        await store.InitializeAsync();
        return store;
    }

    private async Task<SqliteLearnerStore> OpenLearnerStoreAsync(string? path = null)
    {
        var store = new SqliteLearnerStore(path ?? LearnerDbPath);
        await store.InitializeAsync();
        return store;
    }

    private async Task ExecuteRawAsync(string sql, string? path = null)
    {
        using var conn = new SqliteConnection($"Data Source={path ?? GameplayDbPath};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarRawAsync(string sql, string? path = null)
    {
        using var conn = new SqliteConnection($"Data Source={path ?? GameplayDbPath};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var val = await cmd.ExecuteScalarAsync();
        return val is DBNull ? null : val;
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        string factId = "add:1+1",
        bool isCorrect = true,
        bool isEligible = true,
        long responseLatencyMs = 450,
        long resetEpoch = 0,
        DateTimeOffset? createdAt = null,
        int effectiveAttackDamage = 1,
        int boundCriticalStrikeRank = 0,
        int boundOverdriveRank = 0)
    {
        return new CyberDefensePendingIntentRecord(
            submissionId,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            createdAt ?? DateTimeOffset.UtcNow,
            effectiveAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank);
    }

    private static SubmissionChangeSet CreateLearnerChangeSet(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long practicePosition,
        long expectedRevision,
        DateTimeOffset? timestamp = null)
    {
        var stripped = factId.StartsWith("add:", StringComparison.Ordinal) ? factId[4..] : factId;
        var parts = stripped.Split('+');
        int left = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int right = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int answer = left + right;
        var ts = timestamp ?? DateTimeOffset.UtcNow;

        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: ArithmeticOperation.Addition,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: responseLatencyMs,
            timestamp: ts,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: practicePosition);

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;

        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = ArithmeticOperation.Addition,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = responseLatencyMs,
            RollingLatencyMs = responseLatencyMs,
            FluentStreak = isCorrect ? 1 : 0,
            LastPracticedOrder = (int)practicePosition,
            LastPracticedAt = ts
        };

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: progression.OperationProgressions);
    }

    // =========================================================================
    // 1. Deterministic Pending-Intent Ordering & SQLite Transaction-Level Barrier
    // =========================================================================

    [Fact]
    public async Task GetPendingIntents_OrdersByUtcInstantThenOrdinalSubmissionId_AcrossMixedOffsets()
    {
        using var store = await OpenStoreAsync();

        // 10:00Z == 12:00+02:00; 09:59Z is earlier despite "11:59+02:00" string sorting after "10:00+00:00".
        var tEarliestUtc = new DateTimeOffset(2026, 10, 20, 11, 59, 0, TimeSpan.FromHours(2)); // 09:59Z
        var tTieUtcPlus2 = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(2)); // 10:00Z
        var tTieUtcZero = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero); // 10:00Z
        var tLatestUtc = new DateTimeOffset(2026, 10, 20, 10, 1, 0, TimeSpan.Zero); // 10:01Z

        await store.StagePendingIntentAsync(CreateIntent("sub-c", createdAt: tLatestUtc));
        await store.StagePendingIntentAsync(CreateIntent("sub-b", createdAt: tTieUtcPlus2));
        await store.StagePendingIntentAsync(CreateIntent("sub-B", createdAt: tTieUtcZero));
        await store.StagePendingIntentAsync(CreateIntent("sub-a", createdAt: tEarliestUtc));

        var pending = await store.GetPendingIntentsAsync(0);
        Assert.Equal(["sub-a", "sub-B", "sub-b", "sub-c"], pending.Select(x => x.SubmissionId).ToArray());
    }

    [Fact]
    public async Task TransactionOrderingBarrier_RejectsOutOfOrderAndUnstagedIntents_WithoutMutation()
    {
        using var store = await OpenStoreAsync();
        using var learner = await OpenLearnerStoreAsync();
        await ExecuteRawAsync("UPDATE gameplay_progression SET critical_strike_rank = 2, overdrive_rank = 2, attack_rank = 1 WHERE id = 1;");

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var t1 = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddSeconds(1);

        var headIntent = CreateIntent("sub-head", "add:1+1", true, true, 400, 0, t1, 2, 1, 1);
        var tailIntent = CreateIntent("sub-tail", "add:1+0", true, true, 450, 0, t2, 2, 1, 1);
        await store.StagePendingIntentAsync(headIntent);
        await store.StagePendingIntentAsync(tailIntent);

        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-tail", "add:1+0", true, 450, 1, 1))).IsSuccess);

        var runBefore = await store.GetRunStateAsync();
        var progBefore = await store.GetProgressionAsync();
        var modBefore = await store.GetCombatModifierSnapshotAsync();

        // Non-head staged intent is rejected by both ApplyAttemptTransactionAsync and ConsumeAttemptAsync.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(tailIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-tail"));

        // Unstaged intent cannot bypass the staged queue even with earlier timestamp or rank 0.
        var unstagedBypass = CreateIntent("sub-bypass", "add:1+1", true, true, 300, 0, t1.AddMinutes(-5), 1, 0, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(unstagedBypass));

        // Recovery stops at uncommitted head and applies 0 intents.
        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recovery.RecoveredCount);

        Assert.Equal(runBefore, await store.GetRunStateAsync());
        Assert.Equal(progBefore, await store.GetProgressionAsync());
        Assert.Equal(modBefore, await store.GetCombatModifierSnapshotAsync());
        Assert.Null(await store.GetReceiptAsync("sub-head"));
        Assert.Null(await store.GetReceiptAsync("sub-tail"));
        Assert.Equal(["sub-head", "sub-tail"], (await store.GetPendingIntentsAsync(0)).Select(x => x.SubmissionId).ToArray());
    }

    // =========================================================================
    // 2. Question-Bound Critical Strike & Overdrive Execution + Additive Ceiling
    // =========================================================================

    [Fact]
    public async Task CriticalStrikeRank1AndRank2_ExecuteFullCadenceAndPersistV7Receipts()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 1, overdrive_rank = 0, attack_rank = 2
            WHERE id = 1;
            """); // attack_rank 2 => base damage 3

        var t0 = DateTimeOffset.UtcNow;

        // Rank 1 triggers every 5th correct attack (+50%): ceil(3 * 150 / 100) = 5.
        for (int i = 1; i <= 5; i++)
        {
            var intent = CreateIntent($"sub-r1-{i}", createdAt: t0.AddSeconds(i), effectiveAttackDamage: 3, boundCriticalStrikeRank: 1, boundOverdriveRank: 0);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            int expectedBonus = i == 5 ? 50 : 0;
            int expectedDamage = i == 5 ? 5 : 3;
            Assert.Equal(3, receipt.BaseAttackDamage);
            Assert.Equal(1, receipt.BoundCriticalStrikeRank);
            Assert.Equal(0, receipt.BoundOverdriveRank);
            Assert.Equal(expectedBonus, receipt.CriticalBonusPercent);
            Assert.Equal(0, receipt.OverdriveBonusPercent);
            Assert.Equal(expectedDamage, receipt.TransitionResult!.RequestedAttackDamage);
            Assert.Equal(i, (await store.GetCombatModifierSnapshotAsync()).ModifierState.CriticalCorrectAttackCount);
        }

        // Incorrect attempt preserves Critical Strike count (5) and grants 0% bonus.
        var wrongIntent = CreateIntent("sub-r1-wrong", isCorrect: false, createdAt: t0.AddSeconds(6), effectiveAttackDamage: 3, boundCriticalStrikeRank: 1, boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(wrongIntent);
        var wrongReceipt = await store.ApplyAttemptTransactionAsync(wrongIntent);
        Assert.Equal(0, wrongReceipt.CriticalBonusPercent);
        Assert.Equal(0, wrongReceipt.OverdriveBonusPercent);
        Assert.Equal(5, (await store.GetCombatModifierSnapshotAsync()).ModifierState.CriticalCorrectAttackCount);

        // Upgrade to Rank 2 via store purchase (resets critical_correct_attack_count to 0).
        await ExecuteRawAsync("UPDATE gameplay_progression SET cumulative_xp = 1000000, player_level = 80, earned_sp = 40, spent_sp = 0, available_sp = 40 WHERE id = 1;");
        var afterPurchase = await store.PurchaseCriticalStrikeUpgradeAsync(1, 0);
        Assert.Equal(2, afterPurchase.CriticalStrikeRank);
        Assert.Equal(0, afterPurchase.ModifierState.CriticalCorrectAttackCount);

        // Rank 2 triggers every 4th correct attack (+100%): ceil(3 * 200 / 100) = 6.
        for (int i = 1; i <= 4; i++)
        {
            var intent = CreateIntent($"sub-r2-{i}", createdAt: t0.AddSeconds(10 + i), effectiveAttackDamage: 3, boundCriticalStrikeRank: 2, boundOverdriveRank: 0);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            int expectedBonus = i == 4 ? 100 : 0;
            int expectedDamage = i == 4 ? 6 : 3;
            Assert.Equal(expectedBonus, receipt.CriticalBonusPercent);
            Assert.Equal(expectedDamage, receipt.TransitionResult!.RequestedAttackDamage);
            Assert.Equal(i, (await store.GetCombatModifierSnapshotAsync()).ModifierState.CriticalCorrectAttackCount);
        }
    }

    [Fact]
    public async Task OverdriveRank1AndRank2_ArmConsumeOverlapAndCombineAdditivelyWithCriticalStrike()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 2, overdrive_rank = 2, attack_rank = 2
            WHERE id = 1;
            """); // base damage = 3

        var t0 = DateTimeOffset.UtcNow;

        // With Critical Rank 2 (every 4th) and Overdrive Rank 2 (4th arms 2-attack window; 5th & 6th get +50%):
        // Attack 1..3: Crit 0%, OD 0% -> dmg 3, state=(i, i, 0)
        // Attack 4:    Crit 100%, OD 0% (arms window!) -> dmg 6, state=(4, 0, 2)
        // Attack 5:    Crit 0%, OD 50% -> ceil(3 * 150 / 100) = 5, state=(5, 1, 1)
        // Attack 6:    Crit 0%, OD 50% -> ceil(3 * 150 / 100) = 5, state=(6, 2, 0)
        // Attack 7:    Crit 0%, OD 0% -> dmg 3, state=(7, 3, 0)
        // Attack 8:    Crit 100%, OD 0% (re-arms window from overlapped streak 2->3->4!) -> dmg 6, state=(8, 0, 2)
        (int CritBonus, int OdBonus, int Dmg, int NextCount, int NextStreak, int NextRemaining)[] expected =
        [
            (0, 0, 3, 1, 1, 0),
            (0, 0, 3, 2, 2, 0),
            (0, 0, 3, 3, 3, 0),
            (100, 0, 6, 4, 0, 2),
            (0, 50, 5, 5, 1, 1),
            (0, 50, 5, 6, 2, 0),
            (0, 0, 3, 7, 3, 0),
            (100, 0, 6, 8, 0, 2),
        ];

        for (int i = 0; i < expected.Length; i++)
        {
            var intent = CreateIntent($"sub-combo-{i + 1}", createdAt: t0.AddSeconds(i + 1), effectiveAttackDamage: 3, boundCriticalStrikeRank: 2, boundOverdriveRank: 2);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            var exp = expected[i];
            Assert.Equal(exp.CritBonus, receipt.CriticalBonusPercent);
            Assert.Equal(exp.OdBonus, receipt.OverdriveBonusPercent);
            Assert.Equal(exp.Dmg, receipt.TransitionResult!.RequestedAttackDamage);

            var state = (await store.GetCombatModifierSnapshotAsync()).ModifierState;
            Assert.Equal(exp.NextCount, state.CriticalCorrectAttackCount);
            Assert.Equal(exp.NextStreak, state.OverdriveStreak);
            Assert.Equal(exp.NextRemaining, state.OverdriveRemainingBonusAttacks);
        }

        // Now set Critical Count to 3 while Overdrive has 1 remaining bonus attack, so the next correct attack
        // triggers BOTH Critical Rank 2 (+100%) AND Overdrive (+50%) => +150% additive with single ceiling:
        // ceil(3 * 250 / 100) = ceil(7.5) = 8!
        await ExecuteRawAsync("UPDATE gameplay_run_state SET critical_correct_attack_count = 3, overdrive_streak = 1, overdrive_remaining_bonus_attacks = 1 WHERE id = 1;");
        var simultaneous = CreateIntent("sub-simul", createdAt: t0.AddSeconds(20), effectiveAttackDamage: 3, boundCriticalStrikeRank: 2, boundOverdriveRank: 2);
        await store.StagePendingIntentAsync(simultaneous);
        var simulReceipt = await store.ApplyAttemptTransactionAsync(simultaneous);

        Assert.Equal(3, simulReceipt.BaseAttackDamage);
        Assert.Equal(2, simulReceipt.BoundCriticalStrikeRank);
        Assert.Equal(2, simulReceipt.BoundOverdriveRank);
        Assert.Equal(100, simulReceipt.CriticalBonusPercent);
        Assert.Equal(50, simulReceipt.OverdriveBonusPercent);
        Assert.Equal(8, simulReceipt.TransitionResult!.RequestedAttackDamage);
    }

    // =========================================================================
    // 3. Calm Mode Freeze, Firewall Absorption, and Game-Over Modifier Reset
    // =========================================================================

    [Fact]
    public async Task CalmModeAttempt_FreezesModifierState_AndWritesNullV7ReceiptColumns()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 2, overdrive_rank = 2, attack_rank = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET critical_correct_attack_count = 3, overdrive_streak = 1, overdrive_remaining_bonus_attacks = 1
            WHERE id = 1;
            """);

        var beforeRun = await store.GetRunStateAsync();
        var beforeMod = await store.GetCombatModifierSnapshotAsync();
        var beforeRev = await store.GetStoreRevisionAsync();

        var calmIntent = CreateIntent(
            "sub-calm-freeze",
            isCorrect: false,
            isEligible: false,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 2,
            boundOverdriveRank: 2);
        await store.StagePendingIntentAsync(calmIntent);

        var receipt = await store.ApplyAttemptTransactionAsync(calmIntent);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);
        Assert.Null(receipt.BaseAttackDamage);
        Assert.Null(receipt.BoundCriticalStrikeRank);
        Assert.Null(receipt.BoundOverdriveRank);
        Assert.Null(receipt.CriticalBonusPercent);
        Assert.Null(receipt.OverdriveBonusPercent);
        Assert.Null(await store.GetPendingIntentAsync("sub-calm-freeze"));

        Assert.Equal(beforeRun, await store.GetRunStateAsync());
        Assert.Equal(beforeMod, await store.GetCombatModifierSnapshotAsync());
        Assert.Equal(beforeRev, await store.GetStoreRevisionAsync());
    }

    [Fact]
    public async Task FirewallAbsorbedIncorrectAttempt_ClearsOverdrive_AndPreservesCriticalStrikeCount()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 2, overdrive_rank = 2, firewall_rank = 1, activated_firewall_rank = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET shield_capacity = 1, active_shields = 1, shield_recharge_units = 0,
                critical_correct_attack_count = 3, overdrive_streak = 1, overdrive_remaining_bonus_attacks = 1
            WHERE id = 1;
            """);

        var wrongShielded = CreateIntent(
            "sub-shield-wrong",
            isCorrect: false,
            isEligible: true,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 2,
            boundOverdriveRank: 2);
        await store.StagePendingIntentAsync(wrongShielded);

        var receipt = await store.ApplyAttemptTransactionAsync(wrongShielded);
        Assert.True(receipt.TransitionResult!.IsShieldAbsorbed);
        Assert.Equal(0, receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(1, receipt.BaseAttackDamage);
        Assert.Equal(2, receipt.BoundCriticalStrikeRank);
        Assert.Equal(2, receipt.BoundOverdriveRank);
        Assert.Equal(0, receipt.CriticalBonusPercent);
        Assert.Equal(0, receipt.OverdriveBonusPercent);

        var modAfter = (await store.GetCombatModifierSnapshotAsync()).ModifierState;
        Assert.Equal(3, modAfter.CriticalCorrectAttackCount);
        Assert.Equal(0, modAfter.OverdriveStreak);
        Assert.Equal(0, modAfter.OverdriveRemainingBonusAttacks);
    }

    [Fact]
    public async Task GameOver_ResetsRunModifierCountersToZero_WhilePreservingPurchasedModifierRanks()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 2, overdrive_rank = 1, consecutive_early_defeats = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET player_current_hp = 2,
                critical_correct_attack_count = 7,
                overdrive_streak = 1,
                overdrive_remaining_bonus_attacks = 1
            WHERE id = 1;
            """);

        var fatalIntent = CreateIntent(
            "sub-gameover-reset",
            isCorrect: false,
            isEligible: true,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 2,
            boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(fatalIntent);

        var receipt = await store.ApplyAttemptTransactionAsync(fatalIntent);
        Assert.True(receipt.TransitionResult!.IsGameOver);
        Assert.Equal(1, receipt.BaseAttackDamage);
        Assert.Equal(2, receipt.BoundCriticalStrikeRank);
        Assert.Equal(1, receipt.BoundOverdriveRank);
        Assert.Equal(0, receipt.CriticalBonusPercent);
        Assert.Equal(0, receipt.OverdriveBonusPercent);

        var snapshotAfter = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(2, snapshotAfter.CriticalStrikeRank);
        Assert.Equal(1, snapshotAfter.OverdriveRank);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, snapshotAfter.ModifierState);
    }

    // =========================================================================
    // 4. Retroactive Purchase Protection & Corrupt Receipt Fail-Closed Checks
    // =========================================================================

    [Fact]
    public async Task QuestionBoundRankZero_CannotGainRetroactiveModifierProgressOrBonusAfterMidQuestionPurchase()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("UPDATE gameplay_progression SET cumulative_xp = 1000000, player_level = 80, earned_sp = 40, spent_sp = 0, available_sp = 40 WHERE id = 1;");

        // Question presented before purchase binds (0, 0).
        // Purchase happens while question is active, then intent is staged with its bound (0, 0) ranks.
        await store.PurchaseCriticalStrikeUpgradeAsync(0, 0);
        await store.PurchaseOverdriveUpgradeAsync(0, 0);

        var boundZeroIntent = CreateIntent(
            "sub-prebound-zero",
            isCorrect: true,
            isEligible: true,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(boundZeroIntent);

        var receipt = await store.ApplyAttemptTransactionAsync(boundZeroIntent);
        Assert.Equal(1, receipt.BaseAttackDamage);
        Assert.Equal(0, receipt.BoundCriticalStrikeRank);
        Assert.Equal(0, receipt.BoundOverdriveRank);
        Assert.Equal(0, receipt.CriticalBonusPercent);
        Assert.Equal(0, receipt.OverdriveBonusPercent);
        Assert.Equal(1, receipt.TransitionResult!.RequestedAttackDamage);

        // Neither Critical Strike nor Overdrive counters advance for a rank-0 bound question.
        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(1, snapshot.CriticalStrikeRank);
        Assert.Equal(1, snapshot.OverdriveRank);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, snapshot.ModifierState);
    }

    [Fact]
    public async Task IdempotentReplayAndCrashRecovery_PreserveModifierStateXpSpAndReceipts()
    {
        using var store = await OpenStoreAsync();
        using var learner = await OpenLearnerStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 1, overdrive_rank = 1, attack_rank = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET critical_correct_attack_count = 4, overdrive_streak = 0, overdrive_remaining_bonus_attacks = 0
            WHERE id = 1;
            """);

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var intent = CreateIntent(
            "sub-replay-recover",
            factId: "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 1);

        await store.StagePendingIntentAsync(intent);
        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-replay-recover", "add:1+1", true, 500, 1, 1))).IsSuccess);

        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(1, recovery.RecoveredCount);
        var firstReceipt = Assert.Single(recovery.Receipts);
        Assert.Equal(50, firstReceipt.CriticalBonusPercent);
        Assert.Equal(3, firstReceipt.TransitionResult!.RequestedAttackDamage);

        var runAfter = await store.GetRunStateAsync();
        var progAfter = await store.GetProgressionAsync();
        var modAfter = await store.GetCombatModifierSnapshotAsync();

        // Repeated consumption and direct replay return the identical receipt without extra XP/SP or modifier progress.
        var replayConsume = await consumer.ConsumeAttemptAsync("sub-replay-recover");
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, replayConsume.Status);
        Assert.Equal(firstReceipt, replayConsume.Receipt);

        var replayApply = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(firstReceipt, replayApply);

        Assert.Equal(runAfter, await store.GetRunStateAsync());
        Assert.Equal(progAfter, await store.GetProgressionAsync());
        Assert.Equal(modAfter, await store.GetCombatModifierSnapshotAsync());
    }

    [Fact]
    public async Task CorruptV7ReceiptColumns_FailClosedOnReadAndReopen()
    {
        using (var store = await OpenStoreAsync())
        {
            await ExecuteRawAsync("UPDATE gameplay_progression SET critical_strike_rank = 1, overdrive_rank = 1 WHERE id = 1;");
            var intent = CreateIntent("sub-corrupt-receipt", effectiveAttackDamage: 1, boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
            await store.StagePendingIntentAsync(intent);
            await store.ApplyAttemptTransactionAsync(intent);

            // Corrupt critical_bonus_percent so it mismatches requested_attack_damage (1 != ceil(1 * 150 / 100)).
            await ExecuteRawAsync("PRAGMA ignore_check_constraints = ON; UPDATE gameplay_receipt_ledger SET critical_bonus_percent = 50 WHERE submission_id = 'sub-corrupt-receipt';");
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-corrupt-receipt"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

            // Corrupt storage class to REAL (1.0) bypassing integer type check.
            await ExecuteRawAsync("PRAGMA ignore_check_constraints = ON; UPDATE gameplay_receipt_ledger SET critical_bonus_percent = 0, base_attack_damage = 'bad' WHERE submission_id = 'sub-corrupt-receipt';");
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-corrupt-receipt"));
        }

        using var reopened = new SqliteGameplayStore(GameplayDbPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.InitializeAsync());
    }

    [Fact]
    public async Task Ordering_EarlierPracticePosition_ReceivesCriticalStrikeTrigger_WhenTimestampsAreEqualAndGuidsReversed()
    {
        using var store = await OpenStoreAsync();
        using var learner = await OpenLearnerStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 1, attack_rank = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET critical_correct_attack_count = 4
            WHERE id = 1;
            """);

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var equalTime = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        // PracticePosition 1 ("sub-z-pos1") is the 5th correct attack and MUST receive the +50% Critical Strike bonus.
        // PracticePosition 2 ("sub-a-pos2") is the 6th correct attack and MUST receive 0% Critical Strike bonus.
        var intentPos1 = new CyberDefensePendingIntentRecord("sub-z-pos1", "add:1+1", true, true, 400, 0, equalTime, effectiveAttackDamage: 2, boundCriticalStrikeRank: 1, boundOverdriveRank: 0);
        var intentPos2 = new CyberDefensePendingIntentRecord("sub-a-pos2", "add:1+0", true, true, 450, 0, equalTime, effectiveAttackDamage: 2, boundCriticalStrikeRank: 1, boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(intentPos2);
        await store.StagePendingIntentAsync(intentPos1);

        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-z-pos1", "add:1+1", true, 400, 1, 1))).IsSuccess);
        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-a-pos2", "add:1+0", true, 450, 2, 2))).IsSuccess);

        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(2, recovery.RecoveredCount);
        Assert.Equal("sub-z-pos1", recovery.Receipts[0].SubmissionId);
        Assert.Equal(50, recovery.Receipts[0].CriticalBonusPercent);
        Assert.Equal(3, recovery.Receipts[0].TransitionResult!.RequestedAttackDamage);

        Assert.Equal("sub-a-pos2", recovery.Receipts[1].SubmissionId);
        Assert.Equal(0, recovery.Receipts[1].CriticalBonusPercent);
        Assert.Equal(2, recovery.Receipts[1].TransitionResult!.RequestedAttackDamage);
    }

    [Fact]
    public async Task Ordering_WrongAnswerAtEarlierPracticePosition_ResetsOverdriveBeforeLaterCorrectAnswer_WhenTimestampsAreEqualAndGuidsReversed()
    {
        using var store = await OpenStoreAsync();
        using var learner = await OpenLearnerStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET overdrive_rank = 1, attack_rank = 1
            WHERE id = 1;
            UPDATE gameplay_run_state
            SET overdrive_streak = 4, overdrive_remaining_bonus_attacks = 0
            WHERE id = 1;
            """);

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var equalTime = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        // PracticePosition 1 ("sub-z-wrong") is an INCORRECT answer -> resets Overdrive streak from 4 to 0.
        // PracticePosition 2 ("sub-a-correct") is a CORRECT answer -> advances Overdrive streak from 0 to 1 (does NOT arm Overdrive!).
        var intentPos1Wrong = new CyberDefensePendingIntentRecord("sub-z-wrong", "add:1+1", false, true, 400, 0, equalTime, effectiveAttackDamage: 2, boundCriticalStrikeRank: 0, boundOverdriveRank: 1);
        var intentPos2Correct = new CyberDefensePendingIntentRecord("sub-a-correct", "add:1+0", true, true, 450, 0, equalTime, effectiveAttackDamage: 2, boundCriticalStrikeRank: 0, boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(intentPos2Correct);
        await store.StagePendingIntentAsync(intentPos1Wrong);

        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-z-wrong", "add:1+1", false, 400, 1, 1))).IsSuccess);
        Assert.True((await learner.CommitSubmissionAsync(CreateLearnerChangeSet("sub-a-correct", "add:1+0", true, 450, 2, 2))).IsSuccess);

        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(2, recovery.RecoveredCount);
        Assert.Equal("sub-z-wrong", recovery.Receipts[0].SubmissionId);
        Assert.Equal("sub-a-correct", recovery.Receipts[1].SubmissionId);

        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(1, snapshot.ModifierState.OverdriveStreak);
        Assert.Equal(0, snapshot.ModifierState.OverdriveRemainingBonusAttacks);
    }
}
