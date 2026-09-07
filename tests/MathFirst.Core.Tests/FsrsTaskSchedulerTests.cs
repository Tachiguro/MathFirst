namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class FsrsTaskSchedulerTests : IDisposable
{
    private readonly string _testDbDir;

    public FsrsTaskSchedulerTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstFsrsTests_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_fsrs_{Guid.NewGuid():N}.db");

    // =========================================================================
    // 1. CONFIGURATION & CONTRACT TESTS
    // =========================================================================

    [Fact]
    public void FsrsSchedulerAdapter_DefaultConfiguration_MatchesInvariants()
    {
        var adapter = new FsrsSchedulerAdapter();

        Assert.Equal(FsrsSchedulerAdapter.DefaultV1DesiredRetention, adapter.DesiredRetention);
        Assert.Equal(0.95, adapter.DesiredRetention);
        Assert.Equal(21, adapter.Parameters.Count);
        Assert.False(adapter.EnableFuzzing);
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), adapter.VirtualEpoch);
    }

    [Fact]
    public void FsrsSchedulerAdapter_DeterministicCardId_IsConsistentAndFactSpecific()
    {
        var id1a = FsrsSchedulerAdapter.CreateDeterministicCardId("Add:0+1");
        var id1b = FsrsSchedulerAdapter.CreateDeterministicCardId("Add:0+1");
        var id2 = FsrsSchedulerAdapter.CreateDeterministicCardId("Add:0+2");

        Assert.Equal(id1a, id1b);
        Assert.NotEqual(id1a, id2);
        Assert.NotEqual(Guid.Empty, id1a);
    }

    // =========================================================================
    // 2. RATING MAPPER TESTS
    // =========================================================================

    [Theory]
    [InlineData(100, FsrsRating.Easy)]
    [InlineData(500, FsrsRating.Easy)]
    [InlineData(1000, FsrsRating.Easy)]
    [InlineData(1001, FsrsRating.Good)]
    [InlineData(1800, FsrsRating.Good)]
    [InlineData(2500, FsrsRating.Good)]
    [InlineData(2501, FsrsRating.Hard)]
    [InlineData(4000, FsrsRating.Hard)]
    [InlineData(15000, FsrsRating.Hard)]
    public void FsrsRatingMapper_CorrectOutcome_MapsByLatencyThresholds(long latencyMs, FsrsRating expectedRating)
    {
        var rating = FsrsRatingMapper.MapRating(
            AttemptOutcome.Correct,
            latencyMs,
            easyThresholdMs: 1000,
            fluentThresholdMs: 2500);

        Assert.Equal(expectedRating, rating);
    }

    [Theory]
    [InlineData(AttemptOutcome.Incorrect, 200)]
    [InlineData(AttemptOutcome.Incorrect, 1500)]
    [InlineData(AttemptOutcome.Incorrect, 5000)]
    [InlineData(AttemptOutcome.Timeout, 30000)]
    [InlineData(AttemptOutcome.Timeout, 35000)]
    public void FsrsRatingMapper_IncorrectOrTimeout_AlwaysMapsToAgain(AttemptOutcome outcome, long latencyMs)
    {
        var rating = FsrsRatingMapper.MapRating(outcome, latencyMs);
        Assert.Equal(FsrsRating.Again, rating);
    }

    // =========================================================================
    // 3. FSRS INTERVAL EVOLUTION & VIRTUAL TIME MODEL TESTS
    // =========================================================================

    [Fact]
    public void FsrsSchedulerAdapter_InitialReview_CreatesValidCardState()
    {
        var adapter = new FsrsSchedulerAdapter();
        var factId = "Add:1+1";

        var state = adapter.ReviewCard(null, factId, FsrsRating.Good, reviewPracticePosition: 1, reviewDurationMs: 1500);

        Assert.Equal(factId, state.FactId);
        Assert.NotNull(state.Stability);
        Assert.NotNull(state.Difficulty);
        Assert.True(state.Stability > 0);
        Assert.True(state.Difficulty > 0);
        Assert.Equal(1, state.LastReviewPracticePosition);
        Assert.Equal(FsrsRating.Good, state.LastRating);
        Assert.True(state.DuePracticePosition >= 2);
    }

    [Fact]
    public void FsrsSchedulerAdapter_RepeatedFastCorrect_GrowsIntervalsExponentially()
    {
        var adapter = new FsrsSchedulerAdapter();
        var factId = "Add:2+3";

        FsrsCardState? state = null;
        long currentPosition = 1;
        var previousInterval = 0L;

        // Simulate 4 consecutive Easy reviews at the exact due positions
        for (var i = 0; i < 4; i++)
        {
            state = adapter.ReviewCard(state, factId, FsrsRating.Easy, reviewPracticePosition: currentPosition, reviewDurationMs: 800);
            var interval = state.DuePracticePosition - currentPosition;

            Assert.True(interval > previousInterval, $"Step {i}: Expected interval {interval} > previous {previousInterval}");
            previousInterval = interval;
            currentPosition = state.DuePracticePosition;
        }

        Assert.NotNull(state);
        Assert.True(state.Stability > 5.0, "Stability should have grown significantly after repeated Easy ratings");
    }

    [Fact]
    public void FsrsSchedulerAdapter_HardRating_YieldsShorterIntervalThanGoodOrEasy()
    {
        var adapter = new FsrsSchedulerAdapter();

        // Compare first review of identical initial cards under Hard vs Good vs Easy
        var stateHard = adapter.ReviewCard(null, "Fact:Hard", FsrsRating.Hard, reviewPracticePosition: 1, reviewDurationMs: 3000);
        var stateGood = adapter.ReviewCard(null, "Fact:Good", FsrsRating.Good, reviewPracticePosition: 1, reviewDurationMs: 1800);
        var stateEasy = adapter.ReviewCard(null, "Fact:Easy", FsrsRating.Easy, reviewPracticePosition: 1, reviewDurationMs: 600);

        var intervalHard = stateHard.DuePracticePosition - 1;
        var intervalGood = stateGood.DuePracticePosition - 1;
        var intervalEasy = stateEasy.DuePracticePosition - 1;

        Assert.True(intervalHard <= intervalGood);
        Assert.True(intervalGood < intervalEasy);
        Assert.True(stateHard.Difficulty > stateGood.Difficulty);
        Assert.True(stateGood.Difficulty > stateEasy.Difficulty);
    }

    [Fact]
    public void FsrsSchedulerAdapter_LapseOnLearnedCard_IncreasesDifficultyAndResetsInterval()
    {
        var adapter = new FsrsSchedulerAdapter();
        var factId = "Add:4+4";

        // 1. Establish strong initial memory
        var s1 = adapter.ReviewCard(null, factId, FsrsRating.Easy, reviewPracticePosition: 1, reviewDurationMs: 700);
        var s2 = adapter.ReviewCard(s1, factId, FsrsRating.Good, reviewPracticePosition: s1.DuePracticePosition, reviewDurationMs: 1200);

        var preLapseStability = s2.Stability!.Value;
        var preLapseDifficulty = s2.Difficulty!.Value;

        // 2. Lapse (wrong or timeout -> Again)
        var sLapse = adapter.ReviewCard(s2, factId, FsrsRating.Again, reviewPracticePosition: s2.DuePracticePosition, reviewDurationMs: 5000);

        Assert.True(sLapse.Stability < preLapseStability, "Stability must decrease after a lapse");
        Assert.True(sLapse.Difficulty > preLapseDifficulty, "Difficulty must increase after a lapse");
        Assert.True(sLapse.DuePracticePosition >= s2.DuePracticePosition + 1);
        Assert.True(sLapse.DuePracticePosition - s2.DuePracticePosition < s2.DuePracticePosition - s1.DuePracticePosition, "Lapse interval must be much smaller than learned interval");
    }

    [Fact]
    public void FsrsSchedulerAdapter_VirtualTimeModel_IsIndependentOfRealWallClock()
    {
        var adapter = new FsrsSchedulerAdapter();
        var factId = "Add:5+5";

        // Review at practice position 100
        var s1 = adapter.ReviewCard(null, factId, FsrsRating.Good, reviewPracticePosition: 100, reviewDurationMs: 1500);

        // Interval in tasks = DuePracticePosition - 100
        var taskInterval = s1.DuePracticePosition - 100;
        Assert.True(taskInterval >= 1);

        // Same review regardless of real world date/time
        var s2 = adapter.ReviewCard(null, factId, FsrsRating.Good, reviewPracticePosition: 100, reviewDurationMs: 1500);
        Assert.Equal(s1.DuePracticePosition, s2.DuePracticePosition);
        Assert.Equal(s1.Stability, s2.Stability);
        Assert.Equal(s1.Difficulty, s2.Difficulty);
    }

    // =========================================================================
    // 4. ADAPTIVE PRACTICE SELECTOR & 4-TIER PRIORITY TESTS
    // =========================================================================

    [Fact]
    public void AdaptiveSelector_Tier1_SameSessionRemediation_PreemptsDueFsrsCards()
    {
        var rng = new Random(42);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression { CurrentMaxOperand = 1 };
        var facts = ArithmeticCatalog.GetAllFacts(1);

        var itemStates = facts.ToDictionary(f => f.Id, f => ItemLearningState.CreateNew(f), StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        // Fact A has an overdue FSRS card (due at position 1, current is 10)
        var factA = facts[0];
        fsrsStates[factA.Id] = new FsrsCardState(
            factA.Id, Guid.NewGuid(), State: 2, Step: null, Stability: 1.0, Difficulty: 5.0,
            DuePracticePosition: 1, LastReviewPracticePosition: 1, LastRating: FsrsRating.Good);

        // Fact B has due same-session remediation at session order 3
        var factB = facts[1];
        itemStates[factB.Id].NeedsRemediation = true;
        itemStates[factB.Id].RemediationDueOrder = 3;

        // Current session order = 3, current practice position = 10
        selector.ResetLastSelected();
        var selected = selector.SelectNextFact(progression, itemStates, fsrsStates, currentSessionOrder: 3, currentPracticePosition: 10);

        Assert.Equal(factB.Id, selected.Id);
    }

    [Fact]
    public void AdaptiveSelector_Tier2_IntroductionPhase_PreemptsDueFsrsCards()
    {
        var rng = new Random(42);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = LearnerProgression.CreateFresh(); // Addition 0..1 introduction
        var facts = ArithmeticCatalog.GetAllFacts(1);

        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        // First 2 addition facts have total attempts > 0 and overdue FSRS cards
        var fact1 = facts[0];
        var fact2 = facts[1];
        itemStates[fact1.Id] = ItemLearningState.CreateNew(fact1); itemStates[fact1.Id].TotalAttempts = 1;
        itemStates[fact2.Id] = ItemLearningState.CreateNew(fact2); itemStates[fact2.Id].TotalAttempts = 1;

        fsrsStates[fact1.Id] = new FsrsCardState(fact1.Id, Guid.NewGuid(), 2, null, 1.0, 5.0, DuePracticePosition: 1, 1, FsrsRating.Good);
        fsrsStates[fact2.Id] = new FsrsCardState(fact2.Id, Guid.NewGuid(), 2, null, 1.0, 5.0, DuePracticePosition: 1, 1, FsrsRating.Good);

        // Facts 3 and 4 in Addition 0..1 are still unexposed
        selector.ResetLastSelected();
        var selected = selector.SelectNextFact(progression, itemStates, fsrsStates, currentSessionOrder: 3, currentPracticePosition: 10);

        // Must select one of the unexposed introduction facts (not the overdue FSRS cards)
        var unexposedIds = facts.Where(f => f.Operation == ArithmeticOperation.Addition && !itemStates.ContainsKey(f.Id)).Select(f => f.Id).ToHashSet();
        Assert.Contains(selected.Id, unexposedIds);
    }

    [Fact]
    public void AdaptiveSelector_Tier3_DueFsrsCards_PrioritizesMostOverdueCard()
    {
        var rng = new Random(42);
        var selector = new AdaptivePracticeSelector(rng);
        var progression = new LearnerProgression
        {
            OperationMaxOperands = new Dictionary<ArithmeticOperation, int>
            {
                [ArithmeticOperation.Addition] = 10,
                [ArithmeticOperation.Subtraction] = 10,
                [ArithmeticOperation.Multiplication] = 10,
                [ArithmeticOperation.Division] = 10
            }
        };
        var facts = ArithmeticCatalog.GetActiveFacts(progression);
        var itemStates = facts.ToDictionary(f => f.Id, f =>
        {
            var st = ItemLearningState.CreateNew(f);
            st.TotalAttempts = 1;
            return st;
        }, StringComparer.Ordinal);

        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);

        // Card A: Due at 8 (overdue by 2 at pos 10)
        var cardA = facts[0];
        fsrsStates[cardA.Id] = new FsrsCardState(cardA.Id, Guid.NewGuid(), 2, null, 2.0, 4.0, DuePracticePosition: 8, 5, FsrsRating.Good);

        // Card B: Due at 3 (overdue by 7 at pos 10 - MOST OVERDUE)
        var cardB = facts[1];
        fsrsStates[cardB.Id] = new FsrsCardState(cardB.Id, Guid.NewGuid(), 2, null, 2.0, 4.0, DuePracticePosition: 3, 1, FsrsRating.Good);

        // Card C: Due at 15 (not due yet at pos 10)
        var cardC = facts[2];
        fsrsStates[cardC.Id] = new FsrsCardState(cardC.Id, Guid.NewGuid(), 2, null, 5.0, 3.0, DuePracticePosition: 15, 6, FsrsRating.Easy);

        selector.ResetLastSelected();
        var selected = selector.SelectNextFact(progression, itemStates, fsrsStates, currentSessionOrder: 10, currentPracticePosition: 10);

        Assert.Equal(cardB.Id, selected.Id);
    }

    [Fact]
    public void AdaptiveSelector_Tier4_NoDueCards_SurfacesEarliestUpcomingDueCard()
    {
        var rng = new Random(42);
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

        // All cards are in the future (none due at pos 5)
        var fsrsStates = facts.ToDictionary(f => f.Id, f =>
            new FsrsCardState(f.Id, Guid.NewGuid(), 2, null, 5.0, 3.0, DuePracticePosition: 20, 1, FsrsRating.Good),
            StringComparer.Ordinal);

        // Set one card to have the earliest upcoming due position (Due at 12)
        var earliestFact = facts[3];
        fsrsStates[earliestFact.Id] = new FsrsCardState(
            earliestFact.Id, Guid.NewGuid(), 2, null, 2.0, 5.0, DuePracticePosition: 12, 1, FsrsRating.Good);

        selector.ResetLastSelected();
        var selected = selector.SelectNextFact(progression, itemStates, fsrsStates, currentSessionOrder: 5, currentPracticePosition: 5);

        Assert.Equal(earliestFact.Id, selected.Id);
    }

    // =========================================================================
    // 5. SESSION ENGINE END-TO-END FSRS INTEGRATION
    // =========================================================================

    [Fact]
    public async Task TrainingSession_Submissions_IncrementPracticePositionAndUpdateFsrs()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Empty(session.FsrsStates);

        // 1. Submit first answer (correct fluent)
        var fact1 = session.CurrentFact;
        var eval1 = session.SubmitAnswer(fact1.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Single(session.FsrsStates);
        Assert.True(session.FsrsStates.ContainsKey(fact1.Id));
        Assert.Equal(FsrsRating.Easy, session.FsrsStates[fact1.Id].LastRating);
        Assert.True(session.FsrsStates[fact1.Id].DuePracticePosition >= 2);

        // 2. Advance and submit second answer (timeout)
        session.AdvanceToNextFact();
        var fact2 = session.CurrentFact;
        var eval2 = session.RecordTimeout();
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(2, session.Progression.PracticePosition);
        Assert.Equal(2, session.FsrsStates.Count);
        Assert.Equal(FsrsRating.Again, session.FsrsStates[fact2.Id].LastRating);
    }

    // =========================================================================
    // 6. DATABASE MIGRATION V2 -> V3 CONFORMANCE
    // =========================================================================

    [Fact]
    public async Task DatabaseMigration_V2ToV3_ReplaysHistoricalAttemptsLosslessly()
    {
        var dbPath = GetTempDbPath();

        // 1. Create a Schema V2 database with historical attempts
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO schema_info (key, value) VALUES ('store_revision', '5');

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
                ('Add:0+0', 'Addition', 0, 0, 2, 2, 0, 2, 800, 800, 2, 0, 0, 0, 1, '2026-09-07T12:00:00Z'),
                ('Add:0+1', 'Addition', 0, 1, 1, 0, 1, 0, 30000, 30000, 0, 0, 1, 5, 2, '2026-09-07T12:01:00Z');

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
                    submitted_answer, correct_answer, is_correct, outcome, response_latency_ms, timestamp
                ) VALUES
                ('sub1', 'Add:0+0', 'Addition', 0, 0, 0, 0, 1, 'Correct', 800, '2026-09-07T11:58:00Z'),
                ('sub2', 'Add:0+0', 'Addition', 0, 0, 0, 0, 1, 'Correct', 600, '2026-09-07T11:59:00Z'),
                ('sub3', 'Add:0+1', 'Addition', 0, 1, NULL, 1, 0, 'Timeout', 30000, '2026-09-07T12:01:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Open with SqliteLearnerStore to trigger migration to V3
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();

        // 3. Verify Schema V4 and PracticePosition = 3
        Assert.Equal(LearnerProgression.DefaultSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(5, snapshot.Revision);
        Assert.Equal(3, snapshot.Progression.PracticePosition);
        Assert.Equal(2, snapshot.FsrsStates.Count);

        // Verify replayed FSRS states
        Assert.True(snapshot.FsrsStates.ContainsKey("Add:0+0"));
        var fsrs0 = snapshot.FsrsStates["Add:0+0"];
        Assert.Equal(FsrsRating.Easy, fsrs0.LastRating);
        Assert.Equal(2, fsrs0.LastReviewPracticePosition);
        Assert.True(fsrs0.DuePracticePosition > 2);

        Assert.True(snapshot.FsrsStates.ContainsKey("Add:0+1"));
        var fsrs1 = snapshot.FsrsStates["Add:0+1"];
        Assert.Equal(FsrsRating.Again, fsrs1.LastRating);
        Assert.Equal(3, fsrs1.LastReviewPracticePosition);
    }

    [Fact]
    public async Task SqliteLearnerStore_V3_ResetLearningProgress_ClearsFsrsStateAndResetsPosition()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session = new TrainingSession(store);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        session.AdvanceToNextFact();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var preResetSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, preResetSnapshot.Progression.PracticePosition);
        Assert.NotEmpty(preResetSnapshot.FsrsStates);

        // Reset
        await session.ResetLearningProgressAsync();

        var postResetSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(0, postResetSnapshot.Progression.PracticePosition);
        Assert.Empty(postResetSnapshot.FsrsStates);
        Assert.Empty(postResetSnapshot.ItemStates);
        Assert.Empty(postResetSnapshot.RecentAttempts);
        Assert.Equal(1, postResetSnapshot.Revision);
    }

    [Fact]
    public async Task DatabaseMigration_V1ToV3_ThroughV2_ReplaysHistoricalAttemptsLosslessly()
    {
        var dbPath = GetTempDbPath();

        // 1. Create a Schema V1 database with historical attempts
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO schema_info (key, value) VALUES ('store_revision', '2');

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

        // 2. Open with SqliteLearnerStore to trigger chained migration V1 -> V2 -> V3
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(LearnerProgression.DefaultSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(2, snapshot.Revision);
        Assert.Equal(1, snapshot.Progression.PracticePosition);
        Assert.Single(snapshot.FsrsStates);
        Assert.True(snapshot.FsrsStates.ContainsKey("Add:0+0"));
        Assert.Equal(FsrsRating.Easy, snapshot.FsrsStates["Add:0+0"].LastRating);
    }

    [Fact]
    public async Task SqliteLearnerStore_V3_StaleRevisionConflict_RejectsSubmission()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId1 = Guid.NewGuid().ToString("N");
        var attempt1 = new AttemptRecord(subId1, fact.Id, fact.Operation, 0, 1, 1, 1, true, 800, DateTimeOffset.UtcNow);
        var itemState1 = ItemLearningState.CreateNew(fact);
        var prog1 = LearnerProgression.CreateFresh();
        prog1.PracticePosition = 1;
        var fsrs1 = new FsrsCardState(fact.Id, Guid.NewGuid(), 2, null, 1.0, 5.0, 3, 1, FsrsRating.Easy);

        var changeSet1 = new SubmissionChangeSet(subId1, ExpectedRevision: 1, attempt1, itemState1, prog1, fsrs1);
        var res1 = await store.CommitSubmissionAsync(changeSet1);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Stale revision submission (expected 1, actual 2)
        var subId2 = Guid.NewGuid().ToString("N");
        var attempt2 = new AttemptRecord(subId2, fact.Id, fact.Operation, 0, 1, 1, 1, true, 800, DateTimeOffset.UtcNow);
        var changeSet2 = new SubmissionChangeSet(subId2, ExpectedRevision: 1, attempt2, itemState1, prog1, fsrs1);
        var res2 = await store.CommitSubmissionAsync(changeSet2);

        Assert.False(res2.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, res2.Status);
    }

    [Fact]
    public async Task SqliteLearnerStore_V3_DuplicateSubmission_IsIdempotent()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, 800, DateTimeOffset.UtcNow);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();
        prog.PracticePosition = 1;
        var fsrs = new FsrsCardState(fact.Id, Guid.NewGuid(), 2, null, 1.0, 5.0, 3, 1, FsrsRating.Easy);

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog, fsrs);
        var res1 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Duplicate submission with same subId
        var res2 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res2.IsSuccess);
        Assert.Equal(2, res2.NewRevision);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Single(snapshot.RecentAttempts);
        Assert.Single(snapshot.FsrsStates);
    }
}
