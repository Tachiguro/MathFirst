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
        IEnumerable<PracticeSelectionCandidate> earlyReviewCandidates)
    {
        CurrentBandCandidates = Copy(currentBandCandidates, nameof(currentBandCandidates));
        DueCandidates = Copy(dueCandidates, nameof(dueCandidates));
        MaintenanceCandidates = Copy(maintenanceCandidates, nameof(maintenanceCandidates));
        RemediationCandidates = Copy(remediationCandidates, nameof(remediationCandidates));
        EarlyReviewCandidates = Copy(earlyReviewCandidates, nameof(earlyReviewCandidates));

        if (DueCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || MaintenanceCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || RemediationCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize
            || EarlyReviewCandidates.Count > PracticeSelectionEvidenceRequest.CandidateWindowSize)
        {
            throw new ArgumentException("Open-ended practice-selection candidate windows must be bounded.");
        }
    }

    public IReadOnlyList<PracticeSelectionCandidate> CurrentBandCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> DueCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> MaintenanceCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> RemediationCandidates { get; }
    public IReadOnlyList<PracticeSelectionCandidate> EarlyReviewCandidates { get; }

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
        .Concat(EarlyReviewCandidates);

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

        var remediationCandidates = candidates
            .Where(candidate => candidate.ItemState.NeedsRemediation
                && candidate.FsrsState?.LastReviewPracticePosition is not null
                && request.ProspectivePracticePosition >= candidate.FsrsState.LastReviewPracticePosition.Value + 4)
            .OrderBy(candidate => candidate.FsrsState!.LastReviewPracticePosition!.Value)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize)
            .ToArray();

        var remediationEligibleFactIds = remediationCandidates.Select(c => c.Fact.Id).ToHashSet(StringComparer.Ordinal);

        var dueCandidates = candidates
            .Where(candidate => candidate.FsrsState is not null
                && candidate.FsrsState.DuePracticePosition <= request.ProspectivePracticePosition
                && !remediationEligibleFactIds.Contains(candidate.Fact.Id))
            .OrderBy(candidate => candidate.FsrsState!.DuePracticePosition)
            .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize)
            .ToArray();

        var maintenanceCandidates = candidates
            .Where(candidate => !candidate.ItemState.NeedsRemediation
                && candidate.FsrsState is not null
                && candidate.FsrsState.DuePracticePosition > request.ProspectivePracticePosition
                && candidate.FsrsState.LastReviewPracticePosition is not null
                && request.ProspectivePracticePosition >= candidate.FsrsState.LastReviewPracticePosition.Value + 40)
            .OrderBy(candidate => candidate.FsrsState!.LastReviewPracticePosition!.Value)
            .ThenBy(candidate => candidate.FsrsState!.DuePracticePosition)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize)
            .ToArray();

        var earlyReviewCandidates = candidates
            .Where(candidate => !candidate.ItemState.NeedsRemediation
                && candidate.FsrsState is not null
                && candidate.FsrsState.DuePracticePosition > request.ProspectivePracticePosition)
            .OrderBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(candidate => candidate.FsrsState!.DuePracticePosition)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize)
            .ToArray();

        var currentBandCandidates = request.CurrentBandOwnedFrontier
            .Where(fact => byId.ContainsKey(fact.Id) && !remediationEligibleFactIds.Contains(fact.Id))
            .Select(fact => byId[fact.Id])
            .OrderBy(candidate => candidate.ItemState.IsProvisionallyMastered ? 1 : 0)
            .ThenBy(candidate => candidate.ItemState.TotalAttempts)
            .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(PracticeSelectionEvidenceRequest.CandidateWindowSize)
            .ToArray();

        return new PracticeSelectionEvidence(
            currentBandCandidates,
            dueCandidates,
            maintenanceCandidates,
            remediationCandidates,
            earlyReviewCandidates);
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
