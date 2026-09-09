namespace MathFirst.Application.Practice;

using System.Collections.Frozen;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed class PracticeCandidateIndex
{
    private readonly FrozenDictionary<string, IndexedPracticeCandidate> _candidates;

    public IReadOnlyList<ArithmeticFact> MaterializedFacts { get; }

    internal IEnumerable<IndexedPracticeCandidate> Candidates => _candidates.Values;

    public PracticeCandidateIndex(
        IEnumerable<ArithmeticFact> materializedFacts,
        IReadOnlyDictionary<string, ItemLearningState> itemStates,
        IReadOnlyDictionary<string, FsrsCardState> fsrsStates)
    {
        ArgumentNullException.ThrowIfNull(materializedFacts);
        ArgumentNullException.ThrowIfNull(itemStates);
        ArgumentNullException.ThrowIfNull(fsrsStates);

        var factsById = new Dictionary<string, ArithmeticFact>(StringComparer.Ordinal);
        foreach (var fact in materializedFacts)
        {
            ArgumentNullException.ThrowIfNull(fact);
            if (!factsById.TryAdd(fact.Id, fact))
            {
                throw new ArgumentException("Materialized facts cannot contain duplicate FactIds.", nameof(materializedFacts));
            }
        }

        foreach (var factId in itemStates.Keys.Concat(fsrsStates.Keys))
        {
            if (!factsById.ContainsKey(factId))
            {
                throw new ArgumentException(
                    "Every materialized state entry must have a supplied arithmetic fact.",
                    nameof(materializedFacts));
            }
        }

        var candidates = new Dictionary<string, IndexedPracticeCandidate>(StringComparer.Ordinal);
        foreach (var (factId, fact) in factsById)
        {
            itemStates.TryGetValue(factId, out var itemState);
            fsrsStates.TryGetValue(factId, out var fsrsState);
            if (itemState is null && fsrsState is null)
            {
                throw new ArgumentException(
                    "A supplied materialized fact requires item or FSRS state evidence.",
                    nameof(materializedFacts));
            }

            if (itemState is not null
                && (itemState.FactId != factId
                    || itemState.Operation != fact.Operation
                    || itemState.LeftOperand != fact.LeftOperand
                    || itemState.RightOperand != fact.RightOperand))
            {
                throw new ArgumentException("Item state does not match its arithmetic fact.", nameof(itemStates));
            }

            if (fsrsState is not null && fsrsState.FactId != factId)
            {
                throw new ArgumentException("FSRS state does not match its arithmetic fact.", nameof(fsrsStates));
            }

            candidates.Add(
                factId,
                new IndexedPracticeCandidate(
                    fact,
                    itemState?.NeedsRemediation ?? false,
                    itemState?.RemediationDueOrder ?? 0,
                    itemState?.LastPracticedOrder ?? 0,
                    fsrsState?.DuePracticePosition));
        }

        _candidates = candidates.ToFrozenDictionary(StringComparer.Ordinal);
        MaterializedFacts = Array.AsReadOnly(candidates.Values
            .Select(candidate => candidate.Fact)
            .OrderBy(fact => fact.Id, StringComparer.Ordinal)
            .ToArray());
    }

    public bool IsMaterialized(string factId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        return _candidates.ContainsKey(factId);
    }
}

internal sealed record IndexedPracticeCandidate(
    ArithmeticFact Fact,
    bool NeedsRemediation,
    int RemediationDueOrder,
    int LastPracticedOrder,
    long? DuePracticePosition);
