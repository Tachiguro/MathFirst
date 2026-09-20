namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class AuthorityHardeningAndRecoveryInvariantTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstAuthorityHardening_" + Guid.NewGuid().ToString("N"));

    public AuthorityHardeningAndRecoveryInvariantTests()
    {
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
        catch { }
    }

    private string GetDatabasePath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    // ===========================================================================
    // Requirement A: No ordinal compatibility fallback & explicit ordinal authority
    // ===========================================================================

    [Fact]
    public void PracticeSelectionContext_RequiresExplicitScheduledOrdinal_AndSelectorRoleIsIndependentOfGlobalPosition()
    {
        var curriculum = new ArithmeticCurriculum();
        var progressions = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => curriculum.GetCurriculum(op));

        // Materialize one addition fact
        var additionFact = curriculum.Addition.Bands[0].Frontier[0];
        var additionState = ItemLearningState.CreateNew(additionFact);
        var additionCard = new FsrsCardState(additionFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);
        var index = new PracticeCandidateIndex(
            [additionFact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [additionFact.Id] = additionState },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [additionFact.Id] = additionCard });

        // 1. Scheduled ordinal = 1 must produce New role even at high global practice positions (e.g. 1001)
        // Addition is scheduled at pos 1001 with Addition-only.
        var contextHighPositionNew = new PracticeSelectionContext(
            prospectivePracticePosition: 1001,
            currentSessionOrder: 1,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition],
            scheduledOperationAttemptOrdinal: 1);

        var resultNew = new AdaptivePracticeSelector().SelectTargetFact(contextHighPositionNew);
        Assert.Equal(PracticeSelectionRole.New, resultNew.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, resultNew.ResolvedRole);
        Assert.True(resultNew.IsNewIntroduction);

        // 2. Scheduled ordinal = 2 must produce Due role even at prospective position 1001
        var contextHighPositionDue = new PracticeSelectionContext(
            prospectivePracticePosition: 1001,
            currentSessionOrder: 1,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition],
            scheduledOperationAttemptOrdinal: 2);

        var resultDue = new AdaptivePracticeSelector().SelectTargetFact(contextHighPositionDue);
        Assert.Equal(PracticeSelectionRole.Due, resultDue.RequestedRole);
        // Because additionFact is not due at 1001 (due is 500 <= 1001), it IS in fact Due!
        Assert.Equal(PracticeSelectionRole.Due, resultDue.ResolvedRole);
        Assert.Equal(additionFact.Id, resultDue.Fact.Id);

        // 3. Ordinal <= 0 must be rejected
        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeSelectionContext(
            prospectivePracticePosition: 10,
            currentSessionOrder: 1,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition],
            scheduledOperationAttemptOrdinal: 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => new PracticeSelectionContext(
            prospectivePracticePosition: 10,
            currentSessionOrder: 1,
            operationProgressions: progressions,
            curricula: curricula,
            candidateIndex: index,
            recentAcceptedFactsOldestToNewest: [],
            enabledOperations: [ArithmeticOperation.Addition],
            scheduledOperationAttemptOrdinal: -5));
    }

    // ===========================================================================
    // Requirement B: Runtime snapshot fail-closed
    // ===========================================================================

    [Fact]
    public void LearnerSnapshot_ExistingHistory_EmptyItemStates_MissingOperationCounts_FailsClosed()
    {
        var progression = new LearnerProgression { PracticePosition = 5 };
        var recentAttempts = new List<AttemptRecord>
        {
            new(
                "sub-1",
                "add:0+0",
                ArithmeticOperation.Addition,
                0,
                0,
                0,
                0,
                true,
                true,
                1000,
                DateTimeOffset.UtcNow,
                AttemptOutcome.Correct,
                1)
        };

        // Existing history (PracticePosition > 0 and recent attempts present),
        // but ItemStates is empty (runtime snapshot) and operationAcceptedAttemptCounts is null.
        // Must fail closed with InvalidOperationException!
        var ex = Assert.Throws<InvalidOperationException>(() => new LearnerSnapshot(
            progression,
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            recentAttempts,
            revision: 1,
            schemaVersion: 6,
            operationProgressions: null,
            latestAcceptedPracticeAt: DateTimeOffset.UtcNow,
            operationAcceptedAttemptCounts: null));

        Assert.Contains("Authoritative operation accepted attempt counts are required", ex.Message);
    }

    [Fact]
    public void LearnerSnapshot_ZeroPositionWithLatestAcceptedPracticeAt_EmptyItemStates_MissingCounts_FailsClosed()
    {
        var progression = new LearnerProgression { PracticePosition = 0 };

        // Even if PracticePosition == 0, if LatestAcceptedPracticeAt is present,
        // durable history exists, so empty item states without counts must fail closed.
        var ex = Assert.Throws<InvalidOperationException>(() => new LearnerSnapshot(
            progression,
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            [],
            revision: 1,
            schemaVersion: 6,
            operationProgressions: null,
            latestAcceptedPracticeAt: DateTimeOffset.UtcNow,
            operationAcceptedAttemptCounts: null));

        Assert.Contains("Authoritative operation accepted attempt counts are required", ex.Message);
    }

    // ===========================================================================
    // Requirement C: Genuine zero-history snapshot
    // ===========================================================================

    [Fact]
    public void LearnerSnapshot_GenuineZeroHistory_ConstructsCanonicalFourZeroCounts()
    {
        var progression = LearnerProgression.CreateFresh();
        var snapshot = new LearnerSnapshot(
            progression,
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            [],
            revision: 1,
            schemaVersion: 6);

        Assert.NotNull(snapshot.OperationAcceptedAttemptCounts);
        Assert.Equal(4, snapshot.OperationAcceptedAttemptCounts.Count);
        Assert.All(
            PracticeOperationPreferencePolicy.AllOperations,
            op => Assert.Equal(0, snapshot.OperationAcceptedAttemptCounts[op]));
    }

    // ===========================================================================
    // Requirement D: Session duplicate replay does not double-increment runtime count
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_DuplicateCommitReplay_DoesNotDoubleIncrementRuntimeCount()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetOperations([ArithmeticOperation.Addition]);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));

        // Submit and commit answer
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var firstResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(firstResult.IsSuccess);
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));

        // Replay commit on the same session evaluation
        var secondResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
    }

    // ===========================================================================
    // Requirement E: Persistence failure does not advance runtime count
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_PersistenceFailure_DoesNotAdvanceRuntimeCount_AndMaintainsRecoveryConsistency()
    {
        var preferences = new TestPreferenceStore();
        preferences.SetOperations([ArithmeticOperation.Addition]);

        var failingStore = new FailingLearnerStore();
        var session = new TrainingSession(failingStore, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var op = session.CurrentFact.Operation;
        Assert.Equal(ArithmeticOperation.Addition, op);
        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(op));
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.False(session.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // 1. Evaluate a new answer
        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(evaluation.IsCorrect);
        Assert.NotNull(evaluation.ChangeSet);
        Assert.False(session.IsCurrentSubmissionCommitted);

        // 2. CommitSubmissionAsync returns a non-success persistence failure
        var result = await session.CommitCurrentEvaluationAsync();
        Assert.False(result.IsSuccess);

        // 3. _operationAcceptedAttemptCounts for the attempted operation does NOT increment
        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(op));

        // 4. Global authoritative learner state is not published as committed
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.False(session.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.False(session.HasCompletedPracticeHistory);
        Assert.Null(session.LatestAcceptedPracticeAt);

        // 5. Retry/recovery behavior remains consistent while store is still failing
        var retryResult = await session.RecoverFromPersistenceFailureAsync();
        Assert.False(retryResult);
        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(op));
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.False(session.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);

        // 6. When the underlying store becomes healthy, recovery commits the evaluated submission
        failingStore.ShouldFail = false;
        var recoveryResult = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recoveryResult);
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(op));
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.True(session.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
    }

    // ===========================================================================
    // Requirement F: Revision conflict recovery reloads durable counts exactly
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_RevisionConflictRecovery_ReloadsDurableCountsExactly()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetOperations([ArithmeticOperation.Addition]);

        using var store1 = new SqliteLearnerStore(path);
        using var store2 = new SqliteLearnerStore(path);

        var session1 = new TrainingSession(store1, new FixedClock(), preferenceStore: preferences);
        var session2 = new TrainingSession(store2, new FixedClock(), preferenceStore: preferences);

        await session1.InitializeAsync(startTiming: false);
        await session2.InitializeAsync(startTiming: false);

        // Session 1 completes 2 attempts (count N = 2)
        for (var i = 0; i < 2; i++)
        {
            session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
            var res1 = await session1.CommitCurrentEvaluationAsync();
            Assert.True(res1.IsSuccess);
            session1.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.Equal(2, session1.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(2, session1.Progression.PracticePosition);

        // Session 2 reinitializes / catches up, then commits a 3rd attempt
        await session2.InitializeAsync(startTiming: false);
        Assert.Equal(2, session2.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        session2.SubmitAnswer(session2.CurrentFact.CorrectResult);
        var result2 = await session2.CommitCurrentEvaluationAsync();
        Assert.True(result2.IsSuccess);
        Assert.Equal(3, session2.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));

        // Session 1 attempts to commit with stale revision -> conflict
        session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
        var conflictResult = await session1.CommitCurrentEvaluationAsync();
        Assert.False(conflictResult.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, conflictResult.Status);
        // Session 1 does NOT increment to N+1 (remains at 2, not 3)
        Assert.Equal(2, session1.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(2, session1.Progression.PracticePosition);
        Assert.False(session1.IsCurrentSubmissionCommitted);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session1.InteractionState);

        // Authoritative recovery reloads the actual persisted operation count (3)
        var recovered = await session1.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);
        Assert.Equal(3, session1.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(3, session1.Progression.PracticePosition);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session1.InteractionState);

        // Subsequent ScheduledOperationAttemptOrdinal uses durableCount + 1 (i.e. 3 + 1 = 4)
        session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
        var nextResult = await session1.CommitCurrentEvaluationAsync();
        Assert.True(nextResult.IsSuccess);
        Assert.Equal(4, session1.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(4, session1.Progression.PracticePosition);
    }

    // ===========================================================================
    // Requirement G: Reset Learning Progress resets all four operation counts to zero
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_ResetLearningProgress_ResetsAllCountsToZero()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Complete 4 attempts across operations (1 attempt for each of Addition, Subtraction, Multiplication, Division)
        for (var i = 0; i < 4; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.Equal(4, session.Progression.PracticePosition);
        // At least two operations (in fact all four) have nonzero accepted-attempt counts
        Assert.All(
            PracticeOperationPreferencePolicy.AllOperations,
            op => Assert.Equal(1, session.GetOperationAcceptedAttemptCount(op)));

        // Execute Reset Learning Progress
        await session.ResetLearningProgressAsync();

        // Authoritative reload produces exactly four zero counts
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.All(
            PracticeOperationPreferencePolicy.AllOperations,
            op => Assert.Equal(0, session.GetOperationAcceptedAttemptCount(op)));

        // Next ordinal for each operation is 1 (0 + 1)
        Assert.All(
            PracticeOperationPreferencePolicy.AllOperations,
            op => Assert.Equal(1, session.GetOperationAcceptedAttemptCount(op) + 1));

        // Durable snapshot verification: Schema remains V6, position is 0, counts are all 0
        var durableSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, durableSnapshot.SchemaVersion);
        Assert.Equal(0, durableSnapshot.Progression.PracticePosition);
        Assert.NotNull(durableSnapshot.OperationAcceptedAttemptCounts);
        Assert.Equal(4, durableSnapshot.OperationAcceptedAttemptCounts.Count);
        Assert.All(
            PracticeOperationPreferencePolicy.AllOperations,
            op => Assert.Equal(0, durableSnapshot.OperationAcceptedAttemptCounts![op]));

        // Submitting an answer after reset cleanly advances with ordinal 1
        var postResetOp = session.CurrentFact.Operation;
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var postResetResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(postResetResult.IsSuccess);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(postResetOp));
    }

    // ===========================================================================
    // Requirement H: Disable/re-enable operation resumes at previous count + 1
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_DisableAndReEnable_ResumesAtPreviousCountPlusOne()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetOperations([ArithmeticOperation.Subtraction]);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // 3 Subtraction attempts (ordinals 1, 2, 3)
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.Equal(3, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));

        // Switch to Addition only for 5 attempts (global positions 4 through 8)
        preferences.SetOperations([ArithmeticOperation.Addition]);
        session.ClearCurrentAnswerInput();

        using var store2 = new SqliteLearnerStore(path);
        var session2 = new TrainingSession(store2, new FixedClock(), preferenceStore: preferences);
        await session2.InitializeAsync(startTiming: false);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(ArithmeticOperation.Addition, session2.CurrentFact.Operation);
            session2.SubmitAnswer(session2.CurrentFact.CorrectResult);
            var res = await session2.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess);
            session2.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        Assert.Equal(3, session2.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(5, session2.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(8, session2.Progression.PracticePosition);

        // Re-enable Subtraction only
        preferences.SetOperations([ArithmeticOperation.Subtraction]);
        using var store3 = new SqliteLearnerStore(path);
        var session3 = new TrainingSession(store3, new FixedClock(), preferenceStore: preferences);
        await session3.InitializeAsync(startTiming: false);

        // Subtraction resumes with previous count 3; next ordinal is 3 + 1 = 4
        Assert.Equal(3, session3.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(ArithmeticOperation.Subtraction, session3.CurrentFact.Operation);

        // Ordinal 4 in the 10-slot cycle is Maintenance ((4-1)%10 = 3 => Maintenance)
        // If global position 9 were used, (9-1)%10 = 8 => Due.
        // Independent attempt ordinal 4 ensures role cycle is NOT shifted by the 5 Addition attempts.
        Assert.Equal(PracticeSelectionRole.Maintenance, AdaptivePracticeSelector.GetRequestedRole(4));

        // Complete 4th Subtraction attempt
        session3.SubmitAnswer(session3.CurrentFact.CorrectResult);
        var commitRes = await session3.CommitCurrentEvaluationAsync();
        Assert.True(commitRes.IsSuccess);
        Assert.Equal(4, session3.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(9, session3.Progression.PracticePosition);
    }

    // ===========================================================================
    // Requirement I: Legacy supported migration reconstructs exact counts
    // ===========================================================================

    [Fact]
    public async Task SqliteLearnerStore_LegacyV4Migration_ReconstructsExactOperationCounts_AndRemainsV6()
    {
        var path = GetDatabasePath();
        await CreateLegacyV4DatabaseWithMultipleOperationsAsync(path);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        Assert.Equal(20, snapshot.Progression.PracticePosition);
        Assert.NotNull(snapshot.OperationAcceptedAttemptCounts);

        // In the legacy V4 database:
        // Addition: 12 total attempts (across 2 facts)
        // Subtraction: 8 total attempts (across 1 fact)
        // Multiplication: 0
        // Division: 0
        Assert.Equal(12, snapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Addition]);
        Assert.Equal(8, snapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Subtraction]);
        Assert.Equal(0, snapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Multiplication]);
        Assert.Equal(0, snapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Division]);

        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.NotNull(runtimeSnapshot.OperationAcceptedAttemptCounts);
        Assert.Equal(12, runtimeSnapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Addition]);
        Assert.Equal(8, runtimeSnapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Subtraction]);
        Assert.Equal(0, runtimeSnapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Multiplication]);
        Assert.Equal(0, runtimeSnapshot.OperationAcceptedAttemptCounts[ArithmeticOperation.Division]);
    }

    // ===========================================================================
    // Requirement J: Global FSRS virtual time follows PracticePosition, not ordinal
    // ===========================================================================

    [Fact]
    public async Task TrainingSession_FsrsVirtualTime_TracksGlobalPracticePositionNotOperationOrdinal()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        // Enabled: Addition and Subtraction
        preferences.SetOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction]);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Turn 1 (pos 1): Addition (ordinal 1)
        var fact1 = session.CurrentFact;
        session.SubmitAnswer(fact1.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 2 (pos 2): Subtraction (ordinal 1)
        var fact2 = session.CurrentFact;
        session.SubmitAnswer(fact2.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 3 (pos 3): Addition (ordinal 2)
        var fact3 = session.CurrentFact;
        session.SubmitAnswer(fact3.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var snapshot = await store.LoadSnapshotAsync();

        // Check FSRS states: LastReviewPracticePosition and DuePracticePosition must track global position
        var fsrsStates = snapshot.FsrsStates;
        Assert.True(fsrsStates.ContainsKey(fact1.Id));
        Assert.True(fsrsStates.ContainsKey(fact2.Id));

        var card1 = fsrsStates[fact1.Id];
        var card2 = fsrsStates[fact2.Id];

        // Fact 2 was reviewed at global practice position 2
        Assert.Equal(2, card2.LastReviewPracticePosition);
        // Due position is calculated relative to global position 2 (> 2)
        Assert.True(card2.DuePracticePosition > 2);
    }

    // ===========================================================================
    // Requirement K: Exact physical blocker restart regression
    // ===========================================================================

    [Fact]
    public async Task BlockerRegression_26Addition_SwitchToSubtractionOnly_RestartsAtSubtractionOrdinal1New()
    {
        var path = GetDatabasePath();
        var preferences = new TestPreferenceStore();
        preferences.SetOperations([ArithmeticOperation.Addition]);

        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
            await session.InitializeAsync(startTiming: false);

            for (var i = 0; i < 26; i++)
            {
                Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess);
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }
            }

            Assert.Equal(26, session.Progression.PracticePosition);
            Assert.Equal(26, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
            Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
            await store.CloseAsync();
        }

        preferences.SetOperations([ArithmeticOperation.Subtraction]);

        using (var restartedStore = new SqliteLearnerStore(path))
        {
            var restartedSession = new TrainingSession(restartedStore, new FixedClock(), preferenceStore: preferences);
            await restartedSession.InitializeAsync(startTiming: false);

            Assert.True(restartedSession.IsInitialized);
            Assert.Equal(26, restartedSession.Progression.PracticePosition);
            Assert.Equal(0, restartedSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
            Assert.Equal(26, restartedSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
            Assert.Equal(ArithmeticOperation.Subtraction, restartedSession.CurrentFact.Operation);
            Assert.False(string.IsNullOrWhiteSpace(restartedSession.CurrentFact.Id));
            Assert.Equal(SessionInteractionState.AwaitingAnswer, restartedSession.InteractionState);
        }
    }

    // ===========================================================================
    // Helpers
    // ===========================================================================

    private static async Task CreateLegacyV4DatabaseWithMultipleOperationsAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '4');
            INSERT INTO schema_info VALUES ('store_revision', '10');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1), current_operation TEXT NOT NULL,
                current_max_operand INTEGER NOT NULL, operation_max_operands_json TEXT NOT NULL,
                practice_position INTEGER NOT NULL, completed_checkpoint_level INTEGER NOT NULL,
                active_checkpoint_level INTEGER, checkpoint_attempt_count INTEGER NOT NULL,
                checkpoint_correct_count INTEGER NOT NULL, updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 'Addition', 4,
                '{""Addition"":4,""Subtraction"":3,""Multiplication"":2,""Division"":1}', 20, 0, NULL, 0, 0, '2026-09-09T00:00:00Z');
            CREATE TABLE item_learning_state (
                fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT);
            INSERT INTO item_learning_state VALUES
                ('add:1+1', 'Addition', 1, 1, 7, 7, 0, 7, 1000, 1000, 7, 1, 0, 0, 1, '2026-09-09T00:00:00Z'),
                ('add:2+2', 'Addition', 2, 2, 5, 5, 0, 5, 1100, 1100, 5, 1, 0, 0, 2, '2026-09-09T00:00:00Z'),
                ('sub:3-1', 'Subtraction', 3, 1, 8, 8, 0, 8, 1200, 1200, 8, 1, 0, 0, 3, '2026-09-09T00:00:00Z');
            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                submitted_answer INTEGER, correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL, outcome TEXT NOT NULL,
                response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL);
            INSERT INTO attempt_history VALUES
                ('legacy-1', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 1000, '2026-09-09T00:00:00Z'),
                ('legacy-2', 'sub:3-1', 'Subtraction', 3, 1, 2, 2, 1, 'Correct', 1200, '2026-09-09T00:00:00Z');
            CREATE TABLE fsrs_card_state (
                fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL,
                step INTEGER, stability REAL, difficulty REAL,
                due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER,
                last_rating INTEGER);";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed class TestPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operationPreferences = [];

        public void SetOperations(IEnumerable<ArithmeticOperation> enabledOps)
        {
            var set = enabledOps.ToHashSet();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                _operationPreferences[op] = set.Contains(op);
            }
        }

        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operationPreferences.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operationPreferences[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }

    private sealed class FailingLearnerStore : ILearnerStore
    {
        public bool ShouldFail { get; set; } = true;
        public string StoragePath => "inmemory://failing";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                [],
                1,
                6));
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                [],
                1,
                6));
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PracticeSelectionEvidence(
                request.Operation,
                request.ProspectivePracticePosition,
                request.IntroductionFrontier.Select(f => new PracticeSelectionCandidate(f, ItemLearningState.CreateNew(f), null)).ToList(),
                [],
                [],
                [],
                []));
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ShouldFail
                ? PersistenceResult.Unavailable("Simulated commit failure.")
                : PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
