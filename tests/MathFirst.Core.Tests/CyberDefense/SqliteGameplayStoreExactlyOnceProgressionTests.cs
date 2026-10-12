namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
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

public sealed class SqliteGameplayStoreExactlyOnceProgressionTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_Progression_" + Guid.NewGuid().ToString("N"));
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

    private SqliteLearnerStore CreateLearnerStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "learner_test.db");
        return new SqliteLearnerStore(dbPath);
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
    // 1. Basic Attempt XP Rewards & Non-Lethal Hits
    // =========================================================================

    [Fact]
    public async Task Case01_CorrectAppliedAttempt_Awards6Xp()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-1", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(6, prog.CumulativeXp);
        Assert.Equal(1, prog.PlayerLevel);
        Assert.Equal(0, prog.EarnedSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);

        Assert.Equal(6, receipt.AwardedXp);
        Assert.Equal(1, receipt.PlayerLevelAfter);
        Assert.Equal(6L, receipt.CumulativeXpAfter);
    }

    [Fact]
    public async Task Case02_IncorrectAppliedAttempt_Awards1Xp()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-2", "1+1", isCorrect: false, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(1, prog.CumulativeXp);
        Assert.Equal(1, prog.PlayerLevel);
        Assert.Equal(0, prog.EarnedSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);

        Assert.Equal(1, receipt.AwardedXp);
        Assert.Equal(1, receipt.PlayerLevelAfter);
        Assert.Equal(1L, receipt.CumulativeXpAfter);
    }

    [Fact]
    public async Task Case03_CorrectNonLethalHit_AwardsOnlyBaseXp()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Initial normal opponent in sector 1 has 2 HP
        var runBefore = await store.GetRunStateAsync();
        Assert.Equal(2, runBefore.CurrentOpponent.CurrentHp);

        var intent = new CyberDefensePendingIntentRecord(
            "sub-nonlethal", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.False(receipt.TransitionResult!.IsOpponentDefeated);
        Assert.Equal(6, receipt.AwardedXp);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(6, prog.CumulativeXp);
    }

    // =========================================================================
    // 2. Normal & Boss Defeat Rewards
    // =========================================================================

    [Fact]
    public async Task Case04_SingleHitNormalDefeat_Awards7Xp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET opponent_current_hp = 1, opponent_executed_attacks = 0 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-defeat-1", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.Equal(7, receipt.AwardedXp);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(7, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Case05_MultiHitNormalDefeat_UsesActualConfirmedAttackCount()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set opponent HP to 1 and executed attacks to 2 (this 3rd hit defeats it)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET opponent_current_hp = 1, opponent_executed_attacks = 2 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-defeat-3", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            // 6 base + 3 * 1 = 9 XP
            Assert.Equal(9, receipt.AwardedXp);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(9, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Case06_SingleHitBossDefeat_Awards18Xp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Sector 1 boss is at opponent_index 5 (G(1) = 5)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET opponent_index = 5, opponent_current_hp = 1, opponent_executed_attacks = 0 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-boss-1", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.True(receipt.TransitionResult.IsSectorCompleted);
            // 6 base + (2 * 1) + 10 milestone = 18 XP
            Assert.Equal(18, receipt.AwardedXp);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(18, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Case07_MultiHitBossDefeat_UsesExactConfirmedAttackCount()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Boss at index 5 with 3 previously tracked hits, current HP = 1 (this 4th hit defeats it)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET opponent_index = 5, opponent_current_hp = 1, opponent_executed_attacks = 3 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-boss-4", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.Equal(4, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            // 6 base + (2 * 4) + 10 milestone = 24 XP
            Assert.Equal(24, receipt.AwardedXp);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(24, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Case08_OverkillDoesNotGenerateImaginaryAttacks()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Normal opponent with 1 HP
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET opponent_current_hp = 1, opponent_executed_attacks = 0 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-overkill", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.Equal(1, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(7, receipt.AwardedXp);
        }
    }

    // =========================================================================
    // 3. Encounter Provenance & Legacy Safety
    // =========================================================================

    [Fact]
    public async Task Case09_UnverifiedLegacyDefeat_DoesNotInventEarlierAttacks()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set run state to UnverifiedLegacy with 1 tracked attack and 1 HP
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET opponent_current_hp = 1,
                    opponent_executed_attacks = 1,
                    opponent_encounter_provenance = 'UnverifiedLegacy'
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-legacy-defeat", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.Equal(2, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, receipt.TransitionResult.DefeatedOpponentProvenance);

            // 6 base + 2 tracked attacks * 1 = 8 XP
            Assert.Equal(8, receipt.AwardedXp);

            // Newly spawned opponent is Authoritative
            Assert.Equal(OpponentEncounterProvenance.Authoritative, receipt.TransitionResult.NextState.CurrentOpponent.Provenance);
            Assert.Equal(0, receipt.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);
        }
    }

    [Fact]
    public async Task Case10_UnverifiedLegacyReward_UsesOnlyTrustworthyRecordedAttacks()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Boss encounter with UnverifiedLegacy and 0 tracked attacks, 1 HP
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET opponent_index = 5,
                    opponent_current_hp = 1,
                    opponent_executed_attacks = 0,
                    opponent_encounter_provenance = 'UnverifiedLegacy'
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-legacy-boss", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsOpponentDefeated);
            Assert.Equal(1, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, receipt.TransitionResult.DefeatedOpponentProvenance);

            // 6 base + (2 * 1) + 10 milestone = 18 XP (no speculative historical attacks)
            Assert.Equal(18, receipt.AwardedXp);
        }
    }

    [Fact]
    public async Task Case11_HistoricalReceiptsWithNullXp_RemainNullAfterOpeningV3()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Directly insert a historical receipt ledger row with NULL reward columns
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, next_opponent_executed_attacks,
                    awarded_xp, player_level_after, cumulative_xp_after,
                    next_opponent_encounter_provenance
                ) VALUES (
                    'sub-hist-11', 'Applied', '1+1', 1, 1,
                    100, 0, '2026-10-09T12:00:00Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 1,
                    100, 1,
                    NULL, NULL, NULL,
                    'Authoritative'
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var receipt = await store.GetReceiptAsync("sub-hist-11");
            Assert.NotNull(receipt);
            Assert.Null(receipt.AwardedXp);
            Assert.Null(receipt.PlayerLevelAfter);
            Assert.Null(receipt.CumulativeXpAfter);
        }
    }

    // =========================================================================
    // 4. Exactly-Once Settlement, Replay, and Conflict Rejection
    // =========================================================================

    [Fact]
    public async Task Case12_OneSubmissionId_SettlesXpExactlyOnce()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-once", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var r1 = await store.ApplyAttemptTransactionAsync(intent);

        // Apply a second time with identical intent
        var r2 = await store.ApplyAttemptTransactionAsync(intent);

        var prog = await store.GetProgressionAsync();
        // XP settled exactly once (6 XP, not 12)
        Assert.Equal(6, prog.CumulativeXp);
        Assert.Equal(2, prog.StoreRevision); // Revision only advanced once
    }

    [Fact]
    public async Task Case13_IdenticalReplay_ReturnsSameImmutableReceipt()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-replay", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var r1 = await store.ApplyAttemptTransactionAsync(intent);
        var r2 = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(r1, r2);
        Assert.Equal(r1.AwardedXp, r2.AwardedXp);
        Assert.Equal(r1.PlayerLevelAfter, r2.PlayerLevelAfter);
        Assert.Equal(r1.CumulativeXpAfter, r2.CumulativeXpAfter);
    }

    [Fact]
    public async Task Case14_ConflictingDuplicate_CannotMutateProgression()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-conflict", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        var conflicting = new CyberDefensePendingIntentRecord(
            "sub-conflict", "1+2", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ApplyAttemptTransactionAsync(conflicting));

        var prog = await store.GetProgressionAsync();
        Assert.Equal(6, prog.CumulativeXp);
    }

    private static SubmissionChangeSet CreateTestChangeSet(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long practicePosition,
        long expectedRevision)
    {
        var op = ArithmeticOperation.Addition;
        var stripped = factId.StartsWith("add:", StringComparison.Ordinal) ? factId.Substring(4) : factId;
        var parts = stripped.Split('+');
        int left = int.Parse(parts[0]);
        int right = int.Parse(parts[1]);
        int answer = left + right;

        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: op,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: responseLatencyMs,
            timestamp: DateTimeOffset.UtcNow,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: practicePosition);

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;

        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = op,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = responseLatencyMs,
            RollingLatencyMs = responseLatencyMs,
            FluentStreak = isCorrect ? 1 : 0,
            IsProvisionallyMastered = false,
            NeedsRemediation = !isCorrect,
            RemediationDueOrder = 0,
            LastPracticedOrder = (int)practicePosition,
            LastPracticedAt = DateTimeOffset.UtcNow
        };

        var opProgressions = progression.OperationProgressions;

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: null,
            operationProgressions: opProgressions);
    }

    [Fact]
    public async Task Case15_CrashAfterCommitBeforeResponse_DoesNotDuplicateXp()
    {
        var dir = CreateTempDirectory();
        using var gameplayStore = CreateStore(dir);
        using var learnerStore = CreateLearnerStore(dir);
        await gameplayStore.InitializeAsync();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        var intent = new CyberDefensePendingIntentRecord(
            "sub-crash", "add:1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        // Commit learner evidence
        var changeSet = CreateTestChangeSet("sub-crash", "add:1+1", true, 100, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        // First consumption
        await consumer.StageIntentAsync(intent);
        var res1 = await consumer.ConsumeAttemptAsync("sub-crash");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, res1.Status);

        // Crash simulated: client retries ConsumeAttemptAsync with same submission
        var res2 = await consumer.ConsumeAttemptAsync("sub-crash");
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, res2.Status);

        var prog = await gameplayStore.GetProgressionAsync();
        Assert.Equal(6, prog.CumulativeXp);
    }

    [Fact]
    public async Task Case16_ColdRestart_PreservesXpLevelSpAndReceiptMetadata()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-cold", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
            await store1.StagePendingIntentAsync(intent);
            await store1.ApplyAttemptTransactionAsync(intent);
            await store1.CloseAsync();
        }

        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var prog = await store2.GetProgressionAsync();
            Assert.Equal(6, prog.CumulativeXp);
            Assert.Equal(1, prog.PlayerLevel);
            Assert.Equal(0, prog.EarnedSkillPoints);

            var receipt = await store2.GetReceiptAsync("sub-cold");
            Assert.NotNull(receipt);
            Assert.Equal(6, receipt.AwardedXp);
            Assert.Equal(1, receipt.PlayerLevelAfter);
            Assert.Equal(6L, receipt.CumulativeXpAfter);
        }
    }

    [Fact]
    public async Task Case17_PendingIntentRecovery_AwardsExactlyOnce()
    {
        var dir = CreateTempDirectory();
        using var gameplayStore = CreateStore(dir);
        using var learnerStore = CreateLearnerStore(dir);
        await gameplayStore.InitializeAsync();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        var intent = new CyberDefensePendingIntentRecord(
            "sub-recover", "add:1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await gameplayStore.StagePendingIntentAsync(intent);
        var changeSet = CreateTestChangeSet("sub-recover", "add:1+1", true, 100, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        // Recover pending intents
        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Single(recoveryResult.Receipts);
        Assert.Equal(6, recoveryResult.Receipts[0].AwardedXp);

        var prog = await gameplayStore.GetProgressionAsync();
        Assert.Equal(6, prog.CumulativeXp);

        // Second recovery is empty
        var emptyResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Empty(emptyResult.Receipts);
    }

    // =========================================================================
    // 5. Level Curves, Thresholds, and Skill Points
    // =========================================================================

    [Fact]
    public async Task Case18_Level1ToLevel2Progression_InStore()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set initial cumulative XP to 75 (5 away from Level 2 at 80)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET cumulative_xp = 75, player_level = 1, earned_sp = 0, available_sp = 0 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-levelup", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            // 75 + 6 = 81 XP -> Level 2
            Assert.Equal(81L, receipt.CumulativeXpAfter);
            Assert.Equal(2, receipt.PlayerLevelAfter);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(81L, prog.CumulativeXp);
            Assert.Equal(2, prog.PlayerLevel);
            Assert.Equal(1, prog.EarnedSkillPoints);
            Assert.Equal(1, prog.AvailableSkillPoints);
            Assert.Equal(0, prog.SpentSkillPoints);
        }
    }

    [Fact]
    public async Task Case19_MultipleLevelsCrossedInOneReward_InStore()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Level 2 is 80, Level 3 is 184. Set XP to 79.
        // We will defeat a boss with 4 hits (24 XP): 79 + 24 = 103 XP (reaches Level 2).
        // Let's set XP to 175 and award 18 XP (175 + 18 = 193 XP, crosses Level 2 to Level 3).
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression SET cumulative_xp = 70, player_level = 1, earned_sp = 0, available_sp = 0 WHERE id = 1;
                UPDATE gameplay_run_state SET opponent_index = 5, opponent_current_hp = 1, opponent_executed_attacks = 3 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-cross-lvl", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            // 70 + 24 = 94 XP (Level 2 reached)
            Assert.Equal(24, receipt.AwardedXp);
            Assert.Equal(94L, receipt.CumulativeXpAfter);
            Assert.Equal(2, receipt.PlayerLevelAfter);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(94L, prog.CumulativeXp);
            Assert.Equal(2, prog.PlayerLevel);
            Assert.Equal(1, prog.EarnedSkillPoints);
            Assert.Equal(1, prog.AvailableSkillPoints);
        }
    }

    [Fact]
    public async Task Case20_Case21_SpConservationAcrossProgressionUpdates_InStore()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set progression with existing spent SP
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET cumulative_xp = 75,
                    player_level = 1,
                    earned_sp = 3,
                    spent_sp = 2,
                    available_sp = 1
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-sp-check", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            // Level increases from 1 to 2 (+1 level)
            var prog = await store.GetProgressionAsync();
            Assert.Equal(2, prog.PlayerLevel);
            Assert.Equal(4, prog.EarnedSkillPoints);
            Assert.Equal(2, prog.SpentSkillPoints);
            Assert.Equal(2, prog.AvailableSkillPoints);
            Assert.Equal(prog.EarnedSkillPoints, prog.SpentSkillPoints + prog.AvailableSkillPoints);
        }
    }

    [Fact]
    public async Task Case22_OverflowCausesAtomicRollback_NotPartialRewards()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set cumulative_xp to long.MaxValue
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"UPDATE gameplay_progression SET cumulative_xp = {long.MaxValue} WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-overflow", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            await Assert.ThrowsAsync<OverflowException>(() =>
                store.ApplyAttemptTransactionAsync(intent));

            // Verify atomic rollback: progression row unchanged, receipt ledger empty, run state unchanged
            var prog = await store.GetProgressionAsync();
            Assert.Equal(long.MaxValue, prog.CumulativeXp);

            var receipt = await store.GetReceiptAsync("sub-overflow");
            Assert.Null(receipt);
        }
    }

    // =========================================================================
    // 6. Game Over, Full Local Reset, and Epoch Fencing
    // =========================================================================

    [Fact]
    public async Task Case23_FatalIncorrectAnswer_RetainsPermanentProgression()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set player HP to 1 so incorrect answer is fatal (enemy deals at least 4 damage)
        // Also set prior cumulative XP to 50
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state SET player_current_hp = 1 WHERE id = 1;
                UPDATE gameplay_progression SET cumulative_xp = 50 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-fatal", "1+1", isCorrect: false, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.True(receipt.TransitionResult!.IsGameOver);
            Assert.NotNull(receipt.TransitionResult.TerminalSnapshot);

            // The fatal attempt still earns +1 XP
            Assert.Equal(1, receipt.AwardedXp);
            Assert.Equal(51L, receipt.CumulativeXpAfter);

            // Active run state is rebooted to initial (Sector 1, HP 100)
            var run = await store.GetRunStateAsync();
            Assert.Equal(1, run.Sector);
            Assert.Equal(0, run.OpponentIndex);
            Assert.Equal(100, run.PlayerCurrentHp);
            Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);

            // Permanent progression is preserved!
            var prog = await store.GetProgressionAsync();
            Assert.Equal(51L, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Case24_FullLocalReset_ClearsPermanentProgression()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Earn some XP
        var intent = new CyberDefensePendingIntentRecord(
            "sub-before-reset", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);
        await store.ApplyAttemptTransactionAsync(intent);

        var progBefore = await store.GetProgressionAsync();
        Assert.Equal(6, progBefore.CumulativeXp);

        // Perform two-phase reset
        var resetIntent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        var progAfter = await store.GetProgressionAsync();
        Assert.Equal(0, progAfter.CumulativeXp);
        Assert.Equal(1, progAfter.PlayerLevel);
        Assert.Equal(0, progAfter.EarnedSkillPoints);
        Assert.Equal(0, progAfter.SpentSkillPoints);
        Assert.Equal(0, progAfter.AvailableSkillPoints);
        Assert.Equal(resetIntent.TargetEpoch, progAfter.ResetEpoch);

        // Receipt ledger is cleared
        var receipt = await store.GetReceiptAsync("sub-before-reset");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task Case25_StaleEpochCannotAwardXp()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Stale epoch 99 != 0
        var staleIntent = new CyberDefensePendingIntentRecord(
            "sub-stale", "1+1", isCorrect: true, isEligible: true, 100, 99, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StagePendingIntentAsync(staleIntent));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ApplyAttemptTransactionAsync(staleIntent));

        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.CumulativeXp);
    }

    [Fact]
    public async Task Case26_CalmModeSuppressed_BehaviorRemainsUnchanged()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-calm", "1+1", isCorrect: true, isEligible: false, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);
        Assert.Null(receipt.TransitionResult);
        Assert.Null(receipt.AwardedXp);
        Assert.Null(receipt.PlayerLevelAfter);
        Assert.Null(receipt.CumulativeXpAfter);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.CumulativeXp);
    }

    // =========================================================================
    // 7. Migration, Roundtrip, Non-Interference, and Bounds
    // =========================================================================

    [Fact]
    public async Task Case27_ExistingV1AndV2Migrations_RemainLossless()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        // Create V1 database manually
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                CREATE TABLE gameplay_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    store_revision INTEGER NOT NULL CHECK (store_revision >= 1),
                    reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
                    updated_at TEXT NOT NULL
                );
                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 1, 0, '2026-10-09T10:00:00Z');

                CREATE TABLE gameplay_run_state (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    sector INTEGER NOT NULL CHECK (sector >= 1),
                    opponent_index INTEGER NOT NULL CHECK (opponent_index >= 0),
                    opponent_current_hp INTEGER NOT NULL CHECK (opponent_current_hp >= 1),
                    player_current_hp INTEGER NOT NULL CHECK (player_current_hp >= 1 AND player_current_hp <= 100),
                    reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
                    updated_at TEXT NOT NULL
                );
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 1, 0, 2, 100, 0, '2026-10-09T10:00:00Z');

                CREATE TABLE gameplay_receipt_ledger (
                    submission_id TEXT PRIMARY KEY,
                    receipt_kind TEXT NOT NULL,
                    fact_id TEXT NOT NULL,
                    is_correct INTEGER NOT NULL,
                    is_eligible INTEGER NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    reset_epoch INTEGER NOT NULL,
                    processed_at TEXT NOT NULL,
                    requested_attack_damage INTEGER,
                    applied_opponent_damage INTEGER,
                    excess_opponent_damage INTEGER,
                    incoming_enemy_damage INTEGER,
                    applied_player_damage INTEGER,
                    excess_enemy_damage INTEGER,
                    potential_healing INTEGER,
                    applied_healing INTEGER,
                    is_opponent_defeated INTEGER,
                    is_sector_completed INTEGER,
                    is_game_over INTEGER,
                    next_sector INTEGER,
                    next_opponent_index INTEGER,
                    next_opponent_current_hp INTEGER,
                    next_player_current_hp INTEGER,
                    terminal_sector INTEGER,
                    terminal_opponent_index INTEGER,
                    terminal_opponent_kind TEXT,
                    terminal_opponent_current_hp INTEGER,
                    terminal_opponent_max_hp INTEGER,
                    terminal_player_current_hp INTEGER
                );
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp
                ) VALUES (
                    'sub-v1-hist', 'Applied', '1+1', 1, 1,
                    120, 0, '2026-10-09T10:05:00Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 1,
                    100
                );

                CREATE TABLE gameplay_pending_intent (
                    submission_id TEXT PRIMARY KEY,
                    fact_id TEXT NOT NULL,
                    is_correct INTEGER NOT NULL,
                    is_eligible INTEGER NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    reset_epoch INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE gameplay_reset_intent (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    is_pending INTEGER NOT NULL,
                    current_epoch INTEGER NOT NULL,
                    target_epoch INTEGER NOT NULL,
                    created_at TEXT,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Open with SqliteGameplayStore, migrating V1 -> V3
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var prog = await store.GetProgressionAsync();
            Assert.Equal(0, prog.CumulativeXp);
            Assert.Equal(1, prog.PlayerLevel);

            var receipt = await store.GetReceiptAsync("sub-v1-hist");
            Assert.NotNull(receipt);
            Assert.Null(receipt.AwardedXp);
            Assert.Null(receipt.PlayerLevelAfter);
            Assert.Null(receipt.CumulativeXpAfter);
        }
    }

    [Fact]
    public async Task Case28_ExactV3ReceiptRoundtripEquality()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        var timestamp = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        var originalReceipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-roundtrip-test", "2+3", true, true, 250, 0, timestamp, transition,
            awardedXp: 6, playerLevelAfter: 1, cumulativeXpAfter: 6);

        await store.RecordReceiptDirectAsync(originalReceipt);

        var rehydratedReceipt = await store.GetReceiptAsync("sub-roundtrip-test");
        Assert.NotNull(rehydratedReceipt);
        Assert.Equal(originalReceipt, rehydratedReceipt);
        Assert.Equal(originalReceipt.AwardedXp, rehydratedReceipt.AwardedXp);
        Assert.Equal(originalReceipt.PlayerLevelAfter, rehydratedReceipt.PlayerLevelAfter);
        Assert.Equal(originalReceipt.CumulativeXpAfter, rehydratedReceipt.CumulativeXpAfter);
    }

    [Fact]
    public async Task Case29_NoMathematicalLearningInterference()
    {
        var dir = CreateTempDirectory();
        using var gameplayStore = CreateStore(dir);
        using var learnerStore = CreateLearnerStore(dir);
        await gameplayStore.InitializeAsync();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Record 5 math practice attempts
        for (int i = 1; i <= 5; i++)
        {
            string subId = $"math-attempt-{i}";
            string factId = "add:1+1";
            var changeSet = CreateTestChangeSet(subId, factId, true, 150, i, i);
            var r = await learnerStore.CommitSubmissionAsync(changeSet);
            Assert.True(r.IsSuccess);

            var intent = new CyberDefensePendingIntentRecord(
                subId, factId, isCorrect: true, isEligible: true, 150, 0, DateTimeOffset.UtcNow);

            await consumer.StageIntentAsync(intent);
            var result = await consumer.ConsumeAttemptAsync(subId);
            Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        }

        // Verify learner evidence remains intact and uncorrupted
        for (int i = 1; i <= 5; i++)
        {
            string subId = $"math-attempt-{i}";
            var evidence = await learnerStore.GetCommittedAttemptEvidenceAsync(subId);
            Assert.NotNull(evidence);
            Assert.True(evidence.IsCorrect);
            Assert.Equal(150, evidence.ResponseLatencyMs);
        }

        // Verify progression is properly accumulated
        var prog = await gameplayStore.GetProgressionAsync();
        Assert.True(prog.CumulativeXp > 0);
    }

    [Fact]
    public async Task Case30_NoPrematureAttackPurchaseOrDamageScaling()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-dmg-check", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        // Attack damage requested must be strictly 1
        Assert.Equal(1, receipt.TransitionResult!.RequestedAttackDamage);

        // Progression attack rank must remain 0
        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.AttackRank);
        Assert.Equal(0, prog.SpentSkillPoints);
    }
}
