namespace MathFirst.Domain.Curriculum;

/// <summary>
/// Authoritative D10 prerequisite mastery evidence for one curriculum stage transition (ADR-0013).
/// </summary>
public sealed record PrerequisiteMasteryEvidence
{
    public PrerequisiteMasteryEvidence(
        CurriculumStage currentStage,
        ArithmeticOperation? prerequisiteOperation,
        int bandIndex,
        int masteryFactCount,
        IReadOnlyList<string> unresolvedFactIds,
        IReadOnlyList<string> unresolvedBoundaryFactIds)
    {
        ArgumentNullException.ThrowIfNull(unresolvedFactIds);
        ArgumentNullException.ThrowIfNull(unresolvedBoundaryFactIds);

        CurrentStage = currentStage;
        PrerequisiteOperation = prerequisiteOperation;
        BandIndex = bandIndex;
        MasteryFactCount = masteryFactCount;
        UnresolvedFactIds = unresolvedFactIds;
        UnresolvedBoundaryFactIds = unresolvedBoundaryFactIds;
    }

    public CurriculumStage CurrentStage { get; }
    public ArithmeticOperation? PrerequisiteOperation { get; }

    /// <summary>Persisted BandIndex of the prerequisite operation, or -1 when unavailable.</summary>
    public int BandIndex { get; }

    /// <summary>Number of canonical facts owned by the prerequisite operation's D01..D10 bands.</summary>
    public int MasteryFactCount { get; }

    /// <summary>D01..D10 facts without authoritative mastery evidence, in canonical curriculum order.</summary>
    public IReadOnlyList<string> UnresolvedFactIds { get; }

    /// <summary>Explicit D10 boundary facts without authoritative mastery evidence.</summary>
    public IReadOnlyList<string> UnresolvedBoundaryFactIds { get; }

    public bool HasCompletedMasteryBands =>
        PrerequisiteOperation.HasValue && BandIndex >= CurriculumUnlockPolicy.PrerequisiteMasteryBandCount;

    public int DemonstratedFactCount => MasteryFactCount - UnresolvedFactIds.Count;

    public bool IsSatisfied =>
        HasCompletedMasteryBands
        && MasteryFactCount > 0
        && UnresolvedFactIds.Count == 0
        && UnresolvedBoundaryFactIds.Count == 0;
}
