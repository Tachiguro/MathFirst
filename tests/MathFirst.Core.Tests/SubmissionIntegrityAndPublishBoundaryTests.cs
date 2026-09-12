namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class SubmissionIntegrityAndPublishBoundaryTests : IDisposable
{
    private readonly string _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstSubmissionIntegrity_" + Guid.NewGuid().ToString("N"));

    public SubmissionIntegrityAndPublishBoundaryTests()
    {
        Directory.CreateDirectory(_testDbDir);
    }

    [Fact]
    public async Task SqliteStore_RejectsSkippedNewPracticePositionWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "skipped-position.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 900;
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 5;
        var submissionId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            submissionId, fact.Id, fact.Operation, fact.LeftOperand, fact.RightOperand,
            submittedAnswer: 1, correctAnswer: 1, isCorrect: true, isFluent: true, responseLatencyMs: 900,
            timestamp: DateTimeOffset.UtcNow, practicePosition: 5);
        var changeSet = new SubmissionChangeSet(submissionId, before.Revision, attempt, itemState, progression);

        var result = await store.CommitSubmissionAsync(changeSet);

        Assert.False(result.IsSuccess);
        var after = await store.LoadSnapshotAsync();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Progression.PracticePosition, after.Progression.PracticePosition);
        Assert.Empty(after.RecentAttempts);
        Assert.Empty(after.ItemStates);
        Assert.Empty(after.FsrsStates);
    }

    [Fact]
    public async Task TrainingSession_DoesNotPublishCandidateStateWhileCommitIsPending()
    {
        var store = new GatedStore();
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();
        var factBefore = session.CurrentFact;

        session.SubmitAnswer(factBefore.CorrectResult);
        var pendingCommit = session.CommitCurrentEvaluationAsync();
        await store.CommitStarted.Task;

        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(1, session.Progression.StoreRevision);
        Assert.Empty(session.ItemStates);
        Assert.Empty(session.FsrsStates);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(factBefore, session.CurrentFact);

        store.Complete(PersistenceResult.Success(2));
        Assert.True((await pendingCommit).IsSuccess);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(1, session.SessionTotalCount);
        Assert.True(session.ItemStates.ContainsKey(factBefore.Id));
        Assert.True(session.FsrsStates.ContainsKey(factBefore.Id));
        Assert.True(session.AdvanceAfterCorrectAnswer());
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task TrainingSession_DoesNotPublishAdvancementUntilTheTriggeringCommitSucceeds()
    {
        var store = new GatedStore(CreateAdvancementReadySnapshot());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var pending = session.CommitCurrentEvaluationAsync();
        await store.CommitStarted.Task;

        Assert.Equal(156, session.Progression.PracticePosition);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
        Assert.All(
            Enum.GetValues<ArithmeticOperation>().Where(operation => operation != ArithmeticOperation.Addition),
            operation => Assert.Equal(new OperationProgression(operation, 0, 0), session.Progression.OperationProgressions[operation]));

        store.Complete(PersistenceResult.Success(2));
        Assert.True((await pending).IsSuccess);
        Assert.Equal(157, session.Progression.PracticePosition);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(157, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
        Assert.All(
            Enum.GetValues<ArithmeticOperation>().Where(operation => operation != ArithmeticOperation.Addition),
            operation => Assert.Equal(new OperationProgression(operation, 0, 0), session.Progression.OperationProgressions[operation]));
    }

    [Fact]
    public async Task TrainingSession_PersistenceFailureNeverPublishesAdvancement()
    {
        var store = new GatedStore(CreateAdvancementReadySnapshot());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync(startTiming: false);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var pending = session.CommitCurrentEvaluationAsync();
        await store.CommitStarted.Task;

        store.Complete(PersistenceResult.Unavailable("synthetic advancement failure"));
        Assert.False((await pending).IsSuccess);
        Assert.Equal(156, session.Progression.PracticePosition);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
    }

    [Fact]
    public async Task SqliteStore_RejectsStaleAndSkippedPositionsAndProgressionMismatchWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "position-validation.db"));
        await store.InitializeAsync();
        await CommitThroughPositionAsync(store, 10);

        var before = await store.LoadSnapshotAsync();
        var skipped = await store.CommitSubmissionAsync(CreateChangeSet(before.Revision, 12, ArithmeticOperation.Division));
        AssertInvalidAndUnchanged(skipped, before, await store.LoadSnapshotAsync());

        var stale = await store.CommitSubmissionAsync(CreateChangeSet(before.Revision, 10, ArithmeticOperation.Subtraction));
        AssertInvalidAndUnchanged(stale, before, await store.LoadSnapshotAsync());

        var mismatch = CreateChangeSet(before.Revision, 11, ArithmeticOperation.Multiplication);
        mismatch.UpdatedProgression.PracticePosition = 12;
        var mismatchedResult = await store.CommitSubmissionAsync(mismatch);
        AssertInvalidAndUnchanged(mismatchedResult, before, await store.LoadSnapshotAsync());

        var valid = await store.CommitSubmissionAsync(CreateChangeSet(before.Revision, 11, ArithmeticOperation.Multiplication));
        Assert.True(valid.IsSuccess);
        Assert.Equal(11, (await store.LoadSnapshotAsync()).Progression.PracticePosition);
    }

    [Fact]
    public async Task SqliteStore_RejectsInvalidProgressionMutationWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "operation-validation.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();

        var nonScheduledMutation = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Subtraction] =
                new OperationProgression(ArithmeticOperation.Subtraction, 1, 1));
        var nonScheduledResult = await store.CommitSubmissionAsync(nonScheduledMutation);
        AssertInvalidAndUnchanged(nonScheduledResult, before, await store.LoadSnapshotAsync());

        var jump = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 2, 1));
        var jumpResult = await store.CommitSubmissionAsync(jump);
        AssertInvalidAndUnchanged(jumpResult, before, await store.LoadSnapshotAsync());

        var advance = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 1, 1));
        var advancingResult = await store.CommitSubmissionAsync(advance);
        Assert.True(advancingResult.IsSuccess);
        var after = await store.LoadSnapshotAsync();
        Assert.Equal(1, after.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(1, after.Progression.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
    }

    [Fact]
    public async Task SqliteStore_RejectsAttemptOperationAndChangedProgressionMismatchWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "attempt-progression-mismatch.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var changeSet = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Subtraction] =
                new OperationProgression(ArithmeticOperation.Subtraction, 1, 1));

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsMultipleOperationProgressionMutationsWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "multiple-progression-mutations.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var changeSet = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
        {
            progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 1, 1);
            progression.OperationProgressions[ArithmeticOperation.Subtraction] =
                new OperationProgression(ArithmeticOperation.Subtraction, 1, 1);
        });

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsOperationProgressionRegressionWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "progression-regression.db"));
        await store.InitializeAsync();
        var advancing = CreateChangeSet(1, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 1, 1));
        Assert.True((await store.CommitSubmissionAsync(advancing)).IsSuccess);
        var before = await store.LoadSnapshotAsync();
        var regressing = CreateChangeSet(before.Revision, 2, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions[ArithmeticOperation.Addition] =
                new OperationProgression(ArithmeticOperation.Addition, 0, 0));

        var result = await store.CommitSubmissionAsync(regressing);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsItemFactOperationMismatchWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "item-fact-operation-mismatch.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var subtractionFact = new ArithmeticFact(ArithmeticOperation.Subtraction, 1, 0);
        var submissionId = Guid.NewGuid().ToString("N");
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        var attempt = new AttemptRecord(
            submissionId,
            subtractionFact.Id,
            ArithmeticOperation.Addition,
            subtractionFact.LeftOperand,
            subtractionFact.RightOperand,
            1,
            1,
            true,
            true,
            900,
            DateTimeOffset.UtcNow,
            practicePosition: 1);
        var changeSet = new SubmissionChangeSet(
            submissionId,
            before.Revision,
            attempt,
            ItemLearningState.CreateNew(subtractionFact),
            progression);

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsUnknownAttemptOperationWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "unknown-operation.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var submissionId = Guid.NewGuid().ToString("N");
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        var attempt = new AttemptRecord(
            submissionId,
            fact.Id,
            (ArithmeticOperation)999,
            fact.LeftOperand,
            fact.RightOperand,
            1,
            fact.CorrectResult,
            true,
            true,
            900,
            DateTimeOffset.UtcNow,
            practicePosition: 1);
        var changeSet = new SubmissionChangeSet(
            submissionId,
            before.Revision,
            attempt,
            ItemLearningState.CreateNew(fact),
            progression);

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
        Assert.Contains("unknown", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SqliteStore_RejectsMissingCanonicalOperationProgressionWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "missing-operation-progression.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var changeSet = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition, progression =>
            progression.OperationProgressions.Remove(ArithmeticOperation.Division));

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsFsrsFactMismatchWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "fsrs-fact-mismatch.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var baseChangeSet = CreateChangeSet(before.Revision, 1, ArithmeticOperation.Addition);
        var changeSet = new SubmissionChangeSet(
            baseChangeSet.SubmissionId,
            baseChangeSet.ExpectedRevision,
            baseChangeSet.Attempt,
            baseChangeSet.UpdatedItemState,
            baseChangeSet.UpdatedProgression,
            new FsrsCardState(
                "add:9+9",
                Guid.NewGuid(),
                2,
                null,
                1.0,
                5.0,
                2,
                1,
                FsrsRating.Easy),
            baseChangeSet.OperationProgressions);

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_RejectsMathematicallyInconsistentAttemptWithoutWriting()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "attempt-semantics.db"));
        await store.InitializeAsync();
        var before = await store.LoadSnapshotAsync();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var submissionId = Guid.NewGuid().ToString("N");
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        var attempt = new AttemptRecord(
            submissionId,
            fact.Id,
            fact.Operation,
            fact.LeftOperand,
            fact.RightOperand,
            3,
            3,
            true,
            true,
            900,
            DateTimeOffset.UtcNow,
            practicePosition: 1);
        var item = ItemLearningState.CreateNew(fact);
        var changeSet = new SubmissionChangeSet(
            submissionId,
            before.Revision,
            attempt,
            item,
            progression);

        var result = await store.CommitSubmissionAsync(changeSet);

        AssertInvalidAndUnchanged(result, before, await store.LoadSnapshotAsync());
    }

    [Fact]
    public async Task SqliteStore_PreservesIdempotentReplayBeforeRevisionAndPositionValidation()
    {
        using var store = new SqliteLearnerStore(Path.Combine(_testDbDir, "idempotency.db"));
        await store.InitializeAsync();
        var first = CreateChangeSet(1, 1, ArithmeticOperation.Addition);
        Assert.True((await store.CommitSubmissionAsync(first)).IsSuccess);
        Assert.True((await store.CommitSubmissionAsync(CreateChangeSet(2, 2, ArithmeticOperation.Subtraction))).IsSuccess);
        var beforeReplay = await store.LoadSnapshotAsync();

        var replay = await store.CommitSubmissionAsync(first);

        Assert.True(replay.IsSuccess);
        var afterReplay = await store.LoadSnapshotAsync();
        Assert.Equal(beforeReplay.Revision, afterReplay.Revision);
        Assert.Equal(beforeReplay.Progression.PracticePosition, afterReplay.Progression.PracticePosition);
        Assert.Equal(2, afterReplay.RecentAttempts.Count);
    }

    [Fact]
    public void SubmissionChangeSet_DefensivelySnapshotsMutableCandidateState()
    {
        var candidate = LearnerProgression.CreateFresh();
        candidate.PracticePosition = 1;
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var item = ItemLearningState.CreateNew(fact);
        var changeSet = new SubmissionChangeSet(
            "defensive-copy", 1,
            new AttemptRecord("defensive-copy", fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 900, DateTimeOffset.UtcNow, practicePosition: 1),
            item,
            candidate);

        item.TotalAttempts = 99;
        candidate.PracticePosition = 99;
        candidate.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 3, 1);

        Assert.Equal(0, changeSet.UpdatedItemState.TotalAttempts);
        Assert.Equal(1, changeSet.UpdatedProgression.PracticePosition);
        Assert.Equal(0, changeSet.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
    }

    [Theory]
    [InlineData(AttemptOutcome.Correct)]
    [InlineData(AttemptOutcome.Incorrect)]
    [InlineData(AttemptOutcome.Timeout)]
    public async Task TrainingSession_PersistenceFailureNeverPublishesCandidateState(AttemptOutcome outcome)
    {
        var store = new GatedStore();
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();
        var factBefore = session.CurrentFact;

        switch (outcome)
        {
            case AttemptOutcome.Correct:
                session.SubmitAnswer(factBefore.CorrectResult);
                break;
            case AttemptOutcome.Incorrect:
                session.SubmitAnswer(factBefore.CorrectResult + 1);
                break;
            case AttemptOutcome.Timeout:
                session.RecordTimeout();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var pendingCommit = session.CommitCurrentEvaluationAsync();
        await store.CommitStarted.Task;
        store.Complete(PersistenceResult.Unavailable("synthetic persistence failure"));

        Assert.False((await pendingCommit).IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Empty(session.ItemStates);
        Assert.Empty(session.FsrsStates);
        Assert.Equal(factBefore, session.CurrentFact);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of synthetic test data.
        }
    }

    private sealed class FakeClock : IClock
    {
        public long GetTimestamp() => 0;

        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private static async Task CommitThroughPositionAsync(SqliteLearnerStore store, long position)
    {
        for (var practicePosition = 1L; practicePosition <= position; practicePosition++)
        {
            var snapshot = await store.LoadSnapshotAsync();
            var result = await store.CommitSubmissionAsync(CreateChangeSet(
                snapshot.Revision,
                practicePosition,
                ScheduledOperation(practicePosition)));
            Assert.True(result.IsSuccess);
        }
    }

    private static SubmissionChangeSet CreateChangeSet(
        long expectedRevision,
        long practicePosition,
        ArithmeticOperation operation,
        Action<LearnerProgression>? configureProgression = null)
    {
        var fact = operation switch
        {
            ArithmeticOperation.Addition => new ArithmeticFact(operation, 0, 1),
            ArithmeticOperation.Subtraction => new ArithmeticFact(operation, 1, 0),
            ArithmeticOperation.Multiplication => new ArithmeticFact(operation, 1, 1),
            ArithmeticOperation.Division => new ArithmeticFact(operation, 1, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var submissionId = Guid.NewGuid().ToString("N");
        var item = ItemLearningState.CreateNew(fact);
        item.TotalAttempts = 1;
        item.CorrectAttempts = 1;
        item.ConsecutiveCorrectStreak = 1;
        item.LastLatencyMs = 900;
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        configureProgression?.Invoke(progression);
        return new SubmissionChangeSet(
            submissionId,
            expectedRevision,
            new AttemptRecord(submissionId, fact.Id, operation, fact.LeftOperand, fact.RightOperand, 1, fact.CorrectResult, true, true, 900, DateTimeOffset.UtcNow, practicePosition: practicePosition),
            item,
            progression,
            operationProgressions: progression.OperationProgressions);
    }

    private static ArithmeticOperation ScheduledOperation(long practicePosition) => new[]
    {
        ArithmeticOperation.Addition,
        ArithmeticOperation.Subtraction,
        ArithmeticOperation.Multiplication,
        ArithmeticOperation.Division
    }[(int)((practicePosition - 1) % 4)];

    private static void AssertInvalidAndUnchanged(PersistenceResult result, LearnerSnapshot before, LearnerSnapshot after)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(PersistenceStatus.InvalidSubmission, result.Status);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Progression.PracticePosition, after.Progression.PracticePosition);
        Assert.Equal(before.RecentAttempts.Count, after.RecentAttempts.Count);
        Assert.Equal(before.ItemStates.Count, after.ItemStates.Count);
        Assert.Equal(before.FsrsStates.Count, after.FsrsStates.Count);
    }

    private static LearnerSnapshot CreateAdvancementReadySnapshot()
    {
        var curriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var frontier = ownership.GetOwnedFrontier(0);
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 156;
        var items = frontier.ToDictionary(fact => fact.Id, fact =>
        {
            var state = ItemLearningState.CreateNew(fact);
            state.TotalAttempts = 1;
            state.CorrectAttempts = 1;
            state.ConsecutiveCorrectStreak = 1;
            state.LastLatencyMs = 900;
            return state;
        }, StringComparer.Ordinal);
        var attempts = Enumerable.Range(0, 39)
            .Select(index =>
            {
                var fact = frontier[index % frontier.Count];
                var position = 1L + (index * 4L);
                return new AttemptRecord(
                    $"advancement-ready-{position}", fact.Id, fact.Operation, fact.LeftOperand, fact.RightOperand,
                    fact.CorrectResult, fact.CorrectResult, true, true, 900, DateTimeOffset.UtcNow,
                    practicePosition: position);
            })
            .ToArray();
        Assert.All(items.Values, state => Assert.Equal(
            new ArithmeticFact(state.Operation, state.LeftOperand, state.RightOperand).Id,
            state.FactId));
        return new LearnerSnapshot(
            progression,
            items,
            new Dictionary<string, MathFirst.Application.Scheduling.FsrsCardState>(),
            attempts,
            1,
            LearnerProgression.DefaultSchemaVersion,
            progression.OperationProgressions);
    }

    private sealed class GatedStore : ILearnerStore
    {
        private readonly TaskCompletionSource<PersistenceResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly LearnerSnapshot _snapshot;

        public GatedStore(LearnerSnapshot? snapshot = null) => _snapshot = snapshot ?? new LearnerSnapshot(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, MathFirst.Application.Scheduling.FsrsCardState>(),
            [],
            1,
            LearnerProgression.DefaultSchemaVersion);

        public TaskCompletionSource<bool> CommitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string StoragePath => "inmemory://gated-submission";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            CommitStarted.TrySetResult(true);
            return _completion.Task;
        }

        public void Complete(PersistenceResult result) => _completion.SetResult(result);

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
