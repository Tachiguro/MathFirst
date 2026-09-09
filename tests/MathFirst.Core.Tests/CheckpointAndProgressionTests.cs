using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Xunit;

namespace MathFirst.Core.Tests;

public sealed class CheckpointAndProgressionTests : IDisposable
{
    private readonly List<string> _tempDbPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempDbPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private string GetTempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mathfirst_checkpoint_test_{Guid.NewGuid():N}.db");
        _tempDbPaths.Add(path);
        return path;
    }

    private static ProgressionPhase GetPhase(TrainingSession session) =>
        LearningPolicy.DetermineProgressionPhase(session.Progression, session.ItemStates).Phase;

    // =========================================================================
    // 1. CHECKPOINT TRANSITION AND CYCLE TESTS
    // =========================================================================

    [Fact(Skip = "Superseded by Schema V5 independent operation progression.")]
    public async Task Level1_IntroCycle_EntersCheckpoint1BeforeLevel2()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // 1. Initial state: Level 1 Addition introduction (4 facts: 0+0, 0+1, 1+0, 1+1)
        Assert.Equal(ProgressionPhase.IntroducingAddition, GetPhase(session));
        Assert.False(session.Progression.IsInCheckpoint);
        Assert.Equal(0, session.Progression.CompletedCheckpointLevel);

        // Expose all Level 1 addition facts (4 facts)
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact!.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        // 2. Subtraction introduction (3 facts: 0-0, 1-0, 1-1)
        Assert.Equal(ProgressionPhase.IntroducingSubtraction, GetPhase(session));
        Assert.False(session.Progression.IsInCheckpoint);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact!.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        // 3. Multiplication introduction (4 facts: 0*0, 0*1, 1*0, 1*1)
        Assert.Equal(ProgressionPhase.IntroducingMultiplication, GetPhase(session));
        Assert.False(session.Progression.IsInCheckpoint);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact!.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        // 4. Division introduction (2 facts: 0/1, 1/1)
        Assert.Equal(ProgressionPhase.IntroducingDivision, GetPhase(session));
        Assert.False(session.Progression.IsInCheckpoint);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ArithmeticOperation.Division, session.CurrentFact!.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        // 5. All 4 operations for Level 1 are now introduced -> Checkpoint 1 starts!
        Assert.True(session.Progression.IsInCheckpoint);
        Assert.Equal(ProgressionPhase.Checkpoint, GetPhase(session));
        Assert.Equal(1, session.Progression.ActiveCheckpointLevel);
        Assert.Equal(0, session.Progression.CheckpointAttemptCount);
        Assert.Equal(0, session.Progression.CompletedCheckpointLevel);
        Assert.Equal(1, session.Progression.GetMaxOperand(ArithmeticOperation.Addition));
    }

    [Fact(Skip = "Superseded by Schema V5 independent operation progression.")]
    public async Task Checkpoint_Length_IsBoundedAt12AcceptedAttempts_NonMasteryGated()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // Introduce all 13 Level 1 facts (4 Add, 3 Sub, 4 Mul, 2 Div)
        for (var i = 0; i < 13; i++)
        {
            session.SubmitAnswer(session.CurrentFact!.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        Assert.True(session.Progression.IsInCheckpoint);
        Assert.Equal(1, session.Progression.ActiveCheckpointLevel);

        // Run through 11 checkpoint attempts with mixed outcomes (some correct, some incorrect, some timeout)
        for (var attempt = 1; attempt <= 11; attempt++)
        {
            Assert.True(session.Progression.IsInCheckpoint);
            Assert.Equal(attempt - 1, session.Progression.CheckpointAttemptCount);

            if (attempt % 3 == 0)
            {
                // Timeout
                session.RecordTimeout();
            }
            else if (attempt % 3 == 1)
            {
                // Correct
                session.SubmitAnswer(session.CurrentFact!.CorrectResult);
            }
            else
            {
                // Incorrect
                session.SubmitAnswer(session.CurrentFact!.CorrectResult + 99);
            }

            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
        }

        // On 11th attempt done -> count is 11
        Assert.True(session.Progression.IsInCheckpoint);
        Assert.Equal(11, session.Progression.CheckpointAttemptCount);

        // 12th attempt (the final checkpoint question)
        session.SubmitAnswer(session.CurrentFact!.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceToNextFact();

        // Checkpoint 1 completed! Progression advances to Level 2 Addition introduction!
        Assert.False(session.Progression.IsInCheckpoint);
        Assert.Equal(1, session.Progression.CompletedCheckpointLevel);
        Assert.Null(session.Progression.ActiveCheckpointLevel);
        Assert.Equal(0, session.Progression.CheckpointAttemptCount);
        Assert.Equal(ProgressionPhase.IntroducingAddition, GetPhase(session));
        Assert.Equal(2, session.Progression.GetMaxOperand(ArithmeticOperation.Addition));
    }

    [Fact(Skip = "Superseded by Schema V5 independent operation progression.")]
    public async Task Checkpoint_PersistenceAcrossRestarts_RestoresExactAttemptCount()
    {
        var dbPath = GetTempDbPath();

        // 1. First session: Reach Checkpoint 1 and execute 7 of 12 attempts
        using (var store1 = new SqliteLearnerStore(dbPath))
        {
            var session1 = new TrainingSession(store1);
            await session1.InitializeAsync();

            // 13 intro facts
            for (var i = 0; i < 13; i++)
            {
                session1.SubmitAnswer(session1.CurrentFact!.CorrectResult);
                await session1.CommitCurrentEvaluationAsync();
                session1.AdvanceToNextFact();
            }

            Assert.True(session1.Progression.IsInCheckpoint);

            // 7 checkpoint attempts
            for (var i = 0; i < 7; i++)
            {
                session1.SubmitAnswer(session1.CurrentFact!.CorrectResult);
                await session1.CommitCurrentEvaluationAsync();
                session1.AdvanceToNextFact();
            }

            Assert.Equal(7, session1.Progression.CheckpointAttemptCount);
            Assert.Equal(7, session1.Progression.CheckpointCorrectCount);
            Assert.Equal(1, session1.Progression.ActiveCheckpointLevel);
        }

        // 2. Second session: Reopen database and verify restored checkpoint state
        using (var store2 = new SqliteLearnerStore(dbPath))
        {
            var session2 = new TrainingSession(store2);
            await session2.InitializeAsync();

            Assert.True(session2.Progression.IsInCheckpoint);
            Assert.Equal(1, session2.Progression.ActiveCheckpointLevel);
            Assert.Equal(7, session2.Progression.CheckpointAttemptCount);
            Assert.Equal(7, session2.Progression.CheckpointCorrectCount);
            Assert.Equal(0, session2.Progression.CompletedCheckpointLevel);

            // Complete remaining 5 attempts (7 + 5 = 12)
            for (var i = 0; i < 5; i++)
            {
                session2.SubmitAnswer(session2.CurrentFact!.CorrectResult);
                await session2.CommitCurrentEvaluationAsync();
                session2.AdvanceToNextFact();
            }

            // Checkpoint 1 is complete -> now in Level 2 Addition!
            Assert.False(session2.Progression.IsInCheckpoint);
            Assert.Equal(1, session2.Progression.CompletedCheckpointLevel);
            Assert.Equal(ProgressionPhase.IntroducingAddition, GetPhase(session2));
            Assert.Equal(2, session2.Progression.GetMaxOperand(ArithmeticOperation.Addition));
        }
    }

    [Fact(Skip = "Superseded by Schema V5 independent operation progression.")]
    public async Task FinalLevel10Checkpoint_TransitionsToOpenEndedMixedPractice_NoLevel11()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // Simulate progression having completed 9 checkpoints and all 4 operations at 10
        session.Progression.OperationMaxOperands[ArithmeticOperation.Addition] = 10;
        session.Progression.OperationMaxOperands[ArithmeticOperation.Subtraction] = 10;
        session.Progression.OperationMaxOperands[ArithmeticOperation.Multiplication] = 10;
        session.Progression.OperationMaxOperands[ArithmeticOperation.Division] = 10;
        session.Progression.CompletedCheckpointLevel = 9;

        // Expose all 418 facts
        var allFacts = ArithmeticCatalog.GetActiveFacts(session.Progression);
        foreach (var fact in allFacts)
        {
            var st = ItemLearningState.CreateNew(fact);
            st.TotalAttempts = 1;
            st.CorrectAttempts = 1;
            session.ItemStates[fact.Id] = st;
        }

        // Active Checkpoint Level is 10
        var (phase, _, maxOp, _) = LearningPolicy.DetermineProgressionPhase(session.Progression, session.ItemStates);
        Assert.Equal(ProgressionPhase.Checkpoint, phase);
        Assert.Equal(10, maxOp);

        // Complete the 12 attempts of Checkpoint 10
        session.Progression.ActiveCheckpointLevel = 10;
        session.Progression.CheckpointAttemptCount = 11;
        session.Progression.CheckpointCorrectCount = 10;

        // 12th attempt
        var eval = session.SubmitAnswer(session.CurrentFact?.CorrectResult ?? 0);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceToNextFact();

        // Check invariants:
        Assert.Equal(10, session.Progression.CompletedCheckpointLevel);
        Assert.Null(session.Progression.ActiveCheckpointLevel);
        Assert.True(session.Progression.IsAllIntroductionsComplete);
        Assert.Equal(ProgressionPhase.MixedPractice, GetPhase(session));

        // Operands never exceed 10 (no level 11)
        Assert.Equal(10, session.Progression.GetMaxOperand(ArithmeticOperation.Addition));
        Assert.Equal(10, session.Progression.GetMaxOperand(ArithmeticOperation.Subtraction));
        Assert.Equal(10, session.Progression.GetMaxOperand(ArithmeticOperation.Multiplication));
        Assert.Equal(10, session.Progression.GetMaxOperand(ArithmeticOperation.Division));
    }

    // =========================================================================
    // 2. REPETITION DENSITY & DIVERSITY CONSTRAINTS
    // =========================================================================

    [Fact]
    public void ExactFactCooldown_NeverRepeatsSameFactWithin3Positions_WhenAlternativesExist()
    {
        var rng = new Random(1001);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 5,
                [ArithmeticOperation.Subtraction] = 5,
                [ArithmeticOperation.Multiplication] = 5,
                [ArithmeticOperation.Division] = 5
            },
            CompletedCheckpointLevel = 5
        };

        var facts = ArithmeticCatalog.GetActiveFacts(progression);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 2;
            return st;
        }, StringComparer.Ordinal);

        var history = new List<ArithmeticFact>();

        for (var step = 1; step <= 60; step++)
        {
            var fact = selector.SelectNextFact(progression, itemStates, null, step, step);
            history.Add(fact);

            // Verify exact fact cooldown: fact must not equal any of the previous 3 facts
            var lookback = Math.Min(3, history.Count - 1);
            for (var k = 1; k <= lookback; k++)
            {
                var prevFact = history[history.Count - 1 - k];
                Assert.NotEqual(prevFact.Id, fact.Id);
            }
        }
    }

    [Fact]
    public void CommutativeMirrorCooldown_AvoidsAdjacentAndNearMirrorPairs_AdditionAndMultiplication()
    {
        var rng = new Random(2022);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 5,
                [ArithmeticOperation.Subtraction] = 5,
                [ArithmeticOperation.Multiplication] = 5,
                [ArithmeticOperation.Division] = 5
            },
            CompletedCheckpointLevel = 5
        };

        var facts = ArithmeticCatalog.GetActiveFacts(progression);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 2;
            return st;
        }, StringComparer.Ordinal);

        var history = new List<ArithmeticFact>();

        for (var step = 1; step <= 80; step++)
        {
            var fact = selector.SelectNextFact(progression, itemStates, null, step, step);
            history.Add(fact);

            // If commutative (Addition or Multiplication), verify mirror cooldown within 3 positions
            if (fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Multiplication && fact.LeftOperand != fact.RightOperand)
            {
                var lookback = Math.Min(3, history.Count - 1);
                for (var k = 1; k <= lookback; k++)
                {
                    var prev = history[history.Count - 1 - k];
                    if (prev.Operation == fact.Operation)
                    {
                        var isMirror = prev.LeftOperand == fact.RightOperand && prev.RightOperand == fact.LeftOperand;
                        Assert.False(isMirror, $"Mirror violation at step {step}: '{fact.Id}' presented within 3 of '{prev.Id}'");
                    }
                }
            }
        }
    }

    [Fact]
    public void NonCommutativeOperations_SubtractionAndDivision_ExemptFromMirrorKey()
    {
        var sub1 = new ArithmeticFact(ArithmeticOperation.Subtraction, 5, 2);
        var sub2 = new ArithmeticFact(ArithmeticOperation.Subtraction, 2, 0);

        // Subtraction and Division facts are never symmetric mirrors
        Assert.NotEqual(sub1.Id, sub2.Id);
    }

    [Fact]
    public void OperationStreakDiversity_LimitsConsecutiveSameOperationToMax2_WhenAlternativesExist()
    {
        var rng = new Random(4567);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 10,
                [ArithmeticOperation.Subtraction] = 10,
                [ArithmeticOperation.Multiplication] = 10,
                [ArithmeticOperation.Division] = 10
            },
            CompletedCheckpointLevel = 10
        };

        var facts = ArithmeticCatalog.GetActiveFacts(progression);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 2;
            return st;
        }, StringComparer.Ordinal);

        var history = new List<ArithmeticFact>();

        for (var step = 1; step <= 100; step++)
        {
            var fact = selector.SelectNextFact(progression, itemStates, null, step, step);
            history.Add(fact);

            if (history.Count >= 3)
            {
                var last3Ops = history.TakeLast(3).Select(f => f.Operation).ToList();
                var allThreeSame = last3Ops[0] == last3Ops[1] && last3Ops[1] == last3Ops[2];
                Assert.False(allThreeSame, $"Streak violation at step {step}: 3 consecutive '{last3Ops[0]}' operations.");
            }
        }
    }

    [Fact]
    public void RemediationPriority_AlwaysServesDueRemediationOverDiversityConstraints()
    {
        var rng = new Random(777);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 3,
                [ArithmeticOperation.Subtraction] = 3,
                [ArithmeticOperation.Multiplication] = 3,
                [ArithmeticOperation.Division] = 3
            },
            CompletedCheckpointLevel = 3
        };

        var facts = ArithmeticCatalog.GetActiveFacts(progression);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 2;
            return st;
        }, StringComparer.Ordinal);

        // Mark a specific fact as needing remediation due at order 5
        var remediationFact = facts.First(f => f.Id == "add:2+3");
        itemStates[remediationFact.Id].NeedsRemediation = true;
        itemStates[remediationFact.Id].RemediationDueOrder = 5;

        // Even if selector just picked an addition fact at order 4
        var selected = selector.SelectNextFact(progression, itemStates, null, currentSessionOrder: 5, currentPracticePosition: 10);

        // Remediation MUST take priority!
        Assert.Equal(remediationFact.Id, selected.Id);
    }

    // =========================================================================
    // 3. SCHEMA V4 PERSISTENCE & MIGRATION TESTS
    // =========================================================================

    [Fact]
    public async Task SchemaMigration_V3ToV4_PreservesAllDataAndAddsCheckpointColumns()
    {
        var dbPath = GetTempDbPath();

        // 1. Create a V3 database manually
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '3'), ('store_revision', '11');

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    current_operation TEXT NOT NULL,
                    current_max_operand INTEGER NOT NULL,
                    operation_max_operands_json TEXT NOT NULL,
                    practice_position INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO learner_progression (id, current_operation, current_max_operand, operation_max_operands_json, practice_position, updated_at)
                VALUES (1, 'Addition', 3, '{""Addition"":3,""Subtraction"":2,""Multiplication"":2,""Division"":2}', 25, '2026-09-07T12:00:00Z');

                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY,
                    fact_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER,
                    correct_answer INTEGER NOT NULL,
                    is_correct INTEGER NOT NULL,
                    outcome TEXT NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    timestamp TEXT NOT NULL
                );
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, outcome,
                    response_latency_ms, timestamp
                ) VALUES
                ('att_1', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 1200, '2026-09-07T12:00:00Z');

                CREATE TABLE item_learning_state (
                    fact_id TEXT PRIMARY KEY,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL,
                    correct_attempts INTEGER NOT NULL,
                    incorrect_attempts INTEGER NOT NULL,
                    consecutive_correct INTEGER NOT NULL,
                    last_latency_ms INTEGER NOT NULL,
                    rolling_latency_ms INTEGER NOT NULL,
                    fluent_streak INTEGER NOT NULL,
                    is_mastered INTEGER NOT NULL,
                    needs_remediation INTEGER NOT NULL,
                    remediation_due_order INTEGER NOT NULL,
                    last_practiced_order INTEGER NOT NULL,
                    last_practiced_at TEXT
                );
                INSERT INTO item_learning_state (
                    fact_id, operation, left_operand, right_operand,
                    total_attempts, correct_attempts, incorrect_attempts,
                    consecutive_correct, last_latency_ms, rolling_latency_ms,
                    fluent_streak, is_mastered, needs_remediation,
                    remediation_due_order, last_practiced_order, last_practiced_at
                ) VALUES
                ('add:1+1', 'Addition', 1, 1, 1, 1, 0, 1, 1200, 1200, 1, 0, 0, 0, 1, '2026-09-07T12:00:00Z');

                CREATE TABLE fsrs_card_state (
                    fact_id TEXT PRIMARY KEY,
                    card_id TEXT NOT NULL,
                    state INTEGER NOT NULL,
                    step INTEGER,
                    stability REAL,
                    difficulty REAL,
                    due_practice_position INTEGER NOT NULL,
                    last_review_practice_position INTEGER,
                    last_rating INTEGER
                );
                INSERT INTO fsrs_card_state (
                    fact_id, card_id, state, stability, difficulty, due_practice_position, last_review_practice_position, last_rating
                ) VALUES
                ('add:1+1', 'd8d1e2a3-4b5c-6d7e-8f9a-0b1c2d3e4f5a', 2, 4.5, 2.1, 5, 1, 3);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Open with SqliteLearnerStore -> Triggers Migration V3 -> V4
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(5, snapshot.SchemaVersion);
            Assert.Equal(11, snapshot.Revision);
            Assert.Equal(25, snapshot.Progression.PracticePosition);
            Assert.Equal(0, snapshot.Progression.CompletedCheckpointLevel); // V5 removes checkpoint authority.
            Assert.Null(snapshot.Progression.ActiveCheckpointLevel);
            Assert.Equal(0, snapshot.Progression.CheckpointAttemptCount);

            // Item states and FSRS states preserved
            Assert.Single(snapshot.ItemStates);
            Assert.True(snapshot.ItemStates.ContainsKey("add:1+1"));
            Assert.Single(snapshot.FsrsStates);
            Assert.True(snapshot.FsrsStates.ContainsKey("add:1+1"));
            Assert.Equal(4.5, snapshot.FsrsStates["add:1+1"].Stability);
        }
    }

    [Fact]
    public async Task SchemaMigration_V1ToV4_ChainedMigrationSucceeds()
    {
        var dbPath = GetTempDbPath();

        // 1. Create a V1 database
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '1'), ('store_revision', '2');

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    current_operation TEXT NOT NULL,
                    current_max_operand INTEGER NOT NULL,
                    operation_max_operands_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO learner_progression (id, current_operation, current_max_operand, operation_max_operands_json, updated_at)
                VALUES (1, 'Addition', 1, '{""Addition"":1}', '2026-09-07T12:00:00Z');

                CREATE TABLE item_learning_state (
                    fact_id TEXT PRIMARY KEY,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL,
                    correct_attempts INTEGER NOT NULL,
                    incorrect_attempts INTEGER NOT NULL,
                    consecutive_correct INTEGER NOT NULL,
                    last_latency_ms INTEGER NOT NULL,
                    rolling_latency_ms INTEGER NOT NULL,
                    fluent_streak INTEGER NOT NULL,
                    is_mastered INTEGER NOT NULL,
                    needs_remediation INTEGER NOT NULL,
                    remediation_due_order INTEGER NOT NULL,
                    last_practiced_order INTEGER NOT NULL,
                    last_practiced_at TEXT
                );
                INSERT INTO item_learning_state (
                    fact_id, operation, left_operand, right_operand,
                    total_attempts, correct_attempts, incorrect_attempts,
                    consecutive_correct, last_latency_ms, rolling_latency_ms,
                    fluent_streak, is_mastered, needs_remediation,
                    remediation_due_order, last_practiced_order, last_practiced_at
                ) VALUES
                ('Add:0+0', 'Addition', 0, 0, 1, 1, 0, 1, 900, 900, 1, 0, 0, 0, 1, '2026-09-07T12:00:00Z');

                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY,
                    fact_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER NOT NULL,
                    correct_answer INTEGER NOT NULL,
                    is_correct INTEGER NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    timestamp TEXT NOT NULL
                );
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, response_latency_ms, timestamp
                ) VALUES
                ('sub1', 'Add:0+0', 'Addition', 0, 0, 0, 0, 1, 900, '2026-09-07T11:58:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Open with SqliteLearnerStore -> Chained migration V1 -> V2 -> V3 -> V4
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(5, snapshot.SchemaVersion);
            Assert.Equal(2, snapshot.Revision);
            Assert.Equal(1, snapshot.Progression.PracticePosition);
            Assert.Equal(0, snapshot.Progression.CompletedCheckpointLevel);
            Assert.Single(snapshot.FsrsStates);
            Assert.True(snapshot.FsrsStates.ContainsKey("Add:0+0"));
        }
    }

    // =========================================================================
    // 4. HIGH-VOLUME SYNTHETIC PROGRESSION SIMULATION
    // =========================================================================

    [Fact(Skip = "Superseded by Schema V5 independent operation progression.")]
    public async Task MultiHundredAttempt_SyntheticSimulation_VerifiesAllProgressionInvariants()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var recentFacts = new List<ArithmeticFact>();
        var totalAttempts = 0;

        // Simulate 350 learner steps across multiple levels
        for (var step = 1; step <= 350; step++)
        {
            var fact = session.CurrentFact;
            Assert.NotNull(fact);

            // Invariant: Exact duplicate cooldown (within 3 facts) for non-remediation selections
            var isRemediation = session.ItemStates.TryGetValue(fact.Id, out var st) && st.NeedsRemediation;
            if (!isRemediation && recentFacts.Count > 1)
            {
                var lookback = Math.Min(3, recentFacts.Count - 1);
                for (var k = 1; k <= lookback; k++)
                {
                    var prev = recentFacts[recentFacts.Count - 1 - k];
                    Assert.NotEqual(prev.Id, fact.Id);
                }
            }

            recentFacts.Add(fact);

            // Submit 90% correct answers (fluent 1200ms) and 10% wrong answers (needs remediation)
            if (step % 10 == 0)
            {
                session.SubmitAnswer(fact.CorrectResult + 1);
            }
            else
            {
                session.SubmitAnswer(fact.CorrectResult);
            }

            await session.CommitCurrentEvaluationAsync();
            session.AdvanceToNextFact();
            totalAttempts++;
        }

        // Verify progression has advanced through multiple levels with checkpoints
        Assert.True(session.Progression.CompletedCheckpointLevel >= 1);
        Assert.Equal(totalAttempts, session.Progression.PracticePosition);
        Assert.True(session.FsrsStates.Count > 13);
    }
}
