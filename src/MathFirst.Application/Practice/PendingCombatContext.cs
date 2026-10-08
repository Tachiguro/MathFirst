namespace MathFirst.Application.Practice;

using System;

/// <summary>
/// Immutable capture of presentation-level combat eligibility and critical-hit classification
/// established at the time of mathematical answer submission, preserved across UI navigation
/// and component disposal for the lifetime of the pending evaluation.
/// </summary>
public sealed record PendingCombatContext
{
    public PendingCombatContext(
        string submissionId,
        bool wasEligibleAtSubmission,
        bool isCritical)
    {
        SubmissionId = submissionId ?? string.Empty;
        WasEligibleAtSubmission = wasEligibleAtSubmission;
        IsCritical = isCritical;
    }

    public string SubmissionId { get; }
    public bool WasEligibleAtSubmission { get; }
    public bool IsCritical { get; }
}
