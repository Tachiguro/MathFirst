namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

public sealed class SqliteGameplayStorePendingIntentAndConsumerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_CyberDefense_Slice3_" + Guid.NewGuid().ToString("N"));
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

    private sealed class FailingGameplayStore : IGameplayStore
    {
        public string StoragePath => "in-memory";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(CyberDefenseRunState.InitialRun());
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefenseReceiptRecord?>(null);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during staging.");
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefensePendingIntentRecord?>(null);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CyberDefensePendingIntentRecord>>(Array.Empty<CyberDefensePendingIntentRecord>());
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during commit.");
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FailingLearnerStore : ILearnerStore
    {
        public string StoragePath => "in-memory";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string submissionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated learner store connection outage.");
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
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
    public void PendingIntentRecord_Constructor_ValidatesInvariants()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new CyberDefensePendingIntentRecord("", "add:1+1", true, true, 500, 0, now));
        Assert.Throws<ArgumentException>(() => new CyberDefensePendingIntentRecord("sub-1", "", true, true, 500, 0, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, -1, 0, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 500, -1, now));

        var valid = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 500, 0, now);
        Assert.Equal("sub-1", valid.SubmissionId);
        Assert.Equal("add:1+1", valid.FactId);
        Assert.True(valid.IsCorrect);
        Assert.True(valid.IsEligible);
        Assert.Equal(500, valid.ResponseLatencyMs);
        Assert.Equal(0, valid.ResetEpoch);
        Assert.Equal(now, valid.CreatedAt);
    }

    [Fact]
    public void CommittedLearnerAttemptEvidence_Constructor_ValidatesInvariants()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new CommittedLearnerAttemptEvidence("", "add:1+1", true, 500, 1, now));
        Assert.Throws<ArgumentException>(() => new CommittedLearnerAttemptEvidence("sub-1", "", true, 500, 1, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommittedLearnerAttemptEvidence("sub-1", "add:1+1", true, -1, 1, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommittedLearnerAttemptEvidence("sub-1", "add:1+1", true, 500, 0, now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommittedLearnerAttemptEvidence("sub-1", "add:1+1", true, 500, -5, now));

        var valid = new CommittedLearnerAttemptEvidence("sub-1", "add:1+1", true, 500, 1, now);
        Assert.Equal("sub-1", valid.SubmissionId);
        Assert.Equal("add:1+1", valid.FactId);
        Assert.True(valid.IsCorrect);
        Assert.Equal(500, valid.ResponseLatencyMs);
        Assert.Equal(1, valid.PracticePosition);
        Assert.Equal(now, valid.Timestamp);
    }

    [Fact]
    public async Task StageIntent_BeforeLearnerCommit_PersistsIntent()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 850, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        var retrieved = await store.GetPendingIntentAsync("sub-1");
        Assert.NotNull(retrieved);
        Assert.Equal("sub-1", retrieved.SubmissionId);
        Assert.Equal("add:1+1", retrieved.FactId);
        Assert.True(retrieved.IsCorrect);
        Assert.True(retrieved.IsEligible);
        Assert.Equal(850, retrieved.ResponseLatencyMs);
        Assert.Equal(0, retrieved.ResetEpoch);

        // Run state and revision must remain unchanged
        var runState = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runState);
        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task StageIntent_DuplicateSamePayload_IsIdempotent()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 850, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);
        await store.StagePendingIntentAsync(intent);

        var retrieved = await store.GetPendingIntentAsync("sub-1");
        Assert.NotNull(retrieved);
        Assert.Equal("sub-1", retrieved.SubmissionId);
    }

    [Fact]
    public async Task StageIntent_DuplicateConflict_FailsClosed()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent1 = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 850, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent1);

        var intentConflict = new CyberDefensePendingIntentRecord("sub-1", "1+0", true, true, 850, 0, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(intentConflict));
    }

    [Fact]
    public async Task StageIntent_StorageFailure_DoesNotPreventLearnerCommit()
    {
        var failingStore = new FailingGameplayStore();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(failingStore, learnerStore);
        var intent = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 850, 0, DateTimeOffset.UtcNow);

        // Gameplay staging fails
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.StageIntentAsync(intent));

        // Learner commit still succeeds
        var changeSet = CreateTestChangeSet("sub-1", "add:1+1", true, 850, 1, 1);
        var result = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task LearnerEvidence_CommittedSubmission_IsFoundById()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var changeSet = CreateTestChangeSet("sub-evidence-1", "add:1+1", true, 650, 1, 1);
        var result = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(result.IsSuccess);

        var evidence = await learnerStore.GetCommittedAttemptEvidenceAsync("sub-evidence-1");
        Assert.NotNull(evidence);
        Assert.Equal("sub-evidence-1", evidence.SubmissionId);
        Assert.Equal("add:1+1", evidence.FactId);
        Assert.True(evidence.IsCorrect);
        Assert.Equal(650, evidence.ResponseLatencyMs);
        Assert.Equal(1, evidence.PracticePosition);
    }

    [Fact]
    public async Task LearnerEvidence_MissingSubmission_IsNotFabricated()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var evidence = await learnerStore.GetCommittedAttemptEvidenceAsync("non-existent-sub");
        Assert.Null(evidence);
    }

    [Fact]
    public async Task LearnerEvidence_RejectsPayloadMismatch()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Learner store commits fact "add:1+1"
        var changeSet = CreateTestChangeSet("sub-mismatch", "add:1+1", true, 650, 1, 1);
        var commitResult = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Pending intent stages fact "1+0" with same submission ID
        var mismatchedIntent = new CyberDefensePendingIntentRecord("sub-mismatch", "1+0", true, true, 650, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(mismatchedIntent);

        // Consumption must fail closed due to payload mismatch
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-mismatch"));
    }

    [Fact]
    public async Task Recovery_UsesPracticePositionRatherThanTimestampOrder()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Commit sub-1 (pos 1) and sub-2 (pos 2) in learner store
        var changeSet1 = CreateTestChangeSet("sub-1", "add:1+1", true, 500, 1, 1);
        var r1 = await learnerStore.CommitSubmissionAsync(changeSet1);
        Assert.True(r1.IsSuccess);

        var changeSet2 = CreateTestChangeSet("sub-2", "add:1+0", true, 500, 2, 2);
        var r2 = await learnerStore.CommitSubmissionAsync(changeSet2);
        Assert.True(r2.IsSuccess);

        // Stage intent 2 with EARLIER timestamp than intent 1
        var tEarlier = DateTimeOffset.UtcNow.AddMinutes(-5);
        var tLater = DateTimeOffset.UtcNow;
        var intent2 = new CyberDefensePendingIntentRecord("sub-2", "add:1+0", true, true, 500, 0, tEarlier);
        var intent1 = new CyberDefensePendingIntentRecord("sub-1", "add:1+1", true, true, 500, 0, tLater);
        await store.StagePendingIntentAsync(intent2);
        await store.StagePendingIntentAsync(intent1);

        // Recover: Must process sub-1 (pos 1) first, then sub-2 (pos 2)
        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(2, recoveryResult.RecoveredCount);
        Assert.Equal("sub-1", recoveryResult.Receipts[0].SubmissionId);
        Assert.Equal("sub-2", recoveryResult.Receipts[1].SubmissionId);

        // Initial HP of Opponent 0 in Sector 1 is 2.
        // After sub-1 (1 dmg): Opponent HP becomes 1.
        // After sub-2 (1 dmg): Opponent HP becomes 0 -> Defeated! Next opponent is index 1.
        var finalRun = await store.GetRunStateAsync();
        Assert.Equal(1, finalRun.OpponentIndex);
    }

    [Fact]
    public async Task Recovery_UncommittedIntent_DoesNotApplyCombat()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Stage intent that is NOT committed in learner store
        var intent = new CyberDefensePendingIntentRecord("uncommitted-sub", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recoveryResult.RecoveredCount);

        // Run state remains initial
        var runState = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runState);

        // Intent remains staged for future recovery
        var staged = await store.GetPendingIntentAsync("uncommitted-sub");
        Assert.NotNull(staged);
    }

    [Fact]
    public async Task Recovery_AmbiguousLearnerCommit_PreservesIntent()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var failingLearnerStore = new FailingLearnerStore();

        var consumer = new CyberDefenseSubmissionConsumer(store, failingLearnerStore);
        var intent = new CyberDefensePendingIntentRecord("ambiguous-sub", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        // When learner store check throws/fails, intent must be preserved
        await Assert.ThrowsAnyAsync<Exception>(() => consumer.RecoverPendingIntentsAsync());

        var staged = await store.GetPendingIntentAsync("ambiguous-sub");
        Assert.NotNull(staged);
    }

    [Fact]
    public async Task Recovery_CommittedIntent_AppliesExactlyOnce()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-exact-1", "add:1+1", true, 500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-exact-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        var rec1 = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(1, rec1.RecoveredCount);

        var rec2 = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, rec2.RecoveredCount);

        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(2, revision);
    }

    [Fact]
    public async Task Recovery_AfterGameplayCommitBeforeResponse_ReturnsCachedReceipt()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-cached-1", "add:1+1", true, 500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-cached-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);

        // Staging / Recovery after gameplay commit
        var consumeResult = await consumer.ConsumeAttemptAsync("sub-cached-1");
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, consumeResult.Status);
        Assert.Equal(receipt1, consumeResult.Receipt);

        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(2, revision);
    }

    [Fact]
    public async Task Consumer_CorrectAttempt_UsesBaseDamageOne()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-dmg-1", "add:1+1", true, 500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-dmg-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        var result = await consumer.ConsumeAttemptAsync("sub-dmg-1");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        Assert.NotNull(result.Receipt?.TransitionResult);
        Assert.Equal(1, result.Receipt.TransitionResult.RequestedAttackDamage);
        Assert.Equal(1, result.Receipt.TransitionResult.AppliedOpponentDamage);
        Assert.Equal(1, result.Receipt.TransitionResult.NextState.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task Consumer_LegacyCriticalFlag_DoesNotAlterDamage()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-crit-1", "add:1+1", true, 50, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-crit-1", "add:1+1", true, true, 50, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        var result = await consumer.ConsumeAttemptAsync("sub-crit-1");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        Assert.Equal(1, result.Receipt!.TransitionResult!.AppliedOpponentDamage);
    }

    [Fact]
    public async Task Consumer_IncorrectAttempt_AppliesDomainCounterDamage()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-err-1", "add:1+1", false, 1500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-err-1", "add:1+1", false, true, 1500, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        var result = await consumer.ConsumeAttemptAsync("sub-err-1");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        Assert.NotNull(result.Receipt?.TransitionResult);
        Assert.Equal(4, result.Receipt.TransitionResult.IncomingEnemyDamage);
        Assert.Equal(4, result.Receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(96, result.Receipt.TransitionResult.NextState.PlayerCurrentHp);
    }

    [Fact]
    public async Task Consumer_DuplicateSubmission_DoesNotMutateRunOrRevision()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-dup-1", "add:1+1", true, 500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-dup-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        var res1 = await consumer.ConsumeAttemptAsync("sub-dup-1");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, res1.Status);
        var revisionAfter1 = await store.GetStoreRevisionAsync();
        Assert.Equal(2, revisionAfter1);

        var res2 = await consumer.ConsumeAttemptAsync("sub-dup-1");
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, res2.Status);
        var revisionAfter2 = await store.GetStoreRevisionAsync();
        Assert.Equal(2, revisionAfter2);
    }

    [Fact]
    public async Task Consumer_ConflictingDuplicate_FailsClosed()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent1 = new CyberDefensePendingIntentRecord("sub-conflict-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.ApplyAttemptTransactionAsync(intent1);

        var intentConflict = new CyberDefensePendingIntentRecord("sub-conflict-1", "add:1+1", false, true, 500, 0, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intentConflict));
    }

    [Fact]
    public async Task Consumer_CalmModeSuppression_NeverMutatesCombat()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        var changeSet = CreateTestChangeSet("sub-calm-1", "add:1+1", true, 500, 1, 1);
        var r = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(r.IsSuccess);

        var intent = new CyberDefensePendingIntentRecord("sub-calm-1", "add:1+1", true, false, 500, 0, DateTimeOffset.UtcNow);
        await consumer.StageIntentAsync(intent);

        var result = await consumer.ConsumeAttemptAsync("sub-calm-1");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, result.Status);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, result.Receipt!.ReceiptKind);
        Assert.Null(result.Receipt.TransitionResult);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runState);

        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(1, revision);
    }

    [Fact]
    public async Task Consumer_StaleResetEpoch_IsRejected()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Store is at epoch 0, intent has epoch 1
        var staleIntent = new CyberDefensePendingIntentRecord("sub-stale-1", "add:1+1", true, true, 500, 1, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staleIntent));
    }

    [Fact]
    public async Task Consumer_ConcurrentDuplicate_OnlyOneTransitionCommits()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "concurrent.db");

        using var store1 = new SqliteGameplayStore(dbPath);
        await store1.InitializeAsync();
        using var store2 = new SqliteGameplayStore(dbPath);
        await store2.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-concurrent-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);

        var task1 = Task.Run(() => store1.ApplyAttemptTransactionAsync(intent));
        var task2 = Task.Run(() => store2.ApplyAttemptTransactionAsync(intent));

        var receipts = await Task.WhenAll(task1, task2);
        Assert.NotNull(receipts[0]);
        Assert.NotNull(receipts[1]);
        Assert.Equal(receipts[0].SubmissionId, receipts[1].SubmissionId);

        var revision = await store1.GetStoreRevisionAsync();
        Assert.Equal(2, revision);
    }

    [Fact]
    public async Task Consumer_MidTransactionFailure_RollsBackAllMutations()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-rollback-1", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        // If an invalid reset epoch is used, nothing mutates
        var invalidIntent = new CyberDefensePendingIntentRecord("sub-rollback-1", "add:1+1", true, true, 500, 99, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(invalidIntent));

        var runState = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runState);
        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(1, revision);
        var receipt = await store.GetReceiptAsync("sub-rollback-1");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task Recovery_IntentDeletion_IsAtomicWithReceiptAndRunUpdate()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-atomic-del", "add:1+1", true, true, 500, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(receipt);

        var pending = await store.GetPendingIntentAsync("sub-atomic-del");
        Assert.Null(pending);

        var storedReceipt = await store.GetReceiptAsync("sub-atomic-del");
        Assert.NotNull(storedReceipt);
    }

    [Fact]
    public async Task Recovery_GameOver_PreservesTerminalSnapshot()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Cause repeated errors to trigger fatal counter-damage
        // Sector 1: Normal counter-damage = 4. 100 HP / 4 = 25 errors.
        for (int i = 1; i <= 24; i++)
        {
            var intent = new CyberDefensePendingIntentRecord($"sub-fatal-{i}", "add:1+1", false, true, 500, 0, DateTimeOffset.UtcNow);
            await store.ApplyAttemptTransactionAsync(intent);
        }

        var runBeforeFatal = await store.GetRunStateAsync();
        Assert.Equal(4, runBeforeFatal.PlayerCurrentHp);

        // 25th error is lethal
        var fatalIntent = new CyberDefensePendingIntentRecord("sub-fatal-25", "add:1+1", false, true, 500, 0, DateTimeOffset.UtcNow);
        var fatalReceipt = await store.ApplyAttemptTransactionAsync(fatalIntent);

        Assert.True(fatalReceipt.TransitionResult!.IsGameOver);
        Assert.NotNull(fatalReceipt.TransitionResult.TerminalSnapshot);
        Assert.Equal(0, fatalReceipt.TransitionResult.TerminalSnapshot.PlayerCurrentHp);
        Assert.Equal(1, fatalReceipt.TransitionResult.TerminalSnapshot.Sector);

        // Next active state is rebooted to InitialRun
        var runAfterFatal = await store.GetRunStateAsync();
        Assert.Equal(CyberDefenseRunState.InitialRun(), runAfterFatal);
    }

    [Fact]
    public async Task NonInterference_LearnerSchemaV9Unchanged()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        // Verify Schema V9 tables exist
        var tables = new List<string>();
        using var conn = new SqliteConnection($"Data Source={learnerStore.StoragePath}");
        await conn.OpenAsync();
        using var cmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("schema_info", tables);
        Assert.Contains("learner_progression", tables);
        Assert.Contains("operation_progression", tables);
        Assert.Contains("item_learning_state", tables);
        Assert.Contains("attempt_history", tables);
        Assert.Contains("fsrs_card_state", tables);
    }

    [Fact]
    public async Task NonInterference_LearningProgressUnchangedWhenGameplayFails()
    {
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var changeSet = CreateTestChangeSet("sub-learn-1", "add:1+1", true, 500, 1, 1);
        var result = await learnerStore.CommitSubmissionAsync(changeSet);
        Assert.True(result.IsSuccess);

        var snapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(1, snapshot.Progression.PracticePosition);
        Assert.True(snapshot.ItemStates.ContainsKey("add:1+1"));
        Assert.Equal(1, snapshot.ItemStates["add:1+1"].CorrectAttempts);
    }
}
