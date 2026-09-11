namespace MathFirst.Application.Progression;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public static class FastAcquisitionEvaluator
{
    public const long FastAcquisitionMaxLatencyMs = 2000;
    public const int MaxDenseFrontierSize = 12;
    public const int MinDenseFrontierSize = 1;

    public static bool TryEvaluate(
        OperationProgression currentProgression,
        OperationCurriculum curriculum,
        BandAdvancementEvidence evidence,
        out OperationProgression resultingProgression)
    {
        resultingProgression = currentProgression;

        if (!curriculum.TryGetBand(currentProgression.BandIndex, out var currentBand)
            || currentBand is null
            || currentBand.Kind != CurriculumBandKind.Dense)
        {
            return false;
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(currentProgression.BandIndex);
        if (ownedFrontier.Count < MinDenseFrontierSize || ownedFrontier.Count > MaxDenseFrontierSize)
        {
            return false;
        }

        var qualifyingAttempts = evidence.AcceptedAttempts
            .Where(attempt => attempt.PracticePosition > currentProgression.BandStartedPracticePosition)
            .OrderBy(attempt => attempt.PracticePosition)
            .ToArray();

        if (qualifyingAttempts.Length == 0)
        {
            return false;
        }

        var latestAttempt = qualifyingAttempts[^1];
        var startPosition = currentProgression.BandStartedPracticePosition;
        var endPosition = latestAttempt.PracticePosition;

        // 1. Verify Prefix Completeness:
        var expectedPositions = EnumerateOperationPositions(currentProgression.Operation, startPosition, endPosition).ToArray();
        if (expectedPositions.Length != qualifyingAttempts.Length)
        {
            return false;
        }

        for (var i = 0; i < expectedPositions.Length; i++)
        {
            if (qualifyingAttempts[i].PracticePosition != expectedPositions[i])
            {
                return false;
            }
        }

        // 2. Calculate Phase-Aware Requested-New Horizon:
        var horizonPosition = CalculateRequestedNewHorizon(
            currentProgression.Operation,
            startPosition,
            ownedFrontier.Count);

        if (endPosition > horizonPosition)
        {
            return false;
        }

        // 3. Verify Error-Free Prefix:
        if (qualifyingAttempts.Any(attempt => !attempt.IsCorrect || attempt.ResponseLatencyMs < 0))
        {
            return false;
        }

        // 4. Verify First Encounter on Every Owned Fact:
        var ownedFactIds = ownedFrontier.Select(fact => fact.Id).ToHashSet(StringComparer.Ordinal);
        var firstEncounters = new Dictionary<string, BandAttemptEvidence>(StringComparer.Ordinal);

        foreach (var attempt in qualifyingAttempts)
        {
            if (ownedFactIds.Contains(attempt.FactId) && !firstEncounters.ContainsKey(attempt.FactId))
            {
                firstEncounters[attempt.FactId] = attempt;
            }
        }

        if (firstEncounters.Count != ownedFrontier.Count)
        {
            return false;
        }

        foreach (var (_, firstEncounter) in firstEncounters)
        {
            if (!firstEncounter.IsCorrect || firstEncounter.ResponseLatencyMs > FastAcquisitionMaxLatencyMs)
            {
                return false;
            }
        }

        // 5. Ensure Complete Successor Band Exists:
        if (currentProgression.BandIndex == int.MaxValue
            || !curriculum.TryGetBand(currentProgression.BandIndex + 1, out _))
        {
            return false;
        }

        resultingProgression = new OperationProgression(
            currentProgression.Operation,
            currentProgression.BandIndex + 1,
            latestAttempt.PracticePosition);

        return true;
    }

    public static IEnumerable<long> EnumerateOperationPositions(
        ArithmeticOperation operation,
        long startPosition,
        long endPosition)
    {
        for (var pos = checked(startPosition + 1); pos <= endPosition; pos++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(pos) == operation)
            {
                yield return pos;
            }
        }
    }

    public static long CalculateRequestedNewHorizon(
        ArithmeticOperation operation,
        long startPosition,
        int requiredNewCount)
    {
        if (requiredNewCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredNewCount));
        }

        var newCount = 0;
        for (var pos = checked(startPosition + 1); ; pos++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(pos) == operation)
            {
                if (AdaptivePracticeSelector.GetRequestedRole(pos) == PracticeSelectionRole.New)
                {
                    newCount++;
                    if (newCount == requiredNewCount)
                    {
                        return pos;
                    }
                }
            }
        }
    }
}
