namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SchemaV8MigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstSchemaV8_" + Guid.NewGuid().ToString("N"));

    public SchemaV8MigrationTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public async Task FreshDatabase_UsesSchemaV9()
    {
        var path = Path.Combine(_directory, "fresh_v8.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            Assert.Equal(9, LearnerProgression.DefaultSchemaVersion);
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = await cmd.ExecuteScalarAsync();
        Assert.Equal("9", version);
    }

    [Fact]
    public async Task FreshDatabase_AttemptHistoryContainsIsInterrupted()
    {
        var path = Path.Combine(_directory, "fresh_columns.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        Assert.Equal(19, columns.Count);

        var columnMap = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        Assert.True(columnMap.TryGetValue("is_interrupted", out var isInterruptedCol), "Column is_interrupted must exist.");
        Assert.Equal("INTEGER", isInterruptedCol.Type.ToUpperInvariant());
        Assert.True(isInterruptedCol.NotNull, "is_interrupted must be NOT NULL.");
        Assert.Equal("0", isInterruptedCol.DefaultValue?.Trim('\'', ' '));

        var expectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

        var actualColumnNames = columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedColumns, actualColumnNames);

        // Verify CHECK constraint rejects invalid values like 2
        using var invalidInsert = connection.CreateCommand();
        invalidInsert.CommandText = @"
            INSERT INTO attempt_history (
                submission_id, fact_id, operation, left_operand, right_operand,
                submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                response_latency_ms, timestamp, practice_position, is_interrupted
            ) VALUES (
                'invalid-check-test', 'add:1+1', 'Addition', 1, 1,
                2, 2, 1, 1, 'Correct',
                1000, '2026-10-05T00:00:00.0000000Z', 1, 2
            );";
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await invalidInsert.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task NewAttempt_Uninterrupted_PersistsFalse()
    {
        var path = Path.Combine(_directory, "uninterrupted_attempt.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200,
            DateTimeOffset.UtcNow, practicePosition: 1, isInterrupted: false);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 1200;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        var commitResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Verify raw SQL persistence
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT is_interrupted FROM attempt_history WHERE submission_id = @id;";
        cmd.Parameters.AddWithValue("@id", subId);
        var rawValue = await cmd.ExecuteScalarAsync();
        Assert.Equal(0L, Convert.ToInt64(rawValue));

        // Verify loaded AttemptRecord
        var telemetryAttempts = await store.LoadCompleteAttemptTelemetryAsync();
        var reloaded = Assert.Single(telemetryAttempts);
        Assert.False(reloaded.IsInterrupted);
    }

    [Fact]
    public async Task NewAttempt_Interrupted_PersistsTrue()
    {
        var path = Path.Combine(_directory, "interrupted_attempt.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 4500,
            DateTimeOffset.UtcNow, practicePosition: 1, isInterrupted: true);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 4500;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        var commitResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Verify raw SQL persistence
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT is_interrupted FROM attempt_history WHERE submission_id = @id;";
        cmd.Parameters.AddWithValue("@id", subId);
        var rawValue = await cmd.ExecuteScalarAsync();
        Assert.Equal(1L, Convert.ToInt64(rawValue));

        // Verify loaded AttemptRecord
        var telemetryAttempts = await store.LoadCompleteAttemptTelemetryAsync();
        var reloaded = Assert.Single(telemetryAttempts);
        Assert.True(reloaded.IsInterrupted);
    }

    [Fact]
    public async Task InterruptedAttempt_RoundTripsThroughBoundedRecentAttempts()
    {
        var path = Path.Combine(_directory, "bounded_recent_interrupted.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 1, 0, 1, 1, true, true, 3000,
            DateTimeOffset.UtcNow, practicePosition: 1, isInterrupted: true);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 3000;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        var commitResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Load through full snapshot
        var snapshot = await store.LoadSnapshotAsync();
        var loadedAttempt = Assert.Single(snapshot.RecentAttempts);
        Assert.True(loadedAttempt.IsInterrupted);

        // Load through runtime snapshot
        var runtimeSnapshot = await store.LoadRuntimeSnapshotAsync();
        var runtimeAttempt = Assert.Single(runtimeSnapshot.RecentAttempts);
        Assert.True(runtimeAttempt.IsInterrupted);
    }

    [Fact]
    public async Task InterruptedAttempt_RoundTripsThroughCompleteAttemptHistory()
    {
        var path = Path.Combine(_directory, "complete_history_interrupted.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 1, 1, 2, 2, true, true, 2500,
            DateTimeOffset.UtcNow, practicePosition: 1,
            contextVersion: 1, presentedDeadlineMs: 3000, expectedPaceMs: 2500,
            resolvedRole: "Due", operationBandBefore: 0,
            isInterrupted: true);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 2500;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        Assert.True((await store.CommitSubmissionAsync(changeSet)).IsSuccess);

        var list = await store.LoadCompleteAttemptTelemetryAsync();
        var loaded = Assert.Single(list);
        Assert.True(loaded.IsInterrupted);
        Assert.Equal("Due", loaded.ResolvedRole);
        Assert.Equal(1, loaded.ContextVersion);
    }

    [Fact]
    public async Task InterruptedAttempt_RoundTripsThroughLatestFrontierRead()
    {
        var path = Path.Combine(_directory, "frontier_interrupted.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 2000,
            DateTimeOffset.UtcNow, practicePosition: 1,
            contextVersion: 1, presentedDeadlineMs: 3000, expectedPaceMs: 2500,
            resolvedRole: "Frontier", operationBandBefore: 0,
            isInterrupted: true);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.LastLatencyMs = 2000;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        Assert.True((await store.CommitSubmissionAsync(changeSet)).IsSuccess);

        var frontierAttempts = await store.LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation.Addition,
            bandStartedPracticePosition: 0,
            frontierFactIds: new[] { fact.Id });

        var loaded = Assert.Single(frontierAttempts);
        Assert.Equal(fact.Id, loaded.FactId);
        Assert.True(loaded.IsInterrupted);
    }

    [Fact]
    public async Task LegacyReadPath_DefaultsInterruptionFalse()
    {
        var path = Path.Combine(_directory, "legacy_unpositioned.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        // Directly insert an unpositioned attempt (practice_position IS NULL)
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position, is_interrupted
                ) VALUES (
                    'legacy-sub-1', 'add:1+2', 'Addition', 1, 2,
                    3, 3, 1, 1, 'Correct',
                    1100, '2026-09-01T12:00:00.0000000Z', NULL, 0
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            var loaded = Assert.Single(snapshot.RecentAttempts);
            Assert.Equal("legacy-sub-1", loaded.SubmissionId);
            Assert.False(loaded.IsInterrupted);
            Assert.Null(loaded.PracticePosition);
        }
    }

    [Fact]
    public async Task Migration_V7ToV8_AdvancesSchemaVersion()
    {
        var path = Path.Combine(_directory, "migrate_v7_to_v8.db");
        await CreateV7DatabaseAsync(path);

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        Assert.Equal("9", await cmd.ExecuteScalarAsync());

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        Assert.Equal(19, columns.Count);
        Assert.Contains(columns, c => string.Equals(c.Name, "is_interrupted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Migration_V7ToV8_PreservesExistingAttempts()
    {
        var path = Path.Combine(_directory, "migrate_preserve_attempts.db");
        await CreateV7DatabaseAsync(path);

        // Seed representative V7 attempt rows with rich context
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position,
                    attempt_context_version, presented_deadline_ms, expected_pace_ms,
                    resolved_role, operation_band_before
                ) VALUES (
                    'v7-sub-1', 'add:2+3', 'Addition', 2, 3,
                    5, 5, 1, 1, 'Correct',
                    1200, '2026-09-20T10:00:00.0000000Z', 1,
                    1, 3000, 2500, 'Frontier', 0
                ), (
                    'v7-sub-2', 'sub:7-4', 'Subtraction', 7, 4,
                    2, 3, 0, 0, 'Incorrect',
                    2200, '2026-09-20T10:01:00.0000000Z', 2,
                    1, NULL, 2000, 'Due', 1
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var attempts = await store.LoadCompleteAttemptTelemetryAsync();
            Assert.Equal(2, attempts.Count);

            var first = attempts[0];
            Assert.Equal("v7-sub-1", first.SubmissionId);
            Assert.Equal("add:2+3", first.FactId);
            Assert.Equal(ArithmeticOperation.Addition, first.Operation);
            Assert.Equal(2, first.LeftOperand);
            Assert.Equal(3, first.RightOperand);
            Assert.Equal(5, first.SubmittedAnswer);
            Assert.Equal(5, first.CorrectAnswer);
            Assert.True(first.IsCorrect);
            Assert.True(first.IsFluent);
            Assert.Equal(AttemptOutcome.Correct, first.Outcome);
            Assert.Equal(1200, first.ResponseLatencyMs);
            Assert.Equal(1L, first.PracticePosition);
            Assert.Equal(1, first.ContextVersion);
            Assert.Equal(3000, first.PresentedDeadlineMs);
            Assert.Equal(2500, first.ExpectedPaceMs);
            Assert.Equal("Frontier", first.ResolvedRole);
            Assert.Equal(0, first.OperationBandBefore);
            Assert.False(first.IsInterrupted);

            var second = attempts[1];
            Assert.Equal("v7-sub-2", second.SubmissionId);
            Assert.Equal(AttemptOutcome.Incorrect, second.Outcome);
            Assert.False(second.IsCorrect);
            Assert.False(second.IsFluent);
            Assert.Equal(2200, second.ResponseLatencyMs);
            Assert.Equal(2L, second.PracticePosition);
            Assert.Equal(1, second.ContextVersion);
            Assert.Null(second.PresentedDeadlineMs);
            Assert.Equal(2000, second.ExpectedPaceMs);
            Assert.Equal("Due", second.ResolvedRole);
            Assert.Equal(1, second.OperationBandBefore);
            Assert.False(second.IsInterrupted);
        }
    }

    [Fact]
    public async Task Migration_V7ToV8_DefaultsHistoricalRowsFalse()
    {
        var path = Path.Combine(_directory, "historical_default_false.db");
        await CreateV7DatabaseAsync(path);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    'v7-historical-1', 'mul:3*3', 'Multiplication', 3, 3,
                    9, 9, 1, 1, 'Correct',
                    800, '2026-09-18T10:00:00.0000000Z', 1
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            var attempt = Assert.Single(snapshot.RecentAttempts);
            Assert.False(attempt.IsInterrupted);
        }

        // Direct SQL check
        await using var verifyConn = new SqliteConnection($"Data Source={path}");
        await verifyConn.OpenAsync();
        using var verifyCmd = verifyConn.CreateCommand();
        verifyCmd.CommandText = "SELECT is_interrupted FROM attempt_history WHERE submission_id = 'v7-historical-1';";
        var isInterruptedVal = await verifyCmd.ExecuteScalarAsync();
        Assert.Equal(0L, Convert.ToInt64(isInterruptedVal));
    }

    [Fact]
    public async Task Migration_V7ToV8_PreservesHistoricalTimeout()
    {
        var path = Path.Combine(_directory, "historical_timeout.db");
        await CreateV7DatabaseAsync(path);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position,
                    attempt_context_version, presented_deadline_ms, expected_pace_ms,
                    resolved_role, operation_band_before
                ) VALUES (
                    'v7-timeout-1', 'div:12/3', 'Division', 12, 3,
                    NULL, 4, 0, 0, 'Timeout',
                    3500, '2026-09-22T08:00:00.0000000Z', 10,
                    1, 3000, 2000, 'Remediation', 1
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var attempts = await store.LoadCompleteAttemptTelemetryAsync();
            var timeoutAttempt = Assert.Single(attempts);
            Assert.Equal(AttemptOutcome.Timeout, timeoutAttempt.Outcome);
            Assert.Null(timeoutAttempt.SubmittedAnswer);
            Assert.False(timeoutAttempt.IsCorrect);
            Assert.False(timeoutAttempt.IsFluent);
            Assert.Equal(3500, timeoutAttempt.ResponseLatencyMs);
            Assert.Equal(10L, timeoutAttempt.PracticePosition);
            Assert.Equal("Remediation", timeoutAttempt.ResolvedRole);
            Assert.False(timeoutAttempt.IsInterrupted);
        }
    }

    [Fact]
    public async Task ReopenSchemaV8_IsIdempotent()
    {
        var path = Path.Combine(_directory, "reopen_v8_idempotent.db");
        await CreateV7DatabaseAsync(path);

        using (var store1 = new SqliteLearnerStore(path))
        {
            await store1.InitializeAsync();
            await store1.CloseAsync();
        }

        // Second open on migrated V8 database
        using (var store2 = new SqliteLearnerStore(path))
        {
            await store2.InitializeAsync();
            var snapshot = await store2.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            await store2.CloseAsync();
        }

        // Third open
        using (var store3 = new SqliteLearnerStore(path))
        {
            await store3.InitializeAsync();
            var snapshot = await store3.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
        }
    }

    [Fact]
    public async Task MixedInterruptedAndUninterruptedAttempts_RoundTripExactly()
    {
        var path = Path.Combine(_directory, "mixed_interrupted_uninterrupted.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact1 = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var fact2 = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var subId1 = "sub-uninterrupted";
        var subId2 = "sub-interrupted";

        var attempt1 = new AttemptRecord(
            subId1, fact1.Id, fact1.Operation, 0, 1, 1, 1, true, true, 1000,
            DateTimeOffset.UtcNow, practicePosition: 1, isInterrupted: false);
        var item1 = ItemLearningState.CreateNew(fact1);
        item1.TotalAttempts = 1;
        item1.CorrectAttempts = 1;
        item1.ConsecutiveCorrectStreak = 1;
        item1.LastLatencyMs = 1000;

        var prog1 = LearnerProgression.CreateFresh();
        prog1.PracticePosition = 1;

        Assert.True((await store.CommitSubmissionAsync(new SubmissionChangeSet(subId1, 1, attempt1, item1, prog1))).IsSuccess);

        var attempt2 = new AttemptRecord(
            subId2, fact2.Id, fact2.Operation, 1, 0, 1, 1, true, true, 5000,
            DateTimeOffset.UtcNow, practicePosition: 2, isInterrupted: true);
        var item2 = ItemLearningState.CreateNew(fact2);
        item2.TotalAttempts = 1;
        item2.CorrectAttempts = 1;
        item2.ConsecutiveCorrectStreak = 1;
        item2.LastLatencyMs = 5000;

        var prog2 = LearnerProgression.CreateFresh();
        prog2.PracticePosition = 2;

        Assert.True((await store.CommitSubmissionAsync(new SubmissionChangeSet(subId2, 2, attempt2, item2, prog2))).IsSuccess);

        var telemetry = await store.LoadCompleteAttemptTelemetryAsync();
        Assert.Equal(2, telemetry.Count);
        var loaded1 = telemetry.First(a => a.SubmissionId == subId1);
        var loaded2 = telemetry.First(a => a.SubmissionId == subId2);
        Assert.False(loaded1.IsInterrupted);
        Assert.True(loaded2.IsInterrupted);

        var snapshot = await store.LoadSnapshotAsync();
        var snap1 = snapshot.RecentAttempts.First(a => a.SubmissionId == subId1);
        var snap2 = snapshot.RecentAttempts.First(a => a.SubmissionId == subId2);
        Assert.False(snap1.IsInterrupted);
        Assert.True(snap2.IsInterrupted);
    }

    [Fact]
    public async Task Slice1RuntimeSubmission_InterruptedAttempt_PersistsTrue()
    {
        var path = Path.Combine(_directory, "runtime_interrupted.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: true);

        // Simulate interruption while awaiting answer
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.False(session.IsCurrentAttemptInterrupted);

        session.PausePractice();
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);

        // Verify stored attempt
        var loaded = await store.LoadCompleteAttemptTelemetryAsync();
        var recordedAttempt = Assert.Single(loaded);
        Assert.True(recordedAttempt.IsInterrupted);

        var snapshot = await store.LoadSnapshotAsync();
        var snapAttempt = Assert.Single(snapshot.RecentAttempts);
        Assert.True(snapAttempt.IsInterrupted);
    }

    [Fact]
    public async Task Slice1RuntimeSubmission_UninterruptedAttempt_PersistsFalse()
    {
        var path = Path.Combine(_directory, "runtime_uninterrupted.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: true);

        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.False(session.IsCurrentAttemptInterrupted);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);

        // Verify stored attempt
        var loaded = await store.LoadCompleteAttemptTelemetryAsync();
        var recordedAttempt = Assert.Single(loaded);
        Assert.False(recordedAttempt.IsInterrupted);

        var snapshot = await store.LoadSnapshotAsync();
        var snapAttempt = Assert.Single(snapshot.RecentAttempts);
        Assert.False(snapAttempt.IsInterrupted);
    }

    private static async Task<List<(string Name, string Type, bool NotNull, string? DefaultValue)>> GetTableColumnsAsync(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<(string Name, string Type, bool NotNull, string? DefaultValue)>();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetString(4);
            columns.Add((name, type, notNull, defaultValue));
        }
        return columns;
    }

    private static async Task CreateV7DatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT INTO schema_info VALUES ('schema_version', '7');
            INSERT INTO schema_info VALUES ('store_revision', '1');

            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );
            INSERT INTO learner_progression VALUES (1, 0, '2026-09-10T00:00:00.0000000Z');

            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
            );
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);

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

            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY,
                fact_id TEXT NOT NULL,
                operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL,
                submitted_answer INTEGER,
                correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL,
                is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1))
                    CHECK (is_fluent = 0 OR (is_correct = 1 AND outcome = 'Correct')),
                outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0),
                attempt_context_version INTEGER,
                presented_deadline_ms INTEGER,
                expected_pace_ms INTEGER,
                resolved_role TEXT,
                operation_band_before INTEGER
            );

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

            CREATE UNIQUE INDEX ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_fact_position
                ON attempt_history(operation, fact_id, practice_position DESC) WHERE practice_position IS NOT NULL;
        ";
        await cmd.ExecuteNonQueryAsync();
    }
}
