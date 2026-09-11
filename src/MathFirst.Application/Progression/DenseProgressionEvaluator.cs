namespace MathFirst.Application.Progression;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public static class DenseProgressionEvaluator
{
    public const int MaxDenseFrontierSize = 25;
    public const int MinDenseFrontierSize = 1;

    public static BandAdvancementDecision Evaluate(
        OperationProgression currentProgression,
        OperationCurriculum curriculum,
        IReadOnlyList<BandAttemptEvidence> evidenceAttempts)
    {
        ArgumentNullException.ThrowIfNull(currentProgression);
        ArgumentNullException.ThrowIfNull(curriculum);
        ArgumentNullException.ThrowIfNull(evidenceAttempts);

        if (currentProgression.Operation != curriculum.Operation)
        {
            throw new ArgumentException(
                "The progression operation must match the operation curriculum.",
                nameof(curriculum));
        }

        if (!curriculum.TryGetBand(currentProgression.BandIndex, out var currentBand)
            || currentBand is null
            || currentBand.Kind != CurriculumBandKind.Dense)
        {
            return new BandAdvancementDecision(false, currentProgression);
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(currentProgression.BandIndex);
        var frontierCount = ownedFrontier.Count;
        if (frontierCount < MinDenseFrontierSize || frontierCount > MaxDenseFrontierSize)
        {
            return new BandAdvancementDecision(false, currentProgression);
        }

        var ownedFactIds = ownedFrontier.Select(fact => fact.Id).ToHashSet(StringComparer.Ordinal);

        // Group by fact_id and select the latest PracticePosition within the current band instance
        var latestPerFact = new Dictionary<string, BandAttemptEvidence>(StringComparer.Ordinal);
        foreach (var attempt in evidenceAttempts)
        {
            if (attempt is null)
            {
                throw new ArgumentException("Attempt evidence cannot contain null entries.", nameof(evidenceAttempts));
            }

            if (attempt.PracticePosition <= currentProgression.BandStartedPracticePosition)
            {
                continue;
            }

            if (!ownedFactIds.Contains(attempt.FactId))
            {
                continue;
            }

            if (!latestPerFact.TryGetValue(attempt.FactId, out var existing)
                || attempt.PracticePosition > existing.PracticePosition)
            {
                latestPerFact[attempt.FactId] = attempt;
            }
        }

        // Complete coverage required: all owned frontier facts must have a latest current-band attempt
        if (latestPerFact.Count < frontierCount)
        {
            return new BandAdvancementDecision(false, currentProgression);
        }

        // Count correct: C
        var correctCount = latestPerFact.Values.Count(attempt => attempt.IsCorrect);

        // Threshold check: C * 10 >= N * 9 (overflow-safe integer arithmetic)
        if (checked(correctCount * 10) < checked(frontierCount * 9))
        {
            return new BandAdvancementDecision(false, currentProgression);
        }

        // Ensure safe successor band exists
        if (currentProgression.BandIndex == int.MaxValue
            || !curriculum.TryGetBand(currentProgression.BandIndex + 1, out _))
        {
            return new BandAdvancementDecision(false, currentProgression);
        }

        var triggeringPosition = latestPerFact.Values.Max(attempt => attempt.PracticePosition);
        var resultingProgression = new OperationProgression(
            currentProgression.Operation,
            currentProgression.BandIndex + 1,
            triggeringPosition);

        return new BandAdvancementDecision(true, resultingProgression);
    }
}
