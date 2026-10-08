namespace MathFirst.Application.Practice;

using System;

/// <summary>
/// Immutable representation of an evaluated learner attempt submitted for combat dispatch.
/// Stamped with the authoritative learner SubmissionId and durable commit confirmation.
/// </summary>
public sealed record ConfirmedCombatAttempt
{
    public ConfirmedCombatAttempt(
        string submissionId,
        bool isCorrect,
        bool isCritical,
        bool isCommitted,
        bool wasEligibleAtSubmission)
    {
        SubmissionId = submissionId ?? string.Empty;
        IsCorrect = isCorrect;
        IsCritical = isCritical;
        IsCommitted = isCommitted;
        WasEligibleAtSubmission = wasEligibleAtSubmission;
    }

    public string SubmissionId { get; }
    public bool IsCorrect { get; }
    public bool IsCritical { get; }
    public bool IsCommitted { get; }
    public bool WasEligibleAtSubmission { get; }
}
