namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class AdaptivePaceRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstAdaptivePace_" + Guid.NewGuid().ToString("N"));

    public AdaptivePaceRuntimeTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ColdLearner_Unproven_UsesStaticPaceAndFifteenSecondNoveltyDeadline()
    {
        var result = Calculate([], SelectedFact(), [SelectedFact().Id], isProven: false);

        Assert.Equal(4500, result.LearnerPaceMs);
        Assert.Equal(4500, result.OperationPaceMs);
        Assert.Equal(4500, result.BandPaceMs);
        Assert.Equal(4500, result.FactPaceMs);
        Assert.Equal(0, result.InstabilityAllowanceMs);
        Assert.Equal(15000, result.DeadlineMs);
    }

    [Fact]
    public void ColdLearner_Proven_UsesStaticPaceAndNineSecondDeadline()
    {
        var result = Calculate([], SelectedFact(), [SelectedFact().Id], isProven: true);

        Assert.Equal(4500, result.LearnerPaceMs);
        Assert.Equal(4500, result.OperationPaceMs);
        Assert.Equal(4500, result.BandPaceMs);
        Assert.Equal(4500, result.FactPaceMs);
        Assert.Equal(0, result.InstabilityAllowanceMs);
        Assert.Equal(9000, result.DeadlineMs);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 4)]
    [InlineData(int.MaxValue, 10)]
    public void DigitCount_MatchesDeterministicIntegerRules(int value, int expected) =>
        Assert.Equal(expected, AdaptivePacePolicy.GetDigitCount(value));

    [Fact]
    public void DigitCount_NegativeValue_ThrowsArgumentOutOfRangeException() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePacePolicy.GetDigitCount(-1));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1000)]
    [InlineData(3, 2000)]
    [InlineData(4, 3000)]
    [InlineData(10, 9000)]
    public void EntryAllowance_MatchesAnswerLengthFormula(int digitCount, long expectedAllowanceMs) =>
        Assert.Equal(expectedAllowanceMs, AdaptivePacePolicy.CalculateEntryAllowanceMs(digitCount));

    [Fact]
    public void EntryAllowance_InvalidDigitCount_ThrowsArgumentOutOfRangeException() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePacePolicy.CalculateEntryAllowanceMs(0));

    [Theory]
    [InlineData(1, 15000)]
    [InlineData(2, 20000)]
    [InlineData(3, 25000)]
    [InlineData(4, 30000)]
    [InlineData(5, 30000)]
    [InlineData(10, 30000)]
    public void NoveltyFloor_MatchesAnswerLengthFormula(int digitCount, long expectedNoveltyFloorMs) =>
        Assert.Equal(expectedNoveltyFloorMs, AdaptivePacePolicy.CalculateNoveltyFloorMs(digitCount));

    [Fact]
    public void NoveltyFloor_InvalidDigitCount_ThrowsArgumentOutOfRangeException() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePacePolicy.CalculateNoveltyFloorMs(0));

    [Fact]
    public void UnprovenOneDigit_UsesFifteenSecondNoveltyFloor()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 2, 3); // 5 (1 digit)
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: fact.CorrectResult,
            isProven: false);

        Assert.Equal(15000, deadline);
    }

    [Fact]
    public void UnprovenTwoDigit_UsesTwentySecondNoveltyFloor()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 5, 5); // 10 (2 digits)
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: fact.CorrectResult,
            isProven: false);

        Assert.Equal(20000, deadline);
    }

    [Fact]
    public void UnprovenThreeDigit_UsesTwentyFiveSecondNoveltyFloor()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 50, 50); // 100 (3 digits)
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: fact.CorrectResult,
            isProven: false);

        Assert.Equal(25000, deadline);
    }

    [Fact]
    public void UnprovenFourDigit_UsesThirtySecondNoveltyFloor()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 500, 500); // 1000 (4 digits)
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: fact.CorrectResult,
            isProven: false);

        Assert.Equal(30000, deadline);
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void UnprovenGreaterThanFourDigits_CapsAtThirtySeconds(int correctResult)
    {
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: correctResult,
            isProven: false);

        Assert.Equal(30000, deadline);
    }

    [Fact]
    public void ProvenOneDigit_NoNoveltyFloor_AllowsMinimumAdaptiveDeadline()
    {
        var deadlineFast = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 1000,
            instabilityAllowanceMs: 0,
            correctResult: 7,
            isProven: true);
        var deadlineStandard = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: 7,
            isProven: true);

        Assert.Equal(3000, deadlineFast);
        Assert.Equal(9000, deadlineStandard);
    }

    [Fact]
    public void ProvenTwoDigit_IncludesOneSecondEntryAllowance()
    {
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: 10,
            isProven: true);

        // 2 * 4500 + 0 + 1000 = 10000 ms
        Assert.Equal(10000, deadline);
    }

    [Fact]
    public void ProvenThreeDigit_IncludesTwoSecondEntryAllowance()
    {
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: 100,
            isProven: true);

        // 2 * 4500 + 0 + 2000 = 11000 ms
        Assert.Equal(11000, deadline);
    }

    [Fact]
    public void ProvenFourDigit_IncludesThreeSecondEntryAllowance()
    {
        var deadline = AdaptivePacePolicy.CalculateDeadline(
            factPaceMs: 4500,
            instabilityAllowanceMs: 0,
            correctResult: 1000,
            isProven: true);

        // 2 * 4500 + 0 + 3000 = 12000 ms
        Assert.Equal(12000, deadline);
    }

    [Fact]
    public void PaceSamples_IncludeOnlyCorrectPositionedAttempts()
    {
        var fact = SelectedFact();
        var correctOnly = Calculate(
            [Attempt(1, fact, AttemptOutcome.Correct, 1000)],
            fact,
            [fact.Id],
            isProven: true);
        var mixed = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Correct, 1000),
                Attempt(2, fact, AttemptOutcome.Incorrect, 600),
                Attempt(3, fact, AttemptOutcome.Timeout, 12000),
                Attempt(null, fact, AttemptOutcome.Correct, 12000)
            ],
            fact,
            [fact.Id],
            isProven: true);

        Assert.Equal(correctOnly.LearnerPaceMs, mixed.LearnerPaceMs);
        Assert.Equal(correctOnly.OperationPaceMs, mixed.OperationPaceMs);
        Assert.Equal(correctOnly.BandPaceMs, mixed.BandPaceMs);
        Assert.Equal(correctOnly.FactPaceMs, mixed.FactPaceMs);
    }

    [Theory]
    [InlineData(1, 600)]
    [InlineData(599, 600)]
    [InlineData(600, 600)]
    [InlineData(12000, 12000)]
    [InlineData(12001, 12000)]
    [InlineData(99999, 12000)]
    public void LatencySamples_AreClamped(long input, long expected) =>
        Assert.Equal(expected, AdaptivePacePolicy.ClampLatencySample(input));

    [Fact]
    public void Median_UsesOddMiddleAndEvenHalfUpRounding()
    {
        Assert.Equal(1000, AdaptivePacePolicy.Median([12000, 600, 1000]));
        Assert.Equal(1001, AdaptivePacePolicy.Median([1000, 1001]));
    }

    [Fact]
    public void Hierarchy_ShrinksLearnerThenOperationThenBandThenExactFact()
    {
        var selected = SelectedFact();
        var bandPeer = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var operationPeer = new ArithmeticFact(ArithmeticOperation.Addition, 5, 5);
        var otherOperation = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 1);
        var result = Calculate(
            [
                Attempt(1, selected, AttemptOutcome.Correct, 1500),
                Attempt(2, bandPeer, AttemptOutcome.Correct, 1500),
                Attempt(3, operationPeer, AttemptOutcome.Correct, 1500),
                Attempt(4, otherOperation, AttemptOutcome.Correct, 1500)
            ],
            selected,
            [selected.Id, bandPeer.Id],
            isProven: true);

        Assert.Equal(3750, result.LearnerPaceMs);
        Assert.Equal(3136, result.OperationPaceMs);
        Assert.Equal(2727, result.BandPaceMs);
        Assert.Equal(2482, result.FactPaceMs);
        Assert.Equal(5000, result.DeadlineMs);
    }

    [Fact]
    public void Hierarchy_EmptyChildFallsBackExactlyToItsParent()
    {
        var selected = SelectedFact();
        var bandPeer = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var operationPeer = new ArithmeticFact(ArithmeticOperation.Addition, 5, 5);
        var otherOperation = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 1);

        var noOperation = Calculate(
            [Attempt(1, otherOperation, AttemptOutcome.Correct, 1000)],
            selected,
            [selected.Id],
            isProven: true);
        Assert.Equal(noOperation.LearnerPaceMs, noOperation.OperationPaceMs);

        var noBand = Calculate(
            [Attempt(1, operationPeer, AttemptOutcome.Correct, 1000)],
            selected,
            [selected.Id],
            isProven: true);
        Assert.Equal(noBand.OperationPaceMs, noBand.BandPaceMs);

        var noExactFact = Calculate(
            [Attempt(1, bandPeer, AttemptOutcome.Correct, 1000)],
            selected,
            [selected.Id, bandPeer.Id],
            isProven: true);
        Assert.Equal(noExactFact.BandPaceMs, noExactFact.FactPaceMs);
    }

    [Fact]
    public void ExactFact_UsesItsLatestFiveCorrectSamples()
    {
        var fact = SelectedFact();
        var result = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Correct, 12000),
                Attempt(2, fact, AttemptOutcome.Correct, 1000),
                Attempt(3, fact, AttemptOutcome.Correct, 1000),
                Attempt(4, fact, AttemptOutcome.Correct, 1000),
                Attempt(5, fact, AttemptOutcome.Correct, 10000),
                Attempt(6, fact, AttemptOutcome.Correct, 10000)
            ],
            fact,
            [fact.Id],
            isProven: true);

        Assert.Equal(
            AdaptivePacePolicy.Shrink(result.BandPaceMs, 4, [1000, 1000, 1000, 10000, 10000]),
            result.FactPaceMs);
    }

    [Fact]
    public void Instability_UsesLatestFiveExactFactOutcomesAndCapsAtThreeSeconds()
    {
        var fact = SelectedFact();
        var capped = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Timeout, 9000),
                Attempt(2, fact, AttemptOutcome.Incorrect, 2000),
                Attempt(3, fact, AttemptOutcome.Incorrect, 2000),
                Attempt(4, fact, AttemptOutcome.Timeout, 9000),
                Attempt(5, fact, AttemptOutcome.Correct, 2000),
                Attempt(6, fact, AttemptOutcome.Incorrect, 2000),
                Attempt(7, fact, AttemptOutcome.Timeout, 9000)
            ],
            fact,
            [fact.Id],
            isProven: true);
        var uncapped = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Incorrect, 2000),
                Attempt(2, fact, AttemptOutcome.Timeout, 9000)
            ],
            fact,
            [fact.Id],
            isProven: true);

        Assert.Equal(3000, capped.InstabilityAllowanceMs);
        Assert.Equal(2500, uncapped.InstabilityAllowanceMs);
    }

    [Theory]
    [InlineData(1400, 0, 3000)]
    [InlineData(1500, 0, 3000)]
    [InlineData(1501, 0, 3100)]
    [InlineData(14950, 0, 29900)]
    [InlineData(14000, 3000, 30000)]
    public void Deadline_CeilsToHundredMillisecondsAndClamps(
        long paceMs,
        long allowanceMs,
        long expectedDeadlineMs) =>
        Assert.Equal(expectedDeadlineMs, AdaptivePacePolicy.CalculateDeadline(paceMs, allowanceMs, correctResult: 0, isProven: true));

    [Fact]
    public async Task FreshFact_NoItemLearningState_UsesNoveltyFloor()
    {
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // 0 + 0 = 0 (1 digit) is unproven -> 15000 ms
        Assert.Equal(15000, session.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task ExistingItemState_WithZeroCorrectAttempts_RemainsUnproven()
    {
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 5;
        itemState.CorrectAttempts = 0;
        itemState.IncorrectAttempts = 5;

        using var store = new SnapshotStore(FreshSnapshot(new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [fact.Id] = itemState
        }));
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        Assert.Equal(15000, session.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task ExistingDurableItemState_WithCorrectAttemptsGreaterThanZero_IsProvenAfterInit()
    {
        var curriculum = new ArithmeticCurriculum();
        var frontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var itemStates = frontier.ToDictionary(
            fact => fact.Id,
            fact =>
            {
                var state = ItemLearningState.CreateNew(fact);
                state.TotalAttempts = 1;
                state.CorrectAttempts = 1;
                return state;
            },
            StringComparer.Ordinal);

        using var store = new SnapshotStore(FreshSnapshot(itemStates));
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        // Proven 1-digit with cold pace (4500 ms) -> 9000 ms (no novelty floor)
        Assert.Equal(9000, session.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task FirstIncorrect_PreservesNoveltyProtection()
    {
        var path = Path.Combine(_directory, "first-incorrect.db");
        using (var store = new SqliteLearnerStore(path))
        {
            var clock = new FakeClock();
            var session = new TrainingSession(store, clock);
            await session.InitializeAsync();

            Assert.Equal(15000, session.CurrentFactDeadlineMs);
            clock.ElapsedMs = 2000;
            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
            Assert.False(eval.IsCorrect);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.True(session.AcknowledgeFeedback());

            // Still unproven -> next presentation has novelty floor
            Assert.True(session.CurrentFactDeadlineMs >= 15000);
        }
    }

    [Fact]
    public async Task FirstTimeout_PreservesNoveltyProtection()
    {
        var path = Path.Combine(_directory, "first-timeout.db");
        using (var store = new SqliteLearnerStore(path))
        {
            var clock = new FakeClock();
            var session = new TrainingSession(store, clock);
            await session.InitializeAsync();

            Assert.Equal(15000, session.CurrentFactDeadlineMs);
            clock.ElapsedMs = 15000;
            var eval = session.RecordTimeout();
            Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.True(session.AcknowledgeFeedback());

            Assert.True(session.CurrentFactDeadlineMs >= 15000);
        }
    }

    [Fact]
    public async Task FirstSuccessfullyPersistedCorrect_MakesNextPresentationProven()
    {
        var path = Path.Combine(_directory, "first-correct.db");
        string firstFactId;
        using (var store = new SqliteLearnerStore(path))
        {
            var clock = new FakeClock();
            var session = new TrainingSession(store, clock);
            await session.InitializeAsync();

            firstFactId = session.CurrentFact.Id;
            Assert.Equal(15000, session.CurrentFactDeadlineMs); // unproven 1-digit

            clock.ElapsedMs = 2000;
            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True(eval.IsCorrect);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.True(session.AdvanceAfterCorrectAnswer());

            Assert.True(session.ItemStates.TryGetValue(firstFactId, out var state));
            Assert.True(state.CorrectAttempts > 0);
        }

        using (var reopenedStore = new SqliteLearnerStore(path))
        {
            var snapshot = await reopenedStore.LoadSnapshotAsync();
            Assert.True(snapshot.ItemStates.TryGetValue(firstFactId, out var state));
            Assert.Equal(1, state.CorrectAttempts);
        }
    }

    [Fact]
    public async Task PersistenceFailure_OnFirstCorrect_DoesNotMakeFactProven()
    {
        var failingStore = new FailingCommitStore(FreshSnapshot());
        var clock = new FakeClock { ElapsedMs = 2000 };
        var session = new TrainingSession(failingStore, clock);
        await session.InitializeAsync();

        var factId = session.CurrentFact.Id;
        Assert.Equal(15000, session.CurrentFactDeadlineMs);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);

        Assert.False(session.ItemStates.TryGetValue(factId, out var state) && state.CorrectAttempts > 0);
    }

    [Fact]
    public async Task ClassificationAndLatency_RemainIndependentOfNoveltyDeadline()
    {
        var clock = new FakeClock { ElapsedMs = 5710 };
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        Assert.Equal(15000, session.CurrentFactDeadlineMs);
        Assert.Equal(4000, session.CurrentFactFluencyThresholdMs);

        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(evaluation.IsCorrect);
        Assert.Equal(5710, evaluation.LatencyMs);
        Assert.Equal(5710, session.LastResponseLatencyMs);
        Assert.False(evaluation.ChangeSet!.Attempt.IsFluent);

        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);
        Assert.Equal(FsrsRating.Hard, session.FsrsStates[session.CurrentFact.Id].LastRating);
    }

    [Theory]
    [InlineData(14999, AttemptOutcome.Correct)]
    [InlineData(15000, AttemptOutcome.Timeout)]
    [InlineData(15001, AttemptOutcome.Timeout)]
    public async Task SubmissionLogic_AuthoritativelyGradesNoveltyDeadlineBoundary(
        long elapsedMs,
        AttemptOutcome expectedOutcome)
    {
        var clock = new FakeClock { ElapsedMs = elapsedMs };
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        Assert.Equal(15000, session.CurrentFactDeadlineMs);
        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(expectedOutcome, evaluation.Outcome);
        Assert.Equal(expectedOutcome == AttemptOutcome.Correct, evaluation.IsCorrect);
    }

    [Fact]
    public async Task LateSubmitAndUiTimeoutRace_PersistsOneAcceptedTimeoutOnly()
    {
        var path = Path.Combine(_directory, "timeout-race.db");
        var clock = new FakeClock { ElapsedMs = 15000 };
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var submitted = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(AttemptOutcome.Timeout, submitted.Outcome);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        var uiTimeout = session.RecordTimeout();
        Assert.Same(submitted, uiTimeout);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        var snapshot = await store.LoadSnapshotAsync();
        var attempt = Assert.Single(snapshot.RecentAttempts);
        Assert.Equal(AttemptOutcome.Timeout, attempt.Outcome);
        Assert.Equal(1, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task DurablePositionedEvidence_RehydratesSameNextPaceAndDeadline()
    {
        var path = Path.Combine(_directory, "restart.db");
        string expectedFactId;
        long expectedPace;
        long expectedDeadline;
        using (var store = new SqliteLearnerStore(path))
        {
            var clock = new FakeClock();
            var session = new TrainingSession(store, clock);
            await session.InitializeAsync();
            for (var index = 0; index < 12; index++)
            {
                clock.ElapsedMs = 700 + (index % 4 * 100);
                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                Assert.True(session.AdvanceAfterCorrectAnswer());
            }

            expectedFactId = session.CurrentFact.Id;
            expectedPace = session.CurrentFactExpectedPaceMs;
            expectedDeadline = session.CurrentFactDeadlineMs;
        }

        using var reopenedStore = new SqliteLearnerStore(path);
        var reopened = new TrainingSession(reopenedStore, new FakeClock());
        await reopened.InitializeAsync();

        Assert.Equal(expectedFactId, reopened.CurrentFact.Id);
        Assert.Equal(expectedPace, reopened.CurrentFactExpectedPaceMs);
        Assert.Equal(expectedDeadline, reopened.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task RollingLatency_IsNotAdaptivePaceEvidence()
    {
        var curriculum = new ArithmeticCurriculum();
        var frontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var itemStates = frontier.ToDictionary(
            fact => fact.Id,
            fact =>
            {
                var state = ItemLearningState.CreateNew(fact);
                state.TotalAttempts = 50;
                state.CorrectAttempts = 50;
                state.RollingLatencyMs = 600;
                return state;
            },
            StringComparer.Ordinal);
        using var store = new SnapshotStore(FreshSnapshot(itemStates));
        var session = new TrainingSession(store, new FakeClock());

        await session.InitializeAsync();

        Assert.Equal(4500, session.CurrentFactExpectedPaceMs);
        Assert.Equal(9000, session.CurrentFactDeadlineMs);
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
        }
    }

    private static AdaptivePaceResult Calculate(
        IEnumerable<AttemptRecord> attempts,
        ArithmeticFact selectedFact,
        IEnumerable<string> currentOwnedFrontierFactIds,
        bool isProven = false) =>
        AdaptivePacePolicy.Calculate(selectedFact, currentOwnedFrontierFactIds, attempts, isProven);

    private static ArithmeticFact SelectedFact() =>
        new(ArithmeticOperation.Addition, 1, 1);

    private static AttemptRecord Attempt(
        long? practicePosition,
        ArithmeticFact fact,
        AttemptOutcome outcome,
        long latencyMs) =>
        new(
            $"attempt-{practicePosition?.ToString() ?? "legacy"}-{fact.Id}-{outcome}",
            fact.Id,
            fact.Operation,
            fact.LeftOperand,
            fact.RightOperand,
            outcome == AttemptOutcome.Timeout ? null : fact.CorrectResult,
            fact.CorrectResult,
            outcome == AttemptOutcome.Correct,
            outcome == AttemptOutcome.Correct && latencyMs <= 2500,
            latencyMs,
            DateTimeOffset.UnixEpoch.AddSeconds(practicePosition ?? 0),
            outcome,
            practicePosition);

    private static LearnerSnapshot FreshSnapshot(
        IReadOnlyDictionary<string, ItemLearningState>? itemStates = null) =>
        new(
            LearnerProgression.CreateFresh(),
            itemStates ?? new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
            [],
            1,
            LearnerProgression.DefaultSchemaVersion);

    private sealed class FakeClock : IClock
    {
        public long ElapsedMs { get; set; }
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(ElapsedMs);
    }

    private sealed class SnapshotStore(LearnerSnapshot snapshot) : ILearnerStore
    {
        public string StoragePath => "inmemory.db";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FailingCommitStore(LearnerSnapshot snapshot) : ILearnerStore
    {
        public string StoragePath => "failing.db";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Unavailable("Simulated disk write failure."));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
