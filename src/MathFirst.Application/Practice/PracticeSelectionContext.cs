namespace MathFirst.Application.Practice;

using System.Collections.Frozen;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class PracticeSelectionContext
{
    public long ProspectivePracticePosition { get; }
    public int CurrentSessionOrder { get; }
    public IReadOnlyDictionary<ArithmeticOperation, OperationProgression> OperationProgressions { get; }
    public IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> Curricula { get; }
    public PracticeCandidateIndex CandidateIndex { get; }
    public IReadOnlyList<ArithmeticFact> RecentAcceptedFactsOldestToNewest { get; }

    public PracticeSelectionContext(
        long prospectivePracticePosition,
        int currentSessionOrder,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> operationProgressions,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> curricula,
        PracticeCandidateIndex candidateIndex,
        IEnumerable<ArithmeticFact> recentAcceptedFactsOldestToNewest)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prospectivePracticePosition),
                prospectivePracticePosition,
                "Prospective practice position must be positive.");
        }

        if (currentSessionOrder < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSessionOrder),
                currentSessionOrder,
                "Current session order must be non-negative.");
        }

        ArgumentNullException.ThrowIfNull(operationProgressions);
        ArgumentNullException.ThrowIfNull(curricula);
        ArgumentNullException.ThrowIfNull(candidateIndex);
        ArgumentNullException.ThrowIfNull(recentAcceptedFactsOldestToNewest);

        var expectedOperations = Enum.GetValues<ArithmeticOperation>();
        if (operationProgressions.Count != expectedOperations.Length
            || curricula.Count != expectedOperations.Length)
        {
            throw new ArgumentException("Target selection requires progression and curriculum for every operation.");
        }

        foreach (var operation in expectedOperations)
        {
            if (!operationProgressions.TryGetValue(operation, out var progression)
                || progression.Operation != operation)
            {
                throw new ArgumentException("Operation progression keys and values must match.", nameof(operationProgressions));
            }

            if (!curricula.TryGetValue(operation, out var curriculum)
                || curriculum.Operation != operation)
            {
                throw new ArgumentException("Operation curriculum keys and values must match.", nameof(curricula));
            }
        }

        var recentFacts = recentAcceptedFactsOldestToNewest.ToArray();
        if (recentFacts.Any(fact => fact is null))
        {
            throw new ArgumentException("Recent accepted history cannot contain null facts.", nameof(recentAcceptedFactsOldestToNewest));
        }

        ProspectivePracticePosition = prospectivePracticePosition;
        CurrentSessionOrder = currentSessionOrder;
        OperationProgressions = operationProgressions.ToFrozenDictionary();
        Curricula = curricula.ToFrozenDictionary();
        CandidateIndex = candidateIndex;
        RecentAcceptedFactsOldestToNewest = Array.AsReadOnly(recentFacts);
    }
}
