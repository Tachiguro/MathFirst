namespace MathFirst.Application.Persistence;

using System;
using System.Collections.Generic;
using MathFirst.Domain;

public sealed record AttemptRecord
{
    private static readonly HashSet<string> ValidProductionRoles = new(StringComparer.Ordinal)
    {
        "New",
        "Due",
        "Maintenance",
        "Frontier",
        "Remediation",
        "EarlyReview"
    };

    public string SubmissionId { get; }
    public string FactId { get; }
    public ArithmeticOperation Operation { get; }
    public int LeftOperand { get; }
    public int RightOperand { get; }
    public int? SubmittedAnswer { get; }
    public int CorrectAnswer { get; }
    public bool IsCorrect { get; }
    public bool IsFluent { get; }
    public long ResponseLatencyMs { get; }
    public DateTimeOffset Timestamp { get; }
    public AttemptOutcome Outcome { get; }
    public long? PracticePosition { get; }
    public int? ContextVersion { get; }
    public int? PresentedDeadlineMs { get; }
    public int? ExpectedPaceMs { get; }
    public string? ResolvedRole { get; }
    public int? OperationBandBefore { get; }

    public AttemptRecord(
        string submissionId,
        string factId,
        ArithmeticOperation operation,
        int leftOperand,
        int rightOperand,
        int? submittedAnswer,
        int correctAnswer,
        bool isCorrect,
        bool isFluent,
        long responseLatencyMs,
        DateTimeOffset timestamp,
        AttemptOutcome? outcome = null,
        long? practicePosition = null,
        int? contextVersion = null,
        int? presentedDeadlineMs = null,
        int? expectedPaceMs = null,
        string? resolvedRole = null,
        int? operationBandBefore = null)
    {
        SubmissionId = submissionId;
        FactId = factId;
        Operation = operation;
        LeftOperand = leftOperand;
        RightOperand = rightOperand;
        SubmittedAnswer = submittedAnswer;
        CorrectAnswer = correctAnswer;
        IsCorrect = isCorrect;
        IsFluent = isFluent;
        ResponseLatencyMs = responseLatencyMs;
        Timestamp = timestamp;
        Outcome = outcome ?? (isCorrect ? AttemptOutcome.Correct : (submittedAnswer is null ? AttemptOutcome.Timeout : AttemptOutcome.Incorrect));
        if (IsCorrect != (Outcome == AttemptOutcome.Correct))
        {
            throw new ArgumentException("Attempt outcome and correctness must be consistent.", nameof(outcome));
        }
        if (IsFluent && Outcome != AttemptOutcome.Correct)
        {
            throw new ArgumentException("Only a correct attempt may be fluent.", nameof(isFluent));
        }
        if (practicePosition is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(practicePosition), "Practice position must be positive when present.");
        }
        PracticePosition = practicePosition;

        if (contextVersion is null)
        {
            if (presentedDeadlineMs is not null ||
                expectedPaceMs is not null ||
                resolvedRole is not null ||
                operationBandBefore is not null)
            {
                throw new ArgumentException("When context version is null, all companion context fields must be null.", nameof(contextVersion));
            }
        }
        else if (contextVersion != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(contextVersion), "Context version must be 1 when present.");
        }
        else
        {
            if (expectedPaceMs is null)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedPaceMs), "Expected pace is required for context version 1.");
            }
            if (expectedPaceMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedPaceMs), "Expected pace must be positive for context version 1.");
            }

            if (presentedDeadlineMs is not null && presentedDeadlineMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(presentedDeadlineMs), "Presented deadline must be positive when provided.");
            }

            if (operationBandBefore is null)
            {
                throw new ArgumentOutOfRangeException(nameof(operationBandBefore), "Operation band before is required for context version 1.");
            }
            if (operationBandBefore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(operationBandBefore), "Operation band before cannot be negative.");
            }

            if (resolvedRole is null)
            {
                throw new ArgumentException("Resolved role cannot be null for context version 1.", nameof(resolvedRole));
            }
            if (string.IsNullOrWhiteSpace(resolvedRole))
            {
                throw new ArgumentException("Resolved role cannot be blank for context version 1.", nameof(resolvedRole));
            }
            if (!ValidProductionRoles.Contains(resolvedRole))
            {
                throw new ArgumentOutOfRangeException(nameof(resolvedRole), $"Unrecognized resolved role '{resolvedRole}'.");
            }
        }

        ContextVersion = contextVersion;
        PresentedDeadlineMs = presentedDeadlineMs;
        ExpectedPaceMs = expectedPaceMs;
        ResolvedRole = resolvedRole;
        OperationBandBefore = operationBandBefore;
    }
}
