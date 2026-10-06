namespace MathFirst.Core.Tests;

using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Xunit;

public sealed class CyberDefenseCalibrationGateTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class CalibratedTestingStore : ILearnerStore
    {
        private readonly int _initialPositionedCorrectCount;
        private LearnerProgression _progression;
        private readonly Dictionary<string, ItemLearningState> _items = new(StringComparer.Ordinal);
        private readonly List<AttemptRecord> _attempts = [];
        private long _revision = 1;

        public CalibratedTestingStore(int initialPositionedCorrectCount = 0)
        {
            _initialPositionedCorrectCount = initialPositionedCorrectCount;
            _progression = LearnerProgression.CreateFresh();
        }

        public string StoragePath => "inmemory://cyber-defense-calibration-gate";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                _progression,
                _items,
                new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
                _attempts,
                _revision,
                LearnerProgression.DefaultSchemaVersion,
                positionedCorrectAttemptCount: _initialPositionedCorrectCount));

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            _revision = changeSet.ExpectedRevision + 1;
            _progression = changeSet.UpdatedProgression;
            _items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            _attempts.Add(changeSet.Attempt);
            return Task.FromResult(PersistenceResult.Success(_revision));
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static async Task<(TrainingSession Session, FakeClock Clock, CalibratedTestingStore Store)> CreateSessionAsync(
        int positionedCorrectAttemptCount = 0)
    {
        var clock = new FakeClock();
        var store = new CalibratedTestingStore(positionedCorrectAttemptCount);
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: true);
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();
        return (session, clock, store);
    }

    /// <summary>
    /// Translates evaluation outcome to CyberDefense combat state using the exact
    /// classification contract declared in Home.razor.
    /// </summary>
    private static void ApplyCombatFromHome(
        TrainingSession session,
        SubmissionEvaluation? eval,
        CyberDefenseEncounterState encounter)
    {
        var homePath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var home = File.ReadAllText(homePath);

        var isCriticalMatch = Regex.Match(
            home,
            @"var\s+isCritical\s*=\s*(?<expr>[^;]+);",
            RegexOptions.Singleline);

        if (!isCriticalMatch.Success)
        {
            throw new InvalidOperationException("Could not find isCritical definition in Home.razor");
        }

        if (eval is null)
        {
            encounter.RecordIncorrectAnswer();
            return;
        }

        if (eval.IsCorrect)
        {
            var expr = isCriticalMatch.Groups["expr"].Value;
            var gatesOnCalibration = expr.Contains("Session.IsPaceCalibrationReady");

            bool isCritical;
            if (gatesOnCalibration)
            {
                isCritical = session.IsPaceCalibrationReady && eval.LatencyMs <= session.CurrentFactEasyThresholdMs;
            }
            else
            {
                // Uncalibrated latency-only defect in Home.razor before Slice 6
                isCritical = eval.LatencyMs <= session.CurrentFactEasyThresholdMs;
            }

            if (isCritical)
            {
                encounter.RecordCriticalHit();
            }
            else
            {
                encounter.RecordCorrectAnswer();
            }
        }
        else
        {
            encounter.RecordIncorrectAnswer();
        }
    }

    [Fact]
    public async Task CyberDefense_NotCalibrated_FastCorrectDealsNormalDamage()
    {
        // Latency is under easy threshold, but pace calibration is not ready (0 / 24).
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 0);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.False(session.IsPaceCalibrationReady);
        clock.AdvanceMs(800);
        Assert.True(800 <= session.CurrentFactEasyThresholdMs);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);

        ApplyCombatFromHome(session, eval, encounter);

        // Pre-calibration: Critical Hit is impossible. Response speed must NOT increase combat damage.
        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);
        Assert.NotEqual(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task CyberDefense_NotCalibrated_SlowCorrectDealsNormalDamage()
    {
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 0);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.False(session.IsPaceCalibrationReady);
        var slowLatency = session.CurrentFactEasyThresholdMs + 500;
        clock.AdvanceMs(slowLatency);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.LatencyMs > session.CurrentFactEasyThresholdMs);

        ApplyCombatFromHome(session, eval, encounter);

        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task CyberDefense_Calibrated_FastCorrectDealsCriticalDamage()
    {
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.True(session.IsPaceCalibrationReady);
        clock.AdvanceMs(750);
        Assert.True(750 <= session.CurrentFactEasyThresholdMs);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.LatencyMs <= session.CurrentFactEasyThresholdMs);

        ApplyCombatFromHome(session, eval, encounter);

        // Post-calibration: Correct AND ResponseLatencyMs <= CurrentFactEasyThresholdMs = Critical Hit = 2 HP
        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
        Assert.Equal(initialHp - 2, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task CyberDefense_Calibrated_ExactEasyThreshold_IsCritical()
    {
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.True(session.IsPaceCalibrationReady);
        var exactThreshold = session.CurrentFactEasyThresholdMs;
        clock.AdvanceMs(exactThreshold);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.Equal(exactThreshold, eval.LatencyMs);

        ApplyCombatFromHome(session, eval, encounter);

        // Inclusive <=: exact boundary yields Critical Hit = 2 HP
        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
        Assert.Equal(initialHp - 2, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task CyberDefense_Calibrated_SlowCorrectDealsNormalDamage()
    {
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.True(session.IsPaceCalibrationReady);
        var slowLatency = session.CurrentFactEasyThresholdMs + 100;
        clock.AdvanceMs(slowLatency);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.LatencyMs > session.CurrentFactEasyThresholdMs);

        ApplyCombatFromHome(session, eval, encounter);

        // Post-calibration: Correct AND ResponseLatencyMs > CurrentFactEasyThresholdMs = normal hit = 1 HP
        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);
    }

    [Fact]
    public async Task CyberDefense_Calibrated_IncorrectDealsNoDamage()
    {
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;
        var initialShields = encounter.ShieldSegments;

        Assert.True(session.IsPaceCalibrationReady);
        clock.AdvanceMs(400); // Very fast, but incorrect

        var wrongAnswer = session.CurrentFact.CorrectResult + 1;
        var eval = session.SubmitAnswer(wrongAnswer);
        Assert.False(eval.IsCorrect);
        Assert.Equal(AttemptOutcome.Incorrect, eval.Outcome);

        ApplyCombatFromHome(session, eval, encounter);

        // Incorrect answers deal 0 damage and preserve Cyber Defense failure/shield behavior
        Assert.Equal(0, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Blocked, encounter.LastFeedback);
        Assert.Equal(initialHp, encounter.EnemyHitPoints);
        Assert.Equal(initialShields - 1, encounter.ShieldSegments);
    }

    [Fact]
    public async Task CyberDefense_Calibrated_TimeoutDealsNoDamage()
    {
        var (session, _, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;
        var initialShields = encounter.ShieldSegments;

        Assert.True(session.IsPaceCalibrationReady);

        session.RecordTimeout();
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);

        ApplyCombatFromHome(session, eval: null, encounter);

        // Timeout deals 0 damage and decrements shield
        Assert.Equal(0, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Blocked, encounter.LastFeedback);
        Assert.Equal(initialHp, encounter.EnemyHitPoints);
        Assert.Equal(initialShields - 1, encounter.ShieldSegments);
    }

    [Fact]
    public async Task CyberDefense_ReturningCalibratedLearner_CanCriticalImmediately()
    {
        // Snapshot initialization starts learner with IsPaceCalibrationReady == true
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var encounter = new CyberDefenseEncounterState();

        Assert.True(session.IsPaceCalibrationReady);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);

        // First answer submitted by returning learner is fast and correct
        clock.AdvanceMs(600);
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);

        ApplyCombatFromHome(session, eval, encounter);

        // Immediately eligible for Critical Hit with no extra warmup or session-local counter
        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
    }

    [Fact]
    public async Task CyberDefense_CalibrationTransition_CriticalHitOnlyUnlockedAfter24thPositionedCorrectAttempt()
    {
        // Start at 23 positioned correct attempts (not yet calibrated)
        var (session, clock, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 23);
        var encounter = new CyberDefenseEncounterState();

        Assert.False(session.IsPaceCalibrationReady);
        Assert.Equal(23, session.PositionedCorrectAttemptCount);

        // 1. Fast answer while at attempt #23 -> normal 1 HP hit
        clock.AdvanceMs(800);
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        ApplyCombatFromHome(session, eval1, encounter);
        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);

        // Persist the 24th positioned correct attempt
        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);
        Assert.True(session.IsPaceCalibrationReady);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.AdvanceAfterCorrectAnswer());

        // 2. Fast answer on subsequent fact (now calibrated) -> Critical Hit = 2 HP
        clock.AdvanceMs(700);
        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        ApplyCombatFromHome(session, eval2, encounter);
        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
    }

    [Fact]
    public async Task CyberDefense_CombatClassification_HasZeroReverseAuthorityOverLearningTelemetry()
    {
        // Both sessions start with identical state
        var (session1, clock1, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var (session2, clock2, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);

        clock1.AdvanceMs(700);
        clock2.AdvanceMs(700);

        var eval1 = session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
        var eval2 = session2.SubmitAnswer(session2.CurrentFact.CorrectResult);

        // session1 has combat presentation observation applied
        var encounter = new CyberDefenseEncounterState();
        ApplyCombatFromHome(session1, eval1, encounter);
        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);

        // session2 has NO combat presentation observation applied

        // Verify combat processing has ZERO reverse authority over all learning telemetry
        Assert.Equal(eval2.Outcome, eval1.Outcome);
        Assert.Equal(eval2.LatencyMs, eval1.LatencyMs);
        Assert.Equal(eval2.ChangeSet.Attempt.IsFluent, eval1.ChangeSet.Attempt.IsFluent);
        Assert.Equal(eval2.ChangeSet.Attempt.Outcome, eval1.ChangeSet.Attempt.Outcome);
        Assert.Equal(eval2.ChangeSet.Attempt.ResponseLatencyMs, eval1.ChangeSet.Attempt.ResponseLatencyMs);
        Assert.Equal(eval2.ChangeSet.Attempt.PracticePosition, eval1.ChangeSet.Attempt.PracticePosition);

        var item1 = eval1.ChangeSet.UpdatedItemState;
        var item2 = eval2.ChangeSet.UpdatedItemState;
        Assert.Equal(item2.NeedsRemediation, item1.NeedsRemediation);
        Assert.Equal(item2.TotalAttempts, item1.TotalAttempts);
        Assert.Equal(item2.ConsecutiveCorrectStreak, item1.ConsecutiveCorrectStreak);

        var prog1 = eval1.ChangeSet.UpdatedProgression;
        var prog2 = eval2.ChangeSet.UpdatedProgression;
        Assert.Equal(prog2.PracticePosition, prog1.PracticePosition);

        var op = eval1.ChangeSet.Attempt.Operation;
        var opProg1 = eval1.ChangeSet.OperationProgressions[op];
        var opProg2 = eval2.ChangeSet.OperationProgressions[op];
        Assert.Equal(opProg2.BandIndex, opProg1.BandIndex);

        // Commit both and verify final session telemetry
        var commit1 = await session1.CommitCurrentEvaluationAsync();
        var commit2 = await session2.CommitCurrentEvaluationAsync();
        Assert.True(commit1.IsSuccess);
        Assert.True(commit2.IsSuccess);

        Assert.Equal(session2.PositionedCorrectAttemptCount, session1.PositionedCorrectAttemptCount);
        Assert.Equal(session2.IsPaceCalibrationReady, session1.IsPaceCalibrationReady);
        Assert.Equal(session2.SessionCorrectCount, session1.SessionCorrectCount);
        Assert.Equal(session2.SessionTotalCount, session1.SessionTotalCount);
        Assert.Equal(session2.Progression.PracticePosition, session1.Progression.PracticePosition);
    }

    [Theory]
    [InlineData(0, 1500)]
    [InlineData(9, 1500)]
    [InlineData(10, 3000)]
    [InlineData(99, 3000)]
    [InlineData(100, 4500)]
    [InlineData(999, 4500)]
    [InlineData(1000, 6000)]
    public void CyberDefenseRadarTimingPolicy_CalculateCriticalHitThresholdMs_ExactBoundaryMultiplication(int correctResult, long expectedThresholdMs)
    {
        const long baseEasyThresholdMs = 1500;
        var threshold = CyberDefenseRadarTimingPolicy.CalculateCriticalHitThresholdMs(baseEasyThresholdMs, correctResult);
        Assert.Equal(expectedThresholdMs, threshold);
    }

    [Fact]
    public void CyberDefenseRadarTimingPolicy_CalculateCriticalHitThresholdMs_RejectsNegativeInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRadarTimingPolicy.CalculateCriticalHitThresholdMs(-1, 42));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseRadarTimingPolicy.CalculateCriticalHitThresholdMs(1500, -1));
    }

    [Fact]
    public async Task CyberDefense_DigitScaledCriticalHitThreshold_DerivedWithoutMutatingLearningThresholds()
    {
        var (session, _, _) = await CreateSessionAsync(positionedCorrectAttemptCount: 24);
        var fact = session.CurrentFact;
        var digitCount = AdaptivePacePolicy.GetDigitCount(fact.CorrectResult);

        var easyThreshold = session.CurrentFactEasyThresholdMs;
        var fluencyThreshold = session.CurrentFactFluencyThresholdMs;
        var expectedPace = session.CurrentFactExpectedPaceMs;
        var criticalThreshold = session.CurrentFactCriticalHitThresholdMs;

        // Critical Hit threshold is exact digit-scaled multiple
        Assert.Equal(checked(easyThreshold * digitCount), criticalThreshold);

        // Underlying learning thresholds remain completely unchanged and bounded
        Assert.Equal(easyThreshold, session.CurrentFactEasyThresholdMs);
        Assert.InRange(session.CurrentFactEasyThresholdMs, AdaptivePacePolicy.MinimumEasyThresholdMs, AdaptivePacePolicy.MaximumEasyThresholdMs);
        Assert.Equal(fluencyThreshold, session.CurrentFactFluencyThresholdMs);
        Assert.InRange(session.CurrentFactFluencyThresholdMs, AdaptivePacePolicy.MinimumFluencyThresholdMs, AdaptivePacePolicy.MaximumFluencyThresholdMs);
        Assert.Equal(expectedPace, session.CurrentFactExpectedPaceMs);
    }

    [Fact]
    public void CyberDefense_CriticalHitSeparation_PreservesLearningClassificationSemantics()
    {
        // Example: Base easy threshold = 1500ms, fluency threshold = 2200ms.
        // Fact with 3-digit answer (e.g. 100) has CriticalHitThresholdMs = 4500ms.
        // A response at 3000ms is within the 4500ms Critical Hit window, but for learning evaluation
        // it must classify strictly under unchanged learning thresholds (3000ms > 2200ms -> Hard, non-fluent).
        const long easyThresholdMs = 1500;
        const long fluencyThresholdMs = 2200;
        const int threeDigitResult = 100;
        var criticalHitThresholdMs = CyberDefenseRadarTimingPolicy.CalculateCriticalHitThresholdMs(easyThresholdMs, threeDigitResult);
        Assert.Equal(4500, criticalHitThresholdMs);

        const long responseLatencyMs = 3000;
        Assert.True(responseLatencyMs <= criticalHitThresholdMs, "Latency is within the expanded 3-digit Critical Hit window.");

        var classification = AdaptiveAttemptClassifier.Classify(
            AttemptOutcome.Correct,
            responseLatencyMs,
            easyThresholdMs,
            fluencyThresholdMs);

        // Learning classification is NOT made Easy/Fluent by the expanded Critical Hit window
        Assert.False(classification.IsFluent);
        Assert.Equal(FsrsRating.Hard, classification.Rating);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
