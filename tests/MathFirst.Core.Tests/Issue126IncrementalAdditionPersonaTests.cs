namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Issue #126: deterministic synthetic learner personas driving a real <see cref="TrainingSession"/> against isolated
/// temporary SQLite stores and a fake clock. They prove that Addition grows from <c>0..10</c> to <c>0..11</c>,
/// <c>0..12</c>, <c>0..13</c>, ... with full coverage of each new frontier, correctness-only advancement, spaced
/// remediation, deterministic restarts and the owner-approved in-place reinterpretation of saved post-D10 bands.
/// No real learner data is read; every database lives in a per-test temporary directory.
/// </summary>
public sealed class Issue126IncrementalAdditionPersonaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue126_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public Issue126IncrementalAdditionPersonaTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        // No process-global SqliteConnection.ClearAllPools(): it would close other suites' pooled handles (TESTING.md
        // §42). Every SqliteLearnerStore here is disposed (clearing its own pool) and seed connections are non-pooled.
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly IReadOnlyList<ArithmeticOperation> AdditionOnly = [ArithmeticOperation.Addition];
    private static readonly Func<ArithmeticFact, int, bool> AlwaysCorrect = (_, _) => true;

    [Fact]
    public async Task FastAccurateLearner_GrowsTheOperandSetOneIntegerAtATimeThroughN14()
    {
        var result = await RunAsync(new Persona("FastAccurate", 400, 800, AlwaysCorrect), stopAtAdditionBand: 13);
        _output.WriteLine(result.Describe(100));

        AssertIncrementalInvariants(result);
        Assert.Equal(13, result.FinalAdditionBand);
        Assert.Equal(PinnedFastAccurateAdvancements, result.AdvancementsText);

        // 10 -> 11 introduces exactly the 23 max-operand-11 pairs before advancing, and likewise 25 and 27 facts.
        AssertBandCoverage(result, 10, 23);
        AssertBandCoverage(result, 11, 25);
        AssertBandCoverage(result, 12, 27);
        Assert.Contains(result.Trace, turn => turn.FactId == "add:11+11");
        Assert.DoesNotContain(result.Trace, turn => turn.MaxOperand >= 15);
    }

    [Fact]
    public async Task SlowConsistentlyCorrectLearner_ProgressesExactlyLikeTheFastLearner()
    {
        var fast = await RunAsync(new Persona("Fast", 400, 800, AlwaysCorrect), stopAtAdditionBand: 13);
        var slow = await RunAsync(new Persona("Slow", 400, 12_000, AlwaysCorrect), stopAtAdditionBand: 13);
        _output.WriteLine(slow.Describe(30));

        AssertIncrementalInvariants(slow);
        Assert.Equal(13, slow.FinalAdditionBand);
        Assert.Equal(fast.AdvancementsText, slow.AdvancementsText);
        Assert.Equal(fast.Trace.Select(turn => turn.FactId), slow.Trace.Select(turn => turn.FactId));
        Assert.All(slow.Trace, turn => Assert.True(turn.IsCorrect));
    }

    [Fact]
    public async Task WeakFacts_AreRemediatedWithSpacingAndStayEligibleWhileTheFrontierGrowsPastEleven()
    {
        // 4 + 1 is missed on its first three encounters; the new N = 11 fact 11 + 5 on its first four.
        var persona = new Persona(
            "WeakFacts",
            600,
            1500,
            (fact, encounter) => fact.Id switch
            {
                "add:4+1" => encounter > 3,
                "add:11+5" => encounter > 4,
                _ => true
            });
        var result = await RunAsync(persona, stopAtAdditionBand: 13);
        _output.WriteLine(result.Describe(220));

        AssertIncrementalInvariants(result);
        Assert.Equal(13, result.FinalAdditionBand);

        var weak4Plus1 = result.Trace.Where(turn => turn.FactId == "add:4+1").ToList();
        Assert.Equal(new[] { false, false, false, true }, weak4Plus1.Take(4).Select(turn => turn.IsCorrect));
        Assert.Contains(weak4Plus1, turn => turn.Role == nameof(PracticeSelectionRole.Remediation));
        AssertSpaced(weak4Plus1);

        var weak11Plus5 = result.Trace.Where(turn => turn.FactId == "add:11+5").ToList();
        Assert.Equal(new[] { false, false, false, false, true }, weak11Plus5.Take(5).Select(turn => turn.IsCorrect));
        Assert.Contains(weak11Plus5, turn => turn.Role == nameof(PracticeSelectionRole.Remediation));
        AssertSpaced(weak11Plus5);
        _output.WriteLine($"add:11+5 turns: {string.Join(",", weak11Plus5.Select(turn => $"{turn.Attempt}{(turn.IsCorrect ? "+" : "x")}@D{turn.AdditionBandBefore + 1}"))}");

        // Both recovered facts remain eligible for FSRS review at the final N = 14 frontier.
        var ownership = new AcquisitionOwnershipResolver(new ArithmeticCurriculum().Addition);
        Assert.True(ownership.IsEligible("add:4+1", result.FinalAdditionBand));
        Assert.True(ownership.IsEligible("add:11+5", result.FinalAdditionBand));
        Assert.Equal(PinnedWeakFactsAdvancements, result.AdvancementsText);
    }

    private static void AssertSpaced(IReadOnlyList<PersonaTurn> turns)
    {
        for (var index = 1; index < turns.Count; index++)
        {
            Assert.True(turns[index].Attempt - turns[index - 1].Attempt > 1, "Remediation must never be an immediate repeat.");
        }
    }

    [Fact]
    public async Task ColdRestart_ContinuesDeterministicallyFromDurableState()
    {
        var continuous = await RunAsync(new Persona("Continuous", 220, 900, AlwaysCorrect));

        // Two independent databases reach the same durable state; each segment is a fresh session over a reopened
        // store (a cold restart). No database file is copied, so no uncheckpointed WAL content can be lost.
        var path = Path.Combine(_directory, "restart.db");
        var twinPath = Path.Combine(_directory, "restart-twin.db");
        var first = await RunAsync(new Persona("Segment1", 150, 900, AlwaysCorrect), databasePath: path);
        var firstTwin = await RunAsync(new Persona("Segment1Twin", 150, 900, AlwaysCorrect), databasePath: twinPath);
        Assert.Equal(first.Trace.Select(turn => turn.FactId), firstTwin.Trace.Select(turn => turn.FactId));

        var resumed = await RunAsync(new Persona("Segment2", 70, 900, AlwaysCorrect), databasePath: path);
        var resumedTwin = await RunAsync(new Persona("Segment2Twin", 70, 900, AlwaysCorrect), databasePath: twinPath);

        Assert.Equal(resumed.Trace.Select(turn => turn.FactId), resumedTwin.Trace.Select(turn => turn.FactId));
        Assert.Equal(resumed.FinalAdditionBand, resumedTwin.FinalAdditionBand);
        Assert.True(first.FinalAdditionBand >= 10, "The first segment must already have crossed D10.");
        Assert.True(resumed.FinalAdditionBand > first.FinalAdditionBand, "Progression must continue after a cold restart.");
        AssertIncrementalInvariants(resumed);

        // Restart changes no band boundaries: the combined segments reach the same Addition band as one session.
        Assert.Equal(continuous.FinalAdditionBand, resumed.FinalAdditionBand);
        _output.WriteLine($"continuous={continuous.AdvancementsText} segment1={first.AdvancementsText} segment2={resumed.AdvancementsText}");
    }

    [Fact]
    public async Task LegacyPostD10Band_IsReinterpretedInPlaceWithoutRewritingStoredLegacyRows()
    {
        var path = Path.Combine(_directory, "legacy.db");
        await SeedLegacyLearnerAsync(path, additionBandIndex: 10, CurriculumStage.Stage2_Subtraction);

        var result = await RunAsync(
            new Persona("LegacyBand10", 120, 1000, AlwaysCorrect),
            databasePath: path,
            mode: PracticeMode.CurriculumManaged);
        _output.WriteLine(result.Describe(40));

        Assert.Equal(CurriculumStage.Stage2_Subtraction, result.InitialStage);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, result.FinalStage);
        var additionTurns = result.Trace.Where(turn => turn.Operation == ArithmeticOperation.Addition).ToList();
        Assert.NotEmpty(additionTurns);
        Assert.All(additionTurns, turn => Assert.True(turn.MaxOperand <= turn.AdditionBandBefore + 1));
        Assert.DoesNotContain(result.Trace, turn => LegacyAnchorFactIds.Contains(turn.FactId));
        Assert.Contains(additionTurns, turn => turn.MaxOperand == 11 && turn.Role == nameof(PracticeSelectionRole.New));
        Assert.True(result.FinalAdditionBand >= 11, "The reinterpreted N = 11 frontier must be completable.");

        // Physical legacy rows remain stored exactly as seeded; nothing was deleted or rewritten.
        using var reopened = new SqliteLearnerStore(path);
        var legacyStates = await reopened.LoadItemStatesAsync(LegacyAnchorFactIds);
        Assert.Equal(LegacyAnchorFactIds.Count, legacyStates.Count);
        Assert.All(legacyStates.Values, state => Assert.Equal(3, state.TotalAttempts));
        Assert.True(legacyStates["add:20+80"].NeedsRemediation);
        var snapshot = await reopened.LoadSnapshotAsync();
        Assert.All(LegacyAnchorFactIds, factId => Assert.True(snapshot.FsrsStates.ContainsKey(factId)));
    }

    [Theory]
    [InlineData(CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage3_Multiplication)]
    [InlineData(CurriculumStage.Stage4_Division)]
    public async Task EarnedStages_AndOtherOperationBands_ArePreservedOnLegacyPostD10Learners(CurriculumStage earnedStage)
    {
        var path = Path.Combine(_directory, $"stage_{earnedStage}.db");
        await SeedLegacyLearnerAsync(path, additionBandIndex: 12, earnedStage);

        var persona = new Persona("Stage_" + earnedStage, 80, 2000, (fact, encounter) => encounter % 4 != 0);
        var result = await RunAsync(persona, databasePath: path, mode: PracticeMode.CurriculumManaged);

        Assert.Equal(earnedStage, result.InitialStage);
        Assert.Equal(earnedStage, result.FinalStage);
        var unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(earnedStage);
        Assert.All(result.Trace, turn => Assert.Contains(turn.Operation, unlocked));
        foreach (var operation in unlocked)
        {
            Assert.Contains(result.Trace, turn => turn.Operation == operation);
        }

        // The saved BandIndex 12 resumes at N = 13 and grows only one operand at a time from there.
        AssertIncrementalInvariants(result);
        Assert.Contains(result.Trace, turn => turn.Operation == ArithmeticOperation.Addition && turn.MaxOperand == 13);
        Assert.DoesNotContain(result.Trace, turn => turn.Operation == ArithmeticOperation.Addition && turn.MaxOperand > 14);
        Assert.DoesNotContain(result.Trace, turn => LegacyAnchorFactIds.Contains(turn.FactId));
    }

    [Fact]
    public async Task HighFrontierN60_LoadsEveryMaterializedFactAndNeverReintroducesOne()
    {
        // BandIndex 59 (N = 60) owns 121 facts: more than the 64-row review window and the old 25-fact Dense cap.
        var path = Path.Combine(_directory, "high.db");
        await SeedLegacyLearnerAsync(path, additionBandIndex: 59, CurriculumStage.Stage1_Addition, seedLegacyAnchors: false);

        var result = await RunAsync(new Persona("HighN60", 400, 900, AlwaysCorrect), databasePath: path, stopAtAdditionBand: 60);
        _output.WriteLine(result.Describe(10));

        Assert.Equal(60, result.FinalAdditionBand);
        AssertIncrementalInvariants(result);
        AssertBandCoverage(result, 59, 121);
        Assert.All(
            result.Trace.Where(turn => turn.Role == nameof(PracticeSelectionRole.New)),
            turn => Assert.Equal(0, turn.PriorAttempts));

        using var reopened = new SqliteLearnerStore(path);
        var frontierIds = IncrementalAdditionCurriculum.CreateFrontier(59).Select(fact => fact.Id).ToArray();
        var states = await reopened.LoadItemStatesAsync(frontierIds);
        Assert.Equal(121, states.Count);
        Assert.Equal(
            result.Trace.Count(turn => frontierIds.Contains(turn.FactId)),
            states.Values.Sum(state => state.TotalAttempts));
    }

    [Fact]
    public async Task SqliteEvidence_ForAFrontierLargerThanOneParameterBatch_IsCompleteAndOrdinallyOrdered()
    {
        // BandIndex 249 (N = 250) owns 501 facts: three statements of at most 200 bound fact IDs each.
        const int bandIndex = 249;
        const long bandStart = 5_000;
        var frontier = IncrementalAdditionCurriculum.CreateFrontier(bandIndex);
        Assert.Equal(501, frontier.Count);
        Assert.True(frontier.Count > 2 * SqliteLearnerStore.FactIdParameterBatchSize);

        var path = Path.Combine(_directory, "batched.db");
        using (var seed = new SqliteLearnerStore(path))
        {
            await seed.InitializeAsync();
        }

        var expectedLatest = new Dictionary<string, (long Position, bool IsCorrect)>(StringComparer.Ordinal);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();
            var position = 1L;
            for (var index = 0; index < frontier.Count; index++)
            {
                var fact = frontier[index];
                var attempts = new List<(long Position, bool IsCorrect)> { (position++, true) }; // before the band start
                if (index % 7 != 0)
                {
                    attempts.Add((bandStart + position++, false));
                    attempts.Add((bandStart + position++, index % 3 != 0));
                    expectedLatest[fact.Id] = attempts[^1];
                }

                foreach (var (attemptPosition, isCorrect) in attempts)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"
                        INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                        VALUES (@id, @fact, 'Addition', @left, @right, @answer, @correct, @is_correct, 0, @outcome, 9000, '2026-10-31T00:00:00.0000000+00:00', @position);";
                    insert.Parameters.AddWithValue("@id", $"batched-{attemptPosition}");
                    insert.Parameters.AddWithValue("@fact", fact.Id);
                    insert.Parameters.AddWithValue("@left", fact.LeftOperand);
                    insert.Parameters.AddWithValue("@right", fact.RightOperand);
                    insert.Parameters.AddWithValue("@answer", isCorrect ? fact.CorrectResult : fact.CorrectResult + 1);
                    insert.Parameters.AddWithValue("@correct", fact.CorrectResult);
                    insert.Parameters.AddWithValue("@is_correct", isCorrect ? 1 : 0);
                    insert.Parameters.AddWithValue("@outcome", isCorrect ? "Correct" : "Incorrect");
                    insert.Parameters.AddWithValue("@position", attemptPosition);
                    await insert.ExecuteNonQueryAsync();
                }

                using var item = connection.CreateCommand();
                item.Transaction = transaction;
                item.CommandText = @"
                    INSERT INTO item_learning_state (
                        fact_id, operation, left_operand, right_operand,
                        total_attempts, correct_attempts, incorrect_attempts,
                        consecutive_correct, last_latency_ms, rolling_latency_ms,
                        fluent_streak, is_mastered, needs_remediation,
                        remediation_due_order, last_practiced_order, last_practiced_at
                    ) VALUES (@fact, 'Addition', @left, @right, 1, 1, 0, 1, 9000, 9000, 0, 0, 0, 0, 0, NULL);";
                item.Parameters.AddWithValue("@fact", fact.Id);
                item.Parameters.AddWithValue("@left", fact.LeftOperand);
                item.Parameters.AddWithValue("@right", fact.RightOperand);
                await item.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        using var store = new SqliteLearnerStore(path);
        var latest = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStart,
            frontier.Select(fact => fact.Id).ToArray());

        Assert.Equal(expectedLatest.Keys.Order(StringComparer.Ordinal), latest.Select(attempt => attempt.FactId));
        Assert.All(latest, attempt =>
        {
            Assert.Equal(expectedLatest[attempt.FactId].Position, attempt.PracticePosition);
            Assert.Equal(expectedLatest[attempt.FactId].IsCorrect, attempt.IsCorrect);
        });

        // Every materialized owned-frontier fact is visible to the selector, not only the first 64 rows.
        var evidence = await store.LoadPracticeSelectionEvidenceAsync(new PracticeSelectionEvidenceRequest(
            ArithmeticOperation.Addition,
            prospectivePracticePosition: 10_000,
            currentSessionOrder: 0,
            currentBandOwnedFrontier: frontier,
            introductionFrontier: frontier,
            currentBandIndex: bandIndex,
            guidedNumberSpaceGate: GuidedNumberSpaceGate.Unrestricted));
        Assert.Equal(frontier.Select(fact => fact.Id).Order(StringComparer.Ordinal), evidence.CurrentBandCandidates.Select(candidate => candidate.Fact.Id));
    }

    [Fact]
    public async Task PersonaTrace_FirstHundredFactsAreDeterministicAndPinned()
    {
        var first = await RunAsync(new Persona("TraceA", 100, 900, (fact, encounter) => encounter > 1 || fact.Id != "add:4+1"));
        var second = await RunAsync(new Persona("TraceB", 100, 900, (fact, encounter) => encounter > 1 || fact.Id != "add:4+1"));

        Assert.Equal(first.TraceText(100), second.TraceText(100));
        _output.WriteLine(first.TraceText(100));
        Assert.Equal(100, first.Trace.Count);
        Assert.All(first.Trace, turn => Assert.True(turn.MaxOperand <= turn.AdditionBandBefore + 1));
    }

    // Measured on the Issue #126 policy at its introduction. A change here is a learning-policy change.
    private const string PinnedFastAccurateAdvancements = "1@4,2@9,3@16,4@25,5@36,6@49,7@64,8@81,9@100,10@121,11@144,12@169,13@196";
    private const string PinnedWeakFactsAdvancements = "1@4,2@9,3@16,4@32,5@43,6@56,7@71,8@88,9@107,10@128,11@155,12@180,13@207";

    private static readonly IReadOnlyList<string> LegacyAnchorFactIds = ["add:20+80", "add:60+60", "add:10+20"];

    private static void AssertIncrementalInvariants(PersonaResult result)
    {
        foreach (var turn in result.Trace.Where(turn => turn.Operation == ArithmeticOperation.Addition))
        {
            // No presented Addition fact ever exceeds the operand frontier of the learner's current band.
            Assert.True(
                turn.MaxOperand <= turn.AdditionBandBefore + 1,
                $"{turn.FactId} at attempt {turn.Attempt} exceeds N = {turn.AdditionBandBefore + 1}.");
            Assert.True(turn.AdditionBandAfter - turn.AdditionBandBefore is 0 or 1);
        }

        for (var index = 1; index < result.Trace.Count; index++)
        {
            Assert.NotEqual(result.Trace[index - 1].FactId, result.Trace[index].FactId);
        }
    }

    private static void AssertBandCoverage(PersonaResult result, int bandIndex, int expectedFrontierSize)
    {
        var frontier = IncrementalAdditionCurriculum.CreateFrontier(bandIndex).Select(fact => fact.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expectedFrontierSize, frontier.Count);
        var inBand = result.Trace.Where(turn => turn.AdditionBandBefore == bandIndex).ToList();
        Assert.NotEmpty(inBand);
        var advancing = inBand.Last();
        Assert.Equal(bandIndex + 1, advancing.AdditionBandAfter);
        Assert.True(frontier.IsSubsetOf(inBand.Select(turn => turn.FactId)), $"Band {bandIndex} advanced before every new fact was attempted.");
        Assert.All(
            inBand.Where(turn => turn.MaxOperand == bandIndex + 1 && turn.PriorAttempts == 0),
            turn => Assert.Contains(turn.FactId, frontier));
    }

    private async Task SeedLegacyLearnerAsync(
        string path,
        int additionBandIndex,
        CurriculumStage stage,
        bool seedLegacyAnchors = true)
    {
        const long practicePosition = 2_000;
        using (var seed = new SqliteLearnerStore(path))
        {
            await seed.InitializeAsync();
            var progression = LearnerProgression.CreateFresh();
            progression.PracticePosition = practicePosition;
            progression.CurriculumStage = stage;
            var operations = progression.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value);
            operations[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, additionBandIndex, practicePosition);
            progression.OperationProgressions = operations;
            await seed.SaveProgressionAsync(progression);
        }

        // Synthetic D01..D10 learner rows (all mastered, reviewed long ago) plus legacy anchor rows from the rejected
        // post-D10 tens family. These rows are inserted directly, as an older build would have left them.
        var rows = new List<(string FactId, int Left, int Right, bool NeedsRemediation)>();
        for (var left = 0; left <= 10; left++)
        {
            for (var right = 0; right <= 10; right++)
            {
                rows.Add(($"add:{left}+{right}", left, right, false));
            }
        }

        if (seedLegacyAnchors)
        {
            rows.Add(("add:20+80", 20, 80, true));
            rows.Add(("add:60+60", 60, 60, false));
            rows.Add(("add:10+20", 10, 20, false));
        }

        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        foreach (var (factId, left, right, needsRemediation) in rows)
        {
            var isLegacy = LegacyAnchorFactIds.Contains(factId);
            using (var item = connection.CreateCommand())
            {
                item.Transaction = transaction;
                item.CommandText = @"
                    INSERT INTO item_learning_state (
                        fact_id, operation, left_operand, right_operand,
                        total_attempts, correct_attempts, incorrect_attempts,
                        consecutive_correct, last_latency_ms, rolling_latency_ms,
                        fluent_streak, is_mastered, needs_remediation,
                        remediation_due_order, last_practiced_order, last_practiced_at
                    ) VALUES (
                        @fact_id, 'Addition', @left, @right,
                        @total, @correct, @incorrect,
                        @streak, 1000, 1000,
                        1, @mastered, @remediation,
                        0, 0, NULL
                    );";
                item.Parameters.AddWithValue("@fact_id", factId);
                item.Parameters.AddWithValue("@left", left);
                item.Parameters.AddWithValue("@right", right);
                item.Parameters.AddWithValue("@total", isLegacy ? 3 : 2);
                item.Parameters.AddWithValue("@correct", needsRemediation ? 1 : isLegacy ? 3 : 2);
                item.Parameters.AddWithValue("@incorrect", needsRemediation ? 2 : 0);
                item.Parameters.AddWithValue("@streak", needsRemediation ? 0 : 2);
                item.Parameters.AddWithValue("@mastered", needsRemediation ? 0 : 1);
                item.Parameters.AddWithValue("@remediation", needsRemediation ? 1 : 0);
                await item.ExecuteNonQueryAsync();
            }

            using var card = connection.CreateCommand();
            card.Transaction = transaction;
            card.CommandText = @"
                INSERT INTO fsrs_card_state (
                    fact_id, card_id, state, step, stability, difficulty,
                    due_practice_position, last_review_practice_position, last_rating
                ) VALUES (
                    @fact_id, @card_id, 2, NULL, 40.0, 4.0,
                    @due, @last_review, 3
                );";
            card.Parameters.AddWithValue("@fact_id", factId);
            card.Parameters.AddWithValue("@card_id", DeterministicGuid(factId).ToString());
            card.Parameters.AddWithValue("@due", isLegacy ? 1_000 : 4_000 + left * 11 + right);
            card.Parameters.AddWithValue("@last_review", 1_000);
            await card.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    private static Guid DeterministicGuid(string factId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(factId));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private async Task<PersonaResult> RunAsync(
        Persona persona,
        string? databasePath = null,
        PracticeMode mode = PracticeMode.Custom,
        int? stopAtAdditionBand = null)
    {
        var path = databasePath ?? Path.Combine(_directory, $"{persona.Name}_{Guid.NewGuid():N}.db");
        var clock = new PersonaClock { CurrentTurnLatency = TimeSpan.FromMilliseconds(persona.LatencyMs) };

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var preferences = mode == PracticeMode.Custom ? new FixedOperationsPreferenceStore(AdditionOnly) : null;
        var session = new TrainingSession(store, clock, preferenceStore: preferences, practiceMode: mode);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var result = new PersonaResult(persona.Name, session.Progression.CurriculumStage);
        for (var attempt = 1; attempt <= persona.Attempts; attempt++)
        {
            var fact = session.CurrentFact;
            var bandBefore = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            var prior = await store.LoadItemStatesAsync([fact.Id]);
            var priorAttempts = prior.TryGetValue(fact.Id, out var priorState) ? priorState.TotalAttempts : 0;

            clock.AdvanceMs(persona.LatencyMs);
            var isCorrect = persona.Answer(fact, priorAttempts + 1);
            var evaluation = session.SubmitAnswer(isCorrect ? fact.CorrectResult : fact.CorrectResult + 1);
            var role = evaluation.ChangeSet?.Attempt.ResolvedRole ?? string.Empty;
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

            var bandAfter = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            result.Trace.Add(new PersonaTurn(attempt, fact.Id, fact.Operation, Math.Max(fact.LeftOperand, fact.RightOperand), isCorrect, role, bandBefore, bandAfter, priorAttempts));
            if (bandAfter != bandBefore)
            {
                result.Advancements.Add((bandAfter, attempt));
            }

            await AdvanceSessionTurnAsync(session);
            if (stopAtAdditionBand is { } target && bandAfter >= target)
            {
                break;
            }
        }

        result.FinalStage = session.Progression.CurriculumStage;
        result.FinalAdditionBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        return result;
    }

    private static async Task AdvanceSessionTurnAsync(TrainingSession session)
    {
        if (session.InteractionState == SessionInteractionState.CorrectFeedback)
        {
            await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        }
        else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
        {
            await session.AcknowledgeTeachingInterventionAsync(startTiming: false);
        }
        else if (session.InteractionState is SessionInteractionState.IncorrectFeedback or SessionInteractionState.TimeoutFeedback)
        {
            await session.AcknowledgeFeedbackAsync(startTiming: false);
        }

        if (session.InteractionState == SessionInteractionState.SessionCheckIn)
        {
            await session.ContinuePracticeAsync(startTiming: false);
        }

        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }

    private sealed record Persona(string Name, int Attempts, int LatencyMs, Func<ArithmeticFact, int, bool> Answer);

    private sealed record PersonaTurn(
        int Attempt,
        string FactId,
        ArithmeticOperation Operation,
        int MaxOperand,
        bool IsCorrect,
        string Role,
        int AdditionBandBefore,
        int AdditionBandAfter,
        int PriorAttempts);

    private sealed class PersonaResult(string name, CurriculumStage initialStage)
    {
        public string Name { get; } = name;
        public CurriculumStage InitialStage { get; } = initialStage;
        public CurriculumStage FinalStage { get; set; }
        public int FinalAdditionBand { get; set; }
        public List<PersonaTurn> Trace { get; } = [];
        public List<(int Band, int Attempt)> Advancements { get; } = [];

        public string AdvancementsText => string.Join(",", Advancements.Select(a => $"{a.Band}@{a.Attempt}"));

        public string TraceText(int count) => string.Join(
            "\n",
            Trace.Take(count).Select(turn => string.Create(
                CultureInfo.InvariantCulture,
                $"{turn.Attempt:000} {turn.FactId,-10} band={IncrementalAdditionCurriculum.CreateBandId(turn.AdditionBandBefore).Value} role={turn.Role,-12} {(turn.IsCorrect ? "correct" : "MISSED")}{(turn.AdditionBandAfter != turn.AdditionBandBefore ? $" -> advanced to {IncrementalAdditionCurriculum.CreateBandId(turn.AdditionBandAfter).Value}" : string.Empty)}")));

        public string Describe(int traceCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine(CultureInfo.InvariantCulture, $"persona={Name} stage={InitialStage}->{FinalStage} finalAdditionBand={FinalAdditionBand} attempts={Trace.Count}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"advancements={AdvancementsText}");
            var roles = Trace.GroupBy(turn => turn.Role).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}:{group.Count()}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"roles=[{string.Join(",", roles)}]");
            builder.Append(TraceText(traceCount));
            return builder.ToString();
        }
    }

    private sealed class PersonaClock : IClock
    {
        private long _timestamp = 10_000_000;
        public TimeSpan CurrentTurnLatency { get; set; } = TimeSpan.FromMilliseconds(1000);
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => CurrentTurnLatency;
        public void AdvanceMs(long ms) => _timestamp += ms;
    }

    private sealed class FixedOperationsPreferenceStore(IReadOnlyList<ArithmeticOperation> operations) : IPreferenceStore
    {
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => operations.Contains(operation);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => operations;
        public void SetEnabledOperations(IReadOnlyList<ArithmeticOperation> operations) { }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }
}
