namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Domain;

public static class TestStoreEvidenceHelper
{
    public static IReadOnlyList<AttemptRecord> FilterLatestFrontierAttempts(
        IEnumerable<AttemptRecord> attempts,
        ArithmeticOperation operation,
        long bandStartedPracticePosition,
        IReadOnlyList<string> frontierFactIds)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "A valid arithmetic operation is required.");
        }

        if (bandStartedPracticePosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandStartedPracticePosition),
                bandStartedPracticePosition,
                "Band started practice position must be non-negative.");
        }

        ArgumentNullException.ThrowIfNull(frontierFactIds);

        if (frontierFactIds.Count > 25)
        {
            throw new ArgumentException(
                $"Frontier fact count ({frontierFactIds.Count}) exceeds maximum supported dense frontier size (25).",
                nameof(frontierFactIds));
        }

        if (frontierFactIds.Count == 0)
        {
            return [];
        }

        var frontierSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var factId in frontierFactIds)
        {
            if (string.IsNullOrWhiteSpace(factId))
            {
                throw new ArgumentException("Frontier fact ID must not be null or blank.", nameof(frontierFactIds));
            }

            if (!frontierSet.Add(factId))
            {
                throw new ArgumentException($"Duplicate frontier fact ID '{factId}' is not allowed.", nameof(frontierFactIds));
            }
        }

        return attempts
            .Where(a => a.Operation == operation
                && a.PracticePosition.HasValue
                && a.PracticePosition.Value > bandStartedPracticePosition
                && frontierSet.Contains(a.FactId))
            .GroupBy(a => a.FactId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(a => a.PracticePosition!.Value).First())
            .OrderBy(a => a.FactId, StringComparer.Ordinal)
            .ToArray();
    }
}
