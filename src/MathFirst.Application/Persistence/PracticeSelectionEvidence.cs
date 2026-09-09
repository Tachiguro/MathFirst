namespace MathFirst.Application.Persistence;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed class PracticeSelectionEvidenceRequest
{
    public const int CandidateWindowSize = 64;

    public PracticeSelectionEvidenceRequest(
        ArithmeticOperation operation,
        long prospectivePracticePosition,
        int currentSessionOrder,
        IEnumerable<ArithmeticFact> currentBandOwnedFrontier,
        IEnumerable<ArithmeticFact> introductionFrontier)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        if (currentSessionOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentSessionOrder));
        }

        Operation = operation;
        ProspectivePracticePosition = prospectivePracticePosition;
        CurrentSessionOrder = currentSessionOrder;
        CurrentBandOwnedFrontier = CopyFacts(currentBandOwnedFrontier, operation, nameof(currentBandOwnedFrontier));
        IntroductionFrontier = CopyFacts(introductionFrontier, operation, nameof(introductionFrontier));
    }

    public ArithmeticOperation Operation { get; }
    public long ProspectivePracticePosition { get; }
    public int CurrentSessionOrder { get; }
    public IReadOnlyList<ArithmeticFact> CurrentBandOwnedFrontier { get; }
    public IReadOnlyList<ArithmeticFact> IntroductionFrontier { get; }

    private static IReadOnlyList<ArithmeticFact> CopyFacts(
        IEnumerable<ArithmeticFact> facts,
        ArithmeticOperation operation,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var copied = facts.ToArray();
        if (copied.Any(fact => fact is null || fact.Operation != operation)
            || copied.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new ArgumentException("Selection facts must be unique and belong to the requested operation.", parameterName);
        }

        return Array.AsReadOnly(copied);
    }
}

public sealed record PracticeSelectionCandidate(
    ArithmeticFact Fact,
    ItemLearningState ItemState,
    FsrsCardState? FsrsState);

public sealed class PracticeSelectionEvidence
{
    public PracticeSelectionEvidence(
        IEnumerable<PracticeSelectionCandidate> currentBandCandidates,
        IEnumerable<PracticeSelectionCandidate> dueCandidates,
        IEnumerable<PracticeSelectionCandidate> maintenanceCandidates,
        IEnumerable<PracticeSelectionCandidate> remediationCandidates,
        IEnumerable<PracticeSelectionCandidate> anyMaterializedCandidates)
    {
        CurrentBandCandidates = Copy(currentBandCandidates, nameof(currentBandCandidates));
        DueCandidates = Copy(dueCandidates, nameof(dueCandidates));
        MaintenanceCandidates = Copy(maintenanceCandidates, nameof(maintenanceCandidates));
        RemediationCandidates = Copy(remediationCandidates, nameof(remediationCandidates));
        AnyMaterializedCandidates = Copy(anyMaterializedCandidates, nameof(anyMaterializedCandidates));

        if (DueCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || MaintenanceCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || RemediationCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || AnyMaterializedCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize)
        {
            throw new ArgumentException("Open-ended practice-selection candidate windows must be bounded.");
        }
    }

    public IReadOnlyList<PracticeSelectionCandidate> CurrentBandCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> DueCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> MaintenanceCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> RemediationCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> AnyMaterializedCandidates { get; }

    public IReadOnlyDictionary<string, ItemLearningState> ItemStates => AllCandidates()
        .GroupBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().ItemState, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, FsrsCardState> FsrsStates => AllCandidates()
        .Where(candidate => candidate.FsrsState is not null)
        .GroupBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().FsrsState!, StringComparer.Ordinal);

    public IEnumerable<PracticeSelectionCandidate> AllCandidates() => CurrentBandCandidates
        .Concat(DueCandidates)
        .Concat(MaintenanceCandidates)
        .Concat(RemediationCandidates)
        .Concat(AnyMaterializedCandidates);

    public static PracticeSelectionEvidence FromSnapshot(
        LearnerSnapshot snapshot,
        PracticeSelectionEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        var candidates = snapshot.ItemStates.Values
            .Where(state => state.Operation == request.Operation)
            .Select(state => new PracticeSelectionCandidate(
                new ArithmeticFact(state.Operation, state.LeftOperand, state.RightOperand),
                state,
                snapshot.FsrsStates.GetValueOrDefault(state.FactId)))
            .ToArray();
        var byId = candidates.ToDictionary(candidate => candidate.Fact.Id, StringComparer.Ordinal);

        return new PracticeSelectionEvidence(
            request.CurrentBandOwnedFrontier.Where(fact => byId.ContainsKey(fact.Id)).Select(fact => byId[fact.Id]),
            candidates.Where(candidate => candidate.FsrsState?.DuePracticePosition <= request.ProspectivePracticePosition)
                .OrderBy(candidate => candidate.FsrsState!.DuePracticePosition).ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize),
            candidates.Where(candidate => !candidate.ItemState.NeedsRemediation
                    && (candidate.FsrsState is null || candidate.FsrsState.DuePracticePosition > request.ProspectivePracticePosition))
                .OrderBy(candidate => candidate.ItemState.LastPracticedOrder)
                .ThenBy(candidate => candidate.FsrsState?.DuePracticePosition ?? long.MaxValue)
                .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize),
            candidates.Where(candidate => candidate.ItemState.NeedsRemediation
                    && request.CurrentSessionOrder >= candidate.ItemState.RemediationDueOrder)
                .OrderBy(candidate => candidate.ItemState.RemediationDueOrder).ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize),
            candidates.OrderBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize));
    }

    private static IReadOnlyList<PracticeSelectionCandidate> Copy(
        IEnumerable<PracticeSelectionCandidate> candidates,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var copied = candidates.ToArray();
        if (copied.Any(candidate => candidate is null
                || candidate.ItemState.FactId != candidate.Fact.Id
                || (candidate.FsrsState is not null && candidate.FsrsState.FactId != candidate.Fact.Id))
            || copied.Select(candidate => candidate.Fact.Id).Distinct(StringComparer.Ordinal).Count() != copied.Length)
        {
            throw new ArgumentException("Selection candidates must be unique and internally consistent.", parameterName);
        }

        return Array.AsReadOnly(copied);
    }
}
