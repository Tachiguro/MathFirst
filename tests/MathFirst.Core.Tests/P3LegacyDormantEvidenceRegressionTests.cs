namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class P3LegacyDormantEvidenceRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstP3LegacyDormant_" + Guid.NewGuid().ToString("N"));

    public P3LegacyDormantEvidenceRegressionTests() => Directory.CreateDirectory(_directory);

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

    private string GetDatabasePath(string name = "legacy") =>
        Path.Combine(_directory, $"{name}_{Guid.NewGuid():N}.db");

    private sealed class StubPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, bool> _boolPrefs = new();
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public string Language { get; set; } = "system";
        public bool HapticFeedbackEnabled { get; set; } = true;
        public PracticeTimeSetting TimeSetting { get; set; } = PracticeTimeSetting.Standard;

        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string languageCode) => Language = languageCode;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _boolPrefs.GetValueOrDefault($"op_{operation}", true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _boolPrefs[$"op_{operation}"] = enabled;

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations()
        {
            var list = new List<ArithmeticOperation>();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                if (GetOperationEnabled(op))
                {
                    list.Add(op);
                }
            }
            return PracticeOperationPreferencePolicy.NormalizeEnabledOperations(list);
        }

        public void SetEnabledOperations(IReadOnlyList<ArithmeticOperation> operations)
        {
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                SetOperationEnabled(op, operations.Contains(op));
            }
        }

        public PracticeTimeSetting GetPracticeTimeSetting() => TimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => TimeSetting = setting;

        public void ResetPracticePreferences() => _boolPrefs.Clear();
        public void ResetAllPreferences() => _boolPrefs.Clear();
    }

    // =========================================================================
    // DORMANT EVIDENCE & PROGRESSION RESTORATION
    // =========================================================================

    [Fact]
    public async Task DormantEvidence_PreservedUnderLockedStage_AndFullyRestoredWhenStageUnlocked()
    {
        var path = GetDatabasePath("dormant_restore");

        // Create V8 database with advanced Multiplication (BandIndex 3, 10 attempts, 1 mastered fact, 1 FSRS card)
        // Addition and Subtraction are at Band 0 without completed prerequisites, so migration assigns Stage 1
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 10, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 3, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1)), outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);

                INSERT INTO item_learning_state VALUES ('mul:3*3', 'Multiplication', 3, 3, 10, 10, 0, 10, 800, 800, 10, 1, 0, 0, 10, '2026-10-06T00:00:00Z');
                INSERT INTO fsrs_card_state VALUES ('mul:3*3', '00000000-0000-0000-0000-000000000001', 2, NULL, 5.0, 2.0, 100, 10, 3);
            ";
            await cmd.ExecuteNonQueryAsync();

            for (var i = 1; i <= 10; i++)
            {
                using var attCmd = conn.CreateCommand();
                attCmd.CommandText = @"
                    INSERT INTO attempt_history VALUES (@id, 'mul:3*3', 'Multiplication', 3, 3, 9, 9, 1, 1, 'Correct', 800, '2026-10-06T00:00:00Z', @pos, 1, null, 1000, 'New', 0, 0);";
                attCmd.Parameters.AddWithValue("@id", $"sub_mul_{i}");
                attCmd.Parameters.AddWithValue("@pos", i);
                await attCmd.ExecuteNonQueryAsync();
            }
        }

        // 1. Initialize store -> performs V8 to V9 migration
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();

            // Conservative migration stage is Stage 1 because ADD-D01 prerequisite is not met
            Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);

            // Preserved MUL evidence remains intact while locked
            Assert.Equal(3, snapshot.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
            Assert.True(snapshot.ItemStates.TryGetValue("mul:3*3", out var mulItem));
            Assert.Equal(10, mulItem.TotalAttempts);
            Assert.True(mulItem.IsProvisionallyMastered);
            Assert.True(snapshot.FsrsStates.TryGetValue("mul:3*3", out var mulFsrs));
            Assert.Equal(5.0, mulFsrs.Stability);

            var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
            Assert.Equal(10, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));

            // 2. Complete ADD-D01 prerequisite
            var addPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage1_Addition);
            foreach (var id in addPrereqs)
            {
                var parts = id.Split([':', '+']);
                var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, int.Parse(parts[1]), int.Parse(parts[2])));
                item.TotalAttempts = 1;
                item.CorrectAttempts = 1;
                session.ItemStates[id] = item;
            }

            var evalStage2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.Equal(CurriculumStage.Stage2_Subtraction, evalStage2.ChangeSet!.UpdatedProgression.CurriculumStage);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

            if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                session.ContinuePractice(startTiming: false);
            }

            // 3. Complete SUB-D01 prerequisite
            var subPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage2_Subtraction);
            foreach (var id in subPrereqs)
            {
                var parts = id.Split([':', '-']);
                var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Subtraction, int.Parse(parts[1]), int.Parse(parts[2])));
                item.TotalAttempts = 1;
                item.CorrectAttempts = 1;
                session.ItemStates[id] = item;
            }

            var evalStage3 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, evalStage3.ChangeSet!.UpdatedProgression.CurriculumStage);
            await session.CommitCurrentEvaluationAsync();
            Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

            // 4. Multiplication is now unlocked! Preserved evidence is intact and NOT reset
            Assert.Equal(3, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
            Assert.Equal(10, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Multiplication));
            Assert.True(session.ItemStates.ContainsKey("mul:3*3"));
            Assert.True(session.FsrsStates.ContainsKey("mul:3*3"));
        }
    }

    // =========================================================================
    // UP01 - UP06: UPGRADE REGRESSION HARDENING
    // =========================================================================

    [Fact]
    public async Task UP01_MigratedV8ToV9Stage_SurvivesAppAndStoreReopen()
    {
        var path = GetDatabasePath("up01");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 1, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 1, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Migrate to V9
        using (var store1 = new SqliteLearnerStore(path))
        {
            await store1.InitializeAsync();
            var snapshot1 = await store1.LoadSnapshotAsync();
            Assert.Equal(9, snapshot1.SchemaVersion);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot1.Progression.CurriculumStage);
        }

        // Reopen in a second instance
        using (var store2 = new SqliteLearnerStore(path))
        {
            await store2.InitializeAsync();
            var snapshot2 = await store2.LoadSnapshotAsync();
            Assert.Equal(9, snapshot2.SchemaVersion);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot2.Progression.CurriculumStage);
        }
    }

    [Fact]
    public async Task UP02_SequentialLegacyMigrations_V1ThroughV8_ReachValidV9()
    {
        var path = GetDatabasePath("up02");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '5');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    practice_position INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO learner_progression VALUES (1, 10, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (
                    operation TEXT PRIMARY KEY,
                    band_index INTEGER NOT NULL CHECK (band_index >= 0),
                    band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
                );
                INSERT INTO operation_progression VALUES ('Addition', 1, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (
                    fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                    consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL,
                    fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                    remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT
                );

                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1)),
                    outcome TEXT NOT NULL DEFAULT 'Incorrect', response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL,
                    practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER,
                    resolved_role TEXT, operation_band_before INTEGER
                );

                CREATE TABLE fsrs_card_state (
                    fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL,
                    due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(9, snapshot.SchemaVersion);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task UP03_PreferenceValues_DoNotAlterMigrationStage()
    {
        var path = GetDatabasePath("up03");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        // Learning evidence is Band 0, so Stage 1 regardless of preferences
        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task UP04_DivisionOnlyHistory_PreservedWithoutGrantingStage4()
    {
        var path = GetDatabasePath("up04");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 5, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(5, snapshot.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task UP05_MigratedLowerStageLearner_CanProgressNormallyWithoutDeadlock()
    {
        var path = GetDatabasePath("up05");
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 2, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);

        // Practice ADD-D01 facts
        var addPrereqs = CurriculumUnlockPolicy.GetPrerequisiteFactIds(CurriculumStage.Stage1_Addition);
        foreach (var id in addPrereqs)
        {
            var parts = id.Split([':', '+']);
            var item = ItemLearningState.CreateNew(new ArithmeticFact(ArithmeticOperation.Addition, int.Parse(parts[1]), int.Parse(parts[2])));
            item.TotalAttempts = 1;
            item.CorrectAttempts = 1;
            session.ItemStates[id] = item;
        }

        // Submitting answer unlocks Stage 2 (and because Subtraction BandIndex >= 1, next will evaluate toward Stage 3)
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet!.UpdatedProgression.CurriculumStage);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task UP06_FreshV9AndEquivalentMigratedV8_ProduceEquivalentBehavior()
    {
        var pathFresh = GetDatabasePath("up06_fresh");
        var pathMigrated = GetDatabasePath("up06_migrated");

        using (var freshStore = new SqliteLearnerStore(pathFresh))
        {
            await freshStore.InitializeAsync();
        }

        await using (var conn = new SqliteConnection($"Data Source={pathMigrated}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '8');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (id INTEGER PRIMARY KEY CHECK (id = 1), practice_position INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (operation TEXT PRIMARY KEY, band_index INTEGER NOT NULL, band_started_practice_position INTEGER NOT NULL);
                INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
                CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL, practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER, resolved_role TEXT, operation_band_before INTEGER, is_interrupted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var migratedStore = new SqliteLearnerStore(pathMigrated))
        {
            await migratedStore.InitializeAsync();
        }

        using var store1 = new SqliteLearnerStore(pathFresh);
        using var store2 = new SqliteLearnerStore(pathMigrated);

        var snap1 = await store1.LoadSnapshotAsync();
        var snap2 = await store2.LoadSnapshotAsync();

        Assert.Equal(snap1.SchemaVersion, snap2.SchemaVersion);
        Assert.Equal(snap1.Progression.CurriculumStage, snap2.Progression.CurriculumStage);
        Assert.Equal(snap1.Progression.PracticePosition, snap2.Progression.PracticePosition);
    }

    // =========================================================================
    // GUIDED G3 HARDENING ACROSS STAGES (G3A - G3E)
    // =========================================================================

    [Fact]
    public async Task G3A_CurriculumManaged_Stage3_MulBelowBand3_RespectsAdditionCeiling()
    {
        var path = GetDatabasePath("g3a");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0); // Addition ceiling = 1
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 20; i++)
        {
            var gate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(gate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }
    }

    [Fact]
    public async Task G3B_CurriculumManaged_MulBand3OrAbove_DecouplesFromAdditionCeiling()
    {
        var path = GetDatabasePath("g3b");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0); // Addition ceiling = 1
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 3, 0); // Decoupled
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var seenDecoupledFact = false;
        for (var i = 0; i < 30; i++)
        {
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication &&
                (session.CurrentFact.LeftOperand > 1 || session.CurrentFact.RightOperand > 1))
            {
                seenDecoupledFact = true;
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        Assert.True(seenDecoupledFact, "Multiplication band 3+ should decouple from Addition ceiling.");
    }

    [Fact]
    public async Task G3C_CurriculumManaged_Stage4_DivBelowBand3_RespectsAdditionCeiling()
    {
        var path = GetDatabasePath("g3c");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Division] = new OperationProgression(ArithmeticOperation.Division, 0, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var additionCurriculum = new ArithmeticCurriculum().GetCurriculum(ArithmeticOperation.Addition);
        for (var i = 0; i < 20; i++)
        {
            var gate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, session.Progression.OperationProgressions);
            Assert.True(gate.Allows(session.CurrentFact), $"Fact {session.CurrentFact.Id} must be allowed by Guided gate.");
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }
    }

    [Fact]
    public async Task G3D_CurriculumManaged_DivBand3OrAbove_DecouplesFromAdditionCeiling()
    {
        var path = GetDatabasePath("g3d");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0); // Addition ceiling = 1
        prog.OperationProgressions[ArithmeticOperation.Division] = new OperationProgression(ArithmeticOperation.Division, 3, 0); // Decoupled
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var seenDecoupledFact = false;
        for (var i = 0; i < 30; i++)
        {
            if (session.CurrentFact.Operation == ArithmeticOperation.Division &&
                (session.CurrentFact.LeftOperand > 1 || session.CurrentFact.RightOperand > 1))
            {
                seenDecoupledFact = true;
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        Assert.True(seenDecoupledFact, "Division band 3+ should decouple from Addition ceiling.");
    }

    [Fact]
    public async Task G3E_Custom3OperationSubset_RemainsUnrestricted()
    {
        var path = GetDatabasePath("g3e");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prefStore = new StubPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication]);

        var prog = LearnerProgression.CreateFresh();
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        prog.OperationProgressions[ArithmeticOperation.Multiplication] = new OperationProgression(ArithmeticOperation.Multiplication, 2, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync(startTiming: false);

        var seenMulOperandsGreaterThan1 = false;
        for (var i = 0; i < 30; i++)
        {
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication &&
                (session.CurrentFact.LeftOperand > 1 || session.CurrentFact.RightOperand > 1))
            {
                seenMulOperandsGreaterThan1 = true;
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        Assert.True(seenMulOperandsGreaterThan1, "Custom 3-operation subset should remain unrestricted.");
    }

    // =========================================================================
    // BROAD WEAKNESS / REMEDIATION INTERACTION (BW01 - BW07)
    // =========================================================================

    [Fact]
    public void BW01_OneRemediationItemAcrossActiveOperations_ReturnsFalse()
    {
        var curriculum = new ArithmeticCurriculum();
        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true)
        };
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, GuidedNumberSpaceGate.Unrestricted));
    }

    [Fact]
    public void BW02_TwoEligibleRemediationItems_ReturnsTrue()
    {
        var curriculum = new ArithmeticCurriculum();
        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, needsRemediation: true)
        };
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();

        Assert.True(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, GuidedNumberSpaceGate.Unrestricted));
    }

    [Fact]
    public void BW03_TwoWeakFacts_WhereOneBelongsToLockedOperation_OnlyActiveEligibleCounts()
    {
        var curriculum = new ArithmeticCurriculum();
        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 0, 0, needsRemediation: true)
        };
        // Multiplication is locked (only Addition is active)
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, GuidedNumberSpaceGate.Unrestricted));
    }

    [Fact]
    public void BW04_TwoPersistedFacts_WhereOneIsNotOwnershipEligible_OnlyEligibleCounts()
    {
        var curriculum = new ArithmeticCurriculum();
        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 9, 9, needsRemediation: true) // band 9 not owned at band 0
        };
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions(); // Addition BandIndex = 0

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, GuidedNumberSpaceGate.Unrestricted));
    }

    [Fact]
    public void BW05_FactDisallowedByGuidedGate_DoesNotContribute()
    {
        var curriculum = new ArithmeticCurriculum();
        var gate = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, unlockedAdditionBandIndex: 0, multiplicationBandIndex: 0, divisionBandIndex: 0);

        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 5, 5, needsRemediation: true)
        };
        var activeOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Multiplication };
        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 4, 0)
        };

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, gate));
    }

    [Fact]
    public void BW06_ResolvingOneOfTwoWeaknesses_ClearsBroadWeakness()
    {
        var curriculum = new ArithmeticCurriculum();
        var items = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: false),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, needsRemediation: true)
        };
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();

        Assert.False(BroadWeaknessPolicy.HasBroadWeakness(items, activeOps, progressions, curriculum, GuidedNumberSpaceGate.Unrestricted));
    }

    [Fact]
    public void BW07_BroadWeakness_BlocksAdvancementWithoutRelockingStage()
    {
        var dict = new Dictionary<string, ItemLearningState>
        {
            ["add:0+0"] = CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            ["add:0+1"] = CreateItemState(ArithmeticOperation.Addition, 0, 1, needsRemediation: true),
            ["add:1+0"] = CreateItemState(ArithmeticOperation.Addition, 1, 0, needsRemediation: false),
            ["add:1+1"] = CreateItemState(ArithmeticOperation.Addition, 1, 1, needsRemediation: false),
        };

        var nextStage = CurriculumUnlockPolicy.EvaluateNextStage(
            CurriculumStage.Stage2_Subtraction,
            dict,
            hasBroadWeakness: true);

        // Does not advance to Stage 3, but also NEVER regresses to Stage 1
        Assert.Equal(CurriculumStage.Stage2_Subtraction, nextStage);
    }

    // =========================================================================
    // PERSIST01 - PERSIST10: PERSISTENCE & RESET CONTRACTS
    // =========================================================================

    [Fact]
    public async Task PERSIST01_CurriculumStage_SurvivesNormalRestart()
    {
        var path = GetDatabasePath("persist01");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var prog = LearnerProgression.CreateFresh();
            prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
            await store.SaveProgressionAsync(prog);
        }

        using (var reopened = new SqliteLearnerStore(path))
        {
            await reopened.InitializeAsync();
            var snapshot = await reopened.LoadSnapshotAsync();
            Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot.Progression.CurriculumStage);
        }
    }

    [Fact]
    public async Task PERSIST02_StoreRevision_ChangesExactlyAccordingToAcceptedSubmissionContract()
    {
        var path = GetDatabasePath("persist02");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(1, session.Progression.StoreRevision);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var res1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);
        Assert.Equal(2, session.Progression.StoreRevision);
    }

    [Fact]
    public async Task PERSIST03_To_PERSIST05_FailedCommit_DoesNotAdvanceStagePositionOrHistory()
    {
        var path = GetDatabasePath("persist03_05");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.PracticePosition = 5;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Invalidate revision
        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_info SET value = '99' WHERE key = 'store_revision';";
            await cmd.ExecuteNonQueryAsync();
        }

        var res = await session.CommitCurrentEvaluationAsync();
        Assert.False(res.IsSuccess);

        // PERSIST03: Live stage unchanged
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        // PERSIST04: Live position unchanged
        Assert.Equal(5, session.Progression.PracticePosition);

        // PERSIST05: No attempt history created
        await using (var verifyConn = new SqliteConnection($"Data Source={path}"))
        {
            await verifyConn.OpenAsync();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM attempt_history WHERE practice_position = 6;";
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task PERSIST06_IdempotentSubmission_DoesNotDuplicateStageTransition()
    {
        var path = GetDatabasePath("persist06");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var res1 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res1.IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);

        // Replaying same submission does not double-advance
        var res2 = await session.CommitCurrentEvaluationAsync();
        Assert.True(res2.IsSuccess);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task PERSIST07_SchemaV9_RemainsStableAfterRepeatedInitialization()
    {
        var path = GetDatabasePath("persist07");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        await store.InitializeAsync();
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(9, snapshot.SchemaVersion);
    }

    [Fact]
    public async Task PERSIST08_ResetLearningProgress_ReturnsCurriculumStage1()
    {
        var path = GetDatabasePath("persist08");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage4_Division;
        prog.PracticePosition = 100;
        await store.SaveProgressionAsync(prog);

        await store.ResetLearningProgressAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(0, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task PERSIST09_ResetPracticePreferences_LeavesCurriculumStageUnchanged()
    {
        var path = GetDatabasePath("persist09");
        var prefs = new StubPreferenceStore();
        prefs.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, preferenceStore: prefs, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

        prefs.ResetPracticePreferences();
        await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
    }

    [Fact]
    public async Task PERSIST10_FullLocalReset_CreatesFreshV9Stage1()
    {
        var path = GetDatabasePath("persist10");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var prog = LearnerProgression.CreateFresh();
            prog.CurriculumStage = CurriculumStage.Stage3_Multiplication;
            prog.PracticePosition = 50;
            await store.SaveProgressionAsync(prog);

            await store.ResetLearningProgressAsync();

            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
            Assert.Equal(0, snapshot.Progression.PracticePosition);
        }
    }

    // =========================================================================
    // TIMING & INTERRUPTION NON-INTERFERENCE
    // =========================================================================

    [Fact]
    public async Task TimingAndInterruption_DoNotPreventCurriculumUnlockProgression()
    {
        var path = GetDatabasePath("timing_unlock");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage1_Addition;
        prog.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, 1, 0);
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // An interrupted correct attempt with long latency still unlocks Stage 2 mathematically
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var eval = session.LastEvaluation!;
        Assert.Equal(CurriculumStage.Stage2_Subtraction, eval.ChangeSet!.UpdatedProgression.CurriculumStage);

        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    private static ItemLearningState CreateItemState(
        ArithmeticOperation operation,
        int left,
        int right,
        bool needsRemediation = false)
    {
        var fact = new ArithmeticFact(operation, left, right);
        var state = ItemLearningState.CreateNew(fact);
        state.TotalAttempts = 1;
        state.NeedsRemediation = needsRemediation;
        return state;
    }

    private static Dictionary<ArithmeticOperation, OperationProgression> CreateDefaultProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));
}
