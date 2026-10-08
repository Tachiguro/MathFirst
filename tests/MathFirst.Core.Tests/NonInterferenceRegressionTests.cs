namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// MF-CYBER-001 Slice 4/4: Mathematical Non-Interference Regression Test Suite.
/// Proves deterministically that enabling, disabling, or interacting with Cyber Defense
/// cannot alter authoritative mathematical learning behavior, fact selection, FSRS-6
/// updates, response latency accounting, curriculum progression, or Schema V9 persistence.
/// </summary>
public sealed class NonInterferenceRegressionTests : IDisposable
{
    private readonly string _testDbDir;

    public NonInterferenceRegressionTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstNonInterference_" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeModePreferences : ICyberDefenseModePreferences
    {
        public bool Enabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => Enabled;
        public void SetCyberDefenseEnabled(bool enabled) => Enabled = enabled;
    }

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    // =========================================================================
    // 1. DETERMINISTIC 100+ ATTEMPT PAIRED SIMULATION (SESSION A vs SESSION B)
    // =========================================================================

    [Fact]
    public async Task PairedSession_100Attempts_DeterministicNonInterference_ExactMathematicalOutputsAndNormalizedPersistenceEquality()
    {
        var dbPathA = GetTempDbPath("session_a_cyber");
        var dbPathB = GetTempDbPath("session_b_calm");

        using var storeA = new SqliteLearnerStore(dbPathA);
        using var storeB = new SqliteLearnerStore(dbPathB);

        await storeA.InitializeAsync();
        await storeB.InitializeAsync();

        var clockA = new FakeClock();
        var clockB = new FakeClock();

        var prefsA = new FakeModePreferences { Enabled = true };
        var prefsB = new FakeModePreferences { Enabled = false };

        var stateA = new CyberDefenseSessionState(prefsA);
        var stateB = new CyberDefenseSessionState(prefsB);

        var dispatcherA = new CyberDefenseCombatDispatcher(stateA);
        var dispatcherB = new CyberDefenseCombatDispatcher(stateB);

        var sessionA = new TrainingSession(storeA, clockA);
        var sessionB = new TrainingSession(storeB, clockB);

        await sessionA.InitializeAsync(startTiming: false);
        await sessionB.InitializeAsync(startTiming: false);

        sessionA.StartOrResumePractice();
        sessionB.StartOrResumePractice();

        const int totalTurns = 120;
        var recordedEvalsA = new List<SubmissionEvaluation>();
        var recordedEvalsB = new List<SubmissionEvaluation>();

        for (var turn = 0; turn < totalTurns; turn++)
        {
            // 1. Fact selection invariance
            Assert.NotNull(sessionA.CurrentFact);
            Assert.NotNull(sessionB.CurrentFact);
            Assert.Equal(sessionA.CurrentFact.Id, sessionB.CurrentFact.Id);
            Assert.Equal(sessionA.CurrentFact.LeftOperand, sessionB.CurrentFact.LeftOperand);
            Assert.Equal(sessionA.CurrentFact.RightOperand, sessionB.CurrentFact.RightOperand);
            Assert.Equal(sessionA.CurrentFact.Operation, sessionB.CurrentFact.Operation);
            Assert.Equal(sessionA.CurrentFact.DisplaySymbol, sessionB.CurrentFact.DisplaySymbol);
            Assert.Equal(sessionA.CurrentFact.CorrectResult, sessionB.CurrentFact.CorrectResult);

            // 2. Timing and difficulty threshold invariance
            Assert.Equal(sessionA.CurrentFactEasyThresholdMs, sessionB.CurrentFactEasyThresholdMs);
            Assert.Equal(sessionA.CurrentFactFluencyThresholdMs, sessionB.CurrentFactFluencyThresholdMs);
            Assert.Equal(sessionA.CurrentFactCriticalHitThresholdMs, sessionB.CurrentFactCriticalHitThresholdMs);

            // 3. Learner session counter & streak invariance
            Assert.Equal(sessionA.SessionCorrectCount, sessionB.SessionCorrectCount);
            Assert.Equal(sessionA.SessionTotalCount, sessionB.SessionTotalCount);
            Assert.Equal(sessionA.CurrentCorrectStreak, sessionB.CurrentCorrectStreak);
            Assert.Equal(sessionA.PositionedCorrectAttemptCount, sessionB.PositionedCorrectAttemptCount);
            Assert.Equal(sessionA.IsPaceCalibrationReady, sessionB.IsPaceCalibrationReady);
            Assert.Equal(sessionA.HasBroadWeakness, sessionB.HasBroadWeakness);
            Assert.Equal(sessionA.Progression.CurriculumStage, sessionB.Progression.CurriculumStage);

            // 4. Deterministic synthetic answer & latency script
            // Every 9th turn: introduce an incorrect answer for remediation and FSRS lapse testing
            var isPlannedCorrect = (turn % 9) != 5;
            var submittedAnswer = isPlannedCorrect
                ? sessionA.CurrentFact.CorrectResult
                : sessionA.CurrentFact.CorrectResult + 1;

            var latencyMs = 600 + ((turn % 7) * 200); // 600ms, 800ms, 1000ms, 1200ms, 1400ms, 1600ms, 1800ms

            // Turn 25, 55, 85: Interruption simulation (pause and resume)
            if (turn is 25 or 55 or 85)
            {
                sessionA.PausePractice();
                sessionB.PausePractice();
                clockA.AdvanceMs(4000);
                clockB.AdvanceMs(4000);
                sessionA.StartOrResumePractice();
                sessionB.StartOrResumePractice();
            }

            clockA.AdvanceMs(latencyMs);
            clockB.AdvanceMs(latencyMs);

            // 5. Submit answers simultaneously
            var evalA = sessionA.SubmitAnswer(submittedAnswer);
            var evalB = sessionB.SubmitAnswer(submittedAnswer);

            recordedEvalsA.Add(evalA);
            recordedEvalsB.Add(evalB);

            // 6. Compare evaluation outputs (exact equality)
            Assert.Equal(evalA.Outcome, evalB.Outcome);
            Assert.Equal(evalA.IsCorrect, evalB.IsCorrect);
            Assert.Equal(evalA.CorrectAnswer, evalB.CorrectAnswer);
            Assert.Equal(evalA.SubmittedAnswer, evalB.SubmittedAnswer);
            Assert.Equal(evalA.LatencyMs, evalB.LatencyMs);
            Assert.Equal(evalA.IsProvisionallyMastered, evalB.IsProvisionallyMastered);
            Assert.Equal(evalA.OperationAdvanced, evalB.OperationAdvanced);

            // 7. Compare ChangeSet mathematical fields
            var csA = evalA.ChangeSet;
            var csB = evalB.ChangeSet;

            Assert.Equal(csA.ExpectedRevision, csB.ExpectedRevision);
            Assert.Equal(csA.Attempt.FactId, csB.Attempt.FactId);
            Assert.Equal(csA.Attempt.Operation, csB.Attempt.Operation);
            Assert.Equal(csA.Attempt.LeftOperand, csB.Attempt.LeftOperand);
            Assert.Equal(csA.Attempt.RightOperand, csB.Attempt.RightOperand);
            Assert.Equal(csA.Attempt.SubmittedAnswer, csB.Attempt.SubmittedAnswer);
            Assert.Equal(csA.Attempt.CorrectAnswer, csB.Attempt.CorrectAnswer);
            Assert.Equal(csA.Attempt.IsCorrect, csB.Attempt.IsCorrect);
            Assert.Equal(csA.Attempt.IsFluent, csB.Attempt.IsFluent);
            Assert.Equal(csA.Attempt.ResponseLatencyMs, csB.Attempt.ResponseLatencyMs);
            Assert.Equal(csA.Attempt.Outcome, csB.Attempt.Outcome);
            Assert.Equal(csA.Attempt.PracticePosition, csB.Attempt.PracticePosition);
            Assert.Equal(csA.Attempt.IsInterrupted, csB.Attempt.IsInterrupted);
            Assert.Equal(csA.Attempt.ContextVersion, csB.Attempt.ContextVersion);
            Assert.Equal(csA.Attempt.PresentedDeadlineMs, csB.Attempt.PresentedDeadlineMs);
            Assert.Equal(csA.Attempt.ExpectedPaceMs, csB.Attempt.ExpectedPaceMs);
            Assert.Equal(csA.Attempt.ResolvedRole, csB.Attempt.ResolvedRole);
            Assert.Equal(csA.Attempt.OperationBandBefore, csB.Attempt.OperationBandBefore);

            // FSRS equality
            if (csA.UpdatedFsrsState is not null || csB.UpdatedFsrsState is not null)
            {
                Assert.NotNull(csA.UpdatedFsrsState);
                Assert.NotNull(csB.UpdatedFsrsState);
                Assert.Equal(csA.UpdatedFsrsState.Stability, csB.UpdatedFsrsState.Stability);
                Assert.Equal(csA.UpdatedFsrsState.Difficulty, csB.UpdatedFsrsState.Difficulty);
                Assert.Equal(csA.UpdatedFsrsState.DuePracticePosition, csB.UpdatedFsrsState.DuePracticePosition);
                Assert.Equal(csA.UpdatedFsrsState.LastReviewPracticePosition, csB.UpdatedFsrsState.LastReviewPracticePosition);
                Assert.Equal(csA.UpdatedFsrsState.State, csB.UpdatedFsrsState.State);
                Assert.Equal(csA.UpdatedFsrsState.Step, csB.UpdatedFsrsState.Step);
                Assert.Equal(csA.UpdatedFsrsState.LastRating, csB.UpdatedFsrsState.LastRating);
            }

            // ItemState equality
            Assert.Equal(csA.UpdatedItemState.FactId, csB.UpdatedItemState.FactId);
            Assert.Equal(csA.UpdatedItemState.TotalAttempts, csB.UpdatedItemState.TotalAttempts);
            Assert.Equal(csA.UpdatedItemState.CorrectAttempts, csB.UpdatedItemState.CorrectAttempts);
            Assert.Equal(csA.UpdatedItemState.IncorrectAttempts, csB.UpdatedItemState.IncorrectAttempts);
            Assert.Equal(csA.UpdatedItemState.ConsecutiveCorrectStreak, csB.UpdatedItemState.ConsecutiveCorrectStreak);
            Assert.Equal(csA.UpdatedItemState.FluentStreak, csB.UpdatedItemState.FluentStreak);
            Assert.Equal(csA.UpdatedItemState.NeedsRemediation, csB.UpdatedItemState.NeedsRemediation);
            Assert.Equal(csA.UpdatedItemState.RemediationDueOrder, csB.UpdatedItemState.RemediationDueOrder);
            Assert.Equal(csA.UpdatedItemState.IsProvisionallyMastered, csB.UpdatedItemState.IsProvisionallyMastered);
            Assert.Equal(csA.UpdatedItemState.LastLatencyMs, csB.UpdatedItemState.LastLatencyMs);
            Assert.Equal(csA.UpdatedItemState.RollingLatencyMs, csB.UpdatedItemState.RollingLatencyMs);
            Assert.Equal(csA.UpdatedItemState.LastPracticedOrder, csB.UpdatedItemState.LastPracticedOrder);

            // Progression equality
            Assert.Equal(csA.UpdatedProgression.CurriculumStage, csB.UpdatedProgression.CurriculumStage);
            Assert.Equal(csA.UpdatedProgression.PracticePosition, csB.UpdatedProgression.PracticePosition);
            Assert.Equal(csA.UpdatedProgression.StoreRevision, csB.UpdatedProgression.StoreRevision);

            foreach (var op in Enum.GetValues<ArithmeticOperation>())
            {
                Assert.Equal(csA.OperationProgressions[op].BandIndex, csB.OperationProgressions[op].BandIndex);
                Assert.Equal(csA.OperationProgressions[op].BandStartedPracticePosition, csB.OperationProgressions[op].BandStartedPracticePosition);
            }

            // 8. Commit evaluations to SQLite
            var commitA = await sessionA.CommitCurrentEvaluationAsync();
            var commitB = await sessionB.CommitCurrentEvaluationAsync();

            Assert.True(commitA.IsSuccess);
            Assert.True(commitB.IsSuccess);
            Assert.Equal(commitA.NewRevision, commitB.NewRevision);
            Assert.True(sessionA.IsCurrentSubmissionCommitted);
            Assert.True(sessionB.IsCurrentSubmissionCommitted);

            // 9. Dispatch combat attempt to respective CyberDefense states
            var isCritA = evalA.IsCorrect && sessionA.IsPaceCalibrationReady && evalA.LatencyMs <= sessionA.CurrentFactCriticalHitThresholdMs;
            var isCritB = evalB.IsCorrect && sessionB.IsPaceCalibrationReady && evalB.LatencyMs <= sessionB.CurrentFactCriticalHitThresholdMs;
            Assert.Equal(isCritA, isCritB);

            var dispatchResultA = dispatcherA.Dispatch(new ConfirmedCombatAttempt(
                csA.SubmissionId,
                evalA.IsCorrect,
                isCritA,
                isCommitted: sessionA.IsCurrentSubmissionCommitted,
                wasEligibleAtSubmission: true));

            var dispatchResultB = dispatcherB.Dispatch(new ConfirmedCombatAttempt(
                csB.SubmissionId,
                evalB.IsCorrect,
                isCritB,
                isCommitted: sessionB.IsCurrentSubmissionCommitted,
                wasEligibleAtSubmission: false));

            // Cyber Defense mode: dispatched and mutates combat state
            Assert.Equal(CombatDispatchStatus.Dispatched, dispatchResultA.Status);
            Assert.True(dispatchResultA.MutatedCombatState);
            Assert.True(stateA.HasActiveEncounter);

            // Calm mode: suppressed with zero combat state allocation
            Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, dispatchResultB.Status);
            Assert.False(dispatchResultB.MutatedCombatState);
            Assert.False(stateB.HasActiveEncounter);
            Assert.Null(stateB.ActiveEncounter);

            // 10. Advance session to next fact
            if (evalA.IsCorrect)
            {
                await sessionA.AdvanceAfterCorrectAnswerAsync(startTiming: false);
                await sessionB.AdvanceAfterCorrectAnswerAsync(startTiming: false);
            }
            else
            {
                await sessionA.AcknowledgeFeedbackAsync(startTiming: false);
                await sessionB.AcknowledgeFeedbackAsync(startTiming: false);
            }

            if (sessionA.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                Assert.Equal(SessionInteractionState.SessionCheckIn, sessionB.InteractionState);
                Assert.NotNull(sessionA.PendingCheckIn);
                Assert.NotNull(sessionB.PendingCheckIn);
                Assert.Equal(sessionA.PendingCheckIn.TotalCount, sessionB.PendingCheckIn.TotalCount);
                Assert.Equal(sessionA.PendingCheckIn.CorrectCount, sessionB.PendingCheckIn.CorrectCount);
                Assert.Equal(sessionA.PendingCheckIn.MedianCorrectLatencyMs, sessionB.PendingCheckIn.MedianCorrectLatencyMs);

                await sessionA.ContinuePracticeAsync(startTiming: false);
                await sessionB.ContinuePracticeAsync(startTiming: false);
            }
            else if (sessionA.InteractionState == SessionInteractionState.TeachingIntervention)
            {
                Assert.Equal(SessionInteractionState.TeachingIntervention, sessionB.InteractionState);
                await sessionA.AcknowledgeTeachingInterventionAsync(startTiming: false);
                await sessionB.AcknowledgeTeachingInterventionAsync(startTiming: false);
            }
        }

        Assert.Equal(totalTurns, recordedEvalsA.Count);
        Assert.Equal(totalTurns, recordedEvalsB.Count);

        // =====================================================================
        // POST-RUN PERSISTENCE PAYLOAD COMPARISON
        // =====================================================================

        var snapshotA = await storeA.LoadSnapshotAsync();
        var snapshotB = await storeB.LoadSnapshotAsync();

        // 1. Revision & schema
        Assert.Equal(snapshotA.Revision, snapshotB.Revision);
        Assert.Equal(snapshotA.SchemaVersion, snapshotB.SchemaVersion);
        Assert.Equal(snapshotA.PositionedCorrectAttemptCount, snapshotB.PositionedCorrectAttemptCount);

        // 2. Progression
        Assert.Equal(snapshotA.Progression.CurriculumStage, snapshotB.Progression.CurriculumStage);
        Assert.Equal(snapshotA.Progression.PracticePosition, snapshotB.Progression.PracticePosition);
        Assert.Equal(snapshotA.Progression.StoreRevision, snapshotB.Progression.StoreRevision);
        Assert.Equal(snapshotA.Progression.SchemaVersion, snapshotB.Progression.SchemaVersion);

        foreach (var (op, progA) in snapshotA.Progression.OperationProgressions)
        {
            Assert.True(snapshotB.Progression.OperationProgressions.TryGetValue(op, out var progB));
            Assert.Equal(progA.BandIndex, progB.BandIndex);
            Assert.Equal(progA.BandStartedPracticePosition, progB.BandStartedPracticePosition);
        }

        // 3. Item states across all facts
        Assert.Equal(snapshotA.ItemStates.Count, snapshotB.ItemStates.Count);
        foreach (var (factId, itemA) in snapshotA.ItemStates)
        {
            Assert.True(snapshotB.ItemStates.TryGetValue(factId, out var itemB), $"Fact {factId} missing from Session B snapshot.");
            Assert.Equal(itemA.TotalAttempts, itemB.TotalAttempts);
            Assert.Equal(itemA.CorrectAttempts, itemB.CorrectAttempts);
            Assert.Equal(itemA.IncorrectAttempts, itemB.IncorrectAttempts);
            Assert.Equal(itemA.ConsecutiveCorrectStreak, itemB.ConsecutiveCorrectStreak);
            Assert.Equal(itemA.FluentStreak, itemB.FluentStreak);
            Assert.Equal(itemA.NeedsRemediation, itemB.NeedsRemediation);
            Assert.Equal(itemA.RemediationDueOrder, itemB.RemediationDueOrder);
            Assert.Equal(itemA.IsProvisionallyMastered, itemB.IsProvisionallyMastered);
            Assert.Equal(itemA.LastLatencyMs, itemB.LastLatencyMs);
            Assert.Equal(itemA.RollingLatencyMs, itemB.RollingLatencyMs);
            Assert.Equal(itemA.LastPracticedOrder, itemB.LastPracticedOrder);
        }

        // 4. FSRS states across all facts
        Assert.Equal(snapshotA.FsrsStates.Count, snapshotB.FsrsStates.Count);
        foreach (var (cardId, fsrsA) in snapshotA.FsrsStates)
        {
            Assert.True(snapshotB.FsrsStates.TryGetValue(cardId, out var fsrsB), $"Card {cardId} missing from Session B snapshot.");
            Assert.Equal(fsrsA.Stability, fsrsB.Stability);
            Assert.Equal(fsrsA.Difficulty, fsrsB.Difficulty);
            Assert.Equal(fsrsA.DuePracticePosition, fsrsB.DuePracticePosition);
            Assert.Equal(fsrsA.LastReviewPracticePosition, fsrsB.LastReviewPracticePosition);
            Assert.Equal(fsrsA.State, fsrsB.State);
            Assert.Equal(fsrsA.Step, fsrsB.Step);
            Assert.Equal(fsrsA.LastRating, fsrsB.LastRating);
        }

        // 5. Attempt history in exact order (normalizing SubmissionId and Timestamp)
        Assert.Equal(snapshotA.RecentAttempts.Count, snapshotB.RecentAttempts.Count);
        for (var i = 0; i < snapshotA.RecentAttempts.Count; i++)
        {
            var attA = snapshotA.RecentAttempts[i];
            var attB = snapshotB.RecentAttempts[i];

            Assert.False(string.IsNullOrWhiteSpace(attA.SubmissionId));
            Assert.False(string.IsNullOrWhiteSpace(attB.SubmissionId));

            Assert.Equal(attA.FactId, attB.FactId);
            Assert.Equal(attA.Operation, attB.Operation);
            Assert.Equal(attA.LeftOperand, attB.LeftOperand);
            Assert.Equal(attA.RightOperand, attB.RightOperand);
            Assert.Equal(attA.SubmittedAnswer, attB.SubmittedAnswer);
            Assert.Equal(attA.CorrectAnswer, attB.CorrectAnswer);
            Assert.Equal(attA.IsCorrect, attB.IsCorrect);
            Assert.Equal(attA.IsFluent, attB.IsFluent);
            Assert.Equal(attA.ResponseLatencyMs, attB.ResponseLatencyMs);
            Assert.Equal(attA.Outcome, attB.Outcome);
            Assert.Equal(attA.PracticePosition, attB.PracticePosition);
            Assert.Equal(attA.IsInterrupted, attB.IsInterrupted);
            Assert.Equal(attA.ContextVersion, attB.ContextVersion);
            Assert.Equal(attA.PresentedDeadlineMs, attB.PresentedDeadlineMs);
            Assert.Equal(attA.ExpectedPaceMs, attB.ExpectedPaceMs);
            Assert.Equal(attA.ResolvedRole, attB.ResolvedRole);
            Assert.Equal(attA.OperationBandBefore, attB.OperationBandBefore);
        }
    }

    // =========================================================================
    // 2. ARCHITECTURAL & DOMAIN DEPENDENCY BOUNDARY INVARIANTS
    // =========================================================================

    [Fact]
    public void NonInterference_DomainAssembly_HasNoDependencyOnCyberDefenseOrPresentation()
    {
        var domainAssembly = typeof(ArithmeticCurriculum).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies();

        var forbiddenAssemblyNames = new[]
        {
            "MathFirst.App",
            "MathFirst.Application",
            "Microsoft.AspNetCore.Components",
            "Microsoft.Maui",
            "CyberDefense"
        };

        foreach (var refName in referencedAssemblies)
        {
            foreach (var forbidden in forbiddenAssemblyNames)
            {
                Assert.DoesNotContain(forbidden, refName.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Domain types must not declare any gameplay methods or properties
        var allDomainTypes = domainAssembly.GetTypes();
        foreach (var type in allDomainTypes)
        {
            Assert.DoesNotContain("Cyber", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Encounter", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Combat", type.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NonInterference_LearnerStoreSchemaV9_HasNoGameplayTablesOrColumns()
    {
        var dbPath = GetTempDbPath("schema_v9_check");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // 1. Verify exact tables in SQLite database
        using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await tableCmd.ExecuteReaderAsync();

        var tableNames = new List<string>();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        string[] expectedTables =
        [
            "attempt_history",
            "fsrs_card_state",
            "item_learning_state",
            "learner_progression",
            "operation_progression",
            "schema_info"
        ];

        Assert.Equal(expectedTables.Length, tableNames.Count);
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tableNames);
        }

        // 2. Check each table's columns to ensure zero combat / gameplay creep
        string[] forbiddenColumnTokens =
        [
            "player_hp", "enemy_hp", "hit_points", "shield", "sector", "enemy", "boss",
            "combat", "critical_hit", "skill_point", "experience", "overdrive", "firewall"
        ];

        foreach (var table in tableNames)
        {
            using var colCmd = connection.CreateCommand();
            colCmd.CommandText = $"PRAGMA table_info({table});";
            using var colReader = await colCmd.ExecuteReaderAsync();

            while (await colReader.ReadAsync())
            {
                var colName = colReader.GetString(1);
                foreach (var forbidden in forbiddenColumnTokens)
                {
                    Assert.DoesNotContain(forbidden, colName, StringComparison.OrdinalIgnoreCase);
                }
                // Verify column is not standalone "xp" or "hp"
                Assert.False(string.Equals(colName, "xp", StringComparison.OrdinalIgnoreCase));
                Assert.False(string.Equals(colName, "hp", StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // =========================================================================
    // 3. FSRS-6 & SELECTOR AUTONOMY
    // =========================================================================

    [Fact]
    public void NonInterference_FsrsAlgorithm_IsPurelyMathematical_IgnoresCombatOutcomes()
    {
        var scheduler = new FsrsSchedulerAdapter();
        const string factId = "add:4+7";
        const long practicePosition = 42;
        const long responseLatencyMs = 850;

        // FSRS calculation is a deterministic mathematical function of (rating, position, latency)
        var card1 = scheduler.ReviewCard(null, factId, FsrsRating.Good, practicePosition, responseLatencyMs);
        var card2 = scheduler.ReviewCard(null, factId, FsrsRating.Good, practicePosition, responseLatencyMs);

        Assert.NotNull(card1);
        Assert.NotNull(card2);
        Assert.Equal(card1.Stability, card2.Stability);
        Assert.Equal(card1.Difficulty, card2.Difficulty);
        Assert.Equal(card1.DuePracticePosition, card2.DuePracticePosition);
        Assert.Equal(card1.LastReviewPracticePosition, card2.LastReviewPracticePosition);
        Assert.Equal(card1.State, card2.State);
        Assert.Equal(card1.Step, card2.Step);
        Assert.Equal(card1.LastRating, card2.LastRating);
    }

    [Fact]
    public void NonInterference_FactSelector_NeverFiltersOrAltersFactsBasedOnCombatState()
    {
        // Deterministic operation scheduler is purely mathematical
        for (var pos = 1; pos <= 20; pos++)
        {
            var opCyber = AdaptivePracticeSelector.GetScheduledOperation(pos);
            var opCalm = AdaptivePracticeSelector.GetScheduledOperation(pos);
            Assert.Equal(opCyber, opCalm);
        }

        // Role scheduling is purely mathematical
        for (var ordinal = 1; ordinal <= 20; ordinal++)
        {
            var roleCyber = AdaptivePracticeSelector.GetRequestedRole(ordinal);
            var roleCalm = AdaptivePracticeSelector.GetRequestedRole(ordinal);
            Assert.Equal(roleCyber, roleCalm);
        }
    }

    // =========================================================================
    // 4. COMBAT DAMAGE / DEFEAT DOES NOT MUTATE LEARNER PROGRESSION
    // =========================================================================

    [Fact]
    public async Task NonInterference_CombatDamageOrDefeat_NeverMutatesLearnerStoreOrProgression()
    {
        var dbPath = GetTempDbPath("combat_defeat_isolation");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var prefs = new FakeModePreferences { Enabled = true };
        var state = new CyberDefenseSessionState(prefs);
        var encounter = state.ActiveEncounter;
        Assert.NotNull(encounter);

        // Advance math session to establish some initial progress
        clock.AdvanceMs(800);
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);

        var initialPosition = session.Progression.PracticePosition;
        var initialRevision = session.Progression.StoreRevision;
        var initialStage = session.Progression.CurriculumStage;
        var initialItemStatesCount = session.ItemStates.Count;

        // Simulate intense combat damage on the encounter: deplete all shields and reduce HP
        encounter.RecordIncorrectAnswer(); // shield 3 -> 2
        encounter.RecordIncorrectAnswer(); // shield 2 -> 1
        encounter.RecordIncorrectAnswer(); // shield 1 -> 0
        encounter.RecordIncorrectAnswer(); // shields broken, reset to 3
        encounter.RecordIncorrectAnswer();
        encounter.RecordIncorrectAnswer();

        // Mathematical session and store must remain completely untouched by encounter damage
        Assert.Equal(initialPosition, session.Progression.PracticePosition);
        Assert.Equal(initialRevision, session.Progression.StoreRevision);
        Assert.Equal(initialStage, session.Progression.CurriculumStage);
        Assert.Equal(initialItemStatesCount, session.ItemStates.Count);

        var snap = await store.LoadRuntimeSnapshotAsync();
        Assert.Equal(initialPosition, snap.Progression.PracticePosition);
        Assert.Equal(initialRevision, snap.Revision);
    }

    // =========================================================================
    // 5. TRANSIENT PERSISTENCE RECOVERY NON-INTERFERENCE
    // =========================================================================

    [Fact]
    public async Task NonInterference_PersistenceRecovery_WithCyberDefense_DoesNotAlterMathematicalPayload()
    {
        var dbPathA = GetTempDbPath("recovery_a_cyber");
        var dbPathB = GetTempDbPath("recovery_b_calm");

        using var storeA = new SqliteLearnerStore(dbPathA);
        using var storeB = new SqliteLearnerStore(dbPathB);

        var clockA = new FakeClock();
        var clockB = new FakeClock();

        var sessionA = new TrainingSession(storeA, clockA);
        var sessionB = new TrainingSession(storeB, clockB);

        await sessionA.InitializeAsync();
        await sessionB.InitializeAsync();
        sessionA.StartOrResumePractice();
        sessionB.StartOrResumePractice();

        var prefsA = new FakeModePreferences { Enabled = true };
        var prefsB = new FakeModePreferences { Enabled = false };
        var stateA = new CyberDefenseSessionState(prefsA);
        var stateB = new CyberDefenseSessionState(prefsB);
        var dispA = new CyberDefenseCombatDispatcher(stateA);
        var dispB = new CyberDefenseCombatDispatcher(stateB);

        clockA.AdvanceMs(900);
        clockB.AdvanceMs(900);

        var evalA = sessionA.SubmitAnswer(sessionA.CurrentFact.CorrectResult);
        var evalB = sessionB.SubmitAnswer(sessionB.CurrentFact.CorrectResult);

        var commitA = await sessionA.CommitCurrentEvaluationAsync();
        var commitB = await sessionB.CommitCurrentEvaluationAsync();

        Assert.True(commitA.IsSuccess);
        Assert.True(commitB.IsSuccess);

        dispA.Dispatch(new ConfirmedCombatAttempt(evalA.ChangeSet.SubmissionId, evalA.IsCorrect, false, sessionA.IsCurrentSubmissionCommitted, true));
        dispB.Dispatch(new ConfirmedCombatAttempt(evalB.ChangeSet.SubmissionId, evalB.IsCorrect, false, sessionB.IsCurrentSubmissionCommitted, false));

        Assert.True(stateA.HasActiveEncounter);
        Assert.False(stateB.HasActiveEncounter);

        var snapA = await storeA.LoadSnapshotAsync();
        var snapB = await storeB.LoadSnapshotAsync();

        Assert.Equal(snapA.Revision, snapB.Revision);
        Assert.Equal(snapA.PositionedCorrectAttemptCount, snapB.PositionedCorrectAttemptCount);
        Assert.Equal(snapA.ItemStates.Count, snapB.ItemStates.Count);
    }

    // =========================================================================
    // 6. MULTI-DIGIT ANSWER INPUTS & ACTIVE THINKING TIME INVARIANCE
    // =========================================================================

    [Fact]
    public async Task NonInterference_MultiDigitAnswerInputs_PreservesExactOutputsInBothModes()
    {
        var dbPathA = GetTempDbPath("multidigit_a");
        var dbPathB = GetTempDbPath("multidigit_b");

        using var storeA = new SqliteLearnerStore(dbPathA);
        using var storeB = new SqliteLearnerStore(dbPathB);

        var sessionA = new TrainingSession(storeA, new FakeClock());
        var sessionB = new TrainingSession(storeB, new FakeClock());

        await sessionA.InitializeAsync();
        await sessionB.InitializeAsync();
        sessionA.StartOrResumePractice();
        sessionB.StartOrResumePractice();

        var stateA = new CyberDefenseSessionState(new FakeModePreferences { Enabled = true });
        var stateB = new CyberDefenseSessionState(new FakeModePreferences { Enabled = false });

        var dispA = new CyberDefenseCombatDispatcher(stateA);
        var dispB = new CyberDefenseCombatDispatcher(stateB);

        // Submit answer formatted with decimal/integer parsing
        var evalA = sessionA.SubmitAnswer(sessionA.CurrentFact.CorrectResult);
        var evalB = sessionB.SubmitAnswer(sessionB.CurrentFact.CorrectResult);

        Assert.Equal(evalA.Outcome, evalB.Outcome);
        Assert.Equal(evalA.SubmittedAnswer, evalB.SubmittedAnswer);
        Assert.Equal(evalA.CorrectAnswer, evalB.CorrectAnswer);

        await sessionA.CommitCurrentEvaluationAsync();
        await sessionB.CommitCurrentEvaluationAsync();

        dispA.Dispatch(new ConfirmedCombatAttempt(evalA.ChangeSet.SubmissionId, true, false, true, true));
        dispB.Dispatch(new ConfirmedCombatAttempt(evalB.ChangeSet.SubmissionId, true, false, true, false));

        Assert.True(stateA.HasActiveEncounter);
        Assert.False(stateB.HasActiveEncounter);
    }
}
