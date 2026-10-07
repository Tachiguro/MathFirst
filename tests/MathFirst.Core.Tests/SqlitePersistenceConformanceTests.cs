namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqlitePersistenceConformanceTests : IDisposable
{
    private readonly string _testDbDir;

    public SqlitePersistenceConformanceTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstTests_" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup in temp directory
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Conformance_1_EmptyStoreInitializesCleanly()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);

        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(LearnerProgression.DefaultSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(4, snapshot.Progression.OperationProgressions.Count);
        Assert.All(snapshot.Progression.OperationProgressions.Values, progression => Assert.Equal(0, progression.BandIndex));
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Null(snapshot.LatestAcceptedPracticeAt);
        Assert.Equal(0, snapshot.PositionedCorrectAttemptCount);
    }

    [Fact]
    public async Task Conformance_2_AtomicSubmission_UpdatesItemProgressionAndAttemptAtomically()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var acceptedAt = new DateTimeOffset(2026, 9, 9, 10, 15, 0, TimeSpan.Zero);
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200, acceptedAt, practicePosition: 1);

        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 1200;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);

        var result = await store.CommitSubmissionAsync(changeSet);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.NewRevision);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, snapshot.Revision);
        Assert.Single(snapshot.ItemStates);
        Assert.True(snapshot.ItemStates.ContainsKey(fact.Id));
        Assert.Equal(1, snapshot.ItemStates[fact.Id].TotalAttempts);
        Assert.Equal(1200, snapshot.ItemStates[fact.Id].LastLatencyMs);
        Assert.Single(snapshot.RecentAttempts);
        Assert.Equal(subId, snapshot.RecentAttempts[0].SubmissionId);
        Assert.True(snapshot.RecentAttempts[0].IsFluent);
        Assert.Equal(acceptedAt, snapshot.LatestAcceptedPracticeAt);
        Assert.Equal(1, snapshot.PositionedCorrectAttemptCount);

        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Empty(runtimeSnapshot.ItemStates);
        Assert.Equal(acceptedAt, runtimeSnapshot.LatestAcceptedPracticeAt);
        Assert.Equal(1, runtimeSnapshot.PositionedCorrectAttemptCount);
    }

    [Fact]
    public async Task Conformance_3_StaleRevisionConflict_RejectsWrite()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId1 = Guid.NewGuid().ToString("N");
        var attempt1 = new AttemptRecord(subId1, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState1 = ItemLearningState.CreateNew(fact);
        var prog1 = LearnerProgression.CreateFresh();
        prog1.PracticePosition = 1;

        var changeSet1 = new SubmissionChangeSet(subId1, ExpectedRevision: 1, attempt1, itemState1, prog1);
        var res1 = await store.CommitSubmissionAsync(changeSet1);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Attempt second submission with stale revision 1 (when DB is now at revision 2)
        var subId2 = Guid.NewGuid().ToString("N");
        var attempt2 = new AttemptRecord(subId2, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1100, DateTimeOffset.UtcNow);
        var changeSet2 = new SubmissionChangeSet(subId2, ExpectedRevision: 1, attempt2, itemState1, prog1);

        var res2 = await store.CommitSubmissionAsync(changeSet2);
        Assert.False(res2.IsSuccess);
        Assert.Equal(PersistenceStatus.RevisionConflict, res2.Status);
    }

    [Fact]
    public async Task Conformance_4_DuplicateSubmission_IsIdempotent()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();
        prog.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);

        var res1 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res1.IsSuccess);
        Assert.Equal(2, res1.NewRevision);

        // Retrying identical submission ID returns success idempotently without double recording
        var res2 = await store.CommitSubmissionAsync(changeSet);
        Assert.True(res2.IsSuccess);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Single(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task Conformance_5_UnsupportedNewerVersion_IsRejected()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            // Manually write higher schema version into database
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schema_info SET value = '99' WHERE key = 'schema_version';";
            await cmd.ExecuteNonQueryAsync();
        }

        // Reopening store must reject unsupported version
        using var store2 = new SqliteLearnerStore(dbPath);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store2.InitializeAsync());
    }

    [Fact]
    public async Task Conformance_6_CorruptedDatabase_RejectsGracefully()
    {
        var dbPath = GetTempDbPath();
        await File.WriteAllBytesAsync(dbPath, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77 });

        using var store = new SqliteLearnerStore(dbPath);
        // SQLite will reject invalid header
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.InitializeAsync());
    }

    [Fact]
    public async Task Conformance_7_CloseAndReopen_PreservesState()
    {
        var dbPath = GetTempDbPath();
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");

        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1300, DateTimeOffset.UtcNow, practicePosition: 1);
            var itemState = ItemLearningState.CreateNew(fact);
            itemState.TotalAttempts = 5;
            itemState.CorrectAttempts = 5;
            itemState.ConsecutiveCorrectStreak = 5;
            itemState.IsProvisionallyMastered = true;
            itemState.LastLatencyMs = 1300;

            var prog = LearnerProgression.CreateFresh();
            prog.PracticePosition = 1;

            var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
            await store.CommitSubmissionAsync(changeSet);
            await store.CloseAsync();
        }

        // Reopen from disk
        using (var storeReopened = new SqliteLearnerStore(dbPath))
        {
            await storeReopened.InitializeAsync();
            var snapshot = await storeReopened.LoadSnapshotAsync();

            Assert.Equal(2, snapshot.Revision);
            Assert.Equal(0, snapshot.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
            Assert.Single(snapshot.ItemStates);
            Assert.True(snapshot.ItemStates[fact.Id].IsProvisionallyMastered);
            Assert.Equal(5, snapshot.ItemStates[fact.Id].TotalAttempts);
        }
    }

    [Fact]
    public async Task Conformance_8_ResetLearningProgress_RestoresFreshProgression()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);
        var prog = LearnerProgression.CreateFresh();
        prog.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
        await store.CommitSubmissionAsync(changeSet);

        // Reset
        await store.ResetLearningProgressAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, snapshot.Revision);
        Assert.All(snapshot.Progression.OperationProgressions.Values, progression => Assert.Equal(0, progression.BandIndex));
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Null(snapshot.LatestAcceptedPracticeAt);
    }

    [Fact]
    public async Task Conformance_9_OperationProgressions_PreservedAcrossReopen()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();

            var prog = LearnerProgression.CreateFresh();
            prog.PracticePosition = 1;
            prog.OperationProgressions[ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 1, 1);

            var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
            var subId = Guid.NewGuid().ToString("N");
            var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 800, DateTimeOffset.UtcNow, practicePosition: 1);
            var itemState = ItemLearningState.CreateNew(fact);

            var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, prog);
            await store.CommitSubmissionAsync(changeSet);
            await store.CloseAsync();
        }

        using (var storeReopened = new SqliteLearnerStore(dbPath))
        {
            await storeReopened.InitializeAsync();
            var snapshot = await storeReopened.LoadSnapshotAsync();

            Assert.Equal(1, snapshot.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
            Assert.Equal(1, snapshot.Progression.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition);
            Assert.All(
                Enum.GetValues<ArithmeticOperation>().Where(operation => operation != ArithmeticOperation.Addition),
                operation => Assert.Equal(0, snapshot.Progression.OperationProgressions[operation].BandIndex));
        }
    }

    [Fact]
    public async Task Conformance_10_LoadLatestFrontierAttempts_SelectsLatestAttemptPerFact_RespectingStrictBandStart()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        // Insert attempts directly via SQL
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES
                    ('sub-0-10', 'add:0+0', 'Addition', 0, 0, 1, 0, 0, 0, 'Incorrect', 2000, @ts, 10),
                    ('sub-0-11', 'add:0+0', 'Addition', 0, 0, 0, 0, 1, 1, 'Correct', 1200, @ts, 11),
                    ('sub-0-20', 'add:0+0', 'Addition', 0, 0, 0, 0, 1, 0, 'Correct', 3500, @ts, 20),
                    ('sub-1-15', 'add:0+1', 'Addition', 0, 1, 1, 1, 1, 1, 'Correct', 1100, @ts, 15),
                    ('sub-2-8',  'add:0+2', 'Addition', 0, 2, 2, 2, 1, 1, 'Correct', 1000, @ts, 8),
                    ('sub-2-9',  'add:0+2', 'Addition', 0, 2, 2, 2, 1, 1, 'Correct', 1000, @ts, 9);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Band start position is 10. Position 10 must NOT count.
        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 10,
            frontierFactIds: ["add:0+0", "add:0+1", "add:0+2"]);

        // add:0+0 has latest > 10 at pos 20
        // add:0+1 has latest > 10 at pos 15
        // add:0+2 has no attempts > 10 (only 8 and 10), so excluded
        Assert.Equal(2, results.Count);

        // Deterministic ordering by FactId ordinal
        Assert.Equal("add:0+0", results[0].FactId);
        Assert.Equal(20, results[0].PracticePosition);
        Assert.Equal("sub-0-20", results[0].SubmissionId);
        Assert.Equal(ArithmeticOperation.Addition, results[0].Operation);
        Assert.Equal(0, results[0].LeftOperand);
        Assert.Equal(0, results[0].RightOperand);
        Assert.Equal(0, results[0].SubmittedAnswer);
        Assert.Equal(0, results[0].CorrectAnswer);
        Assert.True(results[0].IsCorrect);
        Assert.False(results[0].IsFluent);
        Assert.Equal(AttemptOutcome.Correct, results[0].Outcome);
        Assert.Equal(3500, results[0].ResponseLatencyMs);
        Assert.Equal(dt, results[0].Timestamp);

        Assert.Equal("add:0+1", results[1].FactId);
        Assert.Equal(15, results[1].PracticePosition);
        Assert.Equal("sub-1-15", results[1].SubmissionId);
        Assert.True(results[1].IsFluent);
    }

    [Fact]
    public async Task Conformance_11_LoadLatestFrontierAttempts_AuthoritativeBeyondRecent40Window()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            // Attempt at position 1 for target fact
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES ('sub-target-1', 'add:0+0', 'Addition', 0, 0, 0, 0, 1, 1, 'Correct', 1000, @ts, 1);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();

            // Insert 45 newer attempts for other facts (positions 2..46)
            for (var i = 2; i <= 46; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                    VALUES ('sub-other-{i}', 'add:1+{i % 10}', 'Addition', 1, {i % 10}, {1 + (i % 10)}, {1 + (i % 10)}, 1, 1, 'Correct', 1000, @ts, {i});";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Bounded recent attempts window contains only the latest 40 attempts for Addition (positions 7..46)
        var snapshot = await store.LoadSnapshotAsync();
        Assert.DoesNotContain(snapshot.RecentAttempts, a => a.FactId == "add:0+0");

        // The authoritative latest-per-frontier query still finds the attempt at position 1
        var frontierAttempts = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: ["add:0+0"]);

        Assert.Single(frontierAttempts);
        Assert.Equal("add:0+0", frontierAttempts[0].FactId);
        Assert.Equal(1, frontierAttempts[0].PracticePosition);
        Assert.Equal("sub-target-1", frontierAttempts[0].SubmissionId);
    }

    [Fact]
    public async Task Conformance_12_LoadLatestFrontierAttempts_LaterRepair_ReturnsLatestCorrect()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES
                    ('sub-repair-21', 'add:2+2', 'Addition', 2, 2, 5, 4, 0, 0, 'Incorrect', 2000, @ts, 21),
                    ('sub-repair-25', 'add:2+2', 'Addition', 2, 2, 4, 4, 1, 1, 'Correct', 1000, @ts, 25);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 20,
            frontierFactIds: ["add:2+2"]);

        Assert.Single(results);
        Assert.Equal("sub-repair-25", results[0].SubmissionId);
        Assert.True(results[0].IsCorrect);
        Assert.Equal(25, results[0].PracticePosition);
    }

    [Fact]
    public async Task Conformance_13_LoadLatestFrontierAttempts_LaterFailure_ReturnsLatestFailure()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES
                    ('sub-fail-21', 'add:2+2', 'Addition', 2, 2, 4, 4, 1, 1, 'Correct', 1000, @ts, 21),
                    ('sub-fail-25', 'add:2+2', 'Addition', 2, 2, NULL, 4, 0, 0, 'Timeout', 9000, @ts, 25);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 20,
            frontierFactIds: ["add:2+2"]);

        Assert.Single(results);
        Assert.Equal("sub-fail-25", results[0].SubmissionId);
        Assert.False(results[0].IsCorrect);
        Assert.Equal(AttemptOutcome.Timeout, results[0].Outcome);
        Assert.Null(results[0].SubmittedAnswer);
        Assert.Equal(25, results[0].PracticePosition);
    }

    [Fact]
    public async Task Conformance_14_LoadLatestFrontierAttempts_IgnoresNullPracticePositionLegacyAttempts()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES
                    ('sub-legacy', 'add:3+3', 'Addition', 3, 3, 6, 6, 1, 1, 'Correct', 1000, @ts, NULL);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: ["add:3+3"]);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Conformance_15_LoadLatestFrontierAttempts_IgnoresOtherOperationsAndUnrequestedFacts()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                VALUES
                    ('sub-sub-5', 'sub:4-2', 'Subtraction', 4, 2, 2, 2, 1, 1, 'Correct', 1000, @ts, 5),
                    ('sub-add-6', 'add:5+5', 'Addition', 5, 5, 10, 10, 1, 1, 'Correct', 1000, @ts, 6);";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Query Addition for fact not attempted
        var additionResults = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: ["add:1+1"]);
        Assert.Empty(additionResults);

        // Query Addition requesting the subtraction fact ID
        var mismatchedResults = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: ["sub:4-2"]);
        Assert.Empty(mismatchedResults);

        // Query Subtraction for sub:4-2
        var subtractionResults = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Subtraction,
            bandStartedPracticePosition: 0,
            frontierFactIds: ["sub:4-2"]);
        Assert.Single(subtractionResults);
        Assert.Equal("sub-sub-5", subtractionResults[0].SubmissionId);
    }

    [Fact]
    public async Task Conformance_16_LoadLatestFrontierAttempts_EmptyFrontier_ReturnsEmpty()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: []);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Conformance_17_LoadLatestFrontierAttempts_BoundedToMaxDenseFrontier25_ReturnsAtMostOnePerRow()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

        var factIds = Enumerable.Range(0, 25).Select(i => $"mul:0*{i}").ToArray();

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            // Insert 2 attempts per fact (50 attempts total)
            var position = 1;
            for (var i = 0; i < 25; i++)
            {
                var factId = factIds[i];
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, is_fluent, outcome, response_latency_ms, timestamp, practice_position)
                    VALUES
                        ('sub-{i}-first', '{factId}', 'Multiplication', 0, {i}, 0, 0, 1, 0, 'Correct', 3000, @ts, {position++}),
                        ('sub-{i}-second', '{factId}', 'Multiplication', 0, {i}, 0, 0, 1, 1, 'Correct', 1000, @ts, {position++});";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var results = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Multiplication,
            bandStartedPracticePosition: 0,
            frontierFactIds: factIds);

        Assert.Equal(25, results.Count);
        Assert.Equal(factIds.Length, results.Select(r => r.FactId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(results, r => Assert.EndsWith("-second", r.SubmissionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Conformance_18_LoadLatestFrontierAttempts_InputValidation_RejectsInvalidArguments()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Invalid operation
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            store.LoadLatestFrontierAttemptsAsync((ArithmeticOperation)999, 0, ["add:0+0"]));

        // Negative band start position
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, -1, ["add:0+0"]));

        // Null frontier list
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, 0, null!));

        // Blank fact ID
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, 0, ["add:0+0", ""]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, 0, ["   "]));

        // Frontier exceeding max 25
        var twentySixFacts = Enumerable.Range(0, 26).Select(i => $"add:0+{i}").ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, 0, twentySixFacts));

        // Duplicate fact IDs
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.LoadLatestFrontierAttemptsAsync(ArithmeticOperation.Addition, 0, ["add:0+0", "add:0+0"]));
    }

    [Fact]
    public async Task Conformance_19_DurablePaceCalibrationReadiness_BoundedQueryAndV6SchemaPreserved()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var dt = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            // Insert 23 positioned Correct attempts
            for (var i = 1; i <= 23; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                        response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        'sub-correct-{i}', 'add:0+1', 'Addition', 0, 1,
                        1, 1, 1, 1, 'Correct',
                        1000, @ts, {i}
                    );";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert 5 positioned Incorrect attempts
            for (var i = 1; i <= 5; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                        response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        'sub-incorrect-{i}', 'add:0+1', 'Addition', 0, 1,
                        99, 1, 0, 0, 'Incorrect',
                        3000, @ts, {23 + i}
                    );";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert 5 positioned Timeout attempts
            for (var i = 1; i <= 5; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                        response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        'sub-timeout-{i}', 'add:0+1', 'Addition', 0, 1,
                        NULL, 1, 0, 0, 'Timeout',
                        30000, @ts, {28 + i}
                    );";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert 5 unpositioned (legacy NULL practice_position) Correct attempts
            for (var i = 1; i <= 5; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                        response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        'sub-unpositioned-{i}', 'add:0+1', 'Addition', 0, 1,
                        1, 1, 1, 1, 'Correct',
                        1000, @ts, NULL
                    );";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // Bounded count must filter exactly: practice_position IS NOT NULL AND practice_position > 0 AND outcome = 'Correct'
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);

        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Equal(23, runtimeSnapshot.PositionedCorrectAttemptCount);

        // Add 1 more positioned Correct attempt (#24)
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    'sub-correct-24', 'add:0+1', 'Addition', 0, 1,
                    1, 1, 1, 1, 'Correct',
                    1000, @ts, 34
                );";
            cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }

        snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(24, snapshot.PositionedCorrectAttemptCount);

        // Add 10 more positioned Correct attempts (saturates at 24)
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            for (var i = 25; i <= 34; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                        response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        'sub-correct-{i}', 'add:0+1', 'Addition', 0, 1,
                        1, 1, 1, 1, 'Correct',
                        1000, @ts, {10 + i}
                    );";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", dt.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(24, snapshot.PositionedCorrectAttemptCount);

        runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Equal(24, runtimeSnapshot.PositionedCorrectAttemptCount);

        // Schema V6 conformance assertions:
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();

            // 1. schema_version is 9
            using (var versionCmd = conn.CreateCommand())
            {
                versionCmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
                var version = await versionCmd.ExecuteScalarAsync();
                Assert.Equal("9", version);
            }

            // 2. Expected tables only
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

            // 3. Columns on attempt_history remain exactly V8
            var expectedAttemptColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "submission_id",
                "fact_id",
                "operation",
                "left_operand",
                "right_operand",
                "submitted_answer",
                "correct_answer",
                "is_correct",
                "is_fluent",
                "outcome",
                "response_latency_ms",
                "timestamp",
                "practice_position",
                "attempt_context_version",
                "presented_deadline_ms",
                "expected_pace_ms",
                "resolved_role",
                "operation_band_before",
                "is_interrupted"
            };

            using (var colCmd = conn.CreateCommand())
            {
                colCmd.CommandText = "PRAGMA table_info(attempt_history);";
                using var reader = await colCmd.ExecuteReaderAsync();
                var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadAsync())
                {
                    actualColumns.Add(reader.GetString(1));
                }

                Assert.Equal(expectedAttemptColumns, actualColumns);
            }
        }
    }

    [Fact]
    public async Task Conformance_ReadBoundedRecentAttempts_IncludesLatest40TotalAndLatest40EligibleAttemptsPerOperation()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var baseTime = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        for (int i = 1; i <= 50; i++)
        {
            var subId = $"sub_{i:D4}";
            var isInterrupted = i > 10; // First 10 are eligible (false), next 40 are interrupted (true)
            var attempt = new AttemptRecord(
                subId,
                fact.Id,
                fact.Operation,
                0,
                1,
                1,
                1,
                true,
                true,
                1200,
                baseTime.AddSeconds(i),
                practicePosition: i,
                isInterrupted: isInterrupted);

            var itemState = ItemLearningState.CreateNew(fact);
            itemState.TotalAttempts = i;
            itemState.CorrectAttempts = i;

            var progression = LearnerProgression.CreateFresh();
            progression.PracticePosition = i;

            var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: i, attempt, itemState, progression);
            var result = await store.CommitSubmissionAsync(changeSet);
            Assert.True(result.IsSuccess);
        }

        var snapshot = await store.LoadSnapshotAsync();
        // Positions 1..10 are the 10 available eligible attempts. Positions 11..50 are the latest 40 total attempts.
        // Therefore, RecentAttempts must contain all 50 attempts (positions 1 through 50).
        Assert.Equal(50, snapshot.RecentAttempts.Count);
        Assert.Contains(snapshot.RecentAttempts, a => a.PracticePosition == 1);
        Assert.Contains(snapshot.RecentAttempts, a => a.PracticePosition == 50);

        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Equal(50, runtimeSnapshot.RecentAttempts.Count);
        Assert.Contains(runtimeSnapshot.RecentAttempts, a => a.PracticePosition == 1);
        Assert.Contains(runtimeSnapshot.RecentAttempts, a => a.PracticePosition == 50);
    }

    [Fact]
    public async Task Conformance_20_TransactionRollback_PreservesAllDurableState_WhenMidTransactionFailureOccurs()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // 1. Establish known clean pre-commit snapshot at revision 1 and position 0
        var preSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, preSnapshot.Revision);
        Assert.Equal(0, preSnapshot.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage1_Addition, preSnapshot.Progression.CurriculumStage);
        Assert.Empty(preSnapshot.ItemStates);
        Assert.Empty(preSnapshot.RecentAttempts);

        // 2. Add synthetic trigger that raises an abort on learner_progression update (step 3 of transaction)
        // This fires after step 1 (attempt_history insert) and step 2 (item_learning_state upsert) execute.
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TRIGGER trg_abort_progression_update
                BEFORE UPDATE ON learner_progression
                BEGIN
                    SELECT RAISE(ABORT, 'Synthetic mid-transaction abort on learner_progression');
                END;";
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Prepare an otherwise-valid SubmissionChangeSet
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1000, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);

        // 4. CommitSubmissionAsync must fail due to mid-transaction abort
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await store.CommitSubmissionAsync(changeSet));

        // 5. Verify atomic rollback: all tables remain exactly in pre-commit state
        await using (var verifyConn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await verifyConn.OpenAsync();

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'store_revision';";
                var rev = await cmd.ExecuteScalarAsync();
                Assert.Equal("1", rev);
            }

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT practice_position, curriculum_stage FROM learner_progression WHERE id = 1;";
                using var reader = await cmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt64(0));
                Assert.Equal(1, reader.GetInt32(1));
            }

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM attempt_history;";
                var attemptCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.Equal(0, attemptCount);
            }

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM item_learning_state;";
                var itemCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.Equal(0, itemCount);
            }

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM fsrs_card_state;";
                var fsrsCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.Equal(0, fsrsCount);
            }

            using (var cmd = verifyConn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM operation_progression WHERE band_index != 0 OR band_started_practice_position != 0;";
                var mutatedOpCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                Assert.Equal(0, mutatedOpCount);
            }
        }

        // Store can still read clean snapshot
        var postSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, postSnapshot.Revision);
        Assert.Equal(0, postSnapshot.Progression.PracticePosition);
        Assert.Empty(postSnapshot.ItemStates);
        Assert.Empty(postSnapshot.RecentAttempts);

        // 6. Drop failure trigger and prove subsequent commit succeeds cleanly on the same database
        await using (var dropConn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await dropConn.OpenAsync();
            using var cmd = dropConn.CreateCommand();
            cmd.CommandText = "DROP TRIGGER trg_abort_progression_update;";
            await cmd.ExecuteNonQueryAsync();
        }

        var retryResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(retryResult.IsSuccess);
        Assert.Equal(2, retryResult.NewRevision);

        var finalSnapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, finalSnapshot.Revision);
        Assert.Equal(1, finalSnapshot.Progression.PracticePosition);
        Assert.Single(finalSnapshot.ItemStates);
        Assert.Single(finalSnapshot.RecentAttempts);
        Assert.Equal(subId, finalSnapshot.RecentAttempts[0].SubmissionId);
    }

    [Fact]
    public async Task Conformance_21_CommitSubmission_WithCorruptedStoredCurriculumStage_FailsClosedAndRollsBack()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // 1. Corrupt stored curriculum_stage to 99 by dropping/recreating table without CHECK constraint
        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE temp_lp (id INTEGER PRIMARY KEY, practice_position INTEGER, curriculum_stage INTEGER, updated_at TEXT);
                INSERT INTO temp_lp VALUES (1, 0, 99, '2026-10-06T00:00:00Z');
                DROP TABLE learner_progression;
                ALTER TABLE temp_lp RENAME TO learner_progression;";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Prepare valid SubmissionChangeSet
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1000, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);

        // 3. CommitSubmissionAsync inside transaction invokes ReadCurriculumStageInTxAsync,
        // which fails closed and returns PersistenceResult.InvalidSubmission without mutating DB
        var result = await store.CommitSubmissionAsync(changeSet);

        Assert.False(result.IsSuccess);
        Assert.Equal(PersistenceStatus.InvalidSubmission, result.Status);
        Assert.Contains("Invalid stored curriculum stage 99", result.Message);

        // 4. Verify no partial changes: revision remains 1, attempt_history remains empty
        await using (var verifyConn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await verifyConn.OpenAsync();
            using var cmd = verifyConn.CreateCommand();
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'store_revision';";
            var rev = await cmd.ExecuteScalarAsync();
            Assert.Equal("1", rev);

            using var attemptCmd = verifyConn.CreateCommand();
            attemptCmd.CommandText = "SELECT COUNT(*) FROM attempt_history;";
            var count = Convert.ToInt64(await attemptCmd.ExecuteScalarAsync());
            Assert.Equal(0, count);
        }
    }
}
