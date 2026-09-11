namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class AdaptivePaceRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstAdaptivePace_" + Guid.NewGuid().ToString("N"));

    public AdaptivePaceRuntimeTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ColdLearner_UsesStaticPaceAndNineSecondDeadline()
    {
        var result = Calculate([], SelectedFact(), [SelectedFact().Id]);

        Assert.Equal(4500, result.LearnerPaceMs);
        Assert.Equal(4500, result.OperationPaceMs);
        Assert.Equal(4500, result.BandPaceMs);
        Assert.Equal(4500, result.FactPaceMs);
        Assert.Equal(0, result.InstabilityAllowanceMs);
        Assert.Equal(9000, result.DeadlineMs);
    }

    [Fact]
    public void PaceSamples_IncludeOnlyCorrectPositionedAttempts()
    {
        var fact = SelectedFact();
        var correctOnly = Calculate(
            [Attempt(1, fact, AttemptOutcome.Correct, 1000)],
            fact,
            [fact.Id]);
        var mixed = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Correct, 1000),
                Attempt(2, fact, AttemptOutcome.Incorrect, 600),
                Attempt(3, fact, AttemptOutcome.Timeout, 12000),
                Attempt(null, fact, AttemptOutcome.Correct, 12000)
            ],
            fact,
            [fact.Id]);

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
            [selected.Id, bandPeer.Id]);

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
            [selected.Id]);
        Assert.Equal(noOperation.LearnerPaceMs, noOperation.OperationPaceMs);

        var noBand = Calculate(
            [Attempt(1, operationPeer, AttemptOutcome.Correct, 1000)],
            selected,
            [selected.Id]);
        Assert.Equal(noBand.OperationPaceMs, noBand.BandPaceMs);

        var noExactFact = Calculate(
            [Attempt(1, bandPeer, AttemptOutcome.Correct, 1000)],
            selected,
            [selected.Id, bandPeer.Id]);
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
            [fact.Id]);

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
            [fact.Id]);
        var uncapped = Calculate(
            [
                Attempt(1, fact, AttemptOutcome.Incorrect, 2000),
                Attempt(2, fact, AttemptOutcome.Timeout, 9000)
            ],
            fact,
            [fact.Id]);

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
        Assert.Equal(expectedDeadlineMs, AdaptivePacePolicy.CalculateDeadline(paceMs, allowanceMs));

    [Theory]
    [InlineData(8999, AttemptOutcome.Correct)]
    [InlineData(9000, AttemptOutcome.Timeout)]
    [InlineData(9001, AttemptOutcome.Timeout)]
    public async Task SubmissionLogic_AuthoritativelyGradesDeadlineBoundary(
        long elapsedMs,
        AttemptOutcome expectedOutcome)
    {
        var clock = new FakeClock { ElapsedMs = elapsedMs };
        using var store = new SnapshotStore(FreshSnapshot());
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        Assert.Equal(9000, session.CurrentFactDeadlineMs);
        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(expectedOutcome, evaluation.Outcome);
        Assert.Equal(expectedOutcome == AttemptOutcome.Correct, evaluation.IsCorrect);
    }

    [Fact]
    public async Task LateSubmitAndUiTimeoutRace_PersistsOneAcceptedTimeoutOnly()
    {
        var path = Path.Combine(_directory, "timeout-race.db");
        var clock = new FakeClock { ElapsedMs = 9000 };
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
        IEnumerable<string> currentOwnedFrontierFactIds) =>
        AdaptivePacePolicy.Calculate(selectedFact, currentOwnedFrontierFactIds, attempts);

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
            new Dictionary<string, MathFirst.Application.Scheduling.FsrsCardState>(StringComparer.Ordinal),
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
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
