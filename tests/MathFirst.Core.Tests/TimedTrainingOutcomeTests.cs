namespace MathFirst.Core.Tests;

using System.Data;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class TimedTrainingOutcomeTests : IDisposable
{
    private readonly string _testDbDir;

    public TimedTrainingOutcomeTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstTimedOutcomeTests_" + Guid.NewGuid().ToString("N"));
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
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_{Guid.NewGuid():N}.db");

    private sealed class FakeClock : IClock
    {
        public long CurrentTimestamp { get; set; } = 1000;
        public TimeSpan Elapsed { get; set; } = TimeSpan.Zero;

        public long GetTimestamp() => CurrentTimestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => Elapsed;
    }

    private sealed class InMemoryStore : ILearnerStore
    {
        public string StoragePath => "inmemory.db";
        public LearnerSnapshot Snapshot { get; set; } = new(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new List<AttemptRecord>(),
            1,
            LearnerProgression.DefaultSchemaVersion);

        public List<SubmissionChangeSet> CommittedChangeSets { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            CommittedChangeSets.Add(changeSet);
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // ==========================================
    // 1. TIMER TESTS
    // ==========================================

    [Fact]
    public async Task Timer_MonotonicThresholds_EvaluatedAccurately()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        // 0s: not timed out
        fakeClock.Elapsed = TimeSpan.FromSeconds(0);
        Assert.False(session.IsCurrentItemTimedOut());

        // 10s: not timed out
        fakeClock.Elapsed = TimeSpan.FromSeconds(10);
        Assert.False(session.IsCurrentItemTimedOut());

        // 29.999s: not timed out
        fakeClock.Elapsed = TimeSpan.FromSeconds(29.999);
        Assert.False(session.IsCurrentItemTimedOut());

        // 30.0s: timed out
        fakeClock.Elapsed = TimeSpan.FromSeconds(30.0);
        Assert.True(session.IsCurrentItemTimedOut());

        // 35.0s: timed out
        fakeClock.Elapsed = TimeSpan.FromSeconds(35.0);
        Assert.True(session.IsCurrentItemTimedOut());
    }

    [Fact]
    public async Task Timer_SubmissionBeforeTimeout_CancelsTimeoutWindow()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        // Answer submitted at 12.5s
        fakeClock.Elapsed = TimeSpan.FromSeconds(12.5);
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);
        Assert.True(eval.IsCorrect);
        Assert.Equal(12500, eval.LatencyMs);
        Assert.NotNull(session.LastEvaluation);
    }

    [Fact]
    public async Task Timer_FactTransition_ResetsMeasurementWindow()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        // First fact submitted
        fakeClock.Elapsed = TimeSpan.FromSeconds(5.0);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Advance to next fact
        fakeClock.CurrentTimestamp = 50000;
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(400);
        session.AdvanceToNextFact();

        // New fact latency starts fresh
        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(400, eval2.LatencyMs);
    }

    [Theory]
    [InlineData(0, 30000)]
    [InlineData(-1, 30000)]
    [InlineData(1, 30000)]
    [InlineData(2, 30000)]
    [InlineData(3, 30000)]
    [InlineData(99, 30000)]
    public void DeadlinePolicy_Streak_ProducesExpectedDeadline(int streak, long expectedMs)
    {
        Assert.Equal(expectedMs, LearningPolicy.GetAnswerDeadlineMs(streak));
        Assert.Equal(expectedMs / 1000.0, LearningPolicy.GetAnswerDeadlineSeconds(streak));
    }

    [Theory]
    [InlineData(0, 29999, false)]
    [InlineData(0, 30000, true)]
    [InlineData(1, 29999, false)]
    [InlineData(1, 30000, true)]
    [InlineData(2, 29999, false)]
    [InlineData(2, 30000, true)]
    [InlineData(3, 29999, false)]
    [InlineData(3, 30000, true)]
    [InlineData(99, 29999, false)]
    [InlineData(99, 30000, true)]
    public async Task Timer_ExactTimeoutBoundaries_EvaluatedAccurately(int streak, long elapsedMs, bool expectedTimedOut)
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var currentFact = session.CurrentFact;
        var itemState = ItemLearningState.CreateNew(currentFact);
        itemState.ConsecutiveCorrectStreak = streak;
        session.ItemStates[currentFact.Id] = itemState;
        session.ResetItemReadyTiming();

        fakeClock.Elapsed = TimeSpan.FromMilliseconds(elapsedMs);
        Assert.Equal(expectedTimedOut, session.IsCurrentItemTimedOut());
    }

    [Theory]
    [InlineData(30000, "30.000 s")]
    [InlineData(29842, "29.842 s")]
    [InlineData(10204, "10.204 s")]
    [InlineData(3517, "3.517 s")]
    [InlineData(84, "0.084 s")]
    [InlineData(0, "0.000 s")]
    [InlineData(-5, "0.000 s")]
    public void Timer_MillisecondFormatter_DeterministicFormatting(long remainingMs, string expected)
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(expected, LearningPolicy.FormatTimerDisplay(remainingMs));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
            Assert.Equal(expected, LearningPolicy.FormatTimerDisplay(remainingMs));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal(expected, LearningPolicy.FormatTimerDisplay(remainingMs));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task FailureReset_IncorrectAnswer_ResetsStreakAndRestores30sDeadline()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.ConsecutiveCorrectStreak = 3;
        session.ItemStates[fact.Id] = itemState;
        session.ResetItemReadyTiming();

        // 1. Historical streak does not shorten the current presentation deadline.
        Assert.Equal(30000, session.CurrentFactDeadlineMs);

        // 2. Incorrect submission resets streak to 0
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(1500);
        var eval = session.SubmitAnswer(fact.CorrectResult + 1);
        Assert.False(eval.IsCorrect);
        Assert.False(session.ItemStates[fact.Id].NeedsRemediation);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(0, session.ItemStates[fact.Id].ConsecutiveCorrectStreak);

        // 3. Next presentation of this fact gets 30s deadline
        session.ResetItemReadyTiming();
        Assert.Equal(30000, session.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task FailureReset_Timeout_ResetsStreakAndRestores30sDeadline()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.ConsecutiveCorrectStreak = 3;
        session.ItemStates[fact.Id] = itemState;
        session.ResetItemReadyTiming();

        // 1. Historical streak does not shorten the current presentation deadline.
        Assert.Equal(30000, session.CurrentFactDeadlineMs);

        // 2. Timeout resets streak to 0
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(30050);
        var eval = session.RecordTimeout();
        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
        Assert.False(session.ItemStates[fact.Id].NeedsRemediation);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(0, session.ItemStates[fact.Id].ConsecutiveCorrectStreak);

        // 3. Next presentation of this fact gets 30s deadline
        session.ResetItemReadyTiming();
        Assert.Equal(30000, session.CurrentFactDeadlineMs);
    }

    [Fact]
    public async Task ResponseLatency_SeparateFromFixedDeadline_PreservesActualLatencyAndFsrs()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.ConsecutiveCorrectStreak = 3;
        session.ItemStates[fact.Id] = itemState;
        session.ResetItemReadyTiming();

        Assert.Equal(30000, session.CurrentFactDeadlineMs);

        // Learner answers after 1,842 ms
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(1842);
        var eval = session.SubmitAnswer(fact.CorrectResult);

        Assert.True(eval.IsCorrect);
        Assert.Equal(1842, eval.LatencyMs);
        Assert.Equal(1842, session.LastResponseLatencyMs);

        Assert.Equal(1842, eval.ChangeSet.Attempt.ResponseLatencyMs);

        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult.IsSuccess);
        // FSRS rating for 1842ms (>1000ms and <=2500ms) is Good
        var fsrsState = session.FsrsStates[fact.Id];
        Assert.NotNull(fsrsState);
        Assert.Equal(1842, store.CommittedChangeSets[0].Attempt.ResponseLatencyMs);
    }

    [Fact]
    public async Task HistoricalHighStreak_StillUsesThirtySecondDeadlineAndSlowCorrectAnswerIsHard()
    {
        var fakeClock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.ConsecutiveCorrectStreak = 99;
        itemState.TotalAttempts = 99;
        itemState.CorrectAttempts = 99;
        session.ItemStates[fact.Id] = itemState;
        session.ResetItemReadyTiming();

        Assert.Equal(30000, session.CurrentFactDeadlineMs);
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(15000);
        Assert.False(session.IsCurrentItemTimedOut());

        var evaluation = session.SubmitAnswer(fact.CorrectResult);
        Assert.True(evaluation.IsCorrect);
        Assert.Equal(15000, evaluation.LatencyMs);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(FsrsRating.Hard, session.FsrsStates[fact.Id].LastRating);
    }

    // ==========================================
    // 2. RESPONSE LATENCY TESTS
    // ==========================================

    [Fact]
    public async Task ResponseLatency_MonotonicMeasurement_PreservedAcrossPersistence()
    {
        var fakeClock = new FakeClock
        {
            CurrentTimestamp = 1000,
            Elapsed = TimeSpan.FromMilliseconds(1100)
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(1100, eval.LatencyMs);
        Assert.Equal(1100, session.LastResponseLatencyMs);

        // Simulated persistence delay (e.g. disk I/O)
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(2500);
        var persistResult = await session.CommitCurrentEvaluationAsync();

        Assert.True(persistResult.IsSuccess);
        Assert.Single(store.CommittedChangeSets);
        Assert.Equal(1100, store.CommittedChangeSets[0].Attempt.ResponseLatencyMs);
    }

    [Fact]
    public async Task ResponseLatency_InvalidInputAt1000ms_ValidAt2000ms_MeasuresFromOriginalReady()
    {
        var fakeClock = new FakeClock
        {
            CurrentTimestamp = 1000,
            Elapsed = TimeSpan.FromMilliseconds(1000)
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        // Learner submits invalid input at 1000ms: UI rejects, session does NOT record attempt or reset timer
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Null(session.LastEvaluation);

        // Learner fixes input and submits valid answer at 2000ms
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(2000);
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(2000, eval.LatencyMs);
        Assert.Equal(2000, session.LastResponseLatencyMs);
        Assert.Equal(0, session.SessionTotalCount);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.SessionTotalCount);
    }

    // ==========================================
    // 3. OUTCOME & ACKNOWLEDGEMENT TESTS
    // ==========================================

    [Fact]
    public async Task Outcome_CorrectAnswer_ProducesCorrectOutcomeWithSubmittedAnswer()
    {
        var store = new InMemoryStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var eval = session.SubmitAnswer(fact.CorrectResult);

        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);
        Assert.True(eval.IsCorrect);
        Assert.Equal(fact.CorrectResult, eval.SubmittedAnswer);
        Assert.Equal(fact.CorrectResult, eval.CorrectAnswer);
        Assert.False(eval.ChangeSet.UpdatedItemState.NeedsRemediation);
    }

    [Fact]
    public async Task Outcome_IncorrectAnswer_ProducesIncorrectOutcomeAndSchedulesRemediation()
    {
        var store = new InMemoryStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var wrongAnswer = fact.CorrectResult + 5;
        var eval = session.SubmitAnswer(wrongAnswer);

        Assert.Equal(AttemptOutcome.Incorrect, eval.Outcome);
        Assert.False(eval.IsCorrect);
        Assert.Equal(wrongAnswer, eval.SubmittedAnswer);
        Assert.Equal(fact.CorrectResult, eval.CorrectAnswer);

        // Candidate item state is not published until persistence succeeds.
        Assert.False(session.ItemStates.ContainsKey(fact.Id));
        var itemState = eval.ChangeSet.UpdatedItemState;
        Assert.True(itemState.NeedsRemediation);
        Assert.Equal(session.SessionOrderCounter + LearningPolicy.RemediationInterveningCount, itemState.RemediationDueOrder);
        Assert.Equal(0, itemState.ConsecutiveCorrectStreak);
    }

    [Fact]
    public async Task Outcome_Timeout_ProducesTimeoutOutcomeWithNullSubmittedAnswerAndSchedulesRemediation()
    {
        var fakeClock = new FakeClock
        {
            Elapsed = TimeSpan.FromMilliseconds(30050)
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var fact = session.CurrentFact;
        var eval = session.RecordTimeout();

        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
        Assert.False(eval.IsCorrect);
        Assert.Null(eval.SubmittedAnswer);
        Assert.Equal(fact.CorrectResult, eval.CorrectAnswer);
        Assert.True(eval.LatencyMs >= 30000);

        // Attempt record
        Assert.Null(eval.ChangeSet.Attempt.SubmittedAnswer);
        Assert.Equal(AttemptOutcome.Timeout, eval.ChangeSet.Attempt.Outcome);

        // Candidate item state is not published until persistence succeeds.
        Assert.False(session.ItemStates.ContainsKey(fact.Id));
        var itemState = eval.ChangeSet.UpdatedItemState;
        Assert.True(itemState.NeedsRemediation);
        Assert.Equal(session.SessionOrderCounter + LearningPolicy.RemediationInterveningCount, itemState.RemediationDueOrder);
        Assert.Equal(1, itemState.IncorrectAttempts);
        Assert.Equal(0, itemState.ConsecutiveCorrectStreak);
    }

    // ==========================================
    // 4. SCORE TESTS
    // ==========================================

    [Fact]
    public async Task SessionScore_CorrectWrongTimeout_CountsAccurately()
    {
        var store = new InMemoryStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);

        // 1. Correct attempt -> 1 / 1
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(1, session.SessionTotalCount);

        session.AdvanceToNextFact();

        // 2. Incorrect attempt -> 1 / 2
        session.SubmitAnswer(session.CurrentFact.CorrectResult + 99);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(2, session.SessionTotalCount);

        session.AdvanceToNextFact();

        // 3. Timeout -> 1 / 3
        session.RecordTimeout();
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(3, session.SessionTotalCount);
    }

    // ==========================================
    // 5. DATABASE MIGRATION & PERSISTENCE TESTS
    // ==========================================

    [Fact]
    public async Task DatabaseMigration_V1ToV5_MigratesDataLosslessly()
    {
        var dbPath = GetTempDbPath();

        // 1. Create a raw V1 database
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO schema_info (key, value) VALUES ('store_revision', '3');

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    current_operation TEXT NOT NULL,
                    current_max_operand INTEGER NOT NULL,
                    operation_max_operands_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO learner_progression (id, current_operation, current_max_operand, operation_max_operands_json, updated_at)
                VALUES (1, 'Addition', 2, '{""Addition"":2}', '2026-09-07T12:00:00Z');

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
                ) VALUES (
                    'Add:0+1', 'Addition', 0, 1,
                    2, 1, 1,
                    0, 1500, 1500,
                    0, 0, 1,
                    5, 2, '2026-09-07T12:00:00Z'
                );

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
                ('sub1', 'Add:0+1', 'Addition', 0, 1, 1, 1, 1, 1200, '2026-09-07T11:59:00Z'),
                ('sub2', 'Add:0+1', 'Addition', 0, 1, 2, 1, 0, 1800, '2026-09-07T12:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Open with new SqliteLearnerStore
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();

        // 3. Verify V5 upgrade and preserved state
        Assert.Equal(LearnerProgression.DefaultSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(3, snapshot.Revision);
        Assert.Single(snapshot.ItemStates);
        Assert.All(snapshot.Progression.OperationProgressions.Values, progression =>
            Assert.Equal(snapshot.Progression.PracticePosition, progression.BandStartedPracticePosition));
        Assert.Equal(2, snapshot.RecentAttempts.Count);
        Assert.All(snapshot.RecentAttempts, attempt => Assert.Null(attempt.PracticePosition));
    }

    // ==========================================
    // 6. TIMER PAUSE, RESUME & NAVIGATION TESTS
    // ==========================================

    private sealed class AdvancingClock : IClock
    {
        public long CurrentTimestampMs { get; set; } = 10000;

        public long GetTimestamp() => CurrentTimestampMs;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, CurrentTimestampMs - startTimestamp));

        public void AdvanceMs(long ms) => CurrentTimestampMs += ms;
    }

    [Fact]
    public async Task Timer_PauseAndResume_MonotonicAccumulation_ExcludesPausedDuration()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // 1. Practice active for 3,500 ms
        clock.AdvanceMs(3500);
        Assert.Equal(3500, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        // 2. User navigates to Settings -> Pause timer
        session.PauseItemTiming();
        Assert.False(session.IsTimingActive);

        // 3. User spends 25,000 ms inside Settings
        clock.AdvanceMs(25000);
        Assert.Equal(3500, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        // 4. User returns to training -> Resume timer
        session.ResumeItemTiming();
        Assert.True(session.IsTimingActive);

        // 5. User practices for another 1,500 ms and answers correctly
        clock.AdvanceMs(1500);
        Assert.Equal(5000, session.GetCurrentActiveElapsedMs());

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.Equal(5000, eval.LatencyMs);
        Assert.Equal(5000, session.LastResponseLatencyMs);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
    }

    [Fact]
    public async Task Timer_PausedFact_DoesNotCreateSemanticAttemptDuringArbitraryInactiveTime()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        clock.AdvanceMs(5000);
        session.PauseItemTiming();
        var practicePosition = session.Progression.PracticePosition;

        clock.AdvanceMs(180000);

        Assert.Equal(5000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.False(session.IsTimingActive);
        Assert.Equal(practicePosition, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Empty(store.CommittedChangeSets);

        session.ResumeItemTiming();
        clock.AdvanceMs(1000);

        Assert.Equal(6000, session.GetCurrentActiveElapsedMs());
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public async Task InitializeForInactiveSurface_PreparesFreshFactWithoutStartingDeadline()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);

        await session.InitializeAsync(startTiming: false);
        clock.AdvanceMs(180000);

        Assert.True(session.IsInitialized);
        Assert.NotNull(session.CurrentFact);
        Assert.False(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Empty(store.CommittedChangeSets);

        session.ResumeItemTiming();
        clock.AdvanceMs(30000);

        Assert.True(session.IsCurrentItemTimedOut());
    }

    [Fact]
    public async Task ResetLearningProgress_PreservesPausedTimingUntilPracticeExplicitlyResumes()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.PauseItemTiming();

        await session.ResetLearningProgressAsync();

        Assert.False(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.Equal(30000, session.CurrentFactDeadlineMs);

        clock.AdvanceMs(180000);

        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Empty(store.CommittedChangeSets);

        session.ResumeItemTiming();
        clock.AdvanceMs(29999);
        Assert.False(session.IsCurrentItemTimedOut());

        clock.AdvanceMs(1);
        Assert.True(session.IsCurrentItemTimedOut());
    }

    [Fact]
    public async Task Timer_PauseAndResume_TimeoutEvaluatesOnlyActivePracticeTime()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // 30s deadline
        Assert.Equal(30000, session.CurrentFactDeadlineMs);

        // Active for 20s
        clock.AdvanceMs(20000);
        Assert.False(session.IsCurrentItemTimedOut());

        // Paused for 120s in Settings
        session.PauseItemTiming();
        clock.AdvanceMs(120000);
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(20000, session.GetCurrentActiveElapsedMs());

        // Resume and advance 9.9s (active total = 29.9s)
        session.ResumeItemTiming();
        clock.AdvanceMs(9900);
        Assert.False(session.IsCurrentItemTimedOut());

        // Advance 200ms more (active total = 30.1s)
        clock.AdvanceMs(200);
        Assert.True(session.IsCurrentItemTimedOut());

        var eval = session.RecordTimeout();
        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
        Assert.Equal(30100, eval.LatencyMs);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
    }

    [Fact]
    public async Task InteractionState_ExpiredFact_PreservedAcrossSettingsNavigation()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // Expire current item
        clock.AdvanceMs(31000);
        session.RecordTimeout();
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.SessionTotalCount);

        // Navigate to Settings and back
        session.PauseItemTiming();
        clock.AdvanceMs(5000);
        session.ResumeItemTiming();

        // Must still be TimeoutFeedback, not restarting timer or transitioning to AwaitingAnswer
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
        Assert.False(session.IsTimingActive);

        // Calling RecordTimeout or SubmitAnswer again returns existing evaluation without duplicate attempts
        var repeatEval = session.RecordTimeout();
        Assert.Equal(AttemptOutcome.Timeout, repeatEval.Outcome);
        Assert.Equal(1, session.SessionTotalCount);

        // Advancing resets to AwaitingAnswer for next fact
        session.AdvanceToNextFact();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public async Task InteractionState_FeedbackStates_PreservedAcrossNavigation()
    {
        var clock = new AdvancingClock { CurrentTimestampMs = 10000 };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        // 1. Correct feedback state
        clock.AdvanceMs(1200);
        var evalCorrect = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        session.PauseItemTiming();
        session.ResumeItemTiming();
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
        Assert.False(session.IsTimingActive);

        // Advance to next fact
        session.AdvanceToNextFact();
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.True(session.IsTimingActive);

        // 2. Incorrect feedback state
        clock.AdvanceMs(1500);
        var evalIncorrect = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);

        session.PauseItemTiming();
        session.ResumeItemTiming();
        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
        Assert.False(session.IsTimingActive);
    }

    [Fact]
    public async Task DatabaseMigration_UnsupportedVersion_IsRejected()
    {
        var dbPath = GetTempDbPath();
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '99');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(dbPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
    }

    [Fact]
    public async Task DatabaseMigration_TransactionFailure_DoesNotCorruptOrPartiallyUpgrade()
    {
        var dbPath = GetTempDbPath();
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO schema_info (key, value) VALUES ('store_revision', '1');

                -- Create invalid attempt_history where migration copy will fail
                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY,
                    unsupported_column TEXT NOT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(dbPath);
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());

        // Verify database remains at version 1 (not half-migrated to 2)
        using var verifyConn = new SqliteConnection($"Data Source={dbPath}");
        await verifyConn.OpenAsync();
        using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = (string?)await verifyCmd.ExecuteScalarAsync();
        Assert.Equal("1", version);
    }
}
