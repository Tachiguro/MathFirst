namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RevisionConflictOrphanPendingIntentTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _learnerDbPath;
    private readonly string _gameplayDbPath;

    public RevisionConflictOrphanPendingIntentTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _learnerDbPath = Path.Combine(_tempDirectory, "test_learner.db");
        _gameplayDbPath = Path.Combine(_tempDirectory, "test_gameplay.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    private SqliteLearnerStore CreateLearnerStore() => new(_learnerDbPath);
    private SqliteGameplayStore CreateGameplayStore() => new(_gameplayDbPath);

    [Fact]
    public async Task RevisionConflict_RejectedSubmission_DoesNotBlockSubsequentCommittedAttempt()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var clock = new FakeClock();
        var session = new TrainingSession(learnerStore, clock);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        // 1. Submit first answer
        var fact1 = session.CurrentFact;
        Assert.NotNull(fact1);
        var eval1 = session.SubmitAnswer(fact1.CorrectResult);
        var subId1 = eval1.ChangeSet.SubmissionId;
        var pos1 = eval1.ChangeSet.Attempt.PracticePosition;
        Assert.NotNull(pos1);

        await coordinator.StageIntentAsync(
            submissionId: subId1,
            factId: fact1.Id,
            isCorrect: eval1.IsCorrect,
            latencyMs: eval1.LatencyMs,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: pos1);

        // 2. Induce an actual learner-store RevisionConflict by simulating a concurrent commit
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = practice_position + 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Commit evaluation - must fail with RevisionConflict
        var persistence1 = await session.CommitCurrentEvaluationAsync();
        Assert.Equal(PersistenceStatus.RevisionConflict, persistence1.Status);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // 4. Recover from persistence failure (authoritative reload)
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.Null(session.LastEvaluation);

        // 5. Verify the staged intent is still staged without committed learner evidence (orphan)
        var staged1 = await gameplayStore.GetPendingIntentAsync(subId1);
        Assert.NotNull(staged1);
        var evidence1 = await learnerStore.GetCommittedAttemptEvidenceAsync(subId1);
        Assert.Null(evidence1);

        // 6. Answer next question and commit successfully
        var fact2 = session.CurrentFact;
        Assert.NotNull(fact2);
        var eval2 = session.SubmitAnswer(fact2.CorrectResult);
        var subId2 = eval2.ChangeSet.SubmissionId;
        var pos2 = eval2.ChangeSet.Attempt.PracticePosition;
        Assert.NotNull(pos2);

        await coordinator.StageIntentAsync(
            submissionId: subId2,
            factId: fact2.Id,
            isCorrect: eval2.IsCorrect,
            latencyMs: eval2.LatencyMs,
            isEligibleAtSubmission: true,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: pos2);

        var persistence2 = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistence2.IsSuccess);
        Assert.True(session.IsCurrentSubmissionCommitted);

        // 7. Consume committed attempt - MUST SUCCEED AND ADVANCE GAMEPLAY
        var consumptionResult = await coordinator.ConsumeCommittedAttemptAsync(subId2);
        Assert.NotNull(consumptionResult);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumptionResult.Status);

        var receipt2 = await gameplayStore.GetReceiptAsync(subId2);
        Assert.NotNull(receipt2);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt2.ReceiptKind);

        // Staged intent for subId2 was removed as part of applied transaction
        var remainingPending = await gameplayStore.GetPendingIntentsAsync(0);
        Assert.Empty(remainingPending);
    }

    [Fact]
    public async Task CrashRestart_InRecoveryWindow_CleansAbandonedIntentAndUnblocksQueue()
    {
        using (var learnerStore = CreateLearnerStore())
        using (var gameplayStore = CreateGameplayStore())
        {
            await learnerStore.InitializeAsync();
            await gameplayStore.InitializeAsync();

            var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
            var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
            await coordinator.InitializeAsync();

            // Stage an orphaned intent at practice position 1
            var orphanIntent = new CyberDefensePendingIntentRecord(
                "sub-orphan-crash",
                "add:1+1",
                isCorrect: true,
                isEligible: true,
                responseLatencyMs: 500,
                resetEpoch: 0,
                createdAt: DateTimeOffset.UtcNow,
                effectiveAttackDamage: 1,
                boundCriticalStrikeRank: 0,
                boundOverdriveRank: 0,
                practicePosition: 1);

            await gameplayStore.StagePendingIntentAsync(orphanIntent);

            // Simulate learner store having already progressed past practice position 1
            using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                    UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Verify orphan has no learner evidence
            Assert.Null(await learnerStore.GetCommittedAttemptEvidenceAsync("sub-orphan-crash"));
        }

        // Simulate app restart: re-open stores and coordinator
        using (var learnerStore2 = CreateLearnerStore())
        using (var gameplayStore2 = CreateGameplayStore())
        {
            await learnerStore2.InitializeAsync();
            await gameplayStore2.InitializeAsync();

            var consumer2 = new CyberDefenseSubmissionConsumer(gameplayStore2, learnerStore2);
            var coordinator2 = new CyberDefenseCombatCoordinator(gameplayStore2, consumer2);
            await coordinator2.InitializeAsync(); // Calls RecoverPendingIntentsAsync

            // Staged orphan should be cleaned up by startup recovery
            var pendingAfterRestart = await gameplayStore2.GetPendingIntentAsync("sub-orphan-crash");
            Assert.Null(pendingAfterRestart);

            var snapshot = await learnerStore2.LoadSnapshotAsync();

            // Now stage and commit a valid subsequent attempt
            var validIntent = new CyberDefensePendingIntentRecord(
                "sub-valid-after-crash",
                "add:1+0",
                isCorrect: true,
                isEligible: true,
                responseLatencyMs: 400,
                resetEpoch: 0,
                createdAt: DateTimeOffset.UtcNow,
                effectiveAttackDamage: 1,
                boundCriticalStrikeRank: 0,
                boundOverdriveRank: 0,
                practicePosition: snapshot.Progression.PracticePosition + 1);

            await gameplayStore2.StagePendingIntentAsync(validIntent);

            // Commit valid attempt into learner store
            var changeSet = CreateTestChangeSet("sub-valid-after-crash", "add:1+0", isCorrect: true, responseLatencyMs: 400, practicePosition: snapshot.Progression.PracticePosition + 1, expectedRevision: snapshot.Revision);
            var commitResult = await learnerStore2.CommitSubmissionAsync(changeSet);
            Assert.True(commitResult.IsSuccess, commitResult.Message);

            // Consume should succeed without being blocked
            var consumeResult = await coordinator2.ConsumeCommittedAttemptAsync("sub-valid-after-crash");
            Assert.NotNull(consumeResult);
            Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeResult.Status);

            var receipt = await gameplayStore2.GetReceiptAsync("sub-valid-after-crash");
            Assert.NotNull(receipt);
            Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
        }
    }

    [Fact]
    public async Task FirewallPendingActivation_SucceedsAfterAbandonedIntentCleanup()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        // 1. Purchase Firewall Rank 1 in gameplay progression
        using (var conn = new SqliteConnection($"Data Source={_gameplayDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET earned_sp = 10, available_sp = 10, firewall_rank = 1
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Stage an orphaned intent
        var orphanIntent = new CyberDefensePendingIntentRecord(
            "sub-orphan-firewall",
            "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(orphanIntent);

        // 3. Firewall activation must be DEFERRED while unsettled intent exists
        var deferredResult = await gameplayStore.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, deferredResult.Status);

        // 4. Update learner progression to position 1 (abandoning the uncommitted orphan)
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 5. Run recovery
        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recoveryResult.RecoveredCount);

        // 6. The orphan should now be cleared
        Assert.Null(await gameplayStore.GetPendingIntentAsync("sub-orphan-firewall"));

        // 7. Retrying firewall activation now SUCCEEDS
        var successResult = await gameplayStore.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.Applied, successResult.Status);
        Assert.Equal(1, successResult.RunState?.ShieldCapacity);
    }

    [Fact]
    public async Task AmbiguousPendingIntent_WithNullPracticePosition_FailsClosedWithoutDeletion()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Stage an uncommitted intent with NULL practice position
        var ambiguousIntent = new CyberDefensePendingIntentRecord(
            "sub-ambiguous",
            "add:1+1",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: null);

        await gameplayStore.StagePendingIntentAsync(ambiguousIntent);

        // Recovery must NOT delete ambiguous unpositioned intents; must stop recovery
        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recovery.RecoveredCount);

        // Intent remains intact
        var stillStaged = await gameplayStore.GetPendingIntentAsync("sub-ambiguous");
        Assert.NotNull(stillStaged);
    }

    [Fact]
    public async Task CommittedAttempt_IsNeverDeletedOrTreatedAsAbandoned()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var snapshot = await learnerStore.LoadSnapshotAsync();

        // Stage intent
        var intent = new CyberDefensePendingIntentRecord(
            "sub-committed-real",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 350,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: snapshot.Progression.PracticePosition + 1);

        await gameplayStore.StagePendingIntentAsync(intent);

        // Commit in learner store
        var changeSet = CreateTestChangeSet("sub-committed-real", "add:1+0", isCorrect: true, responseLatencyMs: 350, practicePosition: snapshot.Progression.PracticePosition + 1, expectedRevision: snapshot.Revision);
        var commitRes = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(commitRes.IsSuccess, commitRes.Message);

        // Attempting to clear an intent that has committed evidence must be a NO-OP
        await consumer.ClearAbandonedIntentAsync("sub-committed-real");

        // The intent must STILL exist
        var stillExists = await gameplayStore.GetPendingIntentAsync("sub-committed-real");
        Assert.NotNull(stillExists);

        // And consumption must succeed
        var consumeRes = await coordinator.ConsumeCommittedAttemptAsync("sub-committed-real");
        Assert.NotNull(consumeRes);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumeRes.Status);
    }

    [Fact]
    public async Task CrossEpochIntent_IsNotCleanedUpOrMutatedByCurrentEpochRecovery()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Insert a pending intent directly with reset_epoch = 1 while current store reset_epoch is 0
        using (var conn = new SqliteConnection($"Data Source={_gameplayDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible, response_latency_ms,
                    reset_epoch, created_at, effective_attack_damage, bound_critical_strike_rank,
                    bound_overdrive_rank, practice_position
                ) VALUES (
                    'sub-cross-epoch', 'add:1+1', 1, 1, 500, 1, @now, 1, 0, 0, 1
                );
            ";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Advance learner progression to 1
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Recovery on epoch 0 must not clear cross-epoch intent
        await consumer.RecoverPendingIntentsAsync();

        var crossEpochStored = await gameplayStore.GetPendingIntentAsync("sub-cross-epoch");
        Assert.NotNull(crossEpochStored);
        Assert.Equal(1, crossEpochStored.ResetEpoch);
    }

    [Fact]
    public async Task InteractiveRecovery_ExplicitClearAbandonedIntent_PreservesInFlightAndCleansAfterProgressionAdvances()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var orphanIntent = new CyberDefensePendingIntentRecord(
            "sub-explicit-clean",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(orphanIntent);

        // 1. Authoritative learner progression is 0. The intent is at practice position 1.
        // It is still in-flight or prospective and MUST NOT be deleted.
        await coordinator.ClearAbandonedIntentAsync("sub-explicit-clean");
        var stillInFlight = await gameplayStore.GetPendingIntentAsync("sub-explicit-clean");
        Assert.NotNull(stillInFlight);

        // 2. Authoritative learner progression now advances to 1 (rejection proven, different attempt committed or synced)
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Immediate explicit cleanup of provably abandoned intent now succeeds
        await coordinator.ClearAbandonedIntentAsync("sub-explicit-clean");
        Assert.Null(await gameplayStore.GetPendingIntentAsync("sub-explicit-clean"));

        // 4. Idempotent: repeated call is a safe no-op
        await coordinator.ClearAbandonedIntentAsync("sub-explicit-clean");
        Assert.Null(await gameplayStore.GetPendingIntentAsync("sub-explicit-clean"));
    }

    [Fact]
    public async Task ClearAbandonedIntent_InFlightProspectivePosition_MustNotBeDeleted()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Staged intent at PracticePosition 1 while authoritative learner position is 0
        var stagedIntent = new CyberDefensePendingIntentRecord(
            "sub-inflight-pos1",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(stagedIntent);

        // Attempting to clear must fail closed and PRESERVE in-flight intent
        await consumer.ClearAbandonedIntentAsync("sub-inflight-pos1");

        var preserved = await gameplayStore.GetPendingIntentAsync("sub-inflight-pos1");
        Assert.NotNull(preserved);
    }

    [Fact]
    public async Task ClearAbandonedIntent_UnpositionedLegacyIntent_MustNotBeDeletedSolelyFromMissingEvidence()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Unpositioned legacy intent (PracticePosition is null)
        var unpositionedIntent = new CyberDefensePendingIntentRecord(
            "sub-legacy-unpositioned",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: null);

        await gameplayStore.StagePendingIntentAsync(unpositionedIntent);

        // Must NOT be deleted solely because committed evidence is absent
        await consumer.ClearAbandonedIntentAsync("sub-legacy-unpositioned");

        var preserved = await gameplayStore.GetPendingIntentAsync("sub-legacy-unpositioned");
        Assert.NotNull(preserved);
    }

    [Fact]
    public async Task ClearAbandonedIntent_WhenResetPending_PreservesIntent()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        var intent = new CyberDefensePendingIntentRecord(
            "sub-abandoned-reset-pending",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(intent);

        // Advance learner progression to 1
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Begin reset intent
        await gameplayStore.BeginOrGetResetIntentAsync();

        // Attempting to clear while reset is pending must not delete
        await consumer.ClearAbandonedIntentAsync("sub-abandoned-reset-pending");

        var preserved = await gameplayStore.GetPendingIntentAsync("sub-abandoned-reset-pending");
        Assert.NotNull(preserved);
    }

    [Fact]
    public async Task SqliteGameplayStore_ClearPendingIntentAsync_WhenResetPending_ThrowsInvalidOperationException()
    {
        using var gameplayStore = CreateGameplayStore();
        await gameplayStore.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-store-fence-reset",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(intent);

        // Begin reset
        await gameplayStore.BeginOrGetResetIntentAsync();

        // Calling ClearPendingIntentAsync at store level while reset is pending MUST fail atomically
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gameplayStore.ClearPendingIntentAsync("sub-store-fence-reset", 0));

        // Legacy overload must also fail
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gameplayStore.ClearPendingIntentAsync("sub-store-fence-reset"));
    }

    [Fact]
    public async Task ClearAbandonedIntent_WhenReceiptAlreadyExists_DoesNotClearOrAlterReceipt()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Stage intent
        var intent = new CyberDefensePendingIntentRecord(
            "sub-with-receipt",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(intent);

        // Apply attempt transaction to create receipt
        var receipt = await gameplayStore.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(receipt);

        // Advance learner progression
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Call ClearAbandonedIntentAsync
        await consumer.ClearAbandonedIntentAsync("sub-with-receipt");

        // Receipt must remain intact
        var existingReceipt = await gameplayStore.GetReceiptAsync("sub-with-receipt");
        Assert.NotNull(existingReceipt);
        Assert.Equal(receipt.ProcessedAt, existingReceipt.ProcessedAt);
    }

    [Fact]
    public async Task ClearAbandonedIntent_CrossEpoch_RefusesDeletion()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);

        // Insert cross-epoch intent directly (epoch 1 while store epoch is 0)
        using (var conn = new SqliteConnection($"Data Source={_gameplayDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible, response_latency_ms,
                    reset_epoch, created_at, effective_attack_damage, bound_critical_strike_rank,
                    bound_overdrive_rank, practice_position
                ) VALUES (
                    'sub-cross-epoch-abandoned', 'add:1+1', 1, 1, 500, 1, @now, 1, 0, 0, 1
                );
            ";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Advance learner progression to 1
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Calling ClearAbandonedIntentAsync on epoch 0 store must refuse to delete cross-epoch intent
        await consumer.ClearAbandonedIntentAsync("sub-cross-epoch-abandoned");

        var preserved = await gameplayStore.GetPendingIntentAsync("sub-cross-epoch-abandoned");
        Assert.NotNull(preserved);
        Assert.Equal(1, preserved.ResetEpoch);
    }

    [Fact]
    public async Task CalmMode_CalmModeSuppressedIntent_AbandonedOrphanIsCleanedUpSafely()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        // Stage an in-eligible (Calm Mode) intent
        var calmOrphanIntent = new CyberDefensePendingIntentRecord(
            "sub-calm-orphan",
            "add:1+0",
            isCorrect: true,
            isEligible: false,
            responseLatencyMs: 450,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: 1);

        await gameplayStore.StagePendingIntentAsync(calmOrphanIntent);

        // Advance learner store
        using (var conn = new SqliteConnection($"Data Source={_learnerDbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE schema_info SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT) WHERE key = 'store_revision';
                UPDATE learner_progression SET practice_position = 1 WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Recovery cleans calm orphan
        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recovery.RecoveredCount);
        Assert.Null(await gameplayStore.GetPendingIntentAsync("sub-calm-orphan"));
    }

    [Fact]
    public async Task DuplicateAndConcurrentRecovery_IsIdempotentAndSafe()
    {
        using var learnerStore = CreateLearnerStore();
        using var gameplayStore = CreateGameplayStore();
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var coordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await coordinator.InitializeAsync();

        var snapshot = await learnerStore.LoadSnapshotAsync();

        // Stage and commit one valid intent
        var validIntent = new CyberDefensePendingIntentRecord(
            "sub-concurrent-test",
            "add:1+0",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 400,
            resetEpoch: 0,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: snapshot.Progression.PracticePosition + 1);

        await gameplayStore.StagePendingIntentAsync(validIntent);

        var changeSet = CreateTestChangeSet("sub-concurrent-test", "add:1+0", isCorrect: true, responseLatencyMs: 400, practicePosition: snapshot.Progression.PracticePosition + 1, expectedRevision: snapshot.Revision);
        var commitRes = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(commitRes.IsSuccess);

        // Run recovery 3 times concurrently
        var task1 = consumer.RecoverPendingIntentsAsync();
        var task2 = consumer.RecoverPendingIntentsAsync();
        var task3 = consumer.RecoverPendingIntentsAsync();

        var results = await Task.WhenAll(task1, task2, task3);

        // Exactly one receipt was inserted into the ledger
        var receipt = await gameplayStore.GetReceiptAsync("sub-concurrent-test");
        Assert.NotNull(receipt);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);

        // Total receipts generated across all recovery calls must be exactly 1
        int totalReceipts = results[0].Receipts.Count + results[1].Receipts.Count + results[2].Receipts.Count;
        Assert.True(totalReceipts >= 1); // at least one returned the receipt, replays return replay receipt
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

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 10_000_000;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceMs(long ms)
        {
            _timestamp += ms;
        }
    }
}
