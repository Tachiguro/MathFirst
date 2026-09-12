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
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RuntimePersistenceRegressionTests : IDisposable
{
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstRuntimePersistence_" + Guid.NewGuid().ToString("N"));

    public RuntimePersistenceRegressionTests()
    {
        Directory.CreateDirectory(_testDbDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetDatabasePath() => Path.Combine(_testDbDirectory, $"test_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task DisabledOperationEvidenceFailure_MustNotBlockAdditionOnlyPractice()
    {
        var testDb = GetDatabasePath();
        using var realStore = new SqliteLearnerStore(testDb);
        await realStore.InitializeAsync();

        var failingStore = new ConfigurableFailingLearnerStore(realStore)
        {
            FailSubtractionEvidence = true
        };
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

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

    [Fact]
    public async Task LongAllFourHistory_ThenSwitchToAdditionOnly()
    {
        var testDb = GetDatabasePath();
        using var store = new SqliteLearnerStore(testDb);
        await store.InitializeAsync();
        var prefStore = new TestPreferenceStore();
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
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

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

    [Fact]
    public async Task DisabledOperationAtDenseBand_SwitchToAdditionOnly()
    {
        var testDb = GetDatabasePath();
        using var store = new SqliteLearnerStore(testDb);
        await store.InitializeAsync();
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        // Advance through 400 attempts to get operations to dense bands
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
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

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

    [Fact]
    public async Task DisabledOperationWithDueFsrsItems_SwitchToAdditionOnly()
    {
        var testDb = GetDatabasePath();
        using var store = new SqliteLearnerStore(testDb);
        await store.InitializeAsync();
        var prefStore = new TestPreferenceStore();
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
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

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

    [Fact]
    public async Task AdditionOnly_LongRun_300Attempts()
    {
        var testDb = GetDatabasePath();
        using var store = new SqliteLearnerStore(testDb);
        await store.InitializeAsync();
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        for (int i = 0; i < 300; i++)
        {
            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                session.ContinuePractice(startTiming: false);
            }
            var fact = session.CurrentFact;
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            session.SubmitAnswer(fact.CorrectResult);
            var res = await session.CommitCurrentEvaluationAsync();
            Assert.True(res.IsSuccess, $"Failed at attempt {i + 1}: {res.Message}");
            if (session.InteractionState == SessionInteractionState.CorrectFeedback)
            {
                session.AdvanceAfterCorrectAnswer(startTiming: false);
            }
        }
    }

    [Fact]
    public async Task PostWriteEvidenceFailure_RetryIsIdempotentAndExactlyOnce()
    {
        var testDb = GetDatabasePath();
        using var realStore = new SqliteLearnerStore(testDb);
        await realStore.InitializeAsync();

        var failingStore = new ConfigurableFailingLearnerStore(realStore);
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

        var session = new TrainingSession(failingStore, new FixedClock(), preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        // 1. Submit first fact and trigger evidence preparation failure on post-write
        var fact1 = session.CurrentFact;
        session.SubmitAnswer(fact1.CorrectResult);

        // Configure store to fail during next evidence loading
        failingStore.FailAllEvidence = true;

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess, "Overall commit call should report failure when evidence prep fails.");
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.True(session.IsCurrentSubmissionCommitted, "Durable commit succeeded before evidence prep failed.");

        // 2. Verify DB state directly: durable commit succeeded exactly once
        using (var conn = new SqliteConnection($"Data Source={testDb}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MAX(practice_position) FROM attempt_history;";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0)); // Exactly one attempt record
            Assert.Equal(1, reader.GetInt64(1)); // Practice position 1
        }

        using (var conn = new SqliteConnection($"Data Source={testDb}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT practice_position FROM learner_progression WHERE id = 1;";
            var pos = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            Assert.Equal(1, pos);
        }

        // 3. Clear evidence failure and recover
        failingStore.FailAllEvidence = false;
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered, "Recovery should succeed once evidence loading is healthy.");
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        // 4. Verify DB state after recovery: NO duplicate attempt, NO extra position increment
        using (var conn = new SqliteConnection($"Data Source={testDb}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MAX(practice_position) FROM attempt_history;";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0)); // Still exactly one attempt record
            Assert.Equal(1, reader.GetInt64(1)); // Practice position 1
        }

        // 5. Advance to next fact and verify normal continuation
        var advanced = session.AdvanceAfterCorrectAnswer(startTiming: false);
        Assert.True(advanced);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Equal(1, session.Progression.PracticePosition);

        // 6. Submit second fact and verify it commits as attempt 2
        var fact2 = session.CurrentFact;
        session.SubmitAnswer(fact2.CorrectResult);
        var commit2 = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit2.IsSuccess);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        using (var conn = new SqliteConnection($"Data Source={testDb}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MAX(practice_position) FROM attempt_history;";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt64(0)); // Exactly two attempts
            Assert.Equal(2, reader.GetInt64(1)); // Practice position 2
        }
    }

    [Fact]
    public async Task AdditionOnlyToAdditionAndSubtraction_DeterministicSelectionWithoutFallback()
    {
        var testDb = GetDatabasePath();
        using var realStore = new SqliteLearnerStore(testDb);
        await realStore.InitializeAsync();

        var failingStore = new ConfigurableFailingLearnerStore(realStore)
        {
            FailSubtractionEvidence = true
        };
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

        var session = new TrainingSession(failingStore, new FixedClock(), preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        // Position 0 -> Practice fact 1 (Addition)
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Commit fact 1. Durable practice position becomes 1.
        // Prospective position for next fact is 2.
        // Subtraction evidence prefetch fails and is defensively ignored.
        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        // While in CorrectFeedback, change enabled operations to Addition + Subtraction.
        // For prospective position 2 and [Addition, Subtraction]:
        // GetScheduledOperation(2, [Addition, Subtraction]) == Subtraction.
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction]);

        // Enable Subtraction evidence in store
        failingStore.FailSubtractionEvidence = false;

        // Advance to next fact via async method
        var advanced = await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        Assert.True(advanced);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // CurrentFact MUST be Subtraction, deterministically scheduled
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
    }

    [Fact]
    public async Task AdditionOnlyToSubtractionOnly_ExclusiveSwitchWithMissingPriorCache()
    {
        var testDb = GetDatabasePath();
        using var realStore = new SqliteLearnerStore(testDb);
        await realStore.InitializeAsync();

        var failingStore = new ConfigurableFailingLearnerStore(realStore)
        {
            FailSubtractionEvidence = true
        };
        var prefStore = new TestPreferenceStore();
        prefStore.SetEnabledOperations([ArithmeticOperation.Addition]);

        var session = new TrainingSession(failingStore, new FixedClock(), preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        // Position 0 -> Practice fact 1 (Addition)
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Commit fact 1. Prospective position for next fact is 2.
        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        // While in CorrectFeedback, switch preferences exclusively to Subtraction-only.
        prefStore.SetEnabledOperations([ArithmeticOperation.Subtraction]);

        // Enable Subtraction evidence in store
        failingStore.FailSubtractionEvidence = false;

        // Advance via async method
        var advanced = await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        Assert.True(advanced);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // CurrentFact MUST be Subtraction
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
    }

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
    }

    private sealed class TestPreferenceStore : IPreferenceStore
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

    private sealed class ConfigurableFailingLearnerStore : ILearnerStore
    {
        private readonly ILearnerStore _inner;

        public ConfigurableFailingLearnerStore(ILearnerStore inner)
        {
            _inner = inner;
        }

        public bool FailSubtractionEvidence { get; set; }
        public bool FailAllEvidence { get; set; }

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
            if (FailAllEvidence)
            {
                throw new InvalidOperationException("Simulated failure loading selection evidence.");
            }

            if (FailSubtractionEvidence && request.Operation == ArithmeticOperation.Subtraction)
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
}
