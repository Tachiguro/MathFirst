namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class GuidedNumberSpaceSelectionTests
{
    [Fact]
    public void PracticeSelectionContext_DefaultsToUnrestrictedGate()
    {
        var curriculum = new ArithmeticCurriculum();
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 1,
            currentSessionOrder: 0,
            operationProgressions: CreateProgressions(),
            curricula: CreateCurricula(curriculum),
            candidateIndex: new PracticeCandidateIndex(
                [],
                new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
                new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)),
            recentAcceptedFactsOldestToNewest: [],
            scheduledOperationAttemptOrdinal: 1);

        Assert.Same(GuidedNumberSpaceGate.Unrestricted, context.GuidedNumberSpaceGate);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Multiplication)]
    [InlineData(ArithmeticOperation.Division)]
    public void NewPool_CannotReturnGuidedIneligibleFact(ArithmeticOperation operation)
    {
        var (low, high) = CreateNumberSpaceFacts(operation);
        var setup = CreateSetup(operation, low, high);
        var position = FindScheduledPosition(operation);
        var lowCandidate = CreateCandidate(low, duePosition: 1);
        var evidence = new PracticeSelectionEvidence(
            operation,
            position,
            currentBandCandidates: [],
            dueCandidates: [lowCandidate],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);
        var context = CreateContext(setup, position, 1, evidence);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    [Fact]
    public void RemediationPool_CannotReturnGuidedIneligibleFact()
    {
        var (low, high) = CreateNumberSpaceFacts(ArithmeticOperation.Multiplication);
        var setup = CreateSetup(ArithmeticOperation.Multiplication, low, high);
        var position = FindScheduledPosition(ArithmeticOperation.Multiplication);
        var highCandidate = CreateCandidate(high, duePosition: position + 100, lastReviewPosition: position - 4);
        highCandidate.ItemState.NeedsRemediation = true;
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            position,
            currentBandCandidates: [CreateCandidate(low)],
            dueCandidates: [],
            maintenanceCandidates: [],
            remediationCandidates: [highCandidate],
            earlyReviewCandidates: []);
        var context = CreateContext(setup, position, 2, evidence);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    [Theory]
    [InlineData(2, PracticeSelectionRole.Due)]
    [InlineData(4, PracticeSelectionRole.Maintenance)]
    public void ReviewPool_CannotReturnGuidedIneligibleFact(
        long requestedRoleOrdinal,
        PracticeSelectionRole poisonedPool)
    {
        var (low, high) = CreateNumberSpaceFacts(ArithmeticOperation.Multiplication);
        var setup = CreateSetup(ArithmeticOperation.Multiplication, low, high);
        var position = FindScheduledPosition(ArithmeticOperation.Multiplication);
        var highCandidate = CreateCandidate(
            high,
            duePosition: poisonedPool == PracticeSelectionRole.Due ? 1 : position + 100,
            lastReviewPosition: poisonedPool == PracticeSelectionRole.Maintenance ? position - 40 : 1);
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            position,
            currentBandCandidates: [CreateCandidate(low)],
            dueCandidates: poisonedPool == PracticeSelectionRole.Due ? [highCandidate] : [],
            maintenanceCandidates: poisonedPool == PracticeSelectionRole.Maintenance ? [highCandidate] : [],
            remediationCandidates: [],
            earlyReviewCandidates: []);
        var context = CreateContext(setup, position, requestedRoleOrdinal, evidence);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    [Fact]
    public void FrontierPoolAndCooldownRelaxation_CannotReturnGuidedIneligibleFact()
    {
        var (low, high) = CreateNumberSpaceFacts(ArithmeticOperation.Multiplication);
        var setup = CreateSetup(ArithmeticOperation.Multiplication, low, high);
        var position = FindScheduledPosition(ArithmeticOperation.Multiplication);
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            position,
            currentBandCandidates: [CreateCandidate(high)],
            dueCandidates: [CreateCandidate(low, duePosition: 1)],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);
        var context = CreateContext(setup, position, 5, evidence, recentFacts: [high]);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    [Fact]
    public void EarlyReviewPool_CannotReturnGuidedIneligibleFact()
    {
        var (low, high) = CreateNumberSpaceFacts(ArithmeticOperation.Multiplication);
        var setup = CreateSetup(ArithmeticOperation.Multiplication, low, high);
        var position = FindScheduledPositionWithFirstRanked(
            ArithmeticOperation.Multiplication,
            setup.TargetCurriculum.Bands[0].Id,
            PracticeSelectionRole.EarlyReview,
            high,
            [high, low]);
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Multiplication,
            position,
            currentBandCandidates: [],
            dueCandidates: [],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates:
            [
                CreateCandidate(high, duePosition: position + 100, lastReviewPosition: 1),
                CreateCandidate(low, duePosition: position + 100, lastReviewPosition: 1)
            ]);
        var context = CreateContext(setup, position, 2, evidence);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.EarlyReview, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    [Fact]
    public void OnlyGuidedIneligibleCandidates_FailClosedWithoutLivenessEscape()
    {
        var (_, high) = CreateNumberSpaceFacts(ArithmeticOperation.Division);
        var setup = CreateSetup(ArithmeticOperation.Division, high);
        var position = FindScheduledPosition(ArithmeticOperation.Division);
        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Division,
            position,
            currentBandCandidates: [],
            dueCandidates: [CreateCandidate(high, duePosition: 1)],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: []);
        var context = CreateContext(setup, position, 2, evidence);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AdaptivePracticeSelector().SelectTargetFact(context));

        Assert.Contains("No valid target practice candidate exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnboundedDuePool_FiltersBeforeCandidateWindowTruncation()
    {
        var low = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 2);
        var highFacts = Enumerable.Range(3, 70)
            .Select(left => new ArithmeticFact(ArithmeticOperation.Multiplication, left, 1))
            .ToArray();
        var allFacts = highFacts.Append(low).ToArray();
        var setup = CreateSetup(ArithmeticOperation.Multiplication, allFacts);
        var position = FindScheduledPosition(ArithmeticOperation.Multiplication, minimumPosition: 2);
        var itemStates = allFacts.ToDictionary(
            fact => fact.Id,
            ItemLearningState.CreateNew,
            StringComparer.Ordinal);
        var fsrsStates = allFacts.ToDictionary(
            fact => fact.Id,
            fact => new FsrsCardState(
                fact.Id,
                Guid.Empty,
                1,
                null,
                1,
                1,
                fact.Id == low.Id ? 2 : 1,
                1,
                FsrsRating.Good),
            StringComparer.Ordinal);
        var candidateIndex = new PracticeCandidateIndex(allFacts, itemStates, fsrsStates);
        Assert.True(
            Array.IndexOf(candidateIndex.DueFacts.ToArray(), low)
                >= PracticeSelectionEvidenceRequest.CandidateWindowSize);
        var context = new PracticeSelectionContext(
            position,
            0,
            CreateProgressions(),
            setup.Curricula,
            candidateIndex,
            [],
            2,
            guidedNumberSpaceGate: GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0));

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(low.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.True(context.GuidedNumberSpaceGate.Allows(result.Fact));
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> CreateProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, 0, 0));

    private static IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> CreateCurricula(
        ArithmeticCurriculum curriculum) =>
        new Dictionary<ArithmeticOperation, OperationCurriculum>
        {
            [ArithmeticOperation.Addition] = curriculum.Addition,
            [ArithmeticOperation.Subtraction] = curriculum.Subtraction,
            [ArithmeticOperation.Multiplication] = curriculum.Multiplication,
            [ArithmeticOperation.Division] = curriculum.Division
        };

    private static PracticeSelectionContext CreateContext(
        SelectorSetup setup,
        long position,
        long scheduledOperationAttemptOrdinal,
        PracticeSelectionEvidence evidence,
        IEnumerable<ArithmeticFact>? recentFacts = null) =>
        new(
            position,
            0,
            CreateProgressions(),
            setup.Curricula,
            new PracticeCandidateIndex(evidence),
            recentFacts ?? [],
            scheduledOperationAttemptOrdinal,
            guidedNumberSpaceGate: GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0));

    private static SelectorSetup CreateSetup(
        ArithmeticOperation operation,
        params ArithmeticFact[] frontier)
    {
        var targetCurriculum = new OperationCurriculum(
            operation,
            [
                new CurriculumBand(
                    operation,
                    0,
                    new CurriculumBandId($"TEST-{operation.ToString().ToUpperInvariant()}-D01"),
                    CurriculumBandKind.Dense,
                    frontier)
            ]);
        var canonical = new ArithmeticCurriculum();
        var curricula = CreateCurricula(canonical).ToDictionary(pair => pair.Key, pair => pair.Value);
        curricula[operation] = targetCurriculum;
        return new SelectorSetup(targetCurriculum, curricula);
    }

    private static (ArithmeticFact Low, ArithmeticFact High) CreateNumberSpaceFacts(
        ArithmeticOperation operation) => operation switch
        {
            ArithmeticOperation.Multiplication =>
                (new ArithmeticFact(operation, 1, 2), new ArithmeticFact(operation, 2, 2)),
            ArithmeticOperation.Division =>
                (new ArithmeticFact(operation, 2, 1), new ArithmeticFact(operation, 4, 2)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private static PracticeSelectionCandidate CreateCandidate(
        ArithmeticFact fact,
        long? duePosition = null,
        long? lastReviewPosition = null)
    {
        var item = ItemLearningState.CreateNew(fact);
        var card = duePosition.HasValue || lastReviewPosition.HasValue
            ? new FsrsCardState(
                fact.Id,
                Guid.Empty,
                1,
                null,
                1,
                1,
                duePosition ?? long.MaxValue,
                lastReviewPosition,
                FsrsRating.Good)
            : null;
        return new PracticeSelectionCandidate(fact, item, card);
    }

    private static long FindScheduledPosition(
        ArithmeticOperation operation,
        long minimumPosition = 1)
    {
        for (var position = minimumPosition; position <= 1000; position++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(position) == operation)
            {
                return position;
            }
        }

        throw new InvalidOperationException($"No scheduled position found for {operation}.");
    }

    private static long FindScheduledPositionWithFirstRanked(
        ArithmeticOperation operation,
        CurriculumBandId bandId,
        PracticeSelectionRole role,
        ArithmeticFact expectedFirst,
        IReadOnlyList<ArithmeticFact> pool)
    {
        for (var position = 1L; position <= 10000; position++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(position) != operation)
            {
                continue;
            }

            var ordered = DeterministicFactRanker.Order(pool, operation, bandId, role, position);
            if (ordered[0].Id == expectedFirst.Id)
            {
                return position;
            }
        }

        throw new InvalidOperationException($"No deterministic ranking position found for {expectedFirst.Id}.");
    }

    private sealed record SelectorSetup(
        OperationCurriculum TargetCurriculum,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> Curricula);
}
