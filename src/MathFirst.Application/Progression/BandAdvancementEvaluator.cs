namespace MathFirst.Application.Progression;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class BandAdvancementEvaluator
{
    private const int AdvancementWindowSize = 40;
    private const int RequiredCorrectAttempts = 38;
    private const int RequiredFluentAttempts = 34;
    private const int RequiredFrontierAttempts = 20;
    private const int MaximumRequiredDistinctFrontierFacts = 16;
    private const long FluentLatencyThresholdMs = 2500;

    public BandAdvancementDecision Evaluate(
        OperationProgression currentProgression,
        OperationCurriculum curriculum,
        BandAdvancementEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(currentProgression);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(evidence);

        if (currentProgression.Operation != curriculum.Operation)
        {
            throw new ArgumentException(
                "The progression operation must match the operation curriculum.",
                nameof(curriculum));
        }

        if (!curriculum.TryGetBand(currentProgression.BandIndex, out var currentBand))
        {
            throw new ArgumentException(
                "The current progression band must resolve to a complete curriculum band.",
                nameof(currentProgression));
        }

        ValidateUniquePracticePositions(evidence.AcceptedAttempts);

        var qualifyingAttempts = evidence.AcceptedAttempts
            .Where(attempt => attempt.PracticePosition > currentProgression.BandStartedPracticePosition)
            .OrderBy(attempt => attempt.PracticePosition)
            .ToArray();
        if (qualifyingAttempts.Length < AdvancementWindowSize)
        {
            return Stay(currentProgression);
        }

        var latestWindow = qualifyingAttempts[^AdvancementWindowSize..];
        if (latestWindow.Count(attempt => attempt.IsCorrect) < RequiredCorrectAttempts
            || latestWindow.Count(IsFluent) < RequiredFluentAttempts)
        {
            return Stay(currentProgression);
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(currentProgression.BandIndex);
        var ownedFactIds = ownedFrontier
            .Select(fact => fact.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (latestWindow.Count(attempt => ownedFactIds.Contains(attempt.FactId)) < RequiredFrontierAttempts)
        {
            return Stay(currentProgression);
        }

        var requiredDistinctCount = Math.Min(MaximumRequiredDistinctFrontierFacts, ownedFactIds.Count);
        var distinctFrontierCount = latestWindow
            .Select(attempt => attempt.FactId)
            .Where(ownedFactIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctFrontierCount < requiredDistinctCount)
        {
            return Stay(currentProgression);
        }

        if (!HasRequiredCoverage(currentBand!, ownedFrontier, ownedFactIds, evidence))
        {
            return Stay(currentProgression);
        }

        if (currentProgression.BandIndex == int.MaxValue
            || !curriculum.TryGetBand(currentProgression.BandIndex + 1, out _))
        {
            return Stay(currentProgression);
        }

        var advanced = new OperationProgression(
            currentProgression.Operation,
            currentProgression.BandIndex + 1,
            latestWindow[^1].PracticePosition);
        return new BandAdvancementDecision(true, advanced);
    }

    private static bool IsFluent(BandAttemptEvidence attempt) =>
        attempt.IsCorrect && attempt.ResponseLatencyMs <= FluentLatencyThresholdMs;

    private static bool HasRequiredCoverage(
        CurriculumBand currentBand,
        IReadOnlyList<ArithmeticFact> ownedFrontier,
        IReadOnlySet<string> ownedFactIds,
        BandAdvancementEvidence evidence) => currentBand.Kind switch
    {
        CurriculumBandKind.Dense => ownedFactIds.All(evidence.LifetimeAttemptedFactIds.Contains),
        CurriculumBandKind.Structured => DeterministicFactRanker.SelectStructuredSample(
                ownedFrontier,
                currentBand.Operation,
                currentBand.Id)
            .All(fact => evidence.CurrentBandIntroducedFactIds.Contains(fact.Id)),
        _ => throw new InvalidOperationException("The current curriculum band has an unknown kind.")
    };

    private static void ValidateUniquePracticePositions(IReadOnlyList<BandAttemptEvidence> attempts)
    {
        var positions = new HashSet<long>();
        foreach (var attempt in attempts)
        {
            if (!positions.Add(attempt.PracticePosition))
            {
                throw new ArgumentException(
                    "Accepted rolling evidence cannot contain duplicate practice positions.",
                    nameof(attempts));
            }
        }
    }

    private static BandAdvancementDecision Stay(OperationProgression currentProgression) =>
        new(false, currentProgression);
}
