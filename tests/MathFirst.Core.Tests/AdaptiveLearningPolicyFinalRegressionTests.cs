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
using Xunit.Abstractions;

/// <summary>
/// MF-LEARN-006 Slice 7: Acceptance and Final Regression Suite.
/// Locks the strong-learner exact 482-attempt benchmark, bitwise determinism across runs,
/// absolute no-immediate-repeat invariant, Option-B adaptive discovery, independent G3 decoupling,
/// pace calibration readiness at n=24, real-SQLite restart equivalence, and Schema V6 invariants.
/// </summary>
public sealed class AdaptiveLearningPolicyFinalRegressionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDirectory;

    public AdaptiveLearningPolicyFinalRegressionTests(ITestOutputHelper output)
    {
        _output = output;
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstSlice7Final_" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup in temporary directory
        }
    }

    private string GetDatabasePath(string name = "test") =>
        Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");

    // ===========================================================================
    // TEST 1: PRIMARY EXACT-482 BENCHMARK & STRUCTURAL DERIVATION
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_GuidedFourOperations_ReachesAddP1AnchorAtExactly482AcceptedAttempts()
    {
        // 1. Structural Premises Derivation
        var curriculum = new ArithmeticCurriculum();
        var addOwnership = new AcquisitionOwnershipResolver(curriculum.Addition);
        var addDenseFacts = Enumerable.Range(0, 10).Sum(b => addOwnership.GetOwnedFrontier(b).Count);

        // Addition Dense foundation through ADD-D10 (Bands 0..9) owns exactly 121 unique required facts:
        // (11 x 11 ordered pairs for left, right in [0..10])
        Assert.Equal(121, addDenseFacts);

        // Under 4-operation bounded permutation scheduling with equal turn allocation:
        // 120 complete 4-operation permutation bags account for exactly 480 global turns
        const int completeBags = 120;
        const int operationsCount = 4;
        Assert.Equal(480, completeBags * operationsCount);

        // Position 482 is in bag 120 (0-indexed: 481 / 4 = 120), slot 1
        var scheduledOpAt482 = DeterministicOperationScheduler.GetScheduledOperation(482, PracticeOperationPreferencePolicy.AllOperations);
        Assert.Equal(ArithmeticOperation.Addition, scheduledOpAt482);

        var additionTurnsIn482 = Enumerable.Range(1, 482)
            .Count(p => DeterministicOperationScheduler.GetScheduledOperation(p, PracticeOperationPreferencePolicy.AllOperations) == ArithmeticOperation.Addition);
        Assert.Equal(121, additionTurnsIn482);

        // 2. Canonical Strong Learner Runtime Execution
        var dbPath = GetDatabasePath("benchmark482");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var result = await RunCanonicalStrongLearnerAsync(store);

        // 3. Exact Normative Assertions (Zero Tolerance)
        Assert.Equal(482, result.AdditionP1AnchorPosition);
        Assert.Equal(121, result.AdditionAttemptsAtTransition);
        Assert.Equal(20, result.AdditionCeilingBeforeTransition);
        Assert.Equal(180, result.AdditionCeilingAfterTransition);
        Assert.Equal(10, result.FinalProgression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);

        _output.WriteLine("=== STRONG LEARNER 482 BENCHMARK VERIFIED ===");
        _output.WriteLine($"ADD-D10 -> ADD-P1-ANCHOR transition position: {result.AdditionP1AnchorPosition}");
        _output.WriteLine($"Addition accepted attempts at transition:    {result.AdditionAttemptsAtTransition}");
        _output.WriteLine($"Addition ceiling before transition:          {result.AdditionCeilingBeforeTransition}");
        _output.WriteLine($"Addition ceiling after transition:           {result.AdditionCeilingAfterTransition}");
    }

    // ===========================================================================
    // TEST 2: DETERMINISM ACROSS INDEPENDENT RUNS
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_Benchmark_IsDeterministicAcrossIndependentRuns()
    {
        var dbPath1 = GetDatabasePath("determinism_run1");
        using var store1 = new SqliteLearnerStore(dbPath1);
        await store1.InitializeAsync();
        var run1 = await RunCanonicalStrongLearnerAsync(store1);

        var dbPath2 = GetDatabasePath("determinism_run2");
        using var store2 = new SqliteLearnerStore(dbPath2);
        await store2.InitializeAsync();
        var run2 = await RunCanonicalStrongLearnerAsync(store2);

        // Assert exact trace equality across all 482 attempts
        Assert.Equal(run1.Trace.Count, run2.Trace.Count);
        Assert.Equal(482, run1.Trace.Count);

        for (var i = 0; i < run1.Trace.Count; i++)
        {
            var t1 = run1.Trace[i];
            var t2 = run2.Trace[i];

            Assert.Equal(t1.PracticePosition, t2.PracticePosition);
            Assert.Equal(t1.Operation, t2.Operation);
            Assert.Equal(t1.OperationAttemptOrdinal, t2.OperationAttemptOrdinal);
            Assert.Equal(t1.FactId, t2.FactId);
            Assert.Equal(t1.RequestedRole, t2.RequestedRole);
            Assert.Equal(t1.IsNewIntroduction, t2.IsNewIntroduction);
            Assert.Equal(t1.AdditionCeiling, t2.AdditionCeiling);

            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                Assert.Equal(t1.BandIndices[op], t2.BandIndices[op]);
            }
        }

        Assert.Equal(482, run1.AdditionP1AnchorPosition);
        Assert.Equal(482, run2.AdditionP1AnchorPosition);
        Assert.Equal(121, run1.AdditionAttemptsAtTransition);
        Assert.Equal(121, run2.AdditionAttemptsAtTransition);
    }

    // ===========================================================================
    // TEST 3: ABSOLUTE NO-IMMEDIATE-REPEAT INVARIANT
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_Benchmark_NeverImmediatelyRepeatsFact()
    {
        var dbPath = GetDatabasePath("no_immediate_repeat");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var result = await RunCanonicalStrongLearnerAsync(store);

        Assert.Equal(482, result.Trace.Count);

        for (var i = 1; i < result.Trace.Count; i++)
        {
            var prevFactId = result.Trace[i - 1].FactId;
            var currFactId = result.Trace[i].FactId;

            Assert.True(
                prevFactId != currFactId,
                $"Immediate repetition detected at position {result.Trace[i].PracticePosition}: fact {currFactId} immediately repeated after position {result.Trace[i - 1].PracticePosition}.");
        }
    }

    // ===========================================================================
    // TEST 4: OPTION-B DISCOVERY — 121 ADDITION FACTS IN 121 TURNS
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_Benchmark_Acquires121AdditionFactsIn121AdditionTurns()
    {
        var dbPath = GetDatabasePath("option_b_discovery");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var result = await RunCanonicalStrongLearnerAsync(store);

        var additionEntries = result.Trace.Where(t => t.Operation == ArithmeticOperation.Addition).ToList();
        Assert.Equal(121, additionEntries.Count);

        // Every Addition turn introduced a new, unique fact until Band 10 completed
        var distinctAdditionFacts = additionEntries.Select(t => t.FactId).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(121, distinctAdditionFacts.Count);

        // All 121 facts are marked as new introductions
        Assert.All(additionEntries, entry =>
        {
            Assert.True(entry.IsNewIntroduction, $"Addition turn at position {entry.PracticePosition} ({entry.FactId}) was expected to be a New introduction.");
        });

        // The acquired set matches the exact 121 unique facts of Addition Bands 0..9
        var curriculum = new ArithmeticCurriculum();
        var addOwnership = new AcquisitionOwnershipResolver(curriculum.Addition);
        var expectedAdditionFacts = Enumerable.Range(0, 10)
            .SelectMany(b => addOwnership.GetOwnedFrontier(b).Select(f => f.Id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expectedAdditionFacts, distinctAdditionFacts.ToHashSet(StringComparer.Ordinal));

        // Broad Weakness remained false throughout the clean run
        Assert.All(result.Trace, t =>
        {
            Assert.False(t.HasBroadWeakness, $"Broad Weakness must remain false for clean learner at position {t.PracticePosition}.");
        });
    }

    // ===========================================================================
    // TEST 5: PACE CALIBRATION TRANSITIONS AT EXACTLY 24
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_Benchmark_PaceCalibrationTransitionsAt24()
    {
        var dbPath = GetDatabasePath("pace_calibration_transition");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var result = await RunCanonicalStrongLearnerAsync(store);

        // Before position 24: count < 24 and readiness is false
        for (var pos = 1; pos < 24; pos++)
        {
            var entry = result.Trace[pos - 1];
            Assert.Equal(pos, entry.PositionedCorrectAttemptCount);
            Assert.False(entry.IsPaceCalibrationReady, $"Pace calibration must NOT be ready at position {pos}.");
        }

        // At position 24 and beyond: count is saturated at 24 and readiness is true
        for (var pos = 24; pos <= result.Trace.Count; pos++)
        {
            var entry = result.Trace[pos - 1];
            Assert.Equal(24, entry.PositionedCorrectAttemptCount);
            Assert.True(entry.IsPaceCalibrationReady, $"Pace calibration must be ready at position {pos}.");
        }
    }

    // ===========================================================================
    // TEST 6: G3 SOFT DECOUPLING REMAINS INDEPENDENT
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_Benchmark_G3DecouplingRemainsIndependent()
    {
        var dbPath = GetDatabasePath("g3_decoupling");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var result = await RunCanonicalStrongLearnerAsync(store);

        // Find milestone position where Division reaches BandIndex 3 (Stage 4)
        var divStage4Entry = result.Trace.FirstOrDefault(t => t.BandIndices[ArithmeticOperation.Division] == 3);
        Assert.NotNull(divStage4Entry);
        Assert.Equal(102, divStage4Entry.PracticePosition);

        // Find milestone position where Multiplication reaches BandIndex 3 (Stage 4)
        var mulStage4Entry = result.Trace.FirstOrDefault(t => t.BandIndices[ArithmeticOperation.Multiplication] == 3);
        Assert.NotNull(mulStage4Entry);
        Assert.Equal(103, mulStage4Entry.PracticePosition);

        // Verify independent decoupling:
        // At position 102: Division is decoupled, Multiplication is still coupled
        var gateAt102 = GuidedNumberSpaceGate.ForGuided(
            new ArithmeticCurriculum().Addition,
            divStage4Entry.BandIndices[ArithmeticOperation.Addition],
            divStage4Entry.BandIndices[ArithmeticOperation.Multiplication],
            divStage4Entry.BandIndices[ArithmeticOperation.Division]);
        Assert.True(gateAt102.IsDivisionDecoupled);
        Assert.False(gateAt102.IsMultiplicationDecoupled);

        // At position 103: Both are decoupled
        var gateAt103 = GuidedNumberSpaceGate.ForGuided(
            new ArithmeticCurriculum().Addition,
            mulStage4Entry.BandIndices[ArithmeticOperation.Addition],
            mulStage4Entry.BandIndices[ArithmeticOperation.Multiplication],
            mulStage4Entry.BandIndices[ArithmeticOperation.Division]);
        Assert.True(gateAt103.IsDivisionDecoupled);
        Assert.True(gateAt103.IsMultiplicationDecoupled);

        // Decoupling did not jump to an artificial magic ceiling; operations continue canonical progression
        var finalProg = result.FinalProgression.OperationProgressions;
        Assert.True(finalProg[ArithmeticOperation.Multiplication].BandIndex >= 5);
        Assert.True(finalProg[ArithmeticOperation.Division].BandIndex >= 5);

        // Addition proceeded to 482 without being held up by any legacy Guided plateau
        Assert.Equal(482, result.AdditionP1AnchorPosition);
    }

    // ===========================================================================
    // TEST 7: RESTART EQUIVALENCE ACROSS MULTIPLE CUT POINTS (REAL SQLITE)
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_RestartedRun_ReachesSame482Benchmark()
    {
        var dbPath = GetDatabasePath("restart_equivalence");

        // Cut points:
        // 1. Position 15: before calibration readiness (< 24)
        // 2. Position 24: immediately after calibration readiness
        // 3. Position 120: after both Mul and Div G3 decoupling (Bands >= 3)
        // 4. Position 400: late in Addition dense acquisition (Band 9, ceiling 20)
        var cutPoints = new[] { 15, 24, 120, 400 };

        var restartedResult = await RunRestartedStrongLearnerAsync(dbPath, cutPoints);

        // Assert exact benchmark match despite 4 persistent SQLite restarts
        Assert.Equal(482, restartedResult.AdditionP1AnchorPosition);
        Assert.Equal(121, restartedResult.AdditionAttemptsAtTransition);
        Assert.Equal(20, restartedResult.AdditionCeilingBeforeTransition);
        Assert.Equal(180, restartedResult.AdditionCeilingAfterTransition);
        Assert.Equal(10, restartedResult.FinalProgression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
    }

    // ===========================================================================
    // TEST 8: MULTIPLE RESTARTS PRESERVE DETERMINISTIC PROGRESSION & CONTINUITY
    // ===========================================================================
    [Fact]
    public async Task StrongLearner_MultipleRestarts_PreserveDeterministicProgression()
    {
        // 1. Run uninterrupted baseline
        var baselineDb = GetDatabasePath("baseline_compare");
        using (var baselineStore = new SqliteLearnerStore(baselineDb))
        {
            await baselineStore.InitializeAsync();
            var baseline = await RunCanonicalStrongLearnerAsync(baselineStore);

            // 2. Run restarted learner with 4 cut points
            var restartedDb = GetDatabasePath("restarted_compare");
            var cutPoints = new[] { 15, 24, 120, 400 };
            var restarted = await RunRestartedStrongLearnerAsync(restartedDb, cutPoints);

            // 3. Bitwise trace equality check across all 482 attempts
            Assert.Equal(baseline.Trace.Count, restarted.Trace.Count);
            Assert.Equal(482, baseline.Trace.Count);

            for (var i = 0; i < baseline.Trace.Count; i++)
            {
                var b = baseline.Trace[i];
                var r = restarted.Trace[i];

                Assert.Equal(b.PracticePosition, r.PracticePosition);
                Assert.Equal(b.Operation, r.Operation);
                Assert.Equal(b.FactId, r.FactId);
                Assert.Equal(b.RequestedRole, r.RequestedRole);
                Assert.Equal(b.IsNewIntroduction, r.IsNewIntroduction);
                Assert.Equal(b.IsPaceCalibrationReady, r.IsPaceCalibrationReady);
                Assert.Equal(b.PositionedCorrectAttemptCount, r.PositionedCorrectAttemptCount);
            }

            // 4. Position continuity check for restarted run
            for (var i = 0; i < restarted.Trace.Count; i++)
            {
                var expectedPosition = i + 1;
                Assert.Equal(expectedPosition, restarted.Trace[i].PracticePosition);
            }
        }
    }

    // ===========================================================================
    // TEST 9: COLD-RESTART NEXT-SELECTION DETERMINISM
    // ===========================================================================
    [Fact]
    public async Task SameDurableState_ProducesSameNextSelectionAfterColdRestart()
    {
        var dbPath1 = GetDatabasePath("next_selection_source");
        var cutPoint = 50;

        // Run store to position 50
        using (var setupStore = new SqliteLearnerStore(dbPath1))
        {
            await setupStore.InitializeAsync();
            var clock = new FixedClock(TimeSpan.FromMilliseconds(800));
            var preferences = new TestPreferenceStore();
            preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

            var session = new TrainingSession(setupStore, clock, preferenceStore: preferences, practiceMode: PracticeMode.Custom);
            await session.InitializeAsync(startTiming: false);

            for (var pos = 1; pos <= cutPoint; pos++)
            {
                var fact = session.CurrentFact;
                session.SubmitAnswer(fact.CorrectResult);
                var commitRes = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitRes.IsSuccess);

                if (pos < cutPoint)
                {
                    if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                    {
                        session.AdvanceAfterCorrectAnswer(startTiming: false);
                    }
                    if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                    {
                        session.ContinuePractice(startTiming: false);
                    }
                }
            }

            await setupStore.CloseAsync();
        }

        // Copy DB so two independent stores open identical durable state
        var dbPath2 = GetDatabasePath("next_selection_copy");
        File.Copy(dbPath1, dbPath2, overwrite: true);

        // Reopen Session A on DB 1
        ArithmeticFact factA;
        ArithmeticOperation scheduledOpA;
        PracticeSelectionRole requestedRoleA;
        using (var storeA = new SqliteLearnerStore(dbPath1))
        {
            var preferencesA = new TestPreferenceStore();
            preferencesA.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
            var sessionA = new TrainingSession(storeA, new FixedClock(TimeSpan.FromMilliseconds(800)), preferenceStore: preferencesA, practiceMode: PracticeMode.Custom);
            await sessionA.InitializeAsync(startTiming: false);

            factA = sessionA.CurrentFact;
            scheduledOpA = factA.Operation;
            var opOrdinalA = sessionA.GetOperationAcceptedAttemptCount(scheduledOpA) + 1;
            requestedRoleA = AdaptivePracticeSelector.GetRequestedRole(opOrdinalA);
            await storeA.CloseAsync();
        }

        // Reopen Session B on DB 2
        ArithmeticFact factB;
        ArithmeticOperation scheduledOpB;
        PracticeSelectionRole requestedRoleB;
        using (var storeB = new SqliteLearnerStore(dbPath2))
        {
            var preferencesB = new TestPreferenceStore();
            preferencesB.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
            var sessionB = new TrainingSession(storeB, new FixedClock(TimeSpan.FromMilliseconds(800)), preferenceStore: preferencesB, practiceMode: PracticeMode.Custom);
            await sessionB.InitializeAsync(startTiming: false);

            factB = sessionB.CurrentFact;
            scheduledOpB = factB.Operation;
            var opOrdinalB = sessionB.GetOperationAcceptedAttemptCount(scheduledOpB) + 1;
            requestedRoleB = AdaptivePracticeSelector.GetRequestedRole(opOrdinalB);
            await storeB.CloseAsync();
        }

        // Verify exact next-selection identity
        Assert.Equal(scheduledOpA, scheduledOpB);
        Assert.Equal(factA.Id, factB.Id);
        Assert.Equal(factA.LeftOperand, factB.LeftOperand);
        Assert.Equal(factA.RightOperand, factB.RightOperand);
        Assert.Equal(requestedRoleA, requestedRoleB);
    }

    // ===========================================================================
    // TEST 10: SCHEMA V6 PERSISTENCE CONTRACT (ZERO SCHEMA/MIGRATION MUTATION)
    // ===========================================================================
    [Fact]
    public async Task MfLearn006_FinalPersistenceContract_RemainsSchemaV6()
    {
        // 1. Authoritative Default Schema Version is strictly 9
        Assert.Equal(9, LearnerProgression.DefaultSchemaVersion);

        // 2. Fresh SQLite DB has schema_version = '9'
        var dbPath = GetDatabasePath("schema_v6_conformance");
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
        }

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using (var versionCmd = conn.CreateCommand())
        {
            versionCmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            var version = await versionCmd.ExecuteScalarAsync();
            Assert.Equal("9", version);
        }

        // 3. Exactly the 6 expected Schema V6 tables exist
        var expectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "schema_info",
            "learner_progression",
            "operation_progression",
            "item_learning_state",
            "attempt_history",
            "fsrs_card_state"
        };

        using (var tableCmd = conn.CreateCommand())
        {
            tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = await tableCmd.ExecuteReaderAsync();
            var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                actualTables.Add(reader.GetString(0));
            }

            Assert.Equal(expectedTables, actualTables);
        }

        // 4. Assert NO new persistent flags or columns were added across all tables
        // learner_progression
        var learnerColumns = await GetTableColumnsAsync(conn, "learner_progression");
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "practice_position", "curriculum_stage", "updated_at" }, learnerColumns);
        Assert.DoesNotContain("has_broad_weakness", learnerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("is_pace_calibration_ready", learnerColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("g3_decoupled", learnerColumns, StringComparer.OrdinalIgnoreCase);

        // operation_progression
        var opColumns = await GetTableColumnsAsync(conn, "operation_progression");
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "operation", "band_index", "band_started_practice_position" }, opColumns);
        Assert.DoesNotContain("is_decoupled", opColumns, StringComparer.OrdinalIgnoreCase);

        // attempt_history
        var attemptColumns = await GetTableColumnsAsync(conn, "attempt_history");
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "submission_id", "fact_id", "operation", "left_operand", "right_operand",
            "submitted_answer", "correct_answer", "is_correct", "is_fluent", "outcome",
            "response_latency_ms", "timestamp", "practice_position",
            "attempt_context_version", "presented_deadline_ms", "expected_pace_ms",
            "resolved_role", "operation_band_before", "is_interrupted"
        }, attemptColumns);
        Assert.DoesNotContain("is_critical_hit", attemptColumns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("combat_hp", attemptColumns, StringComparer.OrdinalIgnoreCase);

        // item_learning_state
        var itemColumns = await GetTableColumnsAsync(conn, "item_learning_state");
        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fact_id", "operation", "left_operand", "right_operand", "total_attempts",
            "correct_attempts", "incorrect_attempts", "consecutive_correct", "last_latency_ms",
            "rolling_latency_ms", "fluent_streak", "is_mastered", "needs_remediation",
            "remediation_due_order", "last_practiced_order", "last_practiced_at"
        }, itemColumns);
        Assert.DoesNotContain("option_b_role", itemColumns, StringComparer.OrdinalIgnoreCase);
    }

    // ===========================================================================
    // TEST 11: RUNTIME-DERIVED POLICY STATE (ZERO PERSISTENT FLAGS)
    // ===========================================================================
    [Fact]
    public void MfLearn006_FinalPolicyState_RequiresNoNewPersistentFlags()
    {
        // 1. G3 Soft Decoupling is derived at runtime strictly from BandIndex >= 3
        var curriculum = new ArithmeticCurriculum();
        var coupledProgressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 2, 0),
            [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 2, 0),
        };
        var coupledGate = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, coupledProgressions);
        Assert.False(coupledGate.IsMultiplicationDecoupled);
        Assert.False(coupledGate.IsDivisionDecoupled);

        var decoupledProgressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Subtraction] = new(ArithmeticOperation.Subtraction, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 3, 0),
            [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 3, 0),
        };
        var decoupledGate = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, decoupledProgressions);
        Assert.True(decoupledGate.IsMultiplicationDecoupled);
        Assert.True(decoupledGate.IsDivisionDecoupled);

        // 2. Pace Calibration Readiness is derived strictly from durable positioned Correct count >= 24
        var snapshot23 = new LearnerSnapshot(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            [],
            1,
            LearnerProgression.DefaultSchemaVersion,
            positionedCorrectAttemptCount: 23);
        Assert.Equal(23, snapshot23.PositionedCorrectAttemptCount);
        Assert.False(snapshot23.PositionedCorrectAttemptCount >= AdaptivePacePolicy.PaceCalibrationCorrectAttemptThreshold);

        var snapshot24 = new LearnerSnapshot(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            [],
            1,
            LearnerProgression.DefaultSchemaVersion,
            positionedCorrectAttemptCount: 24);
        Assert.Equal(24, snapshot24.PositionedCorrectAttemptCount);
        Assert.True(snapshot24.PositionedCorrectAttemptCount >= AdaptivePacePolicy.PaceCalibrationCorrectAttemptThreshold);
    }

    // ===========================================================================
    // TEST 12: SLICE-5 RESTART CALIBRATION BOUNDARY (23 vs 24)
    // ===========================================================================
    [Fact]
    public async Task PaceCalibration_Restart_At23And24PositionedCorrect_MaintainsReadinessBoundary()
    {
        var dbPath = GetDatabasePath("calibration_restart_boundary");

        // Seed 23 positioned correct attempts
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            await SeedPositionedCorrectAttemptsAsync(dbPath, 23);
        }

        // Reopen: learner with 23 is NOT ready
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store, new FixedClock(TimeSpan.FromMilliseconds(800)));
            await session.InitializeAsync(startTiming: false);
            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);

            // Submit and commit #24
            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True(eval.IsCorrect);
            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);
            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);

            await store.CloseAsync();
        }

        // Reopen after 24: learner IS ready immediately
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store, new FixedClock(TimeSpan.FromMilliseconds(800)));
            await session.InitializeAsync(startTiming: false);
            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);
        }
    }

    // ===========================================================================
    // SIMULATION HARNESS & HELPERS
    // ===========================================================================

    public sealed record BenchmarkTraceEntry(
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

    public sealed class BenchmarkSimulationResult
    {
        public required IReadOnlyList<BenchmarkTraceEntry> Trace { get; init; }
        public required long AdditionP1AnchorPosition { get; init; }
        public required long AdditionAttemptsAtTransition { get; init; }
        public required int AdditionCeilingBeforeTransition { get; init; }
        public required int AdditionCeilingAfterTransition { get; init; }
        public required LearnerProgression FinalProgression { get; init; }
    }

    private static async Task<BenchmarkSimulationResult> RunCanonicalStrongLearnerAsync(
        ILearnerStore store,
        int maxAttempts = 500)
    {
        var clock = new FixedClock(TimeSpan.FromMilliseconds(800));
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var session = new TrainingSession(store, clock, preferenceStore: preferences, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync(startTiming: false);

        var trace = new List<BenchmarkTraceEntry>(maxAttempts);
        var curriculum = new ArithmeticCurriculum();
        long additionP1AnchorPos = -1;
        long additionAttemptsAtTransition = -1;
        int ceilingBefore = -1;
        int ceilingAfter = -1;

        for (var pos = 1; pos <= maxAttempts; pos++)
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
            if (!commitResult.IsSuccess)
            {
                throw new InvalidOperationException($"Commit failed at position {pos}: {commitResult.Message}");
            }

            var eval = session.LastEvaluation!;
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

        return new BenchmarkSimulationResult
        {
            Trace = trace,
            AdditionP1AnchorPosition = additionP1AnchorPos,
            AdditionAttemptsAtTransition = additionAttemptsAtTransition,
            AdditionCeilingBeforeTransition = ceilingBefore,
            AdditionCeilingAfterTransition = ceilingAfter,
            FinalProgression = session.Progression
        };
    }

    private static async Task<BenchmarkSimulationResult> RunRestartedStrongLearnerAsync(
        string dbPath,
        IReadOnlyList<int> cutPoints,
        int maxAttempts = 500)
    {
        var clock = new FixedClock(TimeSpan.FromMilliseconds(800));
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var cutPointSet = cutPoints.ToHashSet();
        var trace = new List<BenchmarkTraceEntry>(maxAttempts);
        var curriculum = new ArithmeticCurriculum();
        long additionP1AnchorPos = -1;
        long additionAttemptsAtTransition = -1;
        int ceilingBefore = -1;
        int ceilingAfter = -1;

        var currentStore = new SqliteLearnerStore(dbPath);
        await currentStore.InitializeAsync();
        var currentSession = new TrainingSession(currentStore, clock, preferenceStore: preferences, practiceMode: PracticeMode.Custom);
        await currentSession.InitializeAsync(startTiming: false);

        try
        {
            for (var pos = 1; pos <= maxAttempts; pos++)
            {
                var fact = currentSession.CurrentFact;
                var op = fact.Operation;
                var opOrdinal = currentSession.GetOperationAcceptedAttemptCount(op) + 1;
                var requestedRole = AdaptivePracticeSelector.GetRequestedRole(opOrdinal);
                var isNew = !currentSession.ItemStates.ContainsKey(fact.Id) || currentSession.ItemStates[fact.Id].TotalAttempts == 0;
                var bandIndicesBefore = currentSession.Progression.OperationProgressions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BandIndex);

                var gateBefore = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, currentSession.Progression.OperationProgressions);
                var addCeiling = gateBefore.AdditionCeiling ?? 0;

                currentSession.SubmitAnswer(fact.CorrectResult);
                var commitResult = await currentSession.CommitCurrentEvaluationAsync();
                if (!commitResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Commit failed at position {pos}: {commitResult.Message}");
                }

                var eval = currentSession.LastEvaluation!;
                var bandIndicesAfter = currentSession.Progression.OperationProgressions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BandIndex);

                trace.Add(new BenchmarkTraceEntry(
                    PracticePosition: pos,
                    Operation: op,
                    OperationAttemptOrdinal: opOrdinal,
                    FactId: fact.Id,
                    RequestedRole: requestedRole,
                    IsNewIntroduction: isNew,
                    BandIndices: bandIndicesAfter,
                    AdditionCeiling: addCeiling,
                    IsPaceCalibrationReady: currentSession.IsPaceCalibrationReady,
                    PositionedCorrectAttemptCount: currentSession.PositionedCorrectAttemptCount,
                    HasBroadWeakness: currentSession.HasBroadWeakness,
                    OperationAdvanced: eval.OperationAdvanced));

                if (op == ArithmeticOperation.Addition
                    && bandIndicesBefore[ArithmeticOperation.Addition] == 9
                    && bandIndicesAfter[ArithmeticOperation.Addition] == 10)
                {
                    additionP1AnchorPos = pos;
                    additionAttemptsAtTransition = currentSession.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition);
                    ceilingBefore = addCeiling;
                    var gateAfter = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, currentSession.Progression.OperationProgressions);
                    ceilingAfter = gateAfter.AdditionCeiling ?? 0;
                }

                if (cutPointSet.Contains(pos) && additionP1AnchorPos < 0)
                {
                    await currentStore.CloseAsync();
                    currentStore.Dispose();

                    currentStore = new SqliteLearnerStore(dbPath);
                    currentSession = new TrainingSession(currentStore, clock, preferenceStore: preferences, practiceMode: PracticeMode.Custom);
                    await currentSession.InitializeAsync(startTiming: false);

                    Assert.Equal(pos, currentSession.Progression.PracticePosition);
                }
                else
                {
                    if (currentSession.InteractionState == SessionInteractionState.CorrectFeedback)
                    {
                        currentSession.AdvanceAfterCorrectAnswer(startTiming: false);
                    }
                    if (currentSession.InteractionState == SessionInteractionState.SessionCheckIn)
                    {
                        currentSession.ContinuePractice(startTiming: false);
                    }
                }

                if (additionP1AnchorPos > 0)
                {
                    break;
                }
            }

            return new BenchmarkSimulationResult
            {
                Trace = trace,
                AdditionP1AnchorPosition = additionP1AnchorPos,
                AdditionAttemptsAtTransition = additionAttemptsAtTransition,
                AdditionCeilingBeforeTransition = ceilingBefore,
                AdditionCeilingAfterTransition = ceilingAfter,
                FinalProgression = currentSession.Progression
            };
        }
        finally
        {
            await currentStore.CloseAsync();
            currentStore.Dispose();
        }
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static async Task SeedPositionedCorrectAttemptsAsync(string dbPath, int count)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        for (var i = 1; i <= count; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    @submission_id, @fact_id, @operation, @left_operand, @right_operand,
                    @submitted_answer, @correct_answer, @is_correct, @is_fluent, @outcome,
                    @response_latency_ms, @timestamp, @practice_position
                );";
            cmd.Parameters.AddWithValue("@submission_id", $"seed-{i}");
            cmd.Parameters.AddWithValue("@fact_id", "add:0+1");
            cmd.Parameters.AddWithValue("@operation", ArithmeticOperation.Addition.ToString());
            cmd.Parameters.AddWithValue("@left_operand", 0);
            cmd.Parameters.AddWithValue("@right_operand", 1);
            cmd.Parameters.AddWithValue("@submitted_answer", 1);
            cmd.Parameters.AddWithValue("@correct_answer", 1);
            cmd.Parameters.AddWithValue("@is_correct", 1);
            cmd.Parameters.AddWithValue("@is_fluent", 1);
            cmd.Parameters.AddWithValue("@outcome", AttemptOutcome.Correct.ToString());
            cmd.Parameters.AddWithValue("@response_latency_ms", 800);
            cmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@practice_position", i);
            await cmd.ExecuteNonQueryAsync();
        }

        using var progCmd = conn.CreateCommand();
        progCmd.Transaction = transaction;
        progCmd.CommandText = "UPDATE learner_progression SET practice_position = @pos WHERE id = 1;";
        progCmd.Parameters.AddWithValue("@pos", count);
        await progCmd.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    private sealed class FixedClock : IClock
    {
        private readonly TimeSpan _elapsed;
        public FixedClock(TimeSpan elapsed) => _elapsed = elapsed;
        public long GetTimestamp() => 1_000_000L;
        public TimeSpan GetElapsedTime(long startTimestamp) => _elapsed;
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
}
