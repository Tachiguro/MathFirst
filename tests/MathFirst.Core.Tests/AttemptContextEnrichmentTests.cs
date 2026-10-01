namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class AttemptContextEnrichmentTests
{
    private static AttemptRecord CreateAttempt(
        int? contextVersion = null,
        int? presentedDeadlineMs = null,
        int? expectedPaceMs = null,
        string? resolvedRole = null,
        int? operationBandBefore = null,
        bool isCorrect = true,
        int? submittedAnswer = 4,
        AttemptOutcome? outcome = null,
        long? practicePosition = 1)
    {
        return new AttemptRecord(
            submissionId: Guid.NewGuid().ToString("N"),
            factId: "2+2=4",
            operation: ArithmeticOperation.Addition,
            leftOperand: 2,
            rightOperand: 2,
            submittedAnswer: submittedAnswer,
            correctAnswer: 4,
            isCorrect: isCorrect,
            isFluent: false,
            responseLatencyMs: 1200,
            timestamp: DateTimeOffset.UtcNow,
            outcome: outcome,
            practicePosition: practicePosition,
            contextVersion: contextVersion,
            presentedDeadlineMs: presentedDeadlineMs,
            expectedPaceMs: expectedPaceMs,
            resolvedRole: resolvedRole,
            operationBandBefore: operationBandBefore);
    }

    [Fact]
    public void Constructor_LegacyAttempt_ContextPropertiesAreNull()
    {
        var attempt = new AttemptRecord(
            submissionId: "sub-1",
            factId: "2+2=4",
            operation: ArithmeticOperation.Addition,
            leftOperand: 2,
            rightOperand: 2,
            submittedAnswer: 4,
            correctAnswer: 4,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1000,
            timestamp: DateTimeOffset.UtcNow);

        Assert.Null(attempt.ContextVersion);
        Assert.Null(attempt.PresentedDeadlineMs);
        Assert.Null(attempt.ExpectedPaceMs);
        Assert.Null(attempt.ResolvedRole);
        Assert.Null(attempt.OperationBandBefore);
    }

    [Fact]
    public void Constructor_ContextVersion1_WithValidEnrichedContext_AssignsProperties()
    {
        var attempt = CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0);

        Assert.Equal(1, attempt.ContextVersion);
        Assert.Equal(3000, attempt.PresentedDeadlineMs);
        Assert.Equal(2500, attempt.ExpectedPaceMs);
        Assert.Equal("New", attempt.ResolvedRole);
        Assert.Equal(0, attempt.OperationBandBefore);
    }

    [Fact]
    public void Constructor_ContextVersionZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 0,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersionTwo_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 2,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithZeroExpectedPace_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 0,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNegativeExpectedPace_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: -500,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNullExpectedPace_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: null,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithZeroDeadline_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 0,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNegativeDeadline_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: -1000,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNullDeadline_IsValidNoTimePressureContext()
    {
        var attempt = CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: null,
            expectedPaceMs: 2500,
            resolvedRole: "Due",
            operationBandBefore: 1);

        Assert.Equal(1, attempt.ContextVersion);
        Assert.Null(attempt.PresentedDeadlineMs);
        Assert.Equal(2500, attempt.ExpectedPaceMs);
        Assert.Equal("Due", attempt.ResolvedRole);
        Assert.Equal(1, attempt.OperationBandBefore);
    }

    [Fact]
    public void Constructor_ContextVersion1_WithZeroBand_IsValid()
    {
        var attempt = CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "Frontier",
            operationBandBefore: 0);

        Assert.Equal(0, attempt.OperationBandBefore);
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNegativeBand_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "Frontier",
            operationBandBefore: -1));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNullBand_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "Frontier",
            operationBandBefore: null));
    }

    [Fact]
    public void Constructor_ContextNull_WithAnyCompanionValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CreateAttempt(contextVersion: null, presentedDeadlineMs: 3000));
        Assert.Throws<ArgumentException>(() => CreateAttempt(contextVersion: null, expectedPaceMs: 2500));
        Assert.Throws<ArgumentException>(() => CreateAttempt(contextVersion: null, resolvedRole: "New"));
        Assert.Throws<ArgumentException>(() => CreateAttempt(contextVersion: null, operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithNullRole_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: null,
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithBlankRole_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "",
            operationBandBefore: 0));

        Assert.Throws<ArgumentException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "   ",
            operationBandBefore: 0));
    }

    [Theory]
    [InlineData("Acquisition")]
    [InlineData("Review")]
    [InlineData("Retry")]
    [InlineData("Consolidation")]
    public void Constructor_ContextVersion1_WithUnrecognizedRole_ThrowsArgumentOutOfRangeException(string unrecognizedRole)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: unrecognizedRole,
            operationBandBefore: 0));
    }

    [Fact]
    public void Constructor_ContextVersion1_WithAnyMaterializedRole_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAttempt(
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "AnyMaterialized",
            operationBandBefore: 0));
    }

    [Fact]
    public void AttemptRecord_IsImmutable_HasNoPublicSettersOrInitAccessors()
    {
        var properties = typeof(AttemptRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.True(setter is null, $"Property {property.Name} should not have a public setter or init accessor.");
        }
    }

    [Fact]
    public async Task TrainingSession_CapturesPresentationContext_AndAttachesToCorrectAttempt()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

            Assert.NotNull(eval.ChangeSet);
            var attempt = eval.ChangeSet.Attempt;
            Assert.Equal(1, attempt.ContextVersion);
            Assert.NotNull(attempt.PresentedDeadlineMs);
            Assert.True(attempt.PresentedDeadlineMs > 0);
            Assert.True(attempt.ExpectedPaceMs > 0);
            Assert.NotNull(attempt.ResolvedRole);
            Assert.Equal("New", attempt.ResolvedRole);
            Assert.True(attempt.OperationBandBefore >= 0);
            Assert.True(attempt.IsCorrect);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TrainingSession_CapturesPresentationContext_AndAttachesToIncorrectAttempt()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);

            Assert.NotNull(eval.ChangeSet);
            var attempt = eval.ChangeSet.Attempt;
            Assert.Equal(1, attempt.ContextVersion);
            Assert.NotNull(attempt.PresentedDeadlineMs);
            Assert.True(attempt.PresentedDeadlineMs > 0);
            Assert.True(attempt.ExpectedPaceMs > 0);
            Assert.NotNull(attempt.ResolvedRole);
            Assert.True(attempt.OperationBandBefore >= 0);
            Assert.False(attempt.IsCorrect);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TrainingSession_CapturesPresentationContext_AndAttachesToTimeoutAttemptWithNullSubmittedAnswer()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            var eval = session.SubmitTimeout();

            Assert.NotNull(eval.ChangeSet);
            var attempt = eval.ChangeSet.Attempt;
            Assert.Equal(1, attempt.ContextVersion);
            Assert.Null(attempt.SubmittedAnswer);
            Assert.Equal(AttemptOutcome.Timeout, attempt.Outcome);
            Assert.NotNull(attempt.PresentedDeadlineMs);
            Assert.True(attempt.PresentedDeadlineMs > 0);
            Assert.True(attempt.ExpectedPaceMs > 0);
            Assert.NotNull(attempt.ResolvedRole);
            Assert.True(attempt.OperationBandBefore >= 0);
            Assert.False(attempt.IsCorrect);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TrainingSession_MissingPresentationSnapshot_RecordsAllFiveContextValuesAsNullWithoutFabricatingV1()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            session.DiscardPresentationSnapshot();
            var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

            Assert.NotNull(eval.ChangeSet);
            var attempt = eval.ChangeSet.Attempt;
            Assert.Null(attempt.ContextVersion);
            Assert.Null(attempt.PresentedDeadlineMs);
            Assert.Null(attempt.ExpectedPaceMs);
            Assert.Null(attempt.ResolvedRole);
            Assert.Null(attempt.OperationBandBefore);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task TrainingSession_UnsubmittedExerciseDiscard_DiscardsPresentationSnapshotWithoutPersistence()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            Assert.NotNull(session.CurrentPresentationContext);
            session.DiscardPresentationSnapshot();
            Assert.Null(session.CurrentPresentationContext);

            var snapshot = await store.LoadSnapshotAsync();
            Assert.Empty(snapshot.RecentAttempts);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task Persistence_EnrichedAttempt_InsertsAndReadsBackAllFiveContextColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SqliteLearnerStore(dbPath))
            {
                var session = new TrainingSession(store);
                await session.InitializeAsync();

                var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
                var attempt = eval.ChangeSet!.Attempt;

                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitResult.IsSuccess);

                var reloadedSnapshot = await store.LoadSnapshotAsync();
                var loadedAttempt = Assert.Single(reloadedSnapshot.RecentAttempts);

                Assert.Equal(attempt.SubmissionId, loadedAttempt.SubmissionId);
                Assert.Equal(1, loadedAttempt.ContextVersion);
                Assert.Equal(attempt.PresentedDeadlineMs, loadedAttempt.PresentedDeadlineMs);
                Assert.Equal(attempt.ExpectedPaceMs, loadedAttempt.ExpectedPaceMs);
                Assert.Equal(attempt.ResolvedRole, loadedAttempt.ResolvedRole);
                Assert.Equal(attempt.OperationBandBefore, loadedAttempt.OperationBandBefore);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task Persistence_LegacyAttempt_ReadsBackNullContextColumns()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mathfirst_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SqliteLearnerStore(dbPath))
            {
                await store.InitializeAsync();
            }

            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
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
                        'legacy-sub-1', '2+2=4', 'Addition', 2, 2,
                        4, 4, 1, 1, 'Correct',
                        1000, '2026-10-01T00:00:00.0000000+00:00', 1,
                        NULL, NULL, NULL,
                        NULL, NULL
                    );
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var store = new SqliteLearnerStore(dbPath))
            {
                await store.InitializeAsync();
                var snapshot = await store.LoadSnapshotAsync();
                var loadedAttempt = Assert.Single(snapshot.RecentAttempts);

                Assert.Equal("legacy-sub-1", loadedAttempt.SubmissionId);
                Assert.Null(loadedAttempt.ContextVersion);
                Assert.Null(loadedAttempt.PresentedDeadlineMs);
                Assert.Null(loadedAttempt.ExpectedPaceMs);
                Assert.Null(loadedAttempt.ResolvedRole);
                Assert.Null(loadedAttempt.OperationBandBefore);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch { }
        }
    }
}
