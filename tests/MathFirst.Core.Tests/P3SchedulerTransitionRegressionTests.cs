namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class P3SchedulerTransitionRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstP3Scheduler_" + Guid.NewGuid().ToString("N"));

    public P3SchedulerTransitionRegressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch { }
    }

    private string GetDatabasePath(string name = "sched") =>
        Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // SCHED01 - SCHED04: ACTIVE OPERATION SET PER STAGE
    // =========================================================================

    [Fact]
    public async Task SCHED01_Stage1_ActiveSetIsExactlyAddition()
    {
        var path = GetDatabasePath("sched01");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Equal([ArithmeticOperation.Addition], unlockedOps);

        while (session.Progression.CurriculumStage == CurriculumStage.Stage1_Addition)
        {
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }
    }

    [Fact]
    public async Task SCHED02_Stage2_ActiveSetIsExactlyAdditionAndSubtraction()
    {
        var path = GetDatabasePath("sched02");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction], unlockedOps);

        var seenOps = new HashSet<ArithmeticOperation>();
        while (session.Progression.CurriculumStage == CurriculumStage.Stage2_Subtraction)
        {
            seenOps.Add(session.CurrentFact.Operation);
            Assert.Contains(session.CurrentFact.Operation, (IReadOnlyList<ArithmeticOperation>)[ArithmeticOperation.Addition, ArithmeticOperation.Subtraction]);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
    }

    [Fact]
    public async Task SCHED03_Stage3_ActiveSetIsExactlyAdditionSubtractionMultiplication()
    {
        var path = GetDatabasePath("sched03");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var unlockedOps = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication], unlockedOps);

        var seenOps = new HashSet<ArithmeticOperation>();
        while (session.Progression.CurriculumStage == CurriculumStage.Stage3_Multiplication)
        {
            seenOps.Add(session.CurrentFact.Operation);
            Assert.Contains(session.CurrentFact.Operation, (IReadOnlyList<ArithmeticOperation>)[ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication]);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
        Assert.Contains(ArithmeticOperation.Multiplication, seenOps);
    }

    [Fact]
    public async Task SCHED04_Stage4_ActiveSetIsExactlyAllFourOperations()
    {
        var path = GetDatabasePath("sched04");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var seenOps = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 40; i++)
        {
            seenOps.Add(session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }

        Assert.Contains(ArithmeticOperation.Addition, seenOps);
        Assert.Contains(ArithmeticOperation.Subtraction, seenOps);
        Assert.Contains(ArithmeticOperation.Multiplication, seenOps);
        Assert.Contains(ArithmeticOperation.Division, seenOps);
    }

    // =========================================================================
    // SCHED05 - SCHED08: TIMING, FEEDBACK & POSITION CONTRACTS
    // =========================================================================

    [Fact]
    public async Task SCHED05_ExpansionChangesSchedulingOnlyForFutureProspectivePositions()
    {
        var path = GetDatabasePath("sched05");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var pastPos = session.Progression.PracticePosition;
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Past position 0 has attempt at position 1
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task SCHED06_AcceptedFeedbackFactIsNotReplacedAfterUnlock()
    {
        var path = GetDatabasePath("sched06");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var answeredFact = session.CurrentFact;
        session.SubmitAnswer(answeredFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // After unlock commit, CurrentFact remains the answered fact until advanced
        Assert.Equal(answeredFact.Id, session.CurrentFact.Id);
        Assert.Equal(answeredFact.Operation, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task SCHED07_NoPracticePositionIncrementOccursMerelyBecauseStageChanged()
    {
        var path = GetDatabasePath("sched07");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.PracticePosition = 10;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(10, session.Progression.PracticePosition);

        // Submitting 1 answer increments PracticePosition by exactly 1
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(11, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task SCHED08_PracticePositionIncrementsExactlyOncePerAcceptedAttempt()
    {
        var path = GetDatabasePath("sched08");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        for (var i = 1; i <= 15; i++)
        {
            Assert.Equal(i - 1, session.Progression.PracticePosition);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(i, session.Progression.PracticePosition);

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }
        }
    }

    // =========================================================================
    // SCHED09 - SCHED11: NO DUPLICATE PRACTICE POSITION AROUND STAGE TRANSITIONS
    // =========================================================================

    [Fact]
    public async Task SCHED09_NoDuplicatePracticePosition_AroundStage1ToStage2()
    {
        var path = GetDatabasePath("sched09");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var positions = new List<long>();

        // Turn 1 (triggers Stage 1 -> 2 unlock)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

        if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
        {
            session.ContinuePractice(startTiming: false);
        }

        // Turn 2
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);

        Assert.Equal([1, 2], positions);
    }

    [Fact]
    public async Task SCHED10_NoDuplicatePracticePosition_AroundStage2ToStage3()
    {
        var path = GetDatabasePath("sched10");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.PracticePosition = 5;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var positions = new List<long>();

        // Turn 1 (triggers Stage 2 -> 3 unlock)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

        if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
        {
            session.ContinuePractice(startTiming: false);
        }

        // Turn 2
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);

        Assert.Equal([6, 7], positions);
    }

    [Fact]
    public async Task SCHED11_NoDuplicatePracticePosition_AroundStage3ToStage4()
    {
        var path = GetDatabasePath("sched11");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.PracticePosition = 12;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var positions = new List<long>();

        // Turn 1 (triggers Stage 3 -> 4 unlock)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage4_Division, session.Progression.CurriculumStage);

        if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
        {
            session.ContinuePractice(startTiming: false);
        }

        // Turn 2
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        positions.Add(session.Progression.PracticePosition);

        Assert.Equal([13, 14], positions);
    }

    [Fact]
    public async Task SCHED12_DeterministicReplay_ProducesIdenticalScheduledSequence()
    {
        var path1 = GetDatabasePath("sched12_1");
        var path2 = GetDatabasePath("sched12_2");

        var sequence1 = new List<string>();
        var sequence2 = new List<string>();

        using (var store1 = new SqliteLearnerStore(path1))
        {
            var session1 = new TrainingSession(store1, practiceMode: PracticeMode.CurriculumManaged);
            await session1.InitializeAsync(startTiming: false);
            for (var i = 0; i < 25; i++)
            {
                sequence1.Add($"{session1.CurrentFact.Operation}:{session1.CurrentFact.Id}");
                session1.SubmitAnswer(session1.CurrentFact.CorrectResult);
                await session1.CommitCurrentEvaluationAsync();
                if (!session1.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    session1.ContinuePractice(startTiming: false);
                }
            }
        }

        using (var store2 = new SqliteLearnerStore(path2))
        {
            var session2 = new TrainingSession(store2, practiceMode: PracticeMode.CurriculumManaged);
            await session2.InitializeAsync(startTiming: false);
            for (var i = 0; i < 25; i++)
            {
                sequence2.Add($"{session2.CurrentFact.Operation}:{session2.CurrentFact.Id}");
                session2.SubmitAnswer(session2.CurrentFact.CorrectResult);
                await session2.CommitCurrentEvaluationAsync();
                if (!session2.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    session2.ContinuePractice(startTiming: false);
                }
            }
        }

        Assert.Equal(sequence1, sequence2);
    }

    // =========================================================================
    // ORD01 - ORD06: PER-OPERATION ROLE ORDINAL HARDENING
    // =========================================================================

    [Fact]
    public async Task ORD01_FreshNewlyUnlockedSubtraction_HasOrdinal1()
    {
        var path = GetDatabasePath("ord01");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        // Next operation ordinal is 0 + 1 = 1
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction) + 1);
    }

    [Fact]
    public async Task ORD02_FreshNewlyUnlockedMultiplication_HasOrdinal1()
    {
        var path = GetDatabasePath("ord02");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
        Assert.Equal(1, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication) + 1);
    }

    [Fact]
    public async Task ORD03_MigratedLearnerWithDormantSubtractionHistory_ResumesOrdinalFromHistoricalCountPlus1()
    {
        var path = GetDatabasePath("ord03");
        await SeedDormantAttemptsAsync(path, ArithmeticOperation.Subtraction, historicalCount: 6);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(6, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));
        Assert.Equal(7, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction) + 1);
    }

    [Fact]
    public async Task ORD04_MigratedLearnerWithDormantMultiplicationHistory_ResumesOrdinalFromHistoricalCountPlus1()
    {
        var path = GetDatabasePath("ord04");
        await SeedDormantAttemptsAsync(path, ArithmeticOperation.Multiplication, historicalCount: 12);

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(12, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
        Assert.Equal(13, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication) + 1);
    }

    [Fact]
    public async Task ORD05_UnlockingOperation_DoesNotResetOperationSpecificAcceptedCounters()
    {
        var path = GetDatabasePath("ord05");
        await SeedDormantAttemptsAsync(path, ArithmeticOperation.Multiplication, historicalCount: 4);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.PracticePosition = 4;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        prog.OperationProgressions[ArithmeticOperation.Subtraction] = new OperationProgression(ArithmeticOperation.Subtraction, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(4, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));

        // Submit answer that unlocks Stage 3 (Multiplication)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

        // Multiplication accepted count must STILL be 4
        Assert.Equal(4, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
    }

    [Fact]
    public async Task ORD06_GlobalPracticePosition_RemainsIndependentFromOperationOrdinal()
    {
        var path = GetDatabasePath("ord06");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        prog.PracticePosition = 50;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(50, session.Progression.PracticePosition);
        Assert.Equal(0, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction));

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(51, session.Progression.PracticePosition);
        Assert.True(session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition) == 1 ||
                    session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Subtraction) == 1);
    }

    private static async Task SeedDormantAttemptsAsync(string dbPath, ArithmeticOperation op, int historicalCount)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '9');
            INSERT INTO schema_info VALUES ('store_revision', '1');

            CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, curriculum_stage INTEGER NOT NULL DEFAULT 1, updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, " + historicalCount + @", 1, '2026-10-06T00:00:00Z');

            CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);

            CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
            CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1)), outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
        ";
        await cmd.ExecuteNonQueryAsync();

        var factId = $"{op.ToString().ToLowerInvariant()[..3]}:1+1";
        using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = @"
            INSERT INTO item_learning_state VALUES (
                @fact_id, @op, 1, 1, @count, @count, 0, @count, 800, 800, @count, 1, 0, 0, @count, '2026-10-06T00:00:00Z'
            );";
        itemCmd.Parameters.AddWithValue("@fact_id", factId);
        itemCmd.Parameters.AddWithValue("@op", op.ToString());
        itemCmd.Parameters.AddWithValue("@count", historicalCount);
        await itemCmd.ExecuteNonQueryAsync();

        for (var i = 1; i <= historicalCount; i++)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    @id, @fact_id, @op, 1, 1, 2, 2, 1, 1, 'Correct', 800, '2026-10-06T00:00:00Z', @pos
                );";
            insertCmd.Parameters.AddWithValue("@id", $"hist_{op}_{i}");
            insertCmd.Parameters.AddWithValue("@fact_id", factId);
            insertCmd.Parameters.AddWithValue("@op", op.ToString());
            insertCmd.Parameters.AddWithValue("@pos", i);
            await insertCmd.ExecuteNonQueryAsync();
        }
    }
}
