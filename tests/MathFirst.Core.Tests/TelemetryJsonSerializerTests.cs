namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class TelemetryJsonSerializerTests
{
    private static readonly string[] ExpectedEnvelopeProperties =
    {
        "schema_version",
        "exported_at",
        "app_version",
        "build_classification",
        "installation_id",
        "attempt_count",
        "attempts"
    };

    private static readonly string[] ExpectedAttemptProperties =
    {
        "fact_id",
        "operation",
        "left_operand",
        "right_operand",
        "submitted_answer",
        "outcome",
        "is_fluent",
        "response_latency_ms",
        "timestamp",
        "practice_position",
        "context_version",
        "presented_deadline_ms",
        "expected_pace_ms",
        "resolved_role",
        "operation_band_before",
        "is_interrupted"
    };

    private static AttemptRecord CreateAttempt(
        string submissionId = "sub-test-1",
        string factId = "add:2+3",
        ArithmeticOperation operation = ArithmeticOperation.Addition,
        int leftOperand = 2,
        int rightOperand = 3,
        int? submittedAnswer = 5,
        int correctAnswer = 5,
        bool isCorrect = true,
        bool isFluent = true,
        long responseLatencyMs = 1200,
        DateTimeOffset? timestamp = null,
        AttemptOutcome? outcome = null,
        long? practicePosition = 1,
        int? contextVersion = null,
        int? presentedDeadlineMs = null,
        int? expectedPaceMs = null,
        string? resolvedRole = null,
        int? operationBandBefore = null,
        bool isInterrupted = false)
    {
        return new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: operation,
            leftOperand: leftOperand,
            rightOperand: rightOperand,
            submittedAnswer: submittedAnswer,
            correctAnswer: correctAnswer,
            isCorrect: isCorrect,
            isFluent: isFluent,
            responseLatencyMs: responseLatencyMs,
            timestamp: timestamp ?? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            outcome: outcome,
            practicePosition: practicePosition,
            contextVersion: contextVersion,
            presentedDeadlineMs: presentedDeadlineMs,
            expectedPaceMs: expectedPaceMs,
            resolvedRole: resolvedRole,
            operationBandBefore: operationBandBefore,
            isInterrupted: isInterrupted);
    }

    private static async Task<string> SerializeToStringAsync(
        ITelemetryJsonSerializer serializer,
        string installationId = "inst-test-id",
        string appVersion = "1.0.0",
        string buildClassification = "Production",
        DateTimeOffset? exportedAt = null,
        IReadOnlyList<AttemptRecord>? attempts = null)
    {
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(
            stream,
            installationId,
            appVersion,
            buildClassification,
            exportedAt ?? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            attempts ?? Array.Empty<AttemptRecord>());
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AssertPropertyOrder(JsonElement element, params string[] expectedProperties)
    {
        var actualProperties = element.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(expectedProperties, actualProperties);
    }

    [Fact]
    public async Task SerializeAsync_TopLevelEnvelope_ContainsExactSevenProperties()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt();
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
    }

    [Fact]
    public async Task SerializeAsync_SchemaVersion_IsIntegerTwo()
    {
        var serializer = new TelemetryJsonSerializer();
        var json = await SerializeToStringAsync(serializer);

        using var doc = JsonDocument.Parse(json);
        var schemaVersionProp = doc.RootElement.GetProperty("schema_version");
        Assert.Equal(JsonValueKind.Number, schemaVersionProp.ValueKind);
        Assert.Equal(2, schemaVersionProp.GetInt32());
    }

    [Fact]
    public async Task SerializeAsync_AttemptCount_MatchesAttemptsArrayLength()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempts = new[]
        {
            CreateAttempt(submissionId: "s1", leftOperand: 1, rightOperand: 1, submittedAnswer: 2, correctAnswer: 2),
            CreateAttempt(submissionId: "s2", leftOperand: 2, rightOperand: 2, submittedAnswer: 4, correctAnswer: 4),
            CreateAttempt(submissionId: "s3", leftOperand: 3, rightOperand: 3, submittedAnswer: 6, correctAnswer: 6)
        };

        var json = await SerializeToStringAsync(serializer, attempts: attempts);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("attempt_count").GetInt32());
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.Equal(3, attemptsArray.GetArrayLength());

        // Verify caller ordering is preserved
        Assert.Equal(1, attemptsArray[0].GetProperty("left_operand").GetInt32());
        Assert.Equal(2, attemptsArray[1].GetProperty("left_operand").GetInt32());
        Assert.Equal(3, attemptsArray[2].GetProperty("left_operand").GetInt32());
    }

    [Fact]
    public async Task SerializeAsync_EmptyAttempts_ProducesValidEnvelopeWithZeroAttemptCount()
    {
        var serializer = new TelemetryJsonSerializer();
        var json = await SerializeToStringAsync(serializer, attempts: Array.Empty<AttemptRecord>());

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        Assert.Equal(0, doc.RootElement.GetProperty("attempt_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("attempts").GetArrayLength());
    }

    [Fact]
    public async Task SerializeAsync_AttemptObject_ContainsExactSixteenProperties()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt();
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(attemptElement, ExpectedAttemptProperties);
        Assert.Equal(16, attemptElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task SerializeAsync_CanonicalOperations_AreExactEnumNames()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempts = new[]
        {
            CreateAttempt(submissionId: "a1", factId: "add:2+3", operation: ArithmeticOperation.Addition, leftOperand: 2, rightOperand: 3, submittedAnswer: 5, correctAnswer: 5),
            CreateAttempt(submissionId: "a2", factId: "sub:9-4", operation: ArithmeticOperation.Subtraction, leftOperand: 9, rightOperand: 4, submittedAnswer: 5, correctAnswer: 5),
            CreateAttempt(submissionId: "a3", factId: "mul:6*7", operation: ArithmeticOperation.Multiplication, leftOperand: 6, rightOperand: 7, submittedAnswer: 42, correctAnswer: 42),
            CreateAttempt(submissionId: "a4", factId: "div:12/3", operation: ArithmeticOperation.Division, leftOperand: 12, rightOperand: 3, submittedAnswer: 4, correctAnswer: 4)
        };

        var json = await SerializeToStringAsync(serializer, attempts: attempts);

        using var doc = JsonDocument.Parse(json);
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.Equal("Addition", attemptsArray[0].GetProperty("operation").GetString());
        Assert.Equal("Subtraction", attemptsArray[1].GetProperty("operation").GetString());
        Assert.Equal("Multiplication", attemptsArray[2].GetProperty("operation").GetString());
        Assert.Equal("Division", attemptsArray[3].GetProperty("operation").GetString());
    }

    [Fact]
    public async Task SerializeAsync_CanonicalOutcomes_AreExactEnumNames()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempts = new[]
        {
            CreateAttempt(submissionId: "o1", submittedAnswer: 5, correctAnswer: 5, isCorrect: true, isFluent: true, outcome: AttemptOutcome.Correct),
            CreateAttempt(submissionId: "o2", submittedAnswer: 9, correctAnswer: 5, isCorrect: false, isFluent: false, outcome: AttemptOutcome.Incorrect),
            CreateAttempt(submissionId: "o3", submittedAnswer: null, correctAnswer: 5, isCorrect: false, isFluent: false, outcome: AttemptOutcome.Timeout)
        };

        var json = await SerializeToStringAsync(serializer, attempts: attempts);

        using var doc = JsonDocument.Parse(json);
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.Equal("Correct", attemptsArray[0].GetProperty("outcome").GetString());
        Assert.Equal("Incorrect", attemptsArray[1].GetProperty("outcome").GetString());
        Assert.Equal("Timeout", attemptsArray[2].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task SerializeAsync_CanonicalFactIds_MatchOperandTemplates()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempts = new[]
        {
            CreateAttempt(submissionId: "f1", factId: "add:2+3", operation: ArithmeticOperation.Addition, leftOperand: 2, rightOperand: 3, submittedAnswer: 5, correctAnswer: 5),
            CreateAttempt(submissionId: "f2", factId: "sub:9-4", operation: ArithmeticOperation.Subtraction, leftOperand: 9, rightOperand: 4, submittedAnswer: 5, correctAnswer: 5),
            CreateAttempt(submissionId: "f3", factId: "mul:6*7", operation: ArithmeticOperation.Multiplication, leftOperand: 6, rightOperand: 7, submittedAnswer: 42, correctAnswer: 42),
            CreateAttempt(submissionId: "f4", factId: "div:12/3", operation: ArithmeticOperation.Division, leftOperand: 12, rightOperand: 3, submittedAnswer: 4, correctAnswer: 4)
        };

        var json = await SerializeToStringAsync(serializer, attempts: attempts);

        using var doc = JsonDocument.Parse(json);
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.Equal("add:2+3", attemptsArray[0].GetProperty("fact_id").GetString());
        Assert.Equal("sub:9-4", attemptsArray[1].GetProperty("fact_id").GetString());
        Assert.Equal("mul:6*7", attemptsArray[2].GetProperty("fact_id").GetString());
        Assert.Equal("div:12/3", attemptsArray[3].GetProperty("fact_id").GetString());
    }

    [Fact]
    public async Task SerializeAsync_TimeoutAttempt_SerializesSubmittedAnswerAsNull()
    {
        var serializer = new TelemetryJsonSerializer();
        var timeoutAttempt = CreateAttempt(
            submissionId: "t1",
            submittedAnswer: null,
            correctAnswer: 10,
            isCorrect: false,
            isFluent: false,
            outcome: AttemptOutcome.Timeout);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { timeoutAttempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        Assert.Equal(JsonValueKind.Null, attemptElement.GetProperty("submitted_answer").ValueKind);
        Assert.Equal("Timeout", attemptElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task SerializeAsync_ExportedAt_IsUtcRoundTripOString()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T14:30:00.1234567+02:00", CultureInfo.InvariantCulture);

        var json = await SerializeToStringAsync(serializer, exportedAt: exportedAt);

        using var doc = JsonDocument.Parse(json);
        var exportedAtString = doc.RootElement.GetProperty("exported_at").GetString();
        Assert.NotNull(exportedAtString);
        Assert.Equal("2026-10-01T12:30:00.1234567+00:00", exportedAtString);
        Assert.EndsWith("+00:00", exportedAtString, StringComparison.Ordinal);
        Assert.False(exportedAtString.EndsWith("Z", StringComparison.Ordinal));
        Assert.False(exportedAtString.EndsWith("+02:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SerializeAsync_AttemptTimestamp_IsUtcRoundTripOString()
    {
        var serializer = new TelemetryJsonSerializer();
        var timestamp = DateTimeOffset.Parse("2026-09-30T18:45:00.9876543-04:00", CultureInfo.InvariantCulture);
        var attempt = CreateAttempt(timestamp: timestamp);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var timestampString = doc.RootElement.GetProperty("attempts")[0].GetProperty("timestamp").GetString();
        Assert.NotNull(timestampString);
        Assert.Equal("2026-09-30T22:45:00.9876543+00:00", timestampString);
        Assert.EndsWith("+00:00", timestampString, StringComparison.Ordinal);
        Assert.False(timestampString.EndsWith("Z", StringComparison.Ordinal));
        Assert.False(timestampString.EndsWith("-04:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SerializeAsync_TimestampsRoundTripWithDateTimeOffsetRoundtripKind()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T14:30:00.1234567+02:00", CultureInfo.InvariantCulture);
        var attemptTime = DateTimeOffset.Parse("2026-09-30T18:45:00.9876543-04:00", CultureInfo.InvariantCulture);
        var attempt = CreateAttempt(timestamp: attemptTime);

        var json = await SerializeToStringAsync(serializer, exportedAt: exportedAt, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var exportedAtStr = doc.RootElement.GetProperty("exported_at").GetString()!;
        var attemptTimeStr = doc.RootElement.GetProperty("attempts")[0].GetProperty("timestamp").GetString()!;

        var parsedExportedAt = DateTimeOffset.ParseExact(
            exportedAtStr,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        var parsedAttemptTime = DateTimeOffset.ParseExact(
            attemptTimeStr,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

        Assert.Equal(TimeSpan.Zero, parsedExportedAt.Offset);
        Assert.Equal(TimeSpan.Zero, parsedAttemptTime.Offset);
        Assert.Equal(exportedAt.ToUniversalTime(), parsedExportedAt);
        Assert.Equal(attemptTime.ToUniversalTime(), parsedAttemptTime);
    }

    [Fact]
    public async Task SerializeAsync_ExcludesInternalAndProhibitedFields()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(
            submissionId: "unique-sub-id-12345",
            correctAnswer: 999);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        // String checks for prohibited property names and values
        Assert.DoesNotContain("submission_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unique-sub-id-12345", json, StringComparison.Ordinal);
        Assert.DoesNotContain("correct_answer", json, StringComparison.Ordinal);
        Assert.DoesNotContain("is_correct", json, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt_text", json, StringComparison.Ordinal);
        Assert.DoesNotContain("study_state_before", json, StringComparison.Ordinal);
        Assert.DoesNotContain("attempted_at_utc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fsrs_state", json, StringComparison.Ordinal);
        Assert.DoesNotContain("scheduled_days", json, StringComparison.Ordinal);
        Assert.DoesNotContain("elapsed_milliseconds", json, StringComparison.Ordinal);
        Assert.DoesNotContain("export_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("exported_at_utc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("generated_at", json, StringComparison.Ordinal);
        Assert.DoesNotContain("database_schema_version", json, StringComparison.Ordinal);
        Assert.DoesNotContain("device_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("user_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("account_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("session_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("is_timing_eligible", json, StringComparison.Ordinal);
        Assert.DoesNotContain("timing_eligible", json, StringComparison.Ordinal);

        // Structural verification: exact 7 and 16 properties
        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        AssertPropertyOrder(doc.RootElement.GetProperty("attempts")[0], ExpectedAttemptProperties);
    }

    [Fact]
    public async Task SerializeAsync_ExampleA_PreV5LegacyAttempt_MatchesCanonicalJson()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T00:00:00.0000000+00:00", CultureInfo.InvariantCulture);
        var attemptTimestamp = DateTimeOffset.Parse("2026-09-30T23:59:00.0000000+00:00", CultureInfo.InvariantCulture);

        var legacyAttempt = CreateAttempt(
            submissionId: "sub-legacy-1",
            factId: "add:2+3",
            operation: ArithmeticOperation.Addition,
            leftOperand: 2,
            rightOperand: 3,
            submittedAnswer: 5,
            correctAnswer: 5,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1500,
            timestamp: attemptTimestamp,
            outcome: AttemptOutcome.Correct,
            practicePosition: null,
            contextVersion: null,
            presentedDeadlineMs: null,
            expectedPaceMs: null,
            resolvedRole: null,
            operationBandBefore: null,
            isInterrupted: false);

        var json = await SerializeToStringAsync(
            serializer,
            installationId: "inst-example-a",
            appVersion: "1.0.0",
            buildClassification: "Production",
            exportedAt: exportedAt,
            attempts: new[] { legacyAttempt });

        const string expectedJson = "{\"schema_version\":2,\"exported_at\":\"2026-10-01T00:00:00.0000000+00:00\",\"app_version\":\"1.0.0\",\"build_classification\":\"Production\",\"installation_id\":\"inst-example-a\",\"attempt_count\":1,\"attempts\":[{\"fact_id\":\"add:2+3\",\"operation\":\"Addition\",\"left_operand\":2,\"right_operand\":3,\"submitted_answer\":5,\"outcome\":\"Correct\",\"is_fluent\":true,\"response_latency_ms\":1500,\"timestamp\":\"2026-09-30T23:59:00.0000000+00:00\",\"practice_position\":null,\"context_version\":null,\"presented_deadline_ms\":null,\"expected_pace_ms\":null,\"resolved_role\":null,\"operation_band_before\":null,\"is_interrupted\":false}]}";

        Assert.Equal(expectedJson, json);

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        var att = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(att, ExpectedAttemptProperties);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("practice_position").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("context_version").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("presented_deadline_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("expected_pace_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("resolved_role").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("operation_band_before").ValueKind);
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task SerializeAsync_ExampleB_V5V6PositionedLegacyAttempt_MatchesCanonicalJson()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T01:00:00.0000000+00:00", CultureInfo.InvariantCulture);
        var attemptTimestamp = DateTimeOffset.Parse("2026-10-01T00:30:00.0000000+00:00", CultureInfo.InvariantCulture);

        var positionedLegacyAttempt = CreateAttempt(
            submissionId: "sub-legacy-2",
            factId: "sub:9-4",
            operation: ArithmeticOperation.Subtraction,
            leftOperand: 9,
            rightOperand: 4,
            submittedAnswer: 5,
            correctAnswer: 5,
            isCorrect: true,
            isFluent: false,
            responseLatencyMs: 2100,
            timestamp: attemptTimestamp,
            outcome: AttemptOutcome.Correct,
            practicePosition: 42,
            contextVersion: null,
            presentedDeadlineMs: null,
            expectedPaceMs: null,
            resolvedRole: null,
            operationBandBefore: null,
            isInterrupted: false);

        var json = await SerializeToStringAsync(
            serializer,
            installationId: "inst-example-b",
            appVersion: "1.1.0",
            buildClassification: "Production",
            exportedAt: exportedAt,
            attempts: new[] { positionedLegacyAttempt });

        const string expectedJson = "{\"schema_version\":2,\"exported_at\":\"2026-10-01T01:00:00.0000000+00:00\",\"app_version\":\"1.1.0\",\"build_classification\":\"Production\",\"installation_id\":\"inst-example-b\",\"attempt_count\":1,\"attempts\":[{\"fact_id\":\"sub:9-4\",\"operation\":\"Subtraction\",\"left_operand\":9,\"right_operand\":4,\"submitted_answer\":5,\"outcome\":\"Correct\",\"is_fluent\":false,\"response_latency_ms\":2100,\"timestamp\":\"2026-10-01T00:30:00.0000000+00:00\",\"practice_position\":42,\"context_version\":null,\"presented_deadline_ms\":null,\"expected_pace_ms\":null,\"resolved_role\":null,\"operation_band_before\":null,\"is_interrupted\":false}]}";

        Assert.Equal(expectedJson, json);

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        var att = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(att, ExpectedAttemptProperties);
        Assert.Equal(42, att.GetProperty("practice_position").GetInt64());
        Assert.Equal(JsonValueKind.Null, att.GetProperty("context_version").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("presented_deadline_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("expected_pace_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("resolved_role").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("operation_band_before").ValueKind);
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task SerializeAsync_ExampleC_EnrichedTimedAttempt_MatchesCanonicalJson()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T02:00:00.0000000+00:00", CultureInfo.InvariantCulture);
        var attemptTimestamp = DateTimeOffset.Parse("2026-10-01T01:45:00.0000000+00:00", CultureInfo.InvariantCulture);

        var enrichedTimedAttempt = CreateAttempt(
            submissionId: "sub-enriched-1",
            factId: "mul:6*7",
            operation: ArithmeticOperation.Multiplication,
            leftOperand: 6,
            rightOperand: 7,
            submittedAnswer: 42,
            correctAnswer: 42,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 1800,
            timestamp: attemptTimestamp,
            outcome: AttemptOutcome.Correct,
            practicePosition: 101,
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2500,
            resolvedRole: "Due",
            operationBandBefore: 2,
            isInterrupted: false);

        var json = await SerializeToStringAsync(
            serializer,
            installationId: "inst-example-c",
            appVersion: "2.0.0",
            buildClassification: "Production",
            exportedAt: exportedAt,
            attempts: new[] { enrichedTimedAttempt });

        const string expectedJson = "{\"schema_version\":2,\"exported_at\":\"2026-10-01T02:00:00.0000000+00:00\",\"app_version\":\"2.0.0\",\"build_classification\":\"Production\",\"installation_id\":\"inst-example-c\",\"attempt_count\":1,\"attempts\":[{\"fact_id\":\"mul:6*7\",\"operation\":\"Multiplication\",\"left_operand\":6,\"right_operand\":7,\"submitted_answer\":42,\"outcome\":\"Correct\",\"is_fluent\":true,\"response_latency_ms\":1800,\"timestamp\":\"2026-10-01T01:45:00.0000000+00:00\",\"practice_position\":101,\"context_version\":1,\"presented_deadline_ms\":3000,\"expected_pace_ms\":2500,\"resolved_role\":\"Due\",\"operation_band_before\":2,\"is_interrupted\":false}]}";

        Assert.Equal(expectedJson, json);

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        var att = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(att, ExpectedAttemptProperties);
        Assert.Equal(101, att.GetProperty("practice_position").GetInt64());
        Assert.Equal(1, att.GetProperty("context_version").GetInt32());
        Assert.Equal(3000, att.GetProperty("presented_deadline_ms").GetInt32());
        Assert.Equal(2500, att.GetProperty("expected_pace_ms").GetInt32());
        Assert.Equal("Due", att.GetProperty("resolved_role").GetString());
        Assert.Equal(2, att.GetProperty("operation_band_before").GetInt32());
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task SerializeAsync_ExampleD_EnrichedNoTimePressureAttempt_MatchesCanonicalJson()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T03:00:00.0000000+00:00", CultureInfo.InvariantCulture);
        var attemptTimestamp = DateTimeOffset.Parse("2026-10-01T02:30:00.0000000+00:00", CultureInfo.InvariantCulture);

        var enrichedNtpAttempt = CreateAttempt(
            submissionId: "sub-enriched-2",
            factId: "div:12/3",
            operation: ArithmeticOperation.Division,
            leftOperand: 12,
            rightOperand: 3,
            submittedAnswer: 4,
            correctAnswer: 4,
            isCorrect: true,
            isFluent: false,
            responseLatencyMs: 2200,
            timestamp: attemptTimestamp,
            outcome: AttemptOutcome.Correct,
            practicePosition: 50,
            contextVersion: 1,
            presentedDeadlineMs: null,
            expectedPaceMs: 2500,
            resolvedRole: "New",
            operationBandBefore: 0,
            isInterrupted: false);

        var json = await SerializeToStringAsync(
            serializer,
            installationId: "inst-example-d",
            appVersion: "2.0.0",
            buildClassification: "Production",
            exportedAt: exportedAt,
            attempts: new[] { enrichedNtpAttempt });

        const string expectedJson = "{\"schema_version\":2,\"exported_at\":\"2026-10-01T03:00:00.0000000+00:00\",\"app_version\":\"2.0.0\",\"build_classification\":\"Production\",\"installation_id\":\"inst-example-d\",\"attempt_count\":1,\"attempts\":[{\"fact_id\":\"div:12/3\",\"operation\":\"Division\",\"left_operand\":12,\"right_operand\":3,\"submitted_answer\":4,\"outcome\":\"Correct\",\"is_fluent\":false,\"response_latency_ms\":2200,\"timestamp\":\"2026-10-01T02:30:00.0000000+00:00\",\"practice_position\":50,\"context_version\":1,\"presented_deadline_ms\":null,\"expected_pace_ms\":2500,\"resolved_role\":\"New\",\"operation_band_before\":0,\"is_interrupted\":false}]}";

        Assert.Equal(expectedJson, json);

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        var att = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(att, ExpectedAttemptProperties);
        Assert.Equal(50, att.GetProperty("practice_position").GetInt64());
        Assert.Equal(1, att.GetProperty("context_version").GetInt32());
        Assert.Equal(JsonValueKind.Null, att.GetProperty("presented_deadline_ms").ValueKind);
        Assert.Equal(2500, att.GetProperty("expected_pace_ms").GetInt32());
        Assert.Equal("New", att.GetProperty("resolved_role").GetString());
        Assert.Equal(0, att.GetProperty("operation_band_before").GetInt32());
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task SerializeAsync_ExampleE_EnrichedTimeoutAttempt_MatchesCanonicalJson()
    {
        var serializer = new TelemetryJsonSerializer();
        var exportedAt = DateTimeOffset.Parse("2026-10-01T03:30:00.0000000+00:00", CultureInfo.InvariantCulture);
        var attemptTimestamp = DateTimeOffset.Parse("2026-10-01T03:15:00.0000000+00:00", CultureInfo.InvariantCulture);

        var enrichedTimeoutAttempt = CreateAttempt(
            submissionId: "sub-enriched-3",
            factId: "add:7+8",
            operation: ArithmeticOperation.Addition,
            leftOperand: 7,
            rightOperand: 8,
            submittedAnswer: null,
            correctAnswer: 15,
            isCorrect: false,
            isFluent: false,
            responseLatencyMs: 3050,
            timestamp: attemptTimestamp,
            outcome: AttemptOutcome.Timeout,
            practicePosition: 75,
            contextVersion: 1,
            presentedDeadlineMs: 3000,
            expectedPaceMs: 2000,
            resolvedRole: "Remediation",
            operationBandBefore: 1,
            isInterrupted: false);

        var json = await SerializeToStringAsync(
            serializer,
            installationId: "inst-example-e",
            appVersion: "2.0.0",
            buildClassification: "Production",
            exportedAt: exportedAt,
            attempts: new[] { enrichedTimeoutAttempt });

        const string expectedJson = "{\"schema_version\":2,\"exported_at\":\"2026-10-01T03:30:00.0000000+00:00\",\"app_version\":\"2.0.0\",\"build_classification\":\"Production\",\"installation_id\":\"inst-example-e\",\"attempt_count\":1,\"attempts\":[{\"fact_id\":\"add:7+8\",\"operation\":\"Addition\",\"left_operand\":7,\"right_operand\":8,\"submitted_answer\":null,\"outcome\":\"Timeout\",\"is_fluent\":false,\"response_latency_ms\":3050,\"timestamp\":\"2026-10-01T03:15:00.0000000+00:00\",\"practice_position\":75,\"context_version\":1,\"presented_deadline_ms\":3000,\"expected_pace_ms\":2000,\"resolved_role\":\"Remediation\",\"operation_band_before\":1,\"is_interrupted\":false}]}";

        Assert.Equal(expectedJson, json);

        using var doc = JsonDocument.Parse(json);
        AssertPropertyOrder(doc.RootElement, ExpectedEnvelopeProperties);
        var att = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(att, ExpectedAttemptProperties);
        Assert.Equal(75, att.GetProperty("practice_position").GetInt64());
        Assert.Equal(1, att.GetProperty("context_version").GetInt32());
        Assert.Equal(3000, att.GetProperty("presented_deadline_ms").GetInt32());
        Assert.Equal(2000, att.GetProperty("expected_pace_ms").GetInt32());
        Assert.Equal("Remediation", att.GetProperty("resolved_role").GetString());
        Assert.Equal(1, att.GetProperty("operation_band_before").GetInt32());
        Assert.Equal(JsonValueKind.Null, att.GetProperty("submitted_answer").ValueKind);
        Assert.Equal("Timeout", att.GetProperty("outcome").GetString());
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task TelemetryAttempt_IncludesIsInterruptedFalse()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(isInterrupted: false);
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        var isInterruptedProp = attemptElement.GetProperty("is_interrupted");
        Assert.Equal(JsonValueKind.False, isInterruptedProp.ValueKind);
        Assert.False(isInterruptedProp.GetBoolean());
    }

    [Fact]
    public async Task TelemetryAttempt_IncludesIsInterruptedTrue()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(isInterrupted: true);
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        var isInterruptedProp = attemptElement.GetProperty("is_interrupted");
        Assert.Equal(JsonValueKind.True, isInterruptedProp.ValueKind);
        Assert.True(isInterruptedProp.GetBoolean());
    }

    [Fact]
    public async Task TelemetryAttempt_PreservesIsFluentWhenInterrupted()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(
            isFluent: true,
            isInterrupted: true,
            responseLatencyMs: 1100);
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        Assert.True(attemptElement.GetProperty("is_fluent").GetBoolean());
        Assert.True(attemptElement.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task TelemetryAttempt_PreservesResponseLatencyWhenInterrupted()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(
            isInterrupted: true,
            responseLatencyMs: 4321);
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        Assert.Equal(4321, attemptElement.GetProperty("response_latency_ms").GetInt64());
        Assert.True(attemptElement.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task TelemetryAttempt_PreservesOutcomeWhenInterrupted()
    {
        var serializer = new TelemetryJsonSerializer();
        var correctAttempt = CreateAttempt(
            submissionId: "c1",
            submittedAnswer: 5,
            correctAnswer: 5,
            isCorrect: true,
            isFluent: true,
            outcome: AttemptOutcome.Correct,
            isInterrupted: true);
        var incorrectAttempt = CreateAttempt(
            submissionId: "i1",
            submittedAnswer: 9,
            correctAnswer: 5,
            isCorrect: false,
            isFluent: false,
            outcome: AttemptOutcome.Incorrect,
            isInterrupted: true);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { correctAttempt, incorrectAttempt });

        using var doc = JsonDocument.Parse(json);
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.Equal("Correct", attemptsArray[0].GetProperty("outcome").GetString());
        Assert.True(attemptsArray[0].GetProperty("is_interrupted").GetBoolean());
        Assert.Equal("Incorrect", attemptsArray[1].GetProperty("outcome").GetString());
        Assert.True(attemptsArray[1].GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task TelemetryV2_AttemptAddsExactlyOneProperty()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt();
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        var propertyNames = attemptElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(16, propertyNames.Count);
        Assert.Equal(ExpectedAttemptProperties, propertyNames);
    }

    [Fact]
    public async Task TelemetryV2_PreservesExistingPropertyNames()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt();
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        var actualProps = attemptElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var legacy15Props = new[]
        {
            "fact_id", "operation", "left_operand", "right_operand",
            "submitted_answer", "outcome", "is_fluent", "response_latency_ms",
            "timestamp", "practice_position", "context_version",
            "presented_deadline_ms", "expected_pace_ms", "resolved_role",
            "operation_band_before"
        };

        foreach (var prop in legacy15Props)
        {
            Assert.Contains(prop, actualProps);
        }
        Assert.Contains("is_interrupted", actualProps);
        Assert.Equal(16, actualProps.Count);
    }

    [Fact]
    public async Task TelemetryV2_PreservesExistingPropertyOrder()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt();
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        AssertPropertyOrder(attemptElement, ExpectedAttemptProperties);
    }

    [Fact]
    public async Task MixedAttempts_ExportDistinctInterruptionValues()
    {
        var serializer = new TelemetryJsonSerializer();
        var uninterrupted = CreateAttempt(submissionId: "u1", isInterrupted: false);
        var interrupted = CreateAttempt(submissionId: "i1", isInterrupted: true);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { uninterrupted, interrupted });

        using var doc = JsonDocument.Parse(json);
        var attemptsArray = doc.RootElement.GetProperty("attempts");
        Assert.False(attemptsArray[0].GetProperty("is_interrupted").GetBoolean());
        Assert.True(attemptsArray[1].GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task TelemetryExport_DoesNotEmitTimingEligibilityPolicyField()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(isInterrupted: true);
        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        Assert.DoesNotContain("is_timing_eligible", json, StringComparison.Ordinal);
        Assert.DoesNotContain("timing_eligible", json, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(json);
        var attemptElement = doc.RootElement.GetProperty("attempts")[0];
        Assert.False(attemptElement.TryGetProperty("is_timing_eligible", out _));
        Assert.False(attemptElement.TryGetProperty("timing_eligible", out _));
    }

    [Fact]
    public async Task TelemetryV2_ExistingNullSemanticsRemainUnchanged()
    {
        var serializer = new TelemetryJsonSerializer();
        var attempt = CreateAttempt(
            submittedAnswer: null,
            isCorrect: false,
            isFluent: false,
            practicePosition: null,
            contextVersion: null,
            presentedDeadlineMs: null,
            expectedPaceMs: null,
            resolvedRole: null,
            operationBandBefore: null,
            outcome: AttemptOutcome.Timeout);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { attempt });

        using var doc = JsonDocument.Parse(json);
        var att = doc.RootElement.GetProperty("attempts")[0];
        Assert.Equal(JsonValueKind.Null, att.GetProperty("submitted_answer").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("practice_position").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("context_version").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("presented_deadline_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("expected_pace_ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("resolved_role").ValueKind);
        Assert.Equal(JsonValueKind.Null, att.GetProperty("operation_band_before").ValueKind);
        Assert.Equal(JsonValueKind.False, att.GetProperty("is_interrupted").ValueKind);
    }

    [Fact]
    public async Task HistoricalTimeout_ExportsCompatibleUnderV2()
    {
        var serializer = new TelemetryJsonSerializer();
        var timeoutAttempt = CreateAttempt(
            submissionId: "hist-timeout",
            submittedAnswer: null,
            correctAnswer: 12,
            isCorrect: false,
            isFluent: false,
            outcome: AttemptOutcome.Timeout,
            responseLatencyMs: 3000,
            isInterrupted: false);

        var json = await SerializeToStringAsync(serializer, attempts: new[] { timeoutAttempt });

        using var doc = JsonDocument.Parse(json);
        var att = doc.RootElement.GetProperty("attempts")[0];
        Assert.Equal("Timeout", att.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, att.GetProperty("submitted_answer").ValueKind);
        Assert.False(att.GetProperty("is_fluent").GetBoolean());
        Assert.Equal(3000, att.GetProperty("response_latency_ms").GetInt64());
        Assert.False(att.GetProperty("is_interrupted").GetBoolean());
    }

    [Fact]
    public async Task LegacyMigratedAttempt_ExportsIsInterruptedFalse()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "MathFirstLegacyExport_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    INSERT INTO schema_info VALUES ('schema_version', '7');
                    CREATE TABLE attempt_history (
                        submission_id TEXT PRIMARY KEY,
                        fact_id TEXT NOT NULL,
                        operation TEXT NOT NULL,
                        left_operand INTEGER NOT NULL,
                        right_operand INTEGER NOT NULL,
                        submitted_answer INTEGER,
                        correct_answer INTEGER NOT NULL,
                        is_correct INTEGER NOT NULL,
                        is_fluent INTEGER NOT NULL,
                        response_latency_ms INTEGER NOT NULL,
                        timestamp TEXT NOT NULL,
                        outcome TEXT NOT NULL,
                        practice_position INTEGER,
                        attempt_context_version INTEGER,
                        presented_deadline_ms INTEGER,
                        expected_pace_ms INTEGER,
                        resolved_role TEXT,
                        operation_band_before INTEGER
                    );
                    INSERT INTO attempt_history VALUES (
                        'legacy-v7-sub-1', 'add:3+4', 'Addition', 3, 4, 7, 7, 1, 1, 1200,
                        '2026-09-20T10:00:00.0000000+00:00', 'Correct', 1, 1, 3000, 2500, 'Due', 1
                    );
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
                    CREATE TABLE operation_progression (
                        operation TEXT PRIMARY KEY,
                        band_index INTEGER NOT NULL CHECK (band_index >= 0),
                        band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
                    );
                    INSERT INTO operation_progression VALUES ('Addition', 0, 0);
                    INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
                    INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                    INSERT INTO operation_progression VALUES ('Division', 0, 0);
                    CREATE TABLE learner_progression (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        practice_position INTEGER NOT NULL DEFAULT 0,
                        updated_at TEXT NOT NULL
                    );
                    INSERT INTO learner_progression VALUES (1, 1, '2026-09-20T10:00:00.0000000+00:00');
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            using var store = new SqliteLearnerStore(tempDb);
            await store.InitializeAsync();

            var loadedAttempts = await store.LoadCompleteAttemptTelemetryAsync();
            var serializer = new TelemetryJsonSerializer();
            var json = await SerializeToStringAsync(serializer, attempts: loadedAttempts);

            using var doc = JsonDocument.Parse(json);
            var att = doc.RootElement.GetProperty("attempts")[0];
            Assert.Equal("legacy-v7-sub-1", loadedAttempts[0].SubmissionId);
            Assert.False(loadedAttempts[0].IsInterrupted);
            Assert.False(att.GetProperty("is_interrupted").GetBoolean());
            Assert.Equal(2, doc.RootElement.GetProperty("schema_version").GetInt32());
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task PersistedInterruptedAttempt_ExportsTrueAfterRestart()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "MathFirstInterruptedRestartExport_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store1 = new SqliteLearnerStore(tempDb))
            {
                var session = new TrainingSession(store1);
                await session.InitializeAsync(startTiming: true);

                session.PausePractice();
                Assert.True(session.IsCurrentAttemptInterrupted);

                session.StartOrResumePractice();
                Assert.True(session.IsCurrentAttemptInterrupted);

                session.SubmitAnswer(session.CurrentFact.CorrectResult);
                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitResult.IsSuccess);
            }

            // Reopen store after close
            using (var store2 = new SqliteLearnerStore(tempDb))
            {
                await store2.InitializeAsync();
                var loaded = await store2.LoadCompleteAttemptTelemetryAsync();
                var reloadedAttempt = Assert.Single(loaded);
                Assert.True(reloadedAttempt.IsInterrupted);

                var serializer = new TelemetryJsonSerializer();
                var json = await SerializeToStringAsync(serializer, attempts: loaded);

                using var doc = JsonDocument.Parse(json);
                Assert.Equal(2, doc.RootElement.GetProperty("schema_version").GetInt32());
                var exportedAttempt = doc.RootElement.GetProperty("attempts")[0];
                Assert.True(exportedAttempt.GetProperty("is_interrupted").GetBoolean());
                Assert.Equal(reloadedAttempt.ResponseLatencyMs, exportedAttempt.GetProperty("response_latency_ms").GetInt64());
                Assert.Equal(reloadedAttempt.IsFluent, exportedAttempt.GetProperty("is_fluent").GetBoolean());
                Assert.Equal("Correct", exportedAttempt.GetProperty("outcome").GetString());
            }
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
