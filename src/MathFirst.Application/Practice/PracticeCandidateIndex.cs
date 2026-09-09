namespace MathFirst.Application.Practice;

using System.Collections.Frozen;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed class PracticeCandidateIndex
{
    private readonly FrozenDictionary<string, IndexedPracticeCandidate> _candidates;

    public IReadOnlyList<ArithmeticFact> MaterializedFacts { get; }
    public IReadOnlyList<ArithmeticFact> CurrentBandMaterializedFacts { get; }
    public IReadOnlyList<ArithmeticFact> DueFacts { get; }
    public IReadOnlyList<ArithmeticFact> MaintenanceFacts { get; }
    internal IReadOnlyList<IndexedPracticeCandidate> RemediationCandidates { get; }
    public IReadOnlyList<ArithmeticFact> AnyMaterializedFacts { get; }
    public bool HasBoundedSemanticPools { get; }

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
        CurrentBandMaterializedFacts = MaterializedFacts;
        DueFacts = candidates.Values
            .Where(candidate => candidate.DuePracticePosition is not null)
            .OrderBy(candidate => candidate.DuePracticePosition)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Fact)
            .ToArray();
        MaintenanceFacts = candidates.Values
            .Where(candidate => !candidate.NeedsRemediation)
            .OrderBy(candidate => candidate.LastPracticedOrder)
            .ThenBy(candidate => candidate.DuePracticePosition ?? long.MaxValue)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Fact)
            .ToArray();
        RemediationCandidates = candidates.Values
            .Where(candidate => candidate.NeedsRemediation)
            .OrderBy(candidate => candidate.RemediationDueOrder)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .ToArray();
        AnyMaterializedFacts = MaterializedFacts;
        HasBoundedSemanticPools = false;
    }

    public PracticeCandidateIndex(PracticeSelectionEvidence evidence)
        : this(
            evidence?.AllCandidates().Select(candidate => candidate.Fact)
                .GroupBy(fact => fact.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                ?? throw new ArgumentNullException(nameof(evidence)),
            evidence.ItemStates,
            evidence.FsrsStates)
    {
        CurrentBandMaterializedFacts = evidence.CurrentBandCandidates.Select(candidate => candidate.Fact).ToArray();
        DueFacts = evidence.DueCandidates.Select(candidate => candidate.Fact).ToArray();
        MaintenanceFacts = evidence.MaintenanceCandidates.Select(candidate => candidate.Fact).ToArray();
        RemediationCandidates = evidence.RemediationCandidates
            .Select(candidate => new IndexedPracticeCandidate(
                candidate.Fact,
                candidate.ItemState.NeedsRemediation,
                candidate.ItemState.RemediationDueOrder,
                candidate.ItemState.LastPracticedOrder,
                candidate.FsrsState?.DuePracticePosition))
            .ToArray();
        AnyMaterializedFacts = evidence.AnyMaterializedCandidates.Select(candidate => candidate.Fact).ToArray();
        HasBoundedSemanticPools = true;
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
