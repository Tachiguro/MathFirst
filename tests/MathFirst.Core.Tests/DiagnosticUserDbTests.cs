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
using Xunit.Abstractions;

public sealed class DiagnosticUserDbTests
{
    private readonly ITestOutputHelper _output;

    public DiagnosticUserDbTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DiagnoseRealUserDatabaseCopy()
    {
        var scratchDb = @"C:\Users\Herzo\.gemini\antigravity\brain\a9bd6458-5115-49d5-8fa1-61f204018672\scratch\real_user_db\mathfirst_learner.db";
        if (!File.Exists(scratchDb))
        {
            _output.WriteLine("Scratch DB not found");
            return;
        }

        var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_diag_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(scratchDb, testDb, overwrite: true);
        if (File.Exists(scratchDb + "-wal"))
        {
            File.Copy(scratchDb + "-wal", testDb + "-wal", overwrite: true);
        }
        if (File.Exists(scratchDb + "-shm"))
        {
            File.Copy(scratchDb + "-shm", testDb + "-shm", overwrite: true);
        }

        try
        {
            using (var conn = new SqliteConnection($"Data Source={testDb}"))
            {
                await conn.OpenAsync();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT key, value FROM schema_info;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== SCHEMA INFO ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  {reader.GetString(0)} = {reader.GetString(1)}");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, practice_position, updated_at FROM learner_progression;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== LEARNER PROGRESSION ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  id={reader.GetInt64(0)}, pos={reader.GetInt64(1)}, updated_at={reader.GetString(2)}");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT operation, band_index, band_started_practice_position FROM operation_progression ORDER BY operation;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== OPERATION PROGRESSION ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  op={reader.GetString(0)} band_index={reader.GetInt32(1)} band_started_pos={reader.GetInt64(2)}");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM item_learning_state;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== ALL ITEM LEARNING STATES ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  fact={reader.GetString(0)} op={reader.GetString(1)} total={reader.GetInt32(4)} corr={reader.GetInt32(5)} inc={reader.GetInt32(6)}");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM fsrs_card_state;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== ALL FSRS CARD STATES ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  fact={reader.GetString(0)} state={reader.GetInt32(2)} due_pos={reader.GetInt64(6)} last_review_pos={(reader.IsDBNull(7) ? "null" : reader.GetInt64(7).ToString())}");
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, practice_position, timestamp FROM attempt_history ORDER BY practice_position ASC;";
                    using var reader = await cmd.ExecuteReaderAsync();
                    _output.WriteLine("=== ALL ATTEMPTS IN DB ===");
                    while (await reader.ReadAsync())
                    {
                        _output.WriteLine($"  subId={reader.GetString(0)} fact={reader.GetString(1)} op={reader.GetString(2)} {reader.GetInt32(3)}+{reader.GetInt32(4)} sub={reader.GetInt32(5)} corr={reader.GetInt32(6)} outcome={reader.GetString(9)} lat={reader.GetInt64(10)} pos={reader.GetInt64(11)} time={reader.GetString(12)}");
                    }
                }
            }

            using var store = new SqliteLearnerStore(testDb);
            await store.InitializeAsync();
            var snapshot = await store.LoadRuntimeSnapshotAsync();
            _output.WriteLine($"Initial snapshot: Pos={snapshot.Progression.PracticePosition}, Revision={snapshot.Progression.StoreRevision}");
            if (snapshot.OperationProgressions is not null)
            {
                foreach (var op in snapshot.OperationProgressions)
                {
                    _output.WriteLine($"  Snapshot Op={op.Key}, BandIndex={op.Value.BandIndex}, BandStartedPos={op.Value.BandStartedPracticePosition}");
                }
            }

            var prefStore = new DiagPreferenceStore();
            prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
            prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

        }
        finally
        {
            CleanupDb(testDb);
        }
    }

    [Fact]
    public async Task DiagnoseAllInteractionPathsWithRealUserDbCopy()
    {
        var scratchDb = @"C:\Users\Herzo\.gemini\antigravity\brain\a9bd6458-5115-49d5-8fa1-61f204018672\scratch\real_user_db\mathfirst_learner.db";
        if (!File.Exists(scratchDb))
        {
            _output.WriteLine("Scratch DB not found");
            return;
        }

        // Test 1: Start from real DB copy, test timeout on next fact
        {
            var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_diag_timeout_" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(scratchDb, testDb, true);
            if (File.Exists(scratchDb + "-wal")) File.Copy(scratchDb + "-wal", testDb + "-wal", true);
            if (File.Exists(scratchDb + "-shm")) File.Copy(scratchDb + "-shm", testDb + "-shm", true);

            try
            {
                using var store = new SqliteLearnerStore(testDb);
                await store.InitializeAsync();
                var prefStore = new DiagPreferenceStore();
                prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
                prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

                var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
                await session.InitializeAsync(startTiming: false);
                _output.WriteLine($"Timeout test: Pos={session.Progression.PracticePosition}, Fact={session.CurrentFact.Id}");

                // Record timeout
                session.RecordTimeout();
                var res = await session.CommitCurrentEvaluationAsync();
                _output.WriteLine($"Timeout commit: IsSuccess={res.IsSuccess}, Status={res.Status}, Msg={res.Message}");
                Assert.True(res.IsSuccess);
            }
            finally
            {
                CleanupDb(testDb);
            }
        }

        // Test 2: Start from real DB copy, test incorrect answer on next fact
        {
            var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_diag_incorrect_" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(scratchDb, testDb, true);
            if (File.Exists(scratchDb + "-wal")) File.Copy(scratchDb + "-wal", testDb + "-wal", true);
            if (File.Exists(scratchDb + "-shm")) File.Copy(scratchDb + "-shm", testDb + "-shm", true);

            try
            {
                using var store = new SqliteLearnerStore(testDb);
                await store.InitializeAsync();
                var prefStore = new DiagPreferenceStore();
                prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
                prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

                var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
                await session.InitializeAsync(startTiming: false);
                _output.WriteLine($"Incorrect test: Pos={session.Progression.PracticePosition}, Fact={session.CurrentFact.Id}");

                // Submit incorrect
                session.SubmitAnswer(session.CurrentFact.CorrectResult + 5);
                var res = await session.CommitCurrentEvaluationAsync();
                _output.WriteLine($"Incorrect commit: IsSuccess={res.IsSuccess}, Status={res.Status}, Msg={res.Message}");
                Assert.True(res.IsSuccess);
            }
            finally
            {
                CleanupDb(testDb);
            }
        }

        // Test 3: Start from real DB copy, test switching preferences in different states
        {
            var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_diag_switch_" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(scratchDb, testDb, true);
            if (File.Exists(scratchDb + "-wal")) File.Copy(scratchDb + "-wal", testDb + "-wal", true);
            if (File.Exists(scratchDb + "-shm")) File.Copy(scratchDb + "-shm", testDb + "-shm", true);

            try
            {
                using var store = new SqliteLearnerStore(testDb);
                await store.InitializeAsync();
                var prefStore = new DiagPreferenceStore();
                prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

                var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
                await session.InitializeAsync(startTiming: false);

                // Switch to Addition only while AwaitingAnswer
                prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
                prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
                prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

                var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                _output.WriteLine($"Switch in AwaitingAnswer commit: IsSuccess={res.IsSuccess}, Status={res.Status}, Msg={res.Message}");
                Assert.True(res.IsSuccess);
            }
            finally
            {
                CleanupDb(testDb);
            }
        }
    }

    [Fact]
    public async Task DisabledOperationEvidenceFailure_MustNotBlockAdditionOnlyPractice()
    {
        var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_inject_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var realStore = new SqliteLearnerStore(testDb);
            await realStore.InitializeAsync();

            var failingStore = new SubtractionFailingLearnerStore(realStore);
            var prefStore = new DiagPreferenceStore();
            prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
            prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

            var session = new TrainingSession(failingStore, new FixedClock(), preferenceStore: prefStore);
            await session.InitializeAsync(startTiming: false);

            // Attempt 1: Addition -> correct
            var fact = session.CurrentFact;
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            session.SubmitAnswer(fact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();

            Assert.True(res.IsSuccess, $"Commit failed: {res.Message}");
            Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
            var advanced = session.AdvanceAfterCorrectAnswer(startTiming: false);
            Assert.True(advanced);
            Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        }
        finally
        {
            CleanupDb(testDb);
        }
    }

    private sealed class SubtractionFailingLearnerStore : ILearnerStore
    {
        private readonly ILearnerStore _inner;

        public SubtractionFailingLearnerStore(ILearnerStore inner)
        {
            _inner = inner;
        }

        public string StoragePath => _inner.StoragePath;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => _inner.InitializeAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) => _inner.LoadRuntimeSnapshotAsync(cancellationToken);
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => _inner.LoadSnapshotAsync(cancellationToken);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            _inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Operation == ArithmeticOperation.Subtraction)
            {
                throw new InvalidOperationException("Simulated failure loading Subtraction evidence.");
            }
            return _inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);
        }

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            _inner.CommitSubmissionAsync(changeSet, cancellationToken);
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => _inner.ResetLearningProgressAsync(cancellationToken);
        public Task CloseAsync(CancellationToken cancellationToken = default) => _inner.CloseAsync(cancellationToken);
        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public async Task ScenarioA_LongAllFourHistory_ThenSwitchToAdditionOnly()
    {
        var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_scenA_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var store = new SqliteLearnerStore(testDb);
            await store.InitializeAsync();
            var prefStore = new DiagPreferenceStore();
            prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

            var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
            await session.InitializeAsync(startTiming: false);

            // 200 all-four attempts
            for (int i = 0; i < 200; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                session.SubmitAnswer(fact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess, $"Failed at attempt {i + 1}: {res.Message}");
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
            }

            // Switch to Addition only
            prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
            prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

            // Run 200 Addition-only attempts
            for (int i = 0; i < 200; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                session.SubmitAnswer(fact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess, $"Failed in Addition-only at attempt {i + 1}: {res.Message}");
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
            }
        }
        finally
        {
            CleanupDb(testDb);
        }
    }

    [Fact]
    public async Task ScenarioD_DisabledOperationAtDenseBand_SwitchToAdditionOnly()
    {
        var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_scenD_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var store = new SqliteLearnerStore(testDb);
            await store.InitializeAsync();
            var prefStore = new DiagPreferenceStore();
            prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

            var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
            await session.InitializeAsync(startTiming: false);

            // Advance through 500 attempts to get operations to dense bands
            for (int i = 0; i < 400; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                session.SubmitAnswer(fact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess, $"Failed at attempt {i + 1}: {res.Message}");
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
            }

            // Now switch to Addition only
            prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
            prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

            for (int i = 0; i < 200; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                session.SubmitAnswer(fact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess, $"Failed in Addition-only at attempt {i + 1}: {res.Message}");
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
            }
        }
        finally
        {
            CleanupDb(testDb);
        }
    }

    [Fact]
    public async Task ScenarioE_DisabledOperationWithDueFsrsItems_SwitchToAdditionOnly()
    {
        var testDb = Path.Combine(Path.GetTempPath(), "mathfirst_scenE_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var store = new SqliteLearnerStore(testDb);
            await store.InitializeAsync();
            var prefStore = new DiagPreferenceStore();
            prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

            var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
            await session.InitializeAsync(startTiming: false);

            // Run some attempts with mistakes on Subtraction to create FSRS review schedules
            for (int i = 0; i < 50; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                // Give incorrect answer on Subtraction
                if (fact.Operation == ArithmeticOperation.Subtraction)
                {
                    session.SubmitAnswer(fact.CorrectResult + 1);
                }
                else
                {
                    session.SubmitAnswer(fact.CorrectResult);
                }
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess);
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
                else
                {
                    session.AcknowledgeFeedback(startTiming: false);
                }
            }

            // Switch to Addition only
            prefStore.SetOperationEnabled(ArithmeticOperation.Addition, true);
            prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
            prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

            for (int i = 0; i < 200; i++)
            {
                if (session.InteractionState == SessionInteractionState.SessionCheckIn)
                {
                    session.ContinuePractice(startTiming: false);
                }
                var fact = session.CurrentFact;
                if (i > 0)
                {
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                }
                session.SubmitAnswer(fact.CorrectResult);
                var res = await session.CommitCurrentEvaluationAsync();
                Assert.True(res.IsSuccess, $"Failed at attempt {i + 1}: {res.Message} (Status={res.Status})");
                if (session.InteractionState == SessionInteractionState.CorrectFeedback)
                {
                    session.AdvanceAfterCorrectAnswer(startTiming: false);
                }
            }
        }
        finally
        {
            CleanupDb(testDb);
        }
    }

    private static void CleanupDb(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
        catch { }
    }

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed class DiagPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operationPreferences = [];

        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operationPreferences.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operationPreferences[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            _operationPreferences.Clear();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                _operationPreferences[op] = false;
            }
            foreach (var op in operations)
            {
                _operationPreferences[op] = true;
            }
        }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }
}