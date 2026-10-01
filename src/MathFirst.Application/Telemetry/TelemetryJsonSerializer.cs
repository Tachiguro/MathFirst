namespace MathFirst.Application.Telemetry;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;

public sealed class TelemetryJsonSerializer : ITelemetryJsonSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task SerializeAsync(
        Stream output,
        string installationId,
        string appVersion,
        string buildClassification,
        DateTimeOffset exportedAt,
        IReadOnlyList<AttemptRecord> attempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(installationId);
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(buildClassification);
        ArgumentNullException.ThrowIfNull(attempts);

        cancellationToken.ThrowIfCancellationRequested();

        await using var writer = new Utf8JsonWriter(output, WriterOptions);

        writer.WriteStartObject();

        writer.WriteNumber("schema_version", 1);
        writer.WriteString("exported_at", FormatTimestamp(exportedAt));
        writer.WriteString("app_version", appVersion);
        writer.WriteString("build_classification", buildClassification);
        writer.WriteString("installation_id", installationId);
        writer.WriteNumber("attempt_count", attempts.Count);

        writer.WriteStartArray("attempts");

        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.WriteStartObject();

            writer.WriteString("fact_id", FormatFactId(attempt.Operation, attempt.LeftOperand, attempt.RightOperand));
            writer.WriteString("operation", FormatOperation(attempt.Operation));
            writer.WriteNumber("left_operand", attempt.LeftOperand);
            writer.WriteNumber("right_operand", attempt.RightOperand);

            if (attempt.SubmittedAnswer.HasValue)
            {
                writer.WriteNumber("submitted_answer", attempt.SubmittedAnswer.Value);
            }
            else
            {
                writer.WriteNull("submitted_answer");
            }

            writer.WriteString("outcome", FormatOutcome(attempt.Outcome));
            writer.WriteBoolean("is_fluent", attempt.IsFluent);
            writer.WriteNumber("response_latency_ms", attempt.ResponseLatencyMs);
            writer.WriteString("timestamp", FormatTimestamp(attempt.Timestamp));

            if (attempt.PracticePosition.HasValue)
            {
                writer.WriteNumber("practice_position", attempt.PracticePosition.Value);
            }
            else
            {
                writer.WriteNull("practice_position");
            }

            if (attempt.ContextVersion.HasValue)
            {
                writer.WriteNumber("context_version", attempt.ContextVersion.Value);
            }
            else
            {
                writer.WriteNull("context_version");
            }

            if (attempt.PresentedDeadlineMs.HasValue)
            {
                writer.WriteNumber("presented_deadline_ms", attempt.PresentedDeadlineMs.Value);
            }
            else
            {
                writer.WriteNull("presented_deadline_ms");
            }

            if (attempt.ExpectedPaceMs.HasValue)
            {
                writer.WriteNumber("expected_pace_ms", attempt.ExpectedPaceMs.Value);
            }
            else
            {
                writer.WriteNull("expected_pace_ms");
            }

            if (attempt.ResolvedRole is not null)
            {
                writer.WriteString("resolved_role", attempt.ResolvedRole);
            }
            else
            {
                writer.WriteNull("resolved_role");
            }

            if (attempt.OperationBandBefore.HasValue)
            {
                writer.WriteNumber("operation_band_before", attempt.OperationBandBefore.Value);
            }
            else
            {
                writer.WriteNull("operation_band_before");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatFactId(ArithmeticOperation operation, int leftOperand, int rightOperand) =>
        operation switch
        {
            ArithmeticOperation.Addition => $"add:{leftOperand}+{rightOperand}",
            ArithmeticOperation.Subtraction => $"sub:{leftOperand}-{rightOperand}",
            ArithmeticOperation.Multiplication => $"mul:{leftOperand}*{rightOperand}",
            ArithmeticOperation.Division => $"div:{leftOperand}/{rightOperand}",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
        };

    private static string FormatOperation(ArithmeticOperation operation) =>
        operation switch
        {
            ArithmeticOperation.Addition => "Addition",
            ArithmeticOperation.Subtraction => "Subtraction",
            ArithmeticOperation.Multiplication => "Multiplication",
            ArithmeticOperation.Division => "Division",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
        };

    private static string FormatOutcome(AttemptOutcome outcome) =>
        outcome switch
        {
            AttemptOutcome.Correct => "Correct",
            AttemptOutcome.Incorrect => "Incorrect",
            AttemptOutcome.Timeout => "Timeout",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown attempt outcome.")
        };
}
