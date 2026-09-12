namespace MathFirst.Application.Practice;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public sealed class AdaptivePracticeSelector
{
    private const int TargetCandidateWindowSize = 64;

    public static string GetCanonicalMirrorKey(ArithmeticFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Multiplication)
        {
            var min = Math.Min(fact.LeftOperand, fact.RightOperand);
            var max = Math.Max(fact.LeftOperand, fact.RightOperand);
            return $"{fact.Operation}:{min}:{max}";
        }
        return fact.Id;
    }

    public static ArithmeticOperation GetScheduledOperation(
        long prospectivePracticePosition,
        IReadOnlyList<ArithmeticOperation>? enabledOperations = null)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        var enabled = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(enabledOperations);
        var k = enabled.Count;
        var index = checked((int)((prospectivePracticePosition - 1) % k));
        return enabled[index];
    }

    public static long GetOperationAttemptOrdinal(
        long prospectivePracticePosition,
        int enabledOperationCount = 4)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }
        if (enabledOperationCount <= 0 || enabledOperationCount > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(enabledOperationCount));
        }

        return checked(((prospectivePracticePosition - 1) / enabledOperationCount) + 1);
    }

    public static PracticeSelectionRole GetRequestedRole(
        long prospectivePracticePosition,
        int enabledOperationCount = 4)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        return ((GetOperationAttemptOrdinal(prospectivePracticePosition, enabledOperationCount) - 1) % 10) switch
        {
            0 => PracticeSelectionRole.New,
            1 => PracticeSelectionRole.Due,
            2 => PracticeSelectionRole.New,
            3 => PracticeSelectionRole.Maintenance,
            4 => PracticeSelectionRole.Frontier,
            5 => PracticeSelectionRole.New,
            6 => PracticeSelectionRole.Due,
            7 => PracticeSelectionRole.New,
            8 => PracticeSelectionRole.Due,
            9 => PracticeSelectionRole.Frontier,
            _ => throw new InvalidOperationException("The role schedule produced an invalid remainder.")
        };
    }

    public PracticeSelectionResult SelectTargetFact(PracticeSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var operation = GetScheduledOperation(context.ProspectivePracticePosition, context.EnabledOperations);
        var requestedRole = GetRequestedRole(context.ProspectivePracticePosition, context.EnabledOperations.Count);
        var progression = context.OperationProgressions[operation];
        var curriculum = context.Curricula[operation];
        if (!curriculum.TryGetBand(progression.BandIndex, out var band))
        {
            throw new InvalidOperationException("The current target curriculum band is unavailable.");
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(progression.BandIndex);
        var remediation = context.CandidateIndex.RemediationCandidates
            .Where(candidate => candidate.Fact.Operation == operation
                && candidate.ItemState?.NeedsRemediation == true
                && candidate.FsrsState?.LastReviewPracticePosition is not null
                && context.ProspectivePracticePosition >= candidate.FsrsState.LastReviewPracticePosition.Value + 4)
            .ToArray();
        if (remediation.Length > 0)
        {
            var remediationFacts = remediation
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();
            return CreateTargetResult(
                context,
                band!,
                operation,
                requestedRole,
                PracticeSelectionRole.Remediation,
                remediationFacts);
        }

        var introductionFrontier = band!.Kind == CurriculumBandKind.Structured
            ? DeterministicFactRanker.SelectStructuredSample(ownedFrontier, operation, band.Id)
            : ownedFrontier;
        var newPool = introductionFrontier
            .Where(fact => !context.CandidateIndex.IsMaterialized(fact.Id))
            .ToArray();
        if (band.Kind == CurriculumBandKind.Dense && newPool.Length > 0)
        {
            return CreateTargetResult(
                context,
                band,
                operation,
                requestedRole,
                PracticeSelectionRole.New,
                newPool);
        }
        var frontierPool = context.CandidateIndex.HasBoundedSemanticPools
            ? context.CandidateIndex.CurrentBandMaterializedFacts
            : ownedFrontier
                .Where(fact => context.CandidateIndex.IsMaterialized(fact.Id))
                .Select(fact => context.CandidateIndex.GetCandidate(fact.Id))
                .OrderBy(candidate => candidate.ItemState?.IsProvisionallyMastered == true ? 1 : 0)
                .ThenBy(candidate => candidate.ItemState?.TotalAttempts ?? 0)
                .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition is null ? 0 : 1)
                .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
                .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();
        var duePool = context.CandidateIndex.HasBoundedSemanticPools
            ? context.CandidateIndex.DueFacts
            : context.CandidateIndex.Candidates
                .Where(candidate => candidate.Fact.Operation == operation
                    && candidate.FsrsState is not null
                    && candidate.FsrsState.DuePracticePosition <= context.ProspectivePracticePosition)
                .OrderBy(candidate => candidate.FsrsState!.DuePracticePosition)
                .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition is null ? 0 : 1)
                .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
                .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();
        var maintenancePool = context.CandidateIndex.HasBoundedSemanticPools
            ? context.CandidateIndex.MaintenanceFacts
            : context.CandidateIndex.Candidates
                .Where(candidate => candidate.Fact.Operation == operation
                    && candidate.ItemState?.NeedsRemediation != true
                    && candidate.FsrsState is not null
                    && candidate.FsrsState.DuePracticePosition > context.ProspectivePracticePosition
                    && candidate.FsrsState.LastReviewPracticePosition is not null
                    && context.ProspectivePracticePosition >= candidate.FsrsState.LastReviewPracticePosition.Value + 40)
                .OrderBy(candidate => candidate.FsrsState!.LastReviewPracticePosition!.Value)
                .ThenBy(candidate => candidate.FsrsState!.DuePracticePosition)
                .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();
        var earlyReviewPool = context.CandidateIndex.HasBoundedSemanticPools
            ? context.CandidateIndex.EarlyReviewFacts
            : context.CandidateIndex.Candidates
                .Where(candidate => candidate.Fact.Operation == operation
                    && candidate.ItemState?.NeedsRemediation != true
                    && candidate.FsrsState is not null
                    && candidate.FsrsState.DuePracticePosition > context.ProspectivePracticePosition)
                .OrderBy(candidate => candidate.FsrsState?.LastReviewPracticePosition is null ? 0 : 1)
                .ThenBy(candidate => candidate.FsrsState?.LastReviewPracticePosition ?? 0)
                .ThenBy(candidate => candidate.FsrsState!.DuePracticePosition)
                .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();

        var pools = new Dictionary<PracticeSelectionRole, IReadOnlyList<ArithmeticFact>>
        {
            [PracticeSelectionRole.New] = newPool,
            [PracticeSelectionRole.Due] = duePool,
            [PracticeSelectionRole.Maintenance] = maintenancePool,
            [PracticeSelectionRole.Frontier] = frontierPool,
            [PracticeSelectionRole.EarlyReview] = earlyReviewPool
        };

        foreach (var resolvedRole in GetFallbackChain(requestedRole))
        {
            if (pools[resolvedRole].Count > 0)
            {
                return CreateTargetResult(
                    context,
                    band,
                    operation,
                    requestedRole,
                    resolvedRole,
                    pools[resolvedRole]);
            }
        }

        throw new InvalidOperationException(
            $"No valid target practice candidate exists for scheduled operation {operation} at position {context.ProspectivePracticePosition}.");
    }

    private static IReadOnlyList<PracticeSelectionRole> GetFallbackChain(PracticeSelectionRole requestedRole) =>
        requestedRole switch
        {
            PracticeSelectionRole.New =>
            [
                PracticeSelectionRole.New,
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Due,
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.EarlyReview
            ],
            PracticeSelectionRole.Due =>
            [
                PracticeSelectionRole.Due,
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.EarlyReview
            ],
            PracticeSelectionRole.Maintenance =>
            [
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Due,
                PracticeSelectionRole.EarlyReview
            ],
            PracticeSelectionRole.Frontier =>
            [
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Due,
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.EarlyReview
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(requestedRole), requestedRole, "Unknown requested role.")
        };

    private static PracticeSelectionResult CreateTargetResult(
        PracticeSelectionContext context,
        CurriculumBand currentBand,
        ArithmeticOperation operation,
        PracticeSelectionRole requestedRole,
        PracticeSelectionRole resolvedRole,
        IReadOnlyList<ArithmeticFact> semanticPool)
    {
        var (fact, relaxation) = SelectTargetCandidate(
            semanticPool,
            context.RecentAcceptedFactsOldestToNewest);
        var isMaterialized = context.CandidateIndex.IsMaterialized(fact.Id);
        var isNewIntroduction = resolvedRole == PracticeSelectionRole.New && !isMaterialized;

        if (resolvedRole != PracticeSelectionRole.New && !isMaterialized)
        {
            throw new InvalidOperationException("Only the New semantic pool may return an unmaterialized fact.");
        }

        return new PracticeSelectionResult(
            operation,
            requestedRole,
            resolvedRole,
            fact,
            isMaterialized,
            isNewIntroduction,
            currentBand.Id,
            relaxation);
    }

    private static (ArithmeticFact Fact, PracticeCooldownRelaxation Relaxation) SelectTargetCandidate(
        IReadOnlyList<ArithmeticFact> semanticPool,
        IReadOnlyList<ArithmeticFact> recentAcceptedFactsOldestToNewest)
    {
        var recent = recentAcceptedFactsOldestToNewest.TakeLast(LearningPolicy.ExactFactCooldownDistance).ToArray();
        var recentExactIds = recent.Select(fact => fact.Id).ToHashSet(StringComparer.Ordinal);
        var fullyFiltered = semanticPool
            .Where(fact => !recentExactIds.Contains(fact.Id))
            .Where(fact => !HasRecentCommutativeMirror(fact, recent))
            .ToArray();
        if (fullyFiltered.Length > 0)
        {
            return (fullyFiltered[0], PracticeCooldownRelaxation.None);
        }

        var mirrorRelaxed = semanticPool
            .Where(fact => !recentExactIds.Contains(fact.Id))
            .ToArray();
        if (mirrorRelaxed.Length > 0)
        {
            return (mirrorRelaxed[0], PracticeCooldownRelaxation.Mirror);
        }

        return (semanticPool[0], PracticeCooldownRelaxation.Exact);
    }

    private static bool HasRecentCommutativeMirror(
        ArithmeticFact candidate,
        IEnumerable<ArithmeticFact> recentFacts)
    {
        if (candidate.Operation is not (ArithmeticOperation.Addition or ArithmeticOperation.Multiplication)
            || candidate.LeftOperand == candidate.RightOperand)
        {
            return false;
        }

        return recentFacts.Any(recent =>
            recent.Operation == candidate.Operation
            && recent.LeftOperand == candidate.RightOperand
            && recent.RightOperand == candidate.LeftOperand);
    }

}
