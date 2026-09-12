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
    public IReadOnlyList<ArithmeticFact> EarlyReviewFacts { get; }
    public IReadOnlyList<IndexedPracticeCandidate> RemediationCandidates { get; }
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
                    itemState,
                    fsrsState));
        }

        _candidates = candidates.ToFrozenDictionary(StringComparer.Ordinal);
        MaterializedFacts = Array.AsReadOnly(candidates.Values
            .Select(candidate => candidate.Fact)
            .OrderBy(fact => fact.Id, StringComparer.Ordinal)
            .ToArray());
        CurrentBandMaterializedFacts = MaterializedFacts;
        DueFacts = candidates.Values
            .Where(candidate => candidate.FsrsState is not null)
            .OrderBy(candidate => candidate.FsrsState!.DuePracticePosition)
            .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Fact)
            .ToArray();
        MaintenanceFacts = candidates.Values
            .Where(candidate => candidate.ItemState?.NeedsRemediation != true && candidate.FsrsState?.LastReviewPracticePosition is not null)
            .OrderBy(candidate => candidate.FsrsState!.LastReviewPracticePosition!.Value)
            .ThenBy(candidate => candidate.FsrsState?.DuePracticePosition ?? long.MaxValue)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Fact)
            .ToArray();
        RemediationCandidates = candidates.Values
            .Where(candidate => candidate.ItemState?.NeedsRemediation == true && candidate.FsrsState?.LastReviewPracticePosition is not null)
            .OrderBy(candidate => candidate.FsrsState!.LastReviewPracticePosition!.Value)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .ToArray();
        EarlyReviewFacts = candidates.Values
            .Where(candidate => candidate.ItemState?.NeedsRemediation != true && candidate.FsrsState is not null)
            .OrderBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(candidate => candidate.FsrsState?.DuePracticePosition ?? long.MaxValue)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Fact)
            .ToArray();
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
        CurrentBandMaterializedFacts = (evidence.Operation.HasValue
            ? evidence.CurrentBandCandidates.Where(candidate => candidate.Fact.Operation == evidence.Operation.Value)
            : evidence.CurrentBandCandidates)
            .Select(candidate => candidate.Fact)
            .ToArray();
        DueFacts = (evidence.Operation.HasValue
            ? evidence.DueCandidates.Where(candidate => candidate.Fact.Operation == evidence.Operation.Value)
            : evidence.DueCandidates)
            .Select(candidate => candidate.Fact)
            .ToArray();
        MaintenanceFacts = (evidence.Operation.HasValue
            ? evidence.MaintenanceCandidates.Where(candidate => candidate.Fact.Operation == evidence.Operation.Value)
            : evidence.MaintenanceCandidates)
            .Select(candidate => candidate.Fact)
            .ToArray();
        RemediationCandidates = (evidence.Operation.HasValue
            ? evidence.RemediationCandidates.Where(candidate => candidate.Fact.Operation == evidence.Operation.Value)
            : evidence.RemediationCandidates)
            .Select(candidate => new IndexedPracticeCandidate(
                candidate.Fact,
                candidate.ItemState,
                candidate.FsrsState))
            .ToArray();
        EarlyReviewFacts = (evidence.Operation.HasValue
            ? evidence.EarlyReviewCandidates.Where(candidate => candidate.Fact.Operation == evidence.Operation.Value)
            : evidence.EarlyReviewCandidates)
            .Select(candidate => candidate.Fact)
            .ToArray();
        HasBoundedSemanticPools = true;
    }

    public bool IsMaterialized(string factId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        return _candidates.ContainsKey(factId);
    }

    public IndexedPracticeCandidate GetCandidate(string factId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        return _candidates[factId];
    }
}

public sealed record IndexedPracticeCandidate(
    ArithmeticFact Fact,
    ItemLearningState? ItemState,
    FsrsCardState? FsrsState);
