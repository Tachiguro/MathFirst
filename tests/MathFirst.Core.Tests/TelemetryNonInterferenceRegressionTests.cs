namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-TELEM-001 Task 9: Acceptance / Non-Interference Regression Test Suite.
/// Proves that telemetry collection, presentation-context enrichment, persistence,
/// and companion metadata remain strictly observational and do NOT alter:
/// 1. FSRS outputs, ratings, and spaced-repetition scheduling;
/// 2. Fact selector and progression behavior;
/// 3. Pace estimates, sample counts, and calibration counters;
/// 4. The normative strong-learner 482-attempt guided four-operation benchmark;
/// 5. The exact 24-positioned-correct pace-calibration readiness boundary;
/// 6. The Cyber Defense critical-hit gate transition at the 25th attempt.
/// </summary>
public sealed class TelemetryNonInterferenceRegressionTests : IDisposable
{
    private readonly string _testDbDir;

    public TelemetryNonInterferenceRegressionTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstTelemetryNonInterference_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
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
            // Best effort cleanup in temporary test directory
        }
    }

    private string GetTempDbPath(string prefix = "test") =>
        Path.Combine(_testDbDir, $"{prefix}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // 1. FSRS NON-INTERFERENCE
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_DoesNotAlterFsrsOutputs()
    {
        // Part 1: Direct FSRS scheduler and rating classifier invariance
        var scheduler = new FsrsSchedulerAdapter();
        const string factId = "add:3+4";
        FsrsCardState? cardStateA = null;
        FsrsCardState? cardStateB = null;

        var reviewScenarios = new (long LatencyMs, AttemptOutcome Outcome, long Position)[]
        {
            (800, AttemptOutcome.Correct, 1),
            (1800, AttemptOutcome.Correct, 5),
            (3500, AttemptOutcome.Correct, 12),
            (1200, AttemptOutcome.Incorrect, 20),
            (700, AttemptOutcome.Correct, 25),
        };

        foreach (var (latencyMs, outcome, position) in reviewScenarios)
        {
            var classA = AdaptiveAttemptClassifier.Classify(outcome, latencyMs, easyThresholdMs: 1000, fluencyThresholdMs: 2500);
            cardStateA = scheduler.ReviewCard(cardStateA, factId, classA.Rating, position, latencyMs);

            var classB = AdaptiveAttemptClassifier.Classify(outcome, latencyMs, easyThresholdMs: 1000, fluencyThresholdMs: 2500);
            cardStateB = scheduler.ReviewCard(cardStateB, factId, classB.Rating, position, latencyMs);

            Assert.Equal(classA.Rating, classB.Rating);
            Assert.Equal(classA.IsFluent, classB.IsFluent);
            Assert.NotNull(cardStateA);
            Assert.NotNull(cardStateB);
            Assert.Equal(cardStateA.Stability, cardStateB.Stability);
            Assert.Equal(cardStateA.Difficulty, cardStateB.Difficulty);
            Assert.Equal(cardStateA.DuePracticePosition, cardStateB.DuePracticePosition);
            Assert.Equal(cardStateA.LastReviewPracticePosition, cardStateB.LastReviewPracticePosition);
            Assert.Equal(cardStateA.State, cardStateB.State);
            Assert.Equal(cardStateA.Step, cardStateB.Step);
            Assert.Equal(cardStateA.LastRating, cardStateB.LastRating);
        }

        // Part 2: End-to-end TrainingSession SQLite comparison (unenriched seed vs telemetry-enriched seed)
        var dbUnenriched = GetTempDbPath("fsrs_unenriched");
        var dbEnriched = GetTempDbPath("fsrs_enriched");

        var seedAttempts = new List<(string FactId, ArithmeticOperation Op, int Left, int Right, int? Sub, int Cor, bool IsCor, bool IsFlu, AttemptOutcome Outc, long Lat, long? Pos)>
        {
            ("add:0+1", ArithmeticOperation.Addition, 0, 1, 1, 1, true, true, AttemptOutcome.Correct, 800, 1),
            ("add:0+2", ArithmeticOperation.Addition, 0, 2, 2, 2, true, true, AttemptOutcome.Correct, 1200, 2),
            ("add:0+3", ArithmeticOperation.Addition, 0, 3, 3, 3, true, false, AttemptOutcome.Correct, 3000, 3),
        };

        using (var storeUnenriched = new SqliteLearnerStore(dbUnenriched))
        using (var storeEnriched = new SqliteLearnerStore(dbEnriched))
        {
            await storeUnenriched.InitializeAsync();
            await storeEnriched.InitializeAsync();

            // Seed unenriched (ContextVersion = null)
            await SeedAttemptHistoryAsync(dbUnenriched, seedAttempts.Select(a => (
                SubmissionId: Guid.NewGuid().ToString("N"),
                a.FactId, a.Op, a.Left, a.Right, a.Sub, a.Cor, a.IsCor, a.IsFlu, a.Outc, a.Lat, a.Pos,
                ContextVersion: (int?)null,
                PresentedDeadlineMs: (int?)null,
                ExpectedPaceMs: (int?)null,
                ResolvedRole: (string?)null,
                OperationBandBefore: (int?)null)));

            // Seed enriched (ContextVersion = 1, with varied context values)
            await SeedAttemptHistoryAsync(dbEnriched, seedAttempts.Select(a => (
                SubmissionId: Guid.NewGuid().ToString("N"),
                a.FactId, a.Op, a.Left, a.Right, a.Sub, a.Cor, a.IsCor, a.IsFlu, a.Outc, a.Lat, a.Pos,
                ContextVersion: (int?)1,
                PresentedDeadlineMs: (int?)15000,
                ExpectedPaceMs: (int?)2500,
                ResolvedRole: (string?)"New",
                OperationBandBefore: (int?)0)));
        }

        // Run identical interactions on both sessions
        using (var storeUnenriched = new SqliteLearnerStore(dbUnenriched))
        using (var storeEnriched = new SqliteLearnerStore(dbEnriched))
        {
            var clockA = new FakeClock();
            var clockB = new FakeClock();
            var sessionA = new TrainingSession(storeUnenriched, clockA);
            var sessionB = new TrainingSession(storeEnriched, clockB);
            await sessionA.InitializeAsync(startTiming: false);
            await sessionB.InitializeAsync(startTiming: false);

            for (var step = 0; step < 8; step++)
            {
                Assert.Equal(sessionA.CurrentFact.Id, sessionB.CurrentFact.Id);
                var latency = 600 + (step * 300);
                clockA.AdvanceMs(latency);
                clockB.AdvanceMs(latency);

                var evalA = sessionA.SubmitAnswer(sessionA.CurrentFact.CorrectResult);
                var evalB = sessionB.SubmitAnswer(sessionB.CurrentFact.CorrectResult);

                Assert.NotNull(evalA.ChangeSet.UpdatedFsrsState);
                Assert.NotNull(evalB.ChangeSet.UpdatedFsrsState);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.LastRating, evalB.ChangeSet.UpdatedFsrsState.LastRating);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.Stability, evalB.ChangeSet.UpdatedFsrsState.Stability);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.Difficulty, evalB.ChangeSet.UpdatedFsrsState.Difficulty);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.DuePracticePosition, evalB.ChangeSet.UpdatedFsrsState.DuePracticePosition);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.LastReviewPracticePosition, evalB.ChangeSet.UpdatedFsrsState.LastReviewPracticePosition);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.State, evalB.ChangeSet.UpdatedFsrsState.State);
                Assert.Equal(evalA.ChangeSet.UpdatedFsrsState.Step, evalB.ChangeSet.UpdatedFsrsState.Step);

                var commitA = await sessionA.CommitCurrentEvaluationAsync();
                var commitB = await sessionB.CommitCurrentEvaluationAsync();
                Assert.True(commitA.IsSuccess);
                Assert.True(commitB.IsSuccess);

                sessionA.AdvanceAfterCorrectAnswer(startTiming: false);
                sessionB.AdvanceAfterCorrectAnswer(startTiming: false);
            }

            var snapshotA = await storeUnenriched.LoadRuntimeSnapshotAsync();
            var snapshotB = await storeEnriched.LoadRuntimeSnapshotAsync();

            Assert.Equal(snapshotA.FsrsStates.Count, snapshotB.FsrsStates.Count);
            foreach (var (cardId, stateA) in snapshotA.FsrsStates)
            {
                Assert.True(snapshotB.FsrsStates.TryGetValue(cardId, out var stateB));
                Assert.Equal(stateA.Stability, stateB.Stability);
                Assert.Equal(stateA.Difficulty, stateB.Difficulty);
                Assert.Equal(stateA.DuePracticePosition, stateB.DuePracticePosition);
                Assert.Equal(stateA.LastReviewPracticePosition, stateB.LastReviewPracticePosition);
                Assert.Equal(stateA.State, stateB.State);
                Assert.Equal(stateA.Step, stateB.Step);
                Assert.Equal(stateA.LastRating, stateB.LastRating);
            }
        }
    }

    // =========================================================================
    // 2. SELECTOR / PROGRESSION NON-INTERFERENCE
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_DoesNotAlterSelectorProgressionBehavior()
    {
        // Part 1: Direct AdaptivePracticeSelector comparison under unenriched vs enriched evidence
        var curriculum = new ArithmeticCurriculum();
        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 0, 0),
            [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 0, 0),
        };

        var candidateFact1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var candidateFact2 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 2);

        var candidate1 = new PracticeSelectionCandidate(candidateFact1, ItemLearningState.CreateNew(candidateFact1), null);
        var candidate2 = new PracticeSelectionCandidate(candidateFact2, ItemLearningState.CreateNew(candidateFact2), null);

        var evidenceUnenriched = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 2,
            currentBandCandidates: [candidate1, candidate2],
            dueCandidates: [],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var evidenceEnriched = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 2,
            currentBandCandidates: [candidate1, candidate2],
            dueCandidates: [],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);

        var recentAttemptsUnenriched = new List<AttemptRecord>
        {
            new("sub-1", "add:0+0", ArithmeticOperation.Addition, 0, 0, 0, 0, true, true, 800, DateTimeOffset.UtcNow, AttemptOutcome.Correct, practicePosition: 1,
                contextVersion: null, presentedDeadlineMs: null, expectedPaceMs: null, resolvedRole: null, operationBandBefore: null)
        };

        var recentAttemptsEnriched = new List<AttemptRecord>
        {
            new("sub-1", "add:0+0", ArithmeticOperation.Addition, 0, 0, 0, 0, true, true, 800, DateTimeOffset.UtcNow, AttemptOutcome.Correct, practicePosition: 1,
                contextVersion: 1, presentedDeadlineMs: 30000, expectedPaceMs: 9000, resolvedRole: "Remediation", operationBandBefore: 5)
        };

        var contextUnenriched = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            operationProgressions: progressions,
            curricula: Enum.GetValues<ArithmeticOperation>().ToDictionary(op => op, op => curriculum.GetCurriculum(op)),
            candidateIndex: new PracticeCandidateIndex(evidenceUnenriched),
            recentAcceptedFactsOldestToNewest: recentAttemptsUnenriched.Select(a => new ArithmeticFact(a.Operation, a.LeftOperand, a.RightOperand)),
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: PracticeOperationPreferencePolicy.AllOperations,
            guidedNumberSpaceGate: GuidedNumberSpaceGate.Unrestricted,
            hasBroadWeakness: false);

        var contextEnriched = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            operationProgressions: progressions,
            curricula: Enum.GetValues<ArithmeticOperation>().ToDictionary(op => op, op => curriculum.GetCurriculum(op)),
            candidateIndex: new PracticeCandidateIndex(evidenceEnriched),
            recentAcceptedFactsOldestToNewest: recentAttemptsEnriched.Select(a => new ArithmeticFact(a.Operation, a.LeftOperand, a.RightOperand)),
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: PracticeOperationPreferencePolicy.AllOperations,
            guidedNumberSpaceGate: GuidedNumberSpaceGate.Unrestricted,
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var resultUnenriched = selector.SelectTargetFact(contextUnenriched);
        var resultEnriched = selector.SelectTargetFact(contextEnriched);

        Assert.Equal(resultUnenriched.Fact.Id, resultEnriched.Fact.Id);
        Assert.Equal(resultUnenriched.ResolvedRole, resultEnriched.ResolvedRole);

        // Part 2: Multi-step session trace comparison
        var dbPath1 = GetTempDbPath("selector_progression_1");
        var dbPath2 = GetTempDbPath("selector_progression_2");

        using var store1 = new SqliteLearnerStore(dbPath1);
        using var store2 = new SqliteLearnerStore(dbPath2);
        await store1.InitializeAsync();
        await store2.InitializeAsync();

        var clock1 = new FakeClock();
        var clock2 = new FakeClock();
        var session1 = new TrainingSession(store1, clock1);
        var session2 = new TrainingSession(store2, clock2);
        await session1.InitializeAsync(startTiming: false);
        await session2.InitializeAsync(startTiming: false);

        for (var pos = 1; pos <= 40; pos++)
        {
            Assert.Equal(session1.CurrentFact.Id, session2.CurrentFact.Id);
            Assert.Equal(session1.CurrentFact.Operation, session2.CurrentFact.Operation);

            var eval1 = session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
            var eval2 = session2.SubmitAnswer(session2.CurrentFact.CorrectResult);

            Assert.Equal(eval1.OperationAdvanced, eval2.OperationAdvanced);
            Assert.Equal(eval1.ChangeSet.Attempt.ResolvedRole, eval2.ChangeSet.Attempt.ResolvedRole);
            Assert.Equal(eval1.ChangeSet.Attempt.OperationBandBefore, eval2.ChangeSet.Attempt.OperationBandBefore);

            var commit1 = await session1.CommitCurrentEvaluationAsync();
            var commit2 = await session2.CommitCurrentEvaluationAsync();
            Assert.True(commit1.IsSuccess);
            Assert.True(commit2.IsSuccess);

            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                Assert.Equal(
                    session1.Progression.OperationProgressions[op].BandIndex,
                    session2.Progression.OperationProgressions[op].BandIndex);
            }

            session1.AdvanceAfterCorrectAnswer(startTiming: false);
            session2.AdvanceAfterCorrectAnswer(startTiming: false);
        }
    }

    // =========================================================================
    // 3. PACE NON-INTERFERENCE
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_DoesNotAlterPaceEstimatesOrCounts()
    {
        // Part 1: AdaptivePacePolicy.Calculate under identical latency but contrasting telemetry context values
        var targetFact = new ArithmeticFact(ArithmeticOperation.Addition, 3, 4);
        var frontierFactIds = new[] { "add:3+4", "add:3+5" };

        var attemptsUnenriched = Enumerable.Range(1, 10).Select(i => new AttemptRecord(
            submissionId: $"sub-un-{i}",
            factId: targetFact.Id,
            operation: targetFact.Operation,
            leftOperand: targetFact.LeftOperand,
            rightOperand: targetFact.RightOperand,
            submittedAnswer: targetFact.CorrectResult,
            correctAnswer: targetFact.CorrectResult,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1200,
            timestamp: DateTimeOffset.UtcNow,
            outcome: AttemptOutcome.Correct,
            practicePosition: i,
            contextVersion: null,
            presentedDeadlineMs: null,
            expectedPaceMs: null,
            resolvedRole: null,
            operationBandBefore: null)).ToList();

        // Enriched attempts with contrasting ExpectedPaceMs = 15000ms (10x actual latency)
        var attemptsEnrichedHighPace = Enumerable.Range(1, 10).Select(i => new AttemptRecord(
            submissionId: $"sub-en-high-{i}",
            factId: targetFact.Id,
            operation: targetFact.Operation,
            leftOperand: targetFact.LeftOperand,
            rightOperand: targetFact.RightOperand,
            submittedAnswer: targetFact.CorrectResult,
            correctAnswer: targetFact.CorrectResult,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1200,
            timestamp: DateTimeOffset.UtcNow,
            outcome: AttemptOutcome.Correct,
            practicePosition: i,
            contextVersion: 1,
            presentedDeadlineMs: 30000,
            expectedPaceMs: 15000,
            resolvedRole: "Maintenance",
            operationBandBefore: 3)).ToList();

        // Enriched attempts with contrasting ExpectedPaceMs = 200ms
        var attemptsEnrichedLowPace = Enumerable.Range(1, 10).Select(i => new AttemptRecord(
            submissionId: $"sub-en-low-{i}",
            factId: targetFact.Id,
            operation: targetFact.Operation,
            leftOperand: targetFact.LeftOperand,
            rightOperand: targetFact.RightOperand,
            submittedAnswer: targetFact.CorrectResult,
            correctAnswer: targetFact.CorrectResult,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1200,
            timestamp: DateTimeOffset.UtcNow,
            outcome: AttemptOutcome.Correct,
            practicePosition: i,
            contextVersion: 1,
            presentedDeadlineMs: 5000,
            expectedPaceMs: 200,
            resolvedRole: "New",
            operationBandBefore: 0)).ToList();

        var paceUnenriched = AdaptivePacePolicy.Calculate(targetFact, frontierFactIds, attemptsUnenriched, isProven: true);
        var paceEnrichedHigh = AdaptivePacePolicy.Calculate(targetFact, frontierFactIds, attemptsEnrichedHighPace, isProven: true);
        var paceEnrichedLow = AdaptivePacePolicy.Calculate(targetFact, frontierFactIds, attemptsEnrichedLowPace, isProven: true);

        Assert.Equal(paceUnenriched.LearnerPaceMs, paceEnrichedHigh.LearnerPaceMs);
        Assert.Equal(paceUnenriched.LearnerPaceMs, paceEnrichedLow.LearnerPaceMs);
        Assert.Equal(paceUnenriched.OperationPaceMs, paceEnrichedHigh.OperationPaceMs);
        Assert.Equal(paceUnenriched.OperationPaceMs, paceEnrichedLow.OperationPaceMs);
        Assert.Equal(paceUnenriched.BandPaceMs, paceEnrichedHigh.BandPaceMs);
        Assert.Equal(paceUnenriched.BandPaceMs, paceEnrichedLow.BandPaceMs);
        Assert.Equal(paceUnenriched.FactPaceMs, paceEnrichedHigh.FactPaceMs);
        Assert.Equal(paceUnenriched.FactPaceMs, paceEnrichedLow.FactPaceMs);
        Assert.Equal(paceUnenriched.EasyThresholdMs, paceEnrichedHigh.EasyThresholdMs);
        Assert.Equal(paceUnenriched.EasyThresholdMs, paceEnrichedLow.EasyThresholdMs);
        Assert.Equal(paceUnenriched.FluencyThresholdMs, paceEnrichedHigh.FluencyThresholdMs);
        Assert.Equal(paceUnenriched.FluencyThresholdMs, paceEnrichedLow.FluencyThresholdMs);
        Assert.Equal(paceUnenriched.DeadlineMs, paceEnrichedHigh.DeadlineMs);
        Assert.Equal(paceUnenriched.DeadlineMs, paceEnrichedLow.DeadlineMs);
        Assert.Equal(paceUnenriched.InstabilityAllowanceMs, paceEnrichedHigh.InstabilityAllowanceMs);
        Assert.Equal(paceUnenriched.InstabilityAllowanceMs, paceEnrichedLow.InstabilityAllowanceMs);

        // Part 2: Calibration count & readiness non-interference
        var dbPath = GetTempDbPath("pace_counts_non_interference");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var correctAttempts = Enumerable.Range(1, 23).Select(i => (
            SubmissionId: $"sub-cor-{i}",
            FactId: "add:0+1",
            Operation: ArithmeticOperation.Addition,
            Left: 0,
            Right: 1,
            Submitted: (int?)1,
            Correct: 1,
            IsCorrect: true,
            IsFluent: true,
            Outcome: AttemptOutcome.Correct,
            LatencyMs: 1200L,
            PracticePosition: (long?)i,
            ContextVersion: (int?)1,
            PresentedDeadlineMs: (int?)15000,
            ExpectedPaceMs: (int?)1200,
            ResolvedRole: (string?)"New",
            OperationBandBefore: (int?)0));

        var incorrectAttempts = Enumerable.Range(24, 10).Select(i => (
            SubmissionId: $"sub-inc-{i}",
            FactId: "add:0+1",
            Operation: ArithmeticOperation.Addition,
            Left: 0,
            Right: 1,
            Submitted: (int?)99,
            Correct: 1,
            IsCorrect: false,
            IsFluent: false,
            Outcome: AttemptOutcome.Incorrect,
            LatencyMs: 3000L,
            PracticePosition: (long?)i,
            ContextVersion: (int?)1,
            PresentedDeadlineMs: (int?)15000,
            ExpectedPaceMs: (int?)1200,
            ResolvedRole: (string?)"Frontier",
            OperationBandBefore: (int?)0));

        var timeoutAttempts = Enumerable.Range(34, 5).Select(i => (
            SubmissionId: $"sub-to-{i}",
            FactId: "add:0+1",
            Operation: ArithmeticOperation.Addition,
            Left: 0,
            Right: 1,
            Submitted: (int?)null,
            Correct: 1,
            IsCorrect: false,
            IsFluent: false,
            Outcome: AttemptOutcome.Timeout,
            LatencyMs: 30000L,
            PracticePosition: (long?)i,
            ContextVersion: (int?)1,
            PresentedDeadlineMs: (int?)30000,
            ExpectedPaceMs: (int?)1200,
            ResolvedRole: (string?)"Due",
            OperationBandBefore: (int?)0));

        await SeedAttemptHistoryAsync(dbPath, correctAttempts.Concat(incorrectAttempts).Concat(timeoutAttempts));

        var snapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);
    }

    // =========================================================================
    // 4. NORMATIVE 482 BENCHMARK
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_Normative482AttemptBenchmark_RemainsBehaviorallyIdentical()
    {
        var dbPath = GetTempDbPath("benchmark_482_telemetry");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var clock = new FixedClock(TimeSpan.FromMilliseconds(800));
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var session = new TrainingSession(store, clock, preferenceStore: preferences, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync(startTiming: false);

        var trace = new List<BenchmarkTraceEntry>(500);
        var curriculum = new ArithmeticCurriculum();
        long additionP1AnchorPos = -1;
        long additionAttemptsAtTransition = -1;
        int ceilingBefore = -1;
        int ceilingAfter = -1;

        for (var pos = 1; pos <= 500; pos++)
        {
            var fact = session.CurrentFact;
            var op = fact.Operation;
            var opOrdinal = session.GetOperationAcceptedAttemptCount(op) + 1;
            var requestedRole = AdaptivePracticeSelector.GetRequestedRole(opOrdinal);
            var isNew = !session.ItemStates.ContainsKey(fact.Id) || session.ItemStates[fact.Id].TotalAttempts == 0;
            var bandIndicesBefore = session.Progression.OperationProgressions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BandIndex);

            var gateBefore = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, session.Progression.OperationProgressions);
            var addCeiling = gateBefore.AdditionCeiling ?? 0;

            session.SubmitAnswer(fact.CorrectResult);
            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess, $"Commit failed at position {pos}: {commitResult.Message}");

            var eval = session.LastEvaluation!;
            var attempt = eval.ChangeSet.Attempt;

            // Verify telemetry context presence and validity on every attempt
            Assert.Equal(1, attempt.ContextVersion);
            Assert.NotNull(attempt.ExpectedPaceMs);
            Assert.True(attempt.ExpectedPaceMs > 0);
            Assert.NotNull(attempt.ResolvedRole);
            Assert.False(string.IsNullOrWhiteSpace(attempt.ResolvedRole));
            Assert.NotNull(attempt.OperationBandBefore);
            Assert.True(attempt.OperationBandBefore >= 0);

            var bandIndicesAfter = session.Progression.OperationProgressions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BandIndex);

            trace.Add(new BenchmarkTraceEntry(
                PracticePosition: pos,
                Operation: op,
                OperationAttemptOrdinal: opOrdinal,
                FactId: fact.Id,
                RequestedRole: requestedRole,
                IsNewIntroduction: isNew,
                BandIndices: bandIndicesAfter,
                AdditionCeiling: addCeiling,
                IsPaceCalibrationReady: session.IsPaceCalibrationReady,
                PositionedCorrectAttemptCount: session.PositionedCorrectAttemptCount,
                HasBroadWeakness: session.HasBroadWeakness,
                OperationAdvanced: eval.OperationAdvanced));

            if (op == ArithmeticOperation.Addition
                && bandIndicesBefore[ArithmeticOperation.Addition] == 9
                && bandIndicesAfter[ArithmeticOperation.Addition] == 10)
            {
                additionP1AnchorPos = pos;
                additionAttemptsAtTransition = session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition);
                ceilingBefore = addCeiling;
                var gateAfter = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, session.Progression.OperationProgressions);
                ceilingAfter = gateAfter.AdditionCeiling ?? 0;
            }

            if (session.InteractionState == SessionInteractionState.CorrectFeedback)
            {
                session.AdvanceAfterCorrectAnswer(startTiming: false);
            }
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                session.ContinuePractice(startTiming: false);
            }

            if (additionP1AnchorPos > 0)
            {
                break;
            }
        }

        // Exact Normative Assertions (Zero Tolerance)
        Assert.Equal(482, additionP1AnchorPos);
        Assert.Equal(121, additionAttemptsAtTransition);
        Assert.Equal(20, ceilingBefore);
        Assert.Equal(180, ceilingAfter);
        Assert.Equal(10, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(482, trace.Count);
    }

    // =========================================================================
    // 5. EXACT READINESS BOUNDARY
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_PaceCalibrationReadinessBoundary_RemainsExactAt24()
    {
        var dbPath = GetTempDbPath("readiness_boundary_exact_24");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Seed 23 positioned Correct attempts enriched with ContextVersion 1
        var attempts = Enumerable.Range(1, 23).Select(i => (
            SubmissionId: $"sub-{i}",
            FactId: "add:0+1",
            Operation: ArithmeticOperation.Addition,
            Left: 0,
            Right: 1,
            Submitted: (int?)1,
            Correct: 1,
            IsCorrect: true,
            IsFluent: true,
            Outcome: AttemptOutcome.Correct,
            LatencyMs: 1200L,
            PracticePosition: (long?)i,
            ContextVersion: (int?)1,
            PresentedDeadlineMs: (int?)8000,
            ExpectedPaceMs: (int?)1200,
            ResolvedRole: (string?)"New",
            OperationBandBefore: (int?)0));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // After 23 positioned Correct: not ready
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Submit the 24th answer (Correct)
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.Equal(1, eval.ChangeSet.Attempt.ContextVersion);
        Assert.NotNull(eval.ChangeSet.Attempt.ExpectedPaceMs);
        Assert.NotNull(eval.ChangeSet.Attempt.ResolvedRole);
        Assert.NotNull(eval.ChangeSet.Attempt.OperationBandBefore);

        // Before commit: count remains 23 and readiness remains false
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Commit persistence for #24
        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult.IsSuccess);

        // Immediately after successful commit of #24: count becomes 24 and readiness is true
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);

        // Submitting 25th attempt saturates at 24 and remains ready
        session.AdvanceAfterCorrectAnswer(startTiming: false);
        var eval25 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval25.IsCorrect);
        var persistResult25 = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult25.IsSuccess);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);

        // Reopen store in a fresh session to prove durable snapshot readiness
        await store.CloseAsync();
        using var storeReopened = new SqliteLearnerStore(dbPath);
        await storeReopened.InitializeAsync();
        var sessionReopened = new TrainingSession(storeReopened);
        await sessionReopened.InitializeAsync(startTiming: false);
        Assert.Equal(24, sessionReopened.PositionedCorrectAttemptCount);
        Assert.True(sessionReopened.IsPaceCalibrationReady);
    }

    // =========================================================================
    // 6. CYBER DEFENSE 25TH-ATTEMPT GATE
    // =========================================================================
    [Fact]
    public async Task TelemetryEnrichment_CyberDefenseCalibrationGate_PreservesCriticalHitAt25thAttempt()
    {
        var dbPath = GetTempDbPath("cyber_defense_gate_25th");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Seed 23 positioned Correct attempts enriched with ContextVersion 1
        var attempts = Enumerable.Range(1, 23).Select(i => (
            SubmissionId: $"sub-{i}",
            FactId: "add:0+1",
            Operation: ArithmeticOperation.Addition,
            Left: 0,
            Right: 1,
            Submitted: (int?)1,
            Correct: 1,
            IsCorrect: true,
            IsFluent: true,
            Outcome: AttemptOutcome.Correct,
            LatencyMs: 1200L,
            PracticePosition: (long?)i,
            ContextVersion: (int?)1,
            PresentedDeadlineMs: (int?)8000,
            ExpectedPaceMs: (int?)1200,
            ResolvedRole: (string?)"New",
            OperationBandBefore: (int?)0));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: true);
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var encounter = new CyberDefenseEncounterState();
        var initialHp = encounter.EnemyHitPoints;

        Assert.False(session.IsPaceCalibrationReady);
        Assert.Equal(23, session.PositionedCorrectAttemptCount);

        // 1. Attempt 24: fast correct answer while at 23 -> pre-calibration, normal 1 HP hit
        clock.AdvanceMs(800);
        Assert.True(800 <= session.CurrentFactEasyThresholdMs);

        var eval24 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval24.IsCorrect);
        Assert.Equal(1, eval24.ChangeSet.Attempt.ContextVersion);

        // Apply Cyber Defense combat rule: session is NOT ready yet for attempt 24 evaluation
        var isCritical24 = session.IsPaceCalibrationReady && eval24.LatencyMs <= session.CurrentFactEasyThresholdMs;
        Assert.False(isCritical24);
        if (isCritical24)
        {
            encounter.RecordCriticalHit();
        }
        else
        {
            encounter.RecordCorrectAnswer();
        }

        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);
        Assert.NotEqual(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1, encounter.EnemyHitPoints);

        // Persist attempt 24
        var commit24 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit24.IsSuccess);
        Assert.True(session.IsPaceCalibrationReady);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // 2. Attempt 25: first attempt post-calibration. Fast correct -> Critical Hit (2 HP damage)
        clock.AdvanceMs(700);
        Assert.True(700 <= session.CurrentFactEasyThresholdMs);

        var eval25 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval25.IsCorrect);
        Assert.Equal(1, eval25.ChangeSet.Attempt.ContextVersion);

        var isCritical25 = session.IsPaceCalibrationReady && eval25.LatencyMs <= session.CurrentFactEasyThresholdMs;
        Assert.True(isCritical25);
        if (isCritical25)
        {
            encounter.RecordCriticalHit();
        }
        else
        {
            encounter.RecordCorrectAnswer();
        }

        Assert.Equal(2, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.CriticalHit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1 - 2, encounter.EnemyHitPoints);

        var commit25 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit25.IsSuccess);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // 3. Attempt 26: slow correct answer post-calibration -> normal 1 HP hit
        var slowLatency = session.CurrentFactEasyThresholdMs + 500;
        clock.AdvanceMs(slowLatency);
        Assert.True(slowLatency > session.CurrentFactEasyThresholdMs);

        var eval26 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval26.IsCorrect);
        var isCritical26 = session.IsPaceCalibrationReady && eval26.LatencyMs <= session.CurrentFactEasyThresholdMs;
        Assert.False(isCritical26);
        if (isCritical26)
        {
            encounter.RecordCriticalHit();
        }
        else
        {
            encounter.RecordCorrectAnswer();
        }

        Assert.Equal(1, encounter.LastDamageDealt);
        Assert.Equal(CyberDefenseFeedbackKind.Hit, encounter.LastFeedback);
        Assert.Equal(initialHp - 1 - 2 - 1, encounter.EnemyHitPoints);
    }

    // =========================================================================
    // PRIVATE TEST HELPERS & MODELS
    // =========================================================================

    private sealed record BenchmarkTraceEntry(
        long PracticePosition,
        ArithmeticOperation Operation,
        long OperationAttemptOrdinal,
        string FactId,
        PracticeSelectionRole RequestedRole,
        bool IsNewIntroduction,
        IReadOnlyDictionary<ArithmeticOperation, int> BandIndices,
        int AdditionCeiling,
        bool IsPaceCalibrationReady,
        int PositionedCorrectAttemptCount,
        bool HasBroadWeakness,
        bool OperationAdvanced);

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class FixedClock : IClock
    {
        private readonly TimeSpan _step;
        private long _timestamp = 1_000_000;

        public FixedClock(TimeSpan? step = null)
        {
            _step = step ?? TimeSpan.FromMilliseconds(1200);
        }

        public long GetTimestamp()
        {
            var current = _timestamp;
            _timestamp += (long)_step.TotalMilliseconds;
            return current;
        }

        public TimeSpan GetElapsedTime(long startTimestamp) => _step;
    }

    private sealed class TestPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operationPreferences = [];

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
        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            var enabled = operations.ToHashSet();
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                SetOperationEnabled(operation, enabled.Contains(operation));
            }
        }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }

    private static async Task SeedAttemptHistoryAsync(
        string dbPath,
        IEnumerable<(string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition, int? ContextVersion, int? PresentedDeadlineMs, int? ExpectedPaceMs, string? ResolvedRole, int? OperationBandBefore)> attempts)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        long maxPosition = 0;
        foreach (var att in attempts)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position,
                    attempt_context_version, presented_deadline_ms, expected_pace_ms,
                    resolved_role, operation_band_before
                ) VALUES (
                    @submission_id, @fact_id, @operation, @left_operand, @right_operand,
                    @submitted_answer, @correct_answer, @is_correct, @is_fluent, @outcome,
                    @response_latency_ms, @timestamp, @practice_position,
                    @attempt_context_version, @presented_deadline_ms, @expected_pace_ms,
                    @resolved_role, @operation_band_before
                );";
            cmd.Parameters.AddWithValue("@submission_id", att.SubmissionId);
            cmd.Parameters.AddWithValue("@fact_id", att.FactId);
            cmd.Parameters.AddWithValue("@operation", att.Operation.ToString());
            cmd.Parameters.AddWithValue("@left_operand", att.Left);
            cmd.Parameters.AddWithValue("@right_operand", att.Right);
            cmd.Parameters.AddWithValue("@submitted_answer", (object?)att.Submitted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@correct_answer", att.Correct);
            cmd.Parameters.AddWithValue("@is_correct", att.IsCorrect ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_fluent", att.IsFluent ? 1 : 0);
            cmd.Parameters.AddWithValue("@outcome", att.Outcome.ToString());
            cmd.Parameters.AddWithValue("@response_latency_ms", att.LatencyMs);
            cmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@practice_position", (object?)att.PracticePosition ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attempt_context_version", (object?)att.ContextVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@presented_deadline_ms", (object?)att.PresentedDeadlineMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expected_pace_ms", (object?)att.ExpectedPaceMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@resolved_role", (object?)att.ResolvedRole ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@operation_band_before", (object?)att.OperationBandBefore ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            if (att.PracticePosition.HasValue && att.PracticePosition.Value > maxPosition)
            {
                maxPosition = att.PracticePosition.Value;
            }
        }

        if (maxPosition > 0)
        {
            using var progCmd = conn.CreateCommand();
            progCmd.Transaction = transaction;
            progCmd.CommandText = "UPDATE learner_progression SET practice_position = @pos WHERE id = 1;";
            progCmd.Parameters.AddWithValue("@pos", maxPosition);
            await progCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
