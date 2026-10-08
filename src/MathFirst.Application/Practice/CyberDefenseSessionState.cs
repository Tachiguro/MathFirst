namespace MathFirst.Application.Practice;

using System;
using System.Collections.Generic;

/// <summary>
/// Application-scoped state holder managing the lifetime of the transient Cyber Defense encounter
/// and in-memory deduplication of confirmed combat attempt dispatches.
/// Respects Calm Mode preferences: when disabled, no combat state is allocated and queries return null.
/// Preserves combat state when toggling between enabled and disabled, and clears state upon full local reset.
/// </summary>
public sealed class CyberDefenseSessionState
{
    private readonly ICyberDefenseModePreferences _preferences;
    private readonly Func<CyberDefenseEncounterState> _encounterFactory;
    private readonly HashSet<string> _processedSubmissionIds = new(StringComparer.Ordinal);
    private CyberDefenseEncounterState? _encounter;

    public CyberDefenseSessionState(
        ICyberDefenseModePreferences preferences,
        Func<CyberDefenseEncounterState>? encounterFactory = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _encounterFactory = encounterFactory ?? (() => new CyberDefenseEncounterState());
    }

    public bool IsCyberDefenseEnabled => _preferences.GetCyberDefenseEnabled();

    public bool HasActiveEncounter => _encounter is not null;

    public CyberDefenseEncounterState? ActiveEncounter
    {
        get
        {
            if (!IsCyberDefenseEnabled)
            {
                return null;
            }

            _encounter ??= _encounterFactory();
            return _encounter;
        }
    }

    public bool IsSubmissionProcessed(string submissionId)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            return false;
        }

        lock (_processedSubmissionIds)
        {
            return _processedSubmissionIds.Contains(submissionId);
        }
    }

    public CombatDispatchResult DispatchAttempt(ConfirmedCombatAttempt attempt)
    {
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.SubmissionId))
        {
            return CombatDispatchResult.Invalid();
        }

        if (!attempt.IsCommitted)
        {
            return CombatDispatchResult.Uncommitted();
        }

        lock (_processedSubmissionIds)
        {
            if (!_processedSubmissionIds.Add(attempt.SubmissionId))
            {
                return CombatDispatchResult.Duplicate();
            }
        }

        var isEligible = attempt.WasEligibleAtSubmission && IsCyberDefenseEnabled;
        if (!isEligible)
        {
            return CombatDispatchResult.CalmModeSuppressed();
        }

        var encounter = ActiveEncounter;
        if (encounter is null)
        {
            return CombatDispatchResult.CalmModeSuppressed();
        }

        if (attempt.IsCorrect)
        {
            if (attempt.IsCritical)
            {
                encounter.RecordCriticalHit();
            }
            else
            {
                encounter.RecordCorrectAnswer();
            }
        }
        else
        {
            encounter.RecordIncorrectAnswer();
        }

        return CombatDispatchResult.Dispatched();
    }

    public void ClearEncounter()
    {
        _encounter = null;
        lock (_processedSubmissionIds)
        {
            _processedSubmissionIds.Clear();
        }
    }
}
