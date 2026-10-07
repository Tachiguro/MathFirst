namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

/// <summary>
/// Diagnostic characterization tests for the early-state selector failure at prospective practice position 5.
/// Reconstructs the exact physical learner progression through Tasks 1-4 to characterize whether
/// position-5 advance and cold restart from revision-5 state deterministically reproduce the
/// InvalidOperationException due to lack of eligible Addition candidates when broad weakness is active.
/// </summary>
public sealed class EarlyStateSchedulerFailureCharacterizationTests : IDisposable
{
    private readonly string _testDirectory;

    public EarlyStateSchedulerFailureCharacterizationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstEarlyStateChar_" + Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup in temporary directory
        }
    }

    private string GetDatabasePath(string testName) =>
        Path.Combine(_testDirectory, $"{testName}_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EarlyState_AllFour_PositionFiveAdvance_UsesLivenessSafeNewFallback()
    {
        var dbPath = GetDatabasePath("live_advance");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new ScriptedClock();
        var session = new TrainingSession(store, clock, practiceMode: PracticeMode.Custom);

        await session.InitializeAsync(startTiming: true);

        await ReconstructTasksOneThroughFourAsync(session, clock);

        // Observable state assertions prior to prospective position-5 advance
        Assert.Equal(4, session.Progression.PracticePosition);
        Assert.Equal(5, session.Progression.StoreRevision);
        Assert.Equal("add:0+0", session.CurrentFact.Id);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
        Assert.NotNull(session.LastEvaluation);
        Assert.True(session.LastEvaluation.IsCorrect);
        Assert.Equal(AttemptOutcome.Correct, session.LastEvaluation.Outcome);

        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Division));
        Assert.True(session.HasBroadWeakness);

        // Expected future behavior: advance succeeds via terminal New fallback
        var advanced = await session.AdvanceAfterCorrectAnswerAsync(startTiming: true);
        Assert.True(advanced);

        Assert.Equal(4, session.Progression.PracticePosition);
        Assert.Equal(5, session.Progression.StoreRevision);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.NotEqual("add:0+0", session.CurrentFact.Id);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.True(session.HasBroadWeakness);

        // Selected fact must be unmaterialized before selection, gate allowed, curriculum owned, and not immediate predecessor
        var curriculum = new ArithmeticCurriculum();
        var ownership = new AcquisitionOwnershipResolver(curriculum.Addition);
        Assert.True(ownership.IsEligible(session.CurrentFact.Id, 0));
        var gate = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, session.Progression.OperationProgressions);
        Assert.True(gate.Allows(session.CurrentFact));
    }

    [Fact]
    public async Task EarlyState_RevisionFiveRestart_UsesLivenessSafeNewFallback()
    {
        var dbPath = GetDatabasePath("revision_five_restart");
        var clock = new ScriptedClock();

        using (var initialStore = new SqliteLearnerStore(dbPath))
        {
            var initialSession = new TrainingSession(initialStore, clock, practiceMode: PracticeMode.Custom);
            await initialSession.InitializeAsync(startTiming: true);

            await ReconstructTasksOneThroughFourAsync(initialSession, clock);

            // Observable state assertions prior to closing initial session
            Assert.Equal(4, initialSession.Progression.PracticePosition);
            Assert.Equal(5, initialSession.Progression.StoreRevision);
            Assert.Equal("add:0+0", initialSession.CurrentFact.Id);
            Assert.Equal(SessionInteractionState.CorrectFeedback, initialSession.InteractionState);
            Assert.NotNull(initialSession.LastEvaluation);
            Assert.True(initialSession.LastEvaluation.IsCorrect);
            Assert.Equal(AttemptOutcome.Correct, initialSession.LastEvaluation.Outcome);

            Assert.Equal(1, initialSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
            Assert.Equal(1, initialSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
            Assert.Equal(1, initialSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
            Assert.Equal(1, initialSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Division));
            Assert.True(initialSession.HasBroadWeakness);

            await initialStore.CloseAsync();
        }

        // Reopen against the persisted revision-5 SQLite database
        using var reopenedStore = new SqliteLearnerStore(dbPath);
        var reopenedSession = new TrainingSession(reopenedStore, clock, practiceMode: PracticeMode.Custom);

        // Future expected behavior: initialization succeeds from persisted revision-5 state
        await reopenedSession.InitializeAsync(startTiming: true);

        Assert.True(reopenedSession.IsInitialized);
        Assert.Equal(4, reopenedSession.Progression.PracticePosition);
        Assert.Equal(5, reopenedSession.Progression.StoreRevision);
        Assert.Equal(ArithmeticOperation.Addition, reopenedSession.CurrentFact.Operation);
        Assert.NotEqual("add:0+0", reopenedSession.CurrentFact.Id);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, reopenedSession.InteractionState);
        Assert.True(reopenedSession.HasBroadWeakness);
        var reopenedCurriculum = new ArithmeticCurriculum();
        var reopenedGate = GuidedNumberSpaceGate.ForGuided(reopenedCurriculum.Addition, reopenedSession.Progression.OperationProgressions);
        Assert.True(reopenedGate.Allows(reopenedSession.CurrentFact));
    }

    [Fact]
    public void EarlyState_PositionFive_TruthfulRoleSemantics_RequestedDueAndResolvedNew()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();

        var add00 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var sub00 = new ArithmeticFact(ArithmeticOperation.Subtraction, 0, 0);
        var div01 = new ArithmeticFact(ArithmeticOperation.Division, 0, 1);
        var mul00 = new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 0);

        var stateAdd = ItemLearningState.CreateNew(add00);
        stateAdd.TotalAttempts = 1;
        stateAdd.CorrectAttempts = 1;
        stateAdd.ConsecutiveCorrectStreak = 1;
        stateAdd.NeedsRemediation = false;

        var stateSub = ItemLearningState.CreateNew(sub00);
        stateSub.TotalAttempts = 1;
        stateSub.IncorrectAttempts = 1;
        stateSub.NeedsRemediation = true;

        var stateDiv = ItemLearningState.CreateNew(div01);
        stateDiv.TotalAttempts = 1;
        stateDiv.IncorrectAttempts = 1;
        stateDiv.NeedsRemediation = true;

        var stateMul = ItemLearningState.CreateNew(mul00);
        stateMul.TotalAttempts = 1;
        stateMul.CorrectAttempts = 1;
        stateMul.ConsecutiveCorrectStreak = 1;
        stateMul.NeedsRemediation = false;

        var cardAdd = new FsrsCardState(add00.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 4, FsrsRating.Good);
        var cardSub = new FsrsCardState(sub00.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 2, FsrsRating.Again);
        var cardDiv = new FsrsCardState(div01.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 3, FsrsRating.Again);
        var cardMul = new FsrsCardState(mul00.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Good);

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [add00.Id] = stateAdd,
            [sub00.Id] = stateSub,
            [div01.Id] = stateDiv,
            [mul00.Id] = stateMul
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [add00.Id] = cardAdd,
            [sub00.Id] = cardSub,
            [div01.Id] = cardDiv,
            [mul00.Id] = cardMul
        };

        var progressions = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => curriculum.GetCurriculum(op));

        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 5,
            currentSessionOrder: 0,
            progressions,
            curricula,
            new PracticeCandidateIndex([add00, sub00, div01, mul00], states, cards),
            recentAcceptedFactsOldestToNewest: [mul00, sub00, div01, add00],
            scheduledOperationAttemptOrdinal: 2,
            hasBroadWeakness: true);

        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsMaterialized);
        Assert.True(result.IsNewIntroduction);
        Assert.NotEqual(add00.Id, result.Fact.Id);
        Assert.Equal(ArithmeticOperation.Addition, result.Fact.Operation);
    }

    [Fact]
    public void EarlyState_SingleOperation_TurnTwoDue_WithoutBroadWeakness_SucceedsViaOpportunisticNewPromotion()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();

        var add00 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var stateAdd = ItemLearningState.CreateNew(add00);
        stateAdd.TotalAttempts = 1;
        stateAdd.CorrectAttempts = 1;
        stateAdd.ConsecutiveCorrectStreak = 1;
        var cardAdd = new FsrsCardState(add00.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Good);

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [add00.Id] = stateAdd };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [add00.Id] = cardAdd };

        var progressions = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));
        var curricula = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => curriculum.GetCurriculum(op));

        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 0,
            progressions,
            curricula,
            new PracticeCandidateIndex([add00], states, cards),
            recentAcceptedFactsOldestToNewest: [add00],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: [ArithmeticOperation.Addition],
            hasBroadWeakness: false);

        var result = selector.SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsMaterialized);
        Assert.True(result.IsNewIntroduction);
        Assert.NotEqual(add00.Id, result.Fact.Id);
        Assert.Equal(ArithmeticOperation.Addition, result.Fact.Operation);
    }

    private static async Task ReconstructTasksOneThroughFourAsync(TrainingSession session, ScriptedClock clock)
    {
        // ---------------------------------------------------------------------
        // Task 1: Prospective Position 1 (Multiplication: mul:0*0)
        // ---------------------------------------------------------------------
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(1, session.Progression.StoreRevision);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.Equal("mul:0*0", session.CurrentFact.Id);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        clock.LatencyMs = 4042;
        var eval1 = session.SubmitAnswer(0);
        Assert.True(eval1.IsCorrect);
        Assert.Equal(AttemptOutcome.Correct, eval1.Outcome);
        Assert.False(eval1.ChangeSet.Attempt.IsFluent);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        var commit1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit1.IsSuccess);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(2, session.Progression.StoreRevision);

        var advanced1 = await session.AdvanceAfterCorrectAnswerAsync(startTiming: true);
        Assert.True(advanced1);
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.Equal("sub:0-0", session.CurrentFact.Id);

        // ---------------------------------------------------------------------
        // Task 2: Prospective Position 2 (Subtraction: sub:0-0)
        // ---------------------------------------------------------------------
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(2, session.Progression.StoreRevision);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        clock.LatencyMs = 7928;
        var eval2 = session.SubmitAnswer(1); // 0-0 = 0, submitted 1 -> Incorrect
        Assert.False(eval2.IsCorrect);
        Assert.Equal(AttemptOutcome.Incorrect, eval2.Outcome);
        Assert.False(eval2.ChangeSet.Attempt.IsFluent);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);

        var commit2 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit2.IsSuccess);
        Assert.Equal(2, session.Progression.PracticePosition);
        Assert.Equal(3, session.Progression.StoreRevision);

        var ack2 = await session.AcknowledgeFeedbackAsync(startTiming: true);
        Assert.True(ack2);
        Assert.Equal(ArithmeticOperation.Division, session.CurrentFact.Operation);
        Assert.Equal("div:0/1", session.CurrentFact.Id);

        // ---------------------------------------------------------------------
        // Task 3: Prospective Position 3 (Division: div:0/1)
        // ---------------------------------------------------------------------
        Assert.Equal(2, session.Progression.PracticePosition);
        Assert.Equal(3, session.Progression.StoreRevision);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        clock.LatencyMs = 15070;
        var eval3 = session.SubmitTimeout();
        Assert.False(eval3.IsCorrect);
        Assert.Equal(AttemptOutcome.Timeout, eval3.Outcome);
        Assert.False(eval3.ChangeSet.Attempt.IsFluent);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);

        var commit3 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit3.IsSuccess);
        Assert.Equal(3, session.Progression.PracticePosition);
        Assert.Equal(4, session.Progression.StoreRevision);

        var ack3 = await session.AcknowledgeFeedbackAsync(startTiming: true);
        Assert.True(ack3);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        Assert.Equal("add:0+0", session.CurrentFact.Id);

        // ---------------------------------------------------------------------
        // Task 4: Prospective Position 4 (Addition: add:0+0)
        // ---------------------------------------------------------------------
        Assert.Equal(3, session.Progression.PracticePosition);
        Assert.Equal(4, session.Progression.StoreRevision);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        clock.LatencyMs = 3314;
        var eval4 = session.SubmitAnswer(0);
        Assert.True(eval4.IsCorrect);
        Assert.Equal(AttemptOutcome.Correct, eval4.Outcome);
        Assert.True(eval4.ChangeSet.Attempt.IsFluent);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        var commit4 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit4.IsSuccess);
        Assert.Equal(4, session.Progression.PracticePosition);
        Assert.Equal(5, session.Progression.StoreRevision);
    }

    private sealed class ScriptedClock : IClock
    {
        public long LatencyMs { get; set; } = 900;
        public long GetTimestamp() => 1_000_000L;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(LatencyMs);
    }
}
