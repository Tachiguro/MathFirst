namespace MathFirst.Application.Practice;

public enum CombatDispatchStatus
{
    Dispatched = 1,
    DuplicateSuppressed = 2,
    CalmModeSuppressed = 3,
    UncommittedSuppressed = 4,
    InvalidAttempt = 5
}

public sealed record CombatDispatchResult(
    CombatDispatchStatus Status,
    bool MutatedCombatState)
{
    public static CombatDispatchResult Dispatched() =>
        new(CombatDispatchStatus.Dispatched, MutatedCombatState: true);

    public static CombatDispatchResult Duplicate() =>
        new(CombatDispatchStatus.DuplicateSuppressed, MutatedCombatState: false);

    public static CombatDispatchResult CalmModeSuppressed() =>
        new(CombatDispatchStatus.CalmModeSuppressed, MutatedCombatState: false);

    public static CombatDispatchResult Uncommitted() =>
        new(CombatDispatchStatus.UncommittedSuppressed, MutatedCombatState: false);

    public static CombatDispatchResult Invalid() =>
        new(CombatDispatchStatus.InvalidAttempt, MutatedCombatState: false);
}
