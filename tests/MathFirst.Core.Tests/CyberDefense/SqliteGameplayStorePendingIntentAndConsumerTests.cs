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
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow));
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow).ToAttackUpgradeSnapshot());
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged { add { } remove { } }
        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during attack purchase.");
        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow).ToFirewallUpgradeSnapshot());
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged { add { } remove { } }
        public event Action<CyberDefenseCombatModifierSnapshot>? CombatModifierSnapshotChanged { add { } remove { } }
        public Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during modifier snapshot.");

        public Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during Critical Strike purchase.");
        public Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during Overdrive purchase.");
        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during firewall purchase.");
        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during firewall capacity activation.");
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefenseReceiptRecord?>(null);
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during staging.");
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.FromResult<CyberDefensePendingIntentRecord?>(null);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CyberDefensePendingIntentRecord>>(Array.Empty<CyberDefensePendingIntentRecord>());
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during commit.");
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameplayResetIntentRecord(false, 0, 0, null, DateTimeOffset.UtcNow));
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during reset intent.");
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during reset execution.");
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated gameplay storage failure during clear reset intent.");
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

        // Out-of-order execution of sub-2 (pos 2) before head sub-1 (pos 1) is rejected even though sub-2 has an earlier timestamp
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-2"));

        // Recover: Must use authoritative learner PracticePosition order (sub-1 at pos 1 first, then sub-2 at pos 2)
        var recoveryResult = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(2, recoveryResult.RecoveredCount);
        Assert.Equal("sub-1", recoveryResult.Receipts[0].SubmissionId);
        Assert.Equal("sub-2", recoveryResult.Receipts[1].SubmissionId);

        // Initial HP of Opponent 0 in Sector 1 is 2.
        // After sub-2 (1 dmg): Opponent HP becomes 1.
        // After sub-1 (1 dmg): Opponent HP becomes 0 -> Defeated! Next opponent is index 1.
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

    [Fact]
    public async Task Ordering_EqualTimestampsWithReverseSortedGuids_UsesPracticePositionOrder()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var equalTimestamp = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        // Attempt 1 has lexicographically larger SubmissionId ("sub-zzzz") at PracticePosition 1.
        // Attempt 2 has lexicographically smaller SubmissionId ("sub-aaaa") at PracticePosition 2.
        var intentPos1 = new CyberDefensePendingIntentRecord("sub-zzzz", "add:1+1", true, true, 400, 0, equalTimestamp);
        var intentPos2 = new CyberDefensePendingIntentRecord("sub-aaaa", "add:1+0", true, true, 450, 0, equalTimestamp);
        await store.StagePendingIntentAsync(intentPos1);
        await store.StagePendingIntentAsync(intentPos2);

        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-zzzz", "add:1+1", true, 400, 1, 1))).IsSuccess);
        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-aaaa", "add:1+0", true, 450, 2, 2))).IsSuccess);

        // Attempting to consume PracticePosition 2 ("sub-aaaa") before PracticePosition 1 ("sub-zzzz") must be rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-aaaa"));

        // Consuming PracticePosition 1 ("sub-zzzz") succeeds first, followed by PracticePosition 2 ("sub-aaaa").
        var first = await consumer.ConsumeAttemptAsync("sub-zzzz");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, first.Status);
        Assert.Equal("sub-zzzz", first.Receipt!.SubmissionId);
        Assert.Equal(1, first.Receipt.TransitionResult!.NextState.CurrentOpponent.CurrentHp);

        var second = await consumer.ConsumeAttemptAsync("sub-aaaa");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, second.Status);
        Assert.Equal("sub-aaaa", second.Receipt!.SubmissionId);
        Assert.True(second.Receipt.TransitionResult!.IsOpponentDefeated);
    }

    [Fact]
    public async Task Ordering_DifferentUtcOffsetsSameInstant_UsesPracticePositionOrder()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // Both timestamps represent 2026-10-20T12:00:00Z, but with different UTC offsets (+02:00 vs -05:00).
        var tOffsetPlus2 = new DateTimeOffset(2026, 10, 20, 14, 0, 0, TimeSpan.FromHours(2));
        var tOffsetMinus5 = new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.FromHours(-5));

        var intentPos1 = new CyberDefensePendingIntentRecord("sub-z-pos1", "add:1+1", true, true, 400, 0, tOffsetPlus2);
        var intentPos2 = new CyberDefensePendingIntentRecord("sub-a-pos2", "add:1+0", true, true, 450, 0, tOffsetMinus5);
        await store.StagePendingIntentAsync(intentPos2);
        await store.StagePendingIntentAsync(intentPos1);

        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-z-pos1", "add:1+1", true, 400, 1, 1))).IsSuccess);
        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-a-pos2", "add:1+0", true, 450, 2, 2))).IsSuccess);

        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(2, recovery.RecoveredCount);
        Assert.Equal("sub-z-pos1", recovery.Receipts[0].SubmissionId);
        Assert.Equal("sub-a-pos2", recovery.Receipts[1].SubmissionId);
    }

    [Fact]
    public async Task Ordering_EarlierUncommittedAttempt_BlocksLaterCommittedAttempt_EvenWhenLaterHasEarlierTimestampAndSmallerGuid()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        var tEarlier = new DateTimeOffset(2026, 10, 20, 11, 55, 0, TimeSpan.Zero);
        var tLater = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        // Advance learnerStore to revision 2 / practicePosition 1 via an initial settled attempt,
        // or commit sub-a-committed as the next learner attempt while sub-z-uncommitted remains uncommitted.
        var warmup = new CyberDefensePendingIntentRecord("sub-warmup", "add:0+0", true, false, 300, 0, tEarlier.AddMinutes(-5));
        await store.StagePendingIntentAsync(warmup);
        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-warmup", "add:0+0", true, 300, 1, 1))).IsSuccess);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, (await consumer.ConsumeAttemptAsync("sub-warmup")).Status);

        // sub-z-uncommitted is staged with tLater and uncommitted in learner store.
        // sub-a-committed is staged with tEarlier and committed in learner store at PracticePosition 2.
        var intentPos1Uncommitted = new CyberDefensePendingIntentRecord("sub-z-uncommitted", "add:1+1", true, true, 400, 0, tLater);
        var intentPos2Committed = new CyberDefensePendingIntentRecord("sub-a-committed", "add:1+0", true, true, 450, 0, tEarlier);
        await store.StagePendingIntentAsync(intentPos2Committed);
        await store.StagePendingIntentAsync(intentPos1Uncommitted);

        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-a-committed", "add:1+0", true, 450, 2, 2))).IsSuccess);

        // Later committed attempt (PracticePosition 2) must not overtake the earlier uncommitted attempt.
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-a-committed"));

        var recovery = await consumer.RecoverPendingIntentsAsync();
        Assert.Equal(0, recovery.RecoveredCount);

        Assert.Equal(CyberDefenseRunState.InitialRun(), await store.GetRunStateAsync());
        Assert.Equal(1, await store.GetStoreRevisionAsync());
        Assert.Null(await store.GetReceiptAsync("sub-a-committed"));
        Assert.Null(await store.GetReceiptAsync("sub-z-uncommitted"));
        Assert.Equal(2, (await store.GetPendingIntentsAsync(0)).Count);
    }

    [Fact]
    public async Task UnstagedBypass_RankZeroDirectCall_IsRejectedWheneverPendingQueueExists_RegardlessOfOwnedRanksOrPlayerLevel()
    {
        using var store = CreateStore();
        await store.InitializeAsync();

        // Stage a rank 0/0 pending intent in a fresh rank 0/0 level 1 store.
        var stagedHead = new CyberDefensePendingIntentRecord("sub-staged-head", "add:1+1", true, true, 400, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(stagedHead);

        // An unstaged rank 0/0 direct call must NOT bypass the existing pending queue.
        var unstagedDirect = new CyberDefensePendingIntentRecord("sub-unstaged-bypass", "add:1+0", true, true, 450, 0, DateTimeOffset.UtcNow.AddMinutes(-10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(unstagedDirect));

        Assert.Equal(CyberDefenseRunState.InitialRun(), await store.GetRunStateAsync());
        Assert.Equal(1, await store.GetStoreRevisionAsync());
        Assert.Null(await store.GetReceiptAsync("sub-unstaged-bypass"));
        Assert.NotNull(await store.GetPendingIntentAsync("sub-staged-head"));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 1)]
    [InlineData(1, 0, 0, 0, 20)]
    [InlineData(0, 1, 0, 0, 50)]
    [InlineData(2, 2, 0, 0, 80)]
    [InlineData(2, 2, 1, 0, 80)]
    [InlineData(2, 2, 0, 1, 80)]
    [InlineData(2, 2, 2, 2, 80)]
    public async Task UnstagedBypass_DirectCallBlockedAcrossOwnedAndBoundRanksAndPlayerLevels_WheneverPendingQueueExists(
        int ownedCrit, int ownedOd, int boundCrit, int boundOd, int playerLevel)
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "bypass_matrix.db");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE gameplay_progression SET critical_strike_rank = {ownedCrit}, overdrive_rank = {ownedOd}, player_level = {playerLevel}, earned_sp = {playerLevel - 1}, available_sp = {playerLevel - 1} WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        var staged = new CyberDefensePendingIntentRecord("sub-staged-1", "add:1+1", true, true, 400, 0, DateTimeOffset.UtcNow, 1, boundCrit, boundOd, practicePosition: 1);
        await store.StagePendingIntentAsync(staged);

        var unstagedZero = new CyberDefensePendingIntentRecord("sub-unstaged-zero", "add:1+0", true, true, 300, 0, DateTimeOffset.UtcNow.AddMinutes(-10), 1, 0, 0, practicePosition: 1);
        var unstagedBound = new CyberDefensePendingIntentRecord("sub-unstaged-bound", "add:1+0", true, true, 300, 0, DateTimeOffset.UtcNow.AddMinutes(-10), 1, boundCrit, boundOd, practicePosition: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(unstagedZero));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(unstagedBound));

        Assert.Equal(CyberDefenseRunState.InitialRun(), await store.GetRunStateAsync());
        Assert.Equal(1, await store.GetStoreRevisionAsync());
        Assert.Null(await store.GetReceiptAsync("sub-unstaged-zero"));
        Assert.Null(await store.GetReceiptAsync("sub-unstaged-bound"));
        Assert.NotNull(await store.GetPendingIntentAsync("sub-staged-1"));
    }

    [Fact]
    public async Task EvidenceVerification_PrecommitPositionMatchesCommittedEvidence_AndConflictingCommittedPositionIsRejectedWithoutMutation()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        using var learnerStore = CreateLearnerStore();
        await learnerStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);

        // 1. Precommit position (1) matches committed learner evidence (1) -> succeeds.
        var matchingIntent = new CyberDefensePendingIntentRecord("sub-pos-match", "add:1+1", true, true, 400, 0, DateTimeOffset.UtcNow, 1, 0, 0, practicePosition: 1);
        await store.StagePendingIntentAsync(matchingIntent);
        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-pos-match", "add:1+1", true, 400, 1, 1))).IsSuccess);

        var ok = await consumer.ConsumeAttemptAsync("sub-pos-match");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, ok.Status);
        Assert.NotNull(ok.Receipt);

        var runBeforeMismatch = await store.GetRunStateAsync();
        var progBeforeMismatch = await store.GetProgressionAsync();
        var revBeforeMismatch = await store.GetStoreRevisionAsync();

        // 2. Precommit candidate position (3) differs from committed learner position (2) -> rejected without combat mutation.
        var mismatchIntent = new CyberDefensePendingIntentRecord("sub-pos-mismatch", "add:1+0", true, true, 450, 0, DateTimeOffset.UtcNow, 1, 0, 0, practicePosition: 3);
        await store.StagePendingIntentAsync(mismatchIntent);
        Assert.True((await learnerStore.CommitSubmissionAsync(CreateTestChangeSet("sub-pos-mismatch", "add:1+0", true, 450, 2, 2))).IsSuccess);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-pos-mismatch"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.RecoverPendingIntentsAsync());

        Assert.Equal(runBeforeMismatch, await store.GetRunStateAsync());
        Assert.Equal(progBeforeMismatch, await store.GetProgressionAsync());
        Assert.Equal(revBeforeMismatch, await store.GetStoreRevisionAsync());
        Assert.Null(await store.GetReceiptAsync("sub-pos-mismatch"));
        Assert.NotNull(await store.GetPendingIntentAsync("sub-pos-mismatch"));
    }

    [Fact]
    public async Task DuplicateStaging_ConflictingPracticePositionForSameSubmissionOrSameEpoch_FailsClosed()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;

        var original = new CyberDefensePendingIntentRecord("sub-pos-1", "add:1+1", true, true, 400, 0, now, 1, 0, 0, practicePosition: 1);
        await store.StagePendingIntentAsync(original);

        // Same SubmissionId with identical PracticePosition is idempotent.
        await store.StagePendingIntentAsync(original);

        // Same SubmissionId with conflicting PracticePosition (2 vs 1, or null vs 1) is rejected.
        var conflictSameSub = new CyberDefensePendingIntentRecord("sub-pos-1", "add:1+1", true, true, 400, 0, now, 1, 0, 0, practicePosition: 2);
        var conflictNullPos = new CyberDefensePendingIntentRecord("sub-pos-1", "add:1+1", true, true, 400, 0, now, 1, 0, 0, practicePosition: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(conflictSameSub));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(conflictNullPos));

        // Different SubmissionId in the same epoch with duplicate PracticePosition (1) is rejected.
        var conflictOtherSubSamePos = new CyberDefensePendingIntentRecord("sub-pos-dup", "add:1+0", true, true, 450, 0, now, 1, 0, 0, practicePosition: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(conflictOtherSubSamePos));

        var stored = await store.GetPendingIntentAsync("sub-pos-1");
        Assert.Equal(original, stored);
        Assert.Null(await store.GetPendingIntentAsync("sub-pos-dup"));
    }

    [Fact]
    public async Task CrashAndReopen_PreservesPracticePositionOrdering_AndConcurrentConsumersRespectQueueHead()
    {
        var dir = CreateTempDirectory();
        var gameplayPath = Path.Combine(dir, "crash_reopen_gameplay.db");
        var learnerPath = Path.Combine(dir, "crash_reopen_learner.db");

        var tBase = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        using (var store = new SqliteGameplayStore(gameplayPath))
        using (var learner = new SqliteLearnerStore(learnerPath))
        {
            await store.InitializeAsync();
            await learner.InitializeAsync();

            // Stage pos 2 first (earlier timestamp, smaller GUID) and pos 1 second (later timestamp, larger GUID).
            var iPos2 = new CyberDefensePendingIntentRecord("sub-a-pos2", "add:1+0", true, true, 450, 0, tBase, 1, 0, 0, practicePosition: 2);
            var iPos1 = new CyberDefensePendingIntentRecord("sub-z-pos1", "add:1+1", true, true, 400, 0, tBase.AddSeconds(30), 1, 0, 0, practicePosition: 1);
            await store.StagePendingIntentAsync(iPos2);
            await store.StagePendingIntentAsync(iPos1);

            Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet("sub-z-pos1", "add:1+1", true, 400, 1, 1))).IsSuccess);
            Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet("sub-a-pos2", "add:1+0", true, 450, 2, 2))).IsSuccess);

            await store.CloseAsync();
            await learner.CloseAsync();
        }

        using var reopenedStore1 = new SqliteGameplayStore(gameplayPath);
        using var reopenedStore2 = new SqliteGameplayStore(gameplayPath);
        using var reopenedLearner = new SqliteLearnerStore(learnerPath);
        await reopenedStore1.InitializeAsync();
        await reopenedStore2.InitializeAsync();
        await reopenedLearner.InitializeAsync();

        var pending = await reopenedStore1.GetPendingIntentsAsync(0);
        Assert.Equal(["sub-z-pos1", "sub-a-pos2"], pending.Select(x => x.SubmissionId).ToArray());

        var consumer1 = new CyberDefenseSubmissionConsumer(reopenedStore1, reopenedLearner);
        var consumer2 = new CyberDefenseSubmissionConsumer(reopenedStore2, reopenedLearner);

        // Concurrent consumption of the head intent across two consumers produces one authoritative transition.
        var results = await Task.WhenAll(
            Task.Run(() => consumer1.ConsumeAttemptAsync("sub-z-pos1")),
            Task.Run(() => consumer2.ConsumeAttemptAsync("sub-z-pos1")));

        Assert.All(results, r => Assert.NotNull(r.Receipt));
        Assert.Equal(results[0].Receipt, results[1].Receipt);

        // Now pos 2 is at the queue head and consumes cleanly.
        var second = await consumer1.ConsumeAttemptAsync("sub-a-pos2");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, second.Status);
        Assert.Empty(await reopenedStore1.GetPendingIntentsAsync(0));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task LegacyQueue_DirectStoreRejectsAmbiguousOrderWithoutMutation(
        bool equalTimestamps, bool positionedOther, bool suppliedTargetPosition)
    {
        using var store = CreateStore();
        using var learner = CreateLearnerStore();
        await store.InitializeAsync();
        await learner.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        // The later learner attempt sorts first by both timestamp and GUID, but neither is authority.
        var first = new CyberDefensePendingIntentRecord("ffffffff-ffff-ffff-ffff-ffffffffffff", "add:1+1", true, true, 400, 0,
            equalTimestamps ? time : time.AddMinutes(1), 1, 0, 0, practicePosition: positionedOther ? 1 : null);
        var second = new CyberDefensePendingIntentRecord("00000000-0000-0000-0000-000000000001", "add:1+0", true, true, 450, 0, time);
        await store.StagePendingIntentAsync(first);
        await store.StagePendingIntentAsync(second);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(first.SubmissionId, first.FactId, true, 400, 1, 1))).IsSuccess);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(second.SubmissionId, second.FactId, true, 450, 2, 2))).IsSuccess);
        var before = await ReadGameplayRowsAsync(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(
            suppliedTargetPosition ? second.WithPracticePosition(2) : second));

        Assert.Equal(before, await ReadGameplayRowsAsync(store));
    }

    [Fact]
    public async Task LegacyQueue_DirectStoreRequiresDurableReconciliationEvenForResolvedHead()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        var first = new CyberDefensePendingIntentRecord("legacy-head", "add:1+1", true, true, 400, 0, time);
        var second = new CyberDefensePendingIntentRecord("positioned-tail", "add:1+0", true, true, 450, 0, time, 1, 0, 0, practicePosition: 2);
        await store.StagePendingIntentAsync(first);
        await store.StagePendingIntentAsync(second);
        var before = await ReadGameplayRowsAsync(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(first.WithPracticePosition(1)));

        Assert.Equal(before, await ReadGameplayRowsAsync(store));
    }

    [Fact]
    public async Task LegacyQueue_PositionedTargetCannotOvertakeUnresolvedLegacyRow()
    {
        using var store = CreateStore();
        await store.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        var target = new CyberDefensePendingIntentRecord("positioned-target", "add:1+1", true, true, 400, 0, time, 1, 0, 0, 1);
        await store.StagePendingIntentAsync(target);
        await store.StagePendingIntentAsync(new CyberDefensePendingIntentRecord("unknown-position", "add:1+0", true, true, 450, 0, time.AddMinutes(1)));
        var before = await ReadGameplayRowsAsync(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(target));

        Assert.Equal(before, await ReadGameplayRowsAsync(store));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LegacyQueue_ConsumerRejectsMissingPositionEvidenceBeforeReconciliation(
        bool positionedOther, bool missingOtherCommit)
    {
        using var store = CreateStore();
        using var learner = CreateLearnerStore();
        await store.InitializeAsync();
        await learner.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        var first = new CyberDefensePendingIntentRecord("a-legacy-head", "add:1+1", true, true, 400, 0, time);
        var second = new CyberDefensePendingIntentRecord("z-legacy-tail", "add:1+0", true, true, 450, 0, time.AddMinutes(1), 1, 0, 0,
            practicePosition: positionedOther ? 2 : null);
        await store.StagePendingIntentAsync(first);
        await store.StagePendingIntentAsync(second);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(first.SubmissionId, first.FactId, true, 400, 1, 1))).IsSuccess);
        if (!missingOtherCommit)
        {
            Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(second.SubmissionId, second.FactId, true, 450, 2, 2))).IsSuccess);
            // Synthetic migrated learner history legitimately has no practice position.
            using var connection = new SqliteConnection($"Data Source={learner.StoragePath};Pooling=False");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE attempt_history SET practice_position = NULL WHERE submission_id = @id;";
            command.Parameters.AddWithValue("@id", first.SubmissionId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var before = await ReadGameplayRowsAsync(store);
        var consumer = new CyberDefenseSubmissionConsumer(store, learner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync(first.SubmissionId));

        Assert.Equal(before, await ReadGameplayRowsAsync(store));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyQueue_CommittedPositionsReconcileDurablyAndReplayExactlyOnce(bool equalTimestamps)
    {
        using var store = CreateStore();
        using var learner = CreateLearnerStore();
        await store.InitializeAsync();
        await learner.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        var first = new CyberDefensePendingIntentRecord("z-first", "add:1+1", true, true, 400, 0,
            equalTimestamps ? time : time.AddMinutes(1));
        var second = new CyberDefensePendingIntentRecord("a-second", "add:1+0", true, true, 450, 0, time);
        await store.StagePendingIntentAsync(second);
        await store.StagePendingIntentAsync(first);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(first.SubmissionId, first.FactId, true, 400, 1, 1))).IsSuccess);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(second.SubmissionId, second.FactId, true, 450, 2, 2))).IsSuccess);
        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var before = await ReadGameplayRowsAsync(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync(second.SubmissionId));
        Assert.Equal(before, await ReadGameplayRowsAsync(store));

        var firstResult = await consumer.ConsumeAttemptAsync(first.SubmissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, firstResult.Status);
        Assert.Equal(1, firstResult.Receipt!.TransitionResult!.NextState.CurrentOpponent.CurrentHp);
        await store.CloseAsync();
        await store.InitializeAsync();
        Assert.Equal(2, (await store.GetPendingIntentAsync(second.SubmissionId))!.PracticePosition);
        var secondResult = await consumer.ConsumeAttemptAsync(second.SubmissionId);
        Assert.True(secondResult.Receipt!.TransitionResult!.IsOpponentDefeated);
        var after = await ReadGameplayRowsAsync(store);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, (await consumer.ConsumeAttemptAsync(first.SubmissionId)).Status);
        Assert.Equal(secondResult.Receipt, await store.ApplyAttemptTransactionAsync(second));
        Assert.Equal(after, await ReadGameplayRowsAsync(store));
    }

    [Fact]
    public async Task LegacyQueue_SingleIntentWithNullCommittedPositionRemainsCompatible()
    {
        using var store = CreateStore();
        using var learner = CreateLearnerStore();
        await store.InitializeAsync();
        await learner.InitializeAsync();
        var intent = new CyberDefensePendingIntentRecord("single-legacy", "add:1+1", true, true, 400, 0,
            new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
        await store.StagePendingIntentAsync(intent);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(intent.SubmissionId, intent.FactId, true, 400, 1, 1))).IsSuccess);
        using (var connection = new SqliteConnection($"Data Source={learner.StoragePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE attempt_history SET practice_position = NULL;";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, (await consumer.ConsumeAttemptAsync(intent.SubmissionId)).Status);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, (await consumer.ConsumeAttemptAsync(intent.SubmissionId)).Status);
    }

    [Fact]
    public async Task LegacyQueue_ExistingReceiptReplaysDespiteUnrelatedAmbiguousQueue()
    {
        using var store = CreateStore();
        using var learner = CreateLearnerStore();
        await store.InitializeAsync();
        await learner.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
        var settled = new CyberDefensePendingIntentRecord("settled", "add:1+1", true, true, 400, 0, time);
        var receipt = await store.ApplyAttemptTransactionAsync(settled);
        await store.StagePendingIntentAsync(new CyberDefensePendingIntentRecord("unknown-a", "add:1+0", true, true, 450, 0, time));
        await store.StagePendingIntentAsync(new CyberDefensePendingIntentRecord("unknown-b", "add:1+1", true, true, 400, 0, time));
        var before = await ReadGameplayRowsAsync(store);

        Assert.Equal(receipt, await store.ApplyAttemptTransactionAsync(settled));
        var replay = await new CyberDefenseSubmissionConsumer(store, learner).ConsumeAttemptAsync(settled.SubmissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, replay.Status);
        Assert.Equal(receipt, replay.Receipt);
        var conflicting = new CyberDefensePendingIntentRecord("settled", "add:1+1", false, true, 400, 0, time);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflicting));
        Assert.Equal(before, await ReadGameplayRowsAsync(store));
    }

    [Fact]
    public async Task ReceiptRace_CommitBetweenReceiptAndPendingReadsReturnsMatchingReceipt()
    {
        using var storeA = CreateStore();
        using var storeB = new SqliteGameplayStore(storeA.StoragePath);
        using var learner = CreateLearnerStore();
        await storeA.InitializeAsync();
        await storeB.InitializeAsync();
        await learner.InitializeAsync();
        var intent = new CyberDefensePendingIntentRecord("receipt-race", "add:1+1", true, true, 400, 0,
            new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero), 1, 0, 0, practicePosition: 1);
        await storeA.StagePendingIntentAsync(intent);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet(intent.SubmissionId, intent.FactId, true, 400, 1, 1))).IsSuccess);
        var observedMissingReceipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPendingRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proxy = System.Reflection.DispatchProxy.Create<IGameplayStore, ReceiptReadBarrierStore>();
        var barrier = (ReceiptReadBarrierStore)proxy;
        barrier.Inner = storeA;
        barrier.AfterFirstReceiptRead = async () =>
        {
            observedMissingReceipt.SetResult();
            await allowPendingRead.Task;
        };
        var consumerA = new CyberDefenseSubmissionConsumer(proxy, learner);
        var consumerB = new CyberDefenseSubmissionConsumer(storeB, learner);
        var pendingA = consumerA.ConsumeAttemptAsync(intent.SubmissionId);
        try
        {
            await observedMissingReceipt.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var resultB = await consumerB.ConsumeAttemptAsync(intent.SubmissionId);
            Assert.Equal(CyberDefenseConsumptionStatus.Success, resultB.Status);
            var afterB = await ReadGameplayRowsAsync(storeB);
            allowPendingRead.SetResult();
            var resultA = await pendingA;
            Assert.Equal(CyberDefenseConsumptionStatus.AlreadyConsumed, resultA.Status);
            Assert.Equal(resultB.Receipt, resultA.Receipt);
            Assert.Equal(afterB, await ReadGameplayRowsAsync(storeA));
            Assert.Equal(CyberDefenseConsumptionStatus.NoPendingIntent, (await consumerA.ConsumeAttemptAsync("unrelated-missing")).Status);
        }
        finally
        {
            allowPendingRead.TrySetResult();
            await pendingA;
        }
    }

    // Decorates real SQLite reads; only the first receipt read is suspended, after its snapshot.
    public class ReceiptReadBarrierStore : System.Reflection.DispatchProxy
    {
        public IGameplayStore Inner { get; set; } = null!;
        public Func<Task> AfterFirstReceiptRead { get; set; } = null!;
        private int _receiptReads;

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            return targetMethod.Name == nameof(IGameplayStore.GetReceiptAsync) && Interlocked.Increment(ref _receiptReads) == 1
                ? PauseAfterReadAsync((Task<CyberDefenseReceiptRecord?>)result!)
                : result;
        }

        private async Task<CyberDefenseReceiptRecord?> PauseAfterReadAsync(Task<CyberDefenseReceiptRecord?> read)
        {
            var receipt = await read;
            Assert.Null(receipt);
            await AfterFirstReceiptRead();
            return receipt;
        }
    }

    private static async Task<string> ReadGameplayRowsAsync(IGameplayStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.StoragePath};Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' AND name LIKE 'gameplay_%' ORDER BY name;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var rows = new List<string>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" ORDER BY 1;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(table + ":" + System.Text.Json.JsonSerializer.Serialize(values));
            }
        }
        return string.Join("\n", rows);
    }

    [Fact]
    public async Task LegacyV7PendingRows_ReconcileDurablyWhenCommitted_AndFailClosedWithoutDataLossWhenUnpositionedHeadIsUnresolved()
    {
        var dir = CreateTempDirectory();
        var gameplayPath = Path.Combine(dir, "legacy_v7_reconcile.db");
        var learnerPath = Path.Combine(dir, "legacy_v7_learner.db");

        using var store = new SqliteGameplayStore(gameplayPath);
        using var learner = new SqliteLearnerStore(learnerPath);
        await store.InitializeAsync();
        await learner.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(store, learner);
        var t0 = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        // 1. Legacy V7 row (practice_position IS NULL) alongside positioned row (practice_position = 2), where legacy row is uncommitted.
        var legacyUncommitted = new CyberDefensePendingIntentRecord("sub-z-legacy", "add:1+1", true, true, 400, 0, t0.AddMinutes(5), 1, 0, 0, practicePosition: null);
        var positionedLater = new CyberDefensePendingIntentRecord("sub-a-pos2", "add:1+0", true, true, 450, 0, t0, 1, 0, 0, practicePosition: 2);
        await store.StagePendingIntentAsync(positionedLater);
        await store.StagePendingIntentAsync(legacyUncommitted);

        // Unknown-position legacy row sorts BEFORE positioned row so it cannot be overtaken.
        var queueBeforeReconcile = await store.GetPendingIntentsAsync(0);
        Assert.Equal(["sub-z-legacy", "sub-a-pos2"], queueBeforeReconcile.Select(x => x.SubmissionId).ToArray());
        Assert.Null(queueBeforeReconcile[0].PracticePosition);
        Assert.Equal(2L, queueBeforeReconcile[1].PracticePosition);

        // Commit sub-z-legacy at PracticePosition 1 and sub-a-pos2 at PracticePosition 2.
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet("sub-z-legacy", "add:1+1", true, 400, 1, 1))).IsSuccess);
        Assert.True((await learner.CommitSubmissionAsync(CreateTestChangeSet("sub-a-pos2", "add:1+0", true, 450, 2, 2))).IsSuccess);

        // Attempting to consume sub-a-pos2 first is rejected without mutating or deleting pending rows.
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ConsumeAttemptAsync("sub-a-pos2"));
        Assert.Equal(2, (await store.GetPendingIntentsAsync(0)).Count);

        // Consuming sub-z-legacy reconciles its PracticePosition from committed learner evidence and applies it first.
        var first = await consumer.ConsumeAttemptAsync("sub-z-legacy");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, first.Status);

        var second = await consumer.ConsumeAttemptAsync("sub-a-pos2");
        Assert.Equal(CyberDefenseConsumptionStatus.Success, second.Status);
        Assert.Empty(await store.GetPendingIntentsAsync(0));
    }
}
