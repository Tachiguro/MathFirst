namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// First-class immutable provenance model distinguishing complete authoritative encounter tracking
/// from unverified legacy encounters with incomplete or unavailable attack history.
/// </summary>
public enum OpponentEncounterProvenance
{
    /// <summary>
    /// The encounter is known to have begun with an empty counter under reliable tracking.
    /// Every confirmed successful attack during its lifecycle is included in the counter.
    /// </summary>
    Authoritative = 1,

    /// <summary>
    /// The persisted count reflects known tracked attacks, but some earlier encounter history
    /// may be unavailable. The count must not be described as the complete encounter total.
    /// </summary>
    UnverifiedLegacy = 2,
}
