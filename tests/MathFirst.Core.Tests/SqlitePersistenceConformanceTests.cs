namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Domain;
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

        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        Assert.Empty(runtimeSnapshot.ItemStates);
        Assert.Equal(acceptedAt, runtimeSnapshot.LatestAcceptedPracticeAt);
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
}
