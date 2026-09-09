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

    public static ArithmeticOperation GetScheduledOperation(long prospectivePracticePosition)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        return ((prospectivePracticePosition - 1) % 4) switch
        {
            0 => ArithmeticOperation.Addition,
            1 => ArithmeticOperation.Subtraction,
            2 => ArithmeticOperation.Multiplication,
            3 => ArithmeticOperation.Division,
            _ => throw new InvalidOperationException("The operation schedule produced an invalid remainder.")
        };
    }

    public static long GetOperationAttemptOrdinal(long prospectivePracticePosition)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        return ((prospectivePracticePosition - 1) / 4) + 1;
    }

    public static PracticeSelectionRole GetRequestedRole(long prospectivePracticePosition)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prospectivePracticePosition));
        }

        return ((GetOperationAttemptOrdinal(prospectivePracticePosition) - 1) % 10) switch
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
        var operation = GetScheduledOperation(context.ProspectivePracticePosition);
        var requestedRole = GetRequestedRole(context.ProspectivePracticePosition);
        var progression = context.OperationProgressions[operation];
        var curriculum = context.Curricula[operation];
        if (!curriculum.TryGetBand(progression.BandIndex, out var band))
        {
            throw new InvalidOperationException("The current target curriculum band is unavailable.");
        }

        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(progression.BandIndex);
        var materializedCandidates = context.CandidateIndex.Candidates
            .Where(candidate => candidate.Fact.Operation == operation)
            .ToArray();

        var remediation = materializedCandidates
            .Where(candidate => candidate.NeedsRemediation
                && context.CurrentSessionOrder >= candidate.RemediationDueOrder)
            .ToArray();
        if (remediation.Length > 0)
        {
            var earliestDueOrder = remediation.Min(candidate => candidate.RemediationDueOrder);
            var earliestDue = remediation
                .Where(candidate => candidate.RemediationDueOrder == earliestDueOrder)
                .OrderBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
                .Take(TargetCandidateWindowSize)
                .Select(candidate => candidate.Fact)
                .ToArray();
            return CreateTargetResult(
                context,
                band!,
                operation,
                requestedRole,
                PracticeSelectionRole.Remediation,
                earliestDue);
        }

        var introductionFrontier = band!.Kind == CurriculumBandKind.Structured
            ? DeterministicFactRanker.SelectStructuredSample(ownedFrontier, operation, band.Id)
            : ownedFrontier;
        var newPool = introductionFrontier
            .Where(fact => !context.CandidateIndex.IsMaterialized(fact.Id))
            .ToArray();
        var frontierPool = ownedFrontier
            .Where(fact => context.CandidateIndex.IsMaterialized(fact.Id))
            .ToArray();
        var duePool = materializedCandidates
            .Where(candidate => candidate.DuePracticePosition <= context.ProspectivePracticePosition)
            .OrderBy(candidate => candidate.DuePracticePosition)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(TargetCandidateWindowSize)
            .Select(candidate => candidate.Fact)
            .ToArray();
        var maintenancePool = materializedCandidates
            .Where(candidate => candidate.DuePracticePosition > context.ProspectivePracticePosition
                || candidate.DuePracticePosition is null)
            .Where(candidate => !candidate.NeedsRemediation)
            .OrderBy(candidate => candidate.LastPracticedOrder)
            .ThenBy(candidate => candidate.DuePracticePosition ?? long.MaxValue)
            .ThenBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(TargetCandidateWindowSize)
            .Select(candidate => candidate.Fact)
            .ToArray();
        var anyMaterializedPool = materializedCandidates
            .OrderBy(candidate => candidate.Fact.Id, StringComparer.Ordinal)
            .Take(TargetCandidateWindowSize)
            .Select(candidate => candidate.Fact)
            .ToArray();

        var pools = new Dictionary<PracticeSelectionRole, IReadOnlyList<ArithmeticFact>>
        {
            [PracticeSelectionRole.New] = newPool,
            [PracticeSelectionRole.Due] = duePool,
            [PracticeSelectionRole.Maintenance] = maintenancePool,
            [PracticeSelectionRole.Frontier] = frontierPool,
            [PracticeSelectionRole.AnyMaterialized] = anyMaterializedPool
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
                PracticeSelectionRole.AnyMaterialized
            ],
            PracticeSelectionRole.Due =>
            [
                PracticeSelectionRole.Due,
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.AnyMaterialized
            ],
            PracticeSelectionRole.Maintenance =>
            [
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Due,
                PracticeSelectionRole.AnyMaterialized
            ],
            PracticeSelectionRole.Frontier =>
            [
                PracticeSelectionRole.Frontier,
                PracticeSelectionRole.Due,
                PracticeSelectionRole.Maintenance,
                PracticeSelectionRole.AnyMaterialized
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
            operation,
            currentBand.Id,
            resolvedRole,
            context.ProspectivePracticePosition,
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
        ArithmeticOperation operation,
        CurriculumBandId bandId,
        PracticeSelectionRole resolvedRole,
        long prospectivePracticePosition,
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
            return (RankFirst(fullyFiltered, operation, bandId, resolvedRole, prospectivePracticePosition), PracticeCooldownRelaxation.None);
        }

        var mirrorRelaxed = semanticPool
            .Where(fact => !recentExactIds.Contains(fact.Id))
            .ToArray();
        if (mirrorRelaxed.Length > 0)
        {
            return (RankFirst(mirrorRelaxed, operation, bandId, resolvedRole, prospectivePracticePosition), PracticeCooldownRelaxation.Mirror);
        }

        return (RankFirst(semanticPool, operation, bandId, resolvedRole, prospectivePracticePosition), PracticeCooldownRelaxation.Exact);
    }

    private static ArithmeticFact RankFirst(
        IEnumerable<ArithmeticFact> candidates,
        ArithmeticOperation operation,
        CurriculumBandId bandId,
        PracticeSelectionRole resolvedRole,
        long prospectivePracticePosition) => DeterministicFactRanker.Order(
            candidates,
            operation,
            bandId,
            GetRankingRole(resolvedRole),
            prospectivePracticePosition)[0];

    private static FactSelectionRole GetRankingRole(PracticeSelectionRole resolvedRole) => resolvedRole switch
    {
        PracticeSelectionRole.New => FactSelectionRole.New,
        PracticeSelectionRole.Due => FactSelectionRole.Due,
        PracticeSelectionRole.Maintenance => FactSelectionRole.Maintenance,
        PracticeSelectionRole.Frontier => FactSelectionRole.Frontier,
        PracticeSelectionRole.AnyMaterialized => FactSelectionRole.Any,
        PracticeSelectionRole.Remediation => FactSelectionRole.Due,
        _ => throw new ArgumentOutOfRangeException(nameof(resolvedRole), resolvedRole, "Unknown resolved role.")
    };

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
