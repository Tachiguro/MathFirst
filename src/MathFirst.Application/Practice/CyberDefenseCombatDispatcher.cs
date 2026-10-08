namespace MathFirst.Application.Practice;

using System;

/// <summary>
/// Minimal in-memory dispatcher connecting authoritative committed learner attempts
/// to the transient Cyber Defense combat presentation.
/// </summary>
public sealed class CyberDefenseCombatDispatcher
{
    private readonly CyberDefenseSessionState _sessionState;

    public CyberDefenseCombatDispatcher(CyberDefenseSessionState sessionState)
    {
        _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    }

    public CombatDispatchResult Dispatch(ConfirmedCombatAttempt attempt) =>
        _sessionState.DispatchAttempt(attempt);

    public bool IsProcessed(string submissionId) =>
        _sessionState.IsSubmissionProcessed(submissionId);
}
