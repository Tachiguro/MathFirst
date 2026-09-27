namespace MathFirst.Core.Tests;

using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class EvidenceAdaptiveDiscoveryTests
{
    private static PracticeSelectionContext CreateContext(
        long prospectivePosition,
        ArithmeticCurriculum curriculum,
        IReadOnlyList<ArithmeticFact> materializedFacts,
        IReadOnlyDictionary<string, ItemLearningState>? itemStates = null,
        IReadOnlyDictionary<string, FsrsCardState>? fsrsStates = null,
        long scheduledOperationOrdinal = 1,
        IEnumerable<ArithmeticOperation>? enabledOperations = null,
        bool hasBroadWeakness = false,
        PracticeSelectionEvidence? boundedEvidence = null)
    {
        var expectedOperations = Enum.GetValues<ArithmeticOperation>();
        var progressions = expectedOperations.ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));
        var curricula = expectedOperations.ToDictionary(
            op => op,
            op => curriculum.GetCurriculum(op));

        var resolvedItemStates = itemStates != null
            ? new Dictionary<string, ItemLearningState>(itemStates, StringComparer.Ordinal)
            : materializedFacts.ToDictionary(f => f.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);

        var resolvedFsrsStates = fsrsStates != null
            ? new Dictionary<string, FsrsCardState>(fsrsStates, StringComparer.Ordinal)
            : materializedFacts.ToDictionary(f => f.Id, f => CreateDefaultCard(f, prospectivePosition + 100), StringComparer.Ordinal);

        PracticeCandidateIndex candidateIndex;
        if (boundedEvidence != null)
        {
            candidateIndex = new PracticeCandidateIndex(boundedEvidence);
        }
        else
        {
            candidateIndex = new PracticeCandidateIndex(materializedFacts, resolvedItemStates, resolvedFsrsStates);
        }

        return new PracticeSelectionContext(
            prospectivePosition,
            0,
            progressions,
            curricula,
            candidateIndex,
            [],
            scheduledOperationOrdinal,
            enabledOperations ?? [ArithmeticOperation.Addition],
            guidedNumberSpaceGate: null,
            hasBroadWeakness: hasBroadWeakness);
    }

    private static FsrsCardState CreateDefaultCard(
        ArithmeticFact fact,
        long duePosition,
        long? lastReviewPosition = null) =>
        new(fact.Id, Guid.Empty, 1, null, 1, 1, duePosition, lastReviewPosition, FsrsRating.Good);

    [Fact]
    public void DiscoveryPromotion_EmptyDue_CleanLearner_PromotesToNew()
    {
        // Fixture:
        // Nominal requested role = Due (ordinal 2 -> 1: New, 2: Due).
        // No genuinely due candidate (materialized fact has DuePracticePosition > prospectivePosition).
        // Eligible unmaterialized New material exists.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var materializedFact = frontier[0];
        var unmaterializedFacts = frontier.Skip(1).ToArray();
        Assert.NotEmpty(unmaterializedFacts);

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(materializedFact, duePosition: 200, lastReviewPosition: 90);
        var itemState = ItemLearningState.CreateNew(materializedFact);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [materializedFact],
            itemStates: new Dictionary<string, ItemLearningState> { [materializedFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [materializedFact.Id] = card },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(materializedFact.Id, result.Fact.Id);
        Assert.Contains(result.Fact.Id, unmaterializedFacts.Select(f => f.Id));
    }

    [Fact]
    public void DiscoveryPromotion_EmptyMaintenance_CleanLearner_PromotesToNew()
    {
        // Fixture:
        // Nominal requested role = Maintenance (ordinal 4 -> Maintenance).
        // Materialized fact reviewed recently (stale distance < 40 positions).
        // Eligible unmaterialized New material exists.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var materializedFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(materializedFact, duePosition: 200, lastReviewPosition: 80); // 100 - 80 = 20 < 40
        var itemState = ItemLearningState.CreateNew(materializedFact);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [materializedFact],
            itemStates: new Dictionary<string, ItemLearningState> { [materializedFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [materializedFact.Id] = card },
            scheduledOperationOrdinal: 4, // Maintenance
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Maintenance, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(materializedFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_MasteredFrontier_CleanLearner_PromotesToNew()
    {
        // Fixture:
        // Nominal requested role = Frontier (ordinal 5 -> Frontier).
        // Materialized frontier filler is already provisionally mastered.
        // Eligible unmaterialized material remains in current introduction frontier.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var masteredFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(masteredFact, duePosition: 200, lastReviewPosition: 95);
        var itemState = ItemLearningState.CreateNew(masteredFact);
        itemState.IsProvisionallyMastered = true;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [masteredFact],
            itemStates: new Dictionary<string, ItemLearningState> { [masteredFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [masteredFact.Id] = card },
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(masteredFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_GenuineDueWork_PreservesDue()
    {
        // Fixture:
        // Nominal requested role = Due (ordinal 2).
        // Materialized fact is genuinely due (DuePracticePosition <= prospectivePosition).
        // Unmaterialized material exists.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(dueFact, duePosition: 90, lastReviewPosition: 50); // 90 <= 100 -> Due!
        var itemState = ItemLearningState.CreateNew(dueFact);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [dueFact],
            itemStates: new Dictionary<string, ItemLearningState> { [dueFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [dueFact.Id] = card },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(dueFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_UnmasteredFrontier_PreservesFrontier()
    {
        // Fixture:
        // Nominal requested role = Frontier (ordinal 5).
        // Materialized frontier candidate is unmastered (IsProvisionallyMastered = false).
        // Unmaterialized material exists.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var unmasteredFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(unmasteredFact, duePosition: 200, lastReviewPosition: 95);
        var itemState = ItemLearningState.CreateNew(unmasteredFact);
        itemState.IsProvisionallyMastered = false;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [unmasteredFact],
            itemStates: new Dictionary<string, ItemLearningState> { [unmasteredFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [unmasteredFact.Id] = card },
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(unmasteredFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_StaleMaintenanceWork_PreservesMaintenance()
    {
        // Fixture:
        // Nominal requested role = Maintenance (ordinal 4).
        // Materialized fact is stale (lastReviewPosition <= prospectivePosition - 40, Due > prospectivePosition).
        // Unmaterialized material exists.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var staleFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(staleFact, duePosition: 200, lastReviewPosition: 50); // 100 - 50 = 50 >= 40
        var itemState = ItemLearningState.CreateNew(staleFact);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [staleFact],
            itemStates: new Dictionary<string, ItemLearningState> { [staleFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [staleFact.Id] = card },
            scheduledOperationOrdinal: 4, // Maintenance
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Maintenance, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Maintenance, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(staleFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_BroadWeakness_SuppressesReviewToNew()
    {
        // Fixture:
        // Review role otherwise empty / non-useful (e.g. Due with no due candidates).
        // Eligible unmaterialized New material exists.
        // Legitimate consolidation candidate exists (a non-due mastered materialized fact).
        // HasBroadWeakness = true.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var consolidationFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(consolidationFact, duePosition: 200, lastReviewPosition: 90);
        var itemState = ItemLearningState.CreateNew(consolidationFact);
        itemState.IsProvisionallyMastered = true;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [consolidationFact],
            itemStates: new Dictionary<string, ItemLearningState> { [consolidationFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [consolidationFact.Id] = card },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: true);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.NotEqual(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(consolidationFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_NominalNew_RemainsProtectedDuringBroadWeakness()
    {
        // Fixture:
        // Nominal requested role = New (ordinal 1).
        // Unmaterialized material exists.
        // HasBroadWeakness = true.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var materializedFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(materializedFact, duePosition: 200, lastReviewPosition: 90);
        var itemState = ItemLearningState.CreateNew(materializedFact);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [materializedFact],
            itemStates: new Dictionary<string, ItemLearningState> { [materializedFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [materializedFact.Id] = card },
            scheduledOperationOrdinal: 1, // New
            hasBroadWeakness: true);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.New, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(materializedFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_NoUnmaterializedMaterial_UsesExistingFallback()
    {
        // Fixture:
        // Nominal requested role = Due (ordinal 2).
        // No due work.
        // ALL facts in the band are already materialized (newPool is empty).
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;

        const long prospectivePosition = 100;
        var itemStates = frontier.ToDictionary(f => f.Id, f =>
        {
            var s = ItemLearningState.CreateNew(f);
            s.IsProvisionallyMastered = true;
            return s;
        }, StringComparer.Ordinal);
        var fsrsStates = frontier.ToDictionary(
            f => f.Id,
            f => CreateDefaultCard(f, duePosition: 200, lastReviewPosition: 90),
            StringComparer.Ordinal);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: frontier,
            itemStates: itemStates,
            fsrsStates: fsrsStates,
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.NotEqual(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
    }

    [Fact]
    public void DiscoveryPromotion_NoFixedPercentageBehavior_MultiTurn()
    {
        // Fixture:
        // Over a full 10-attempt cycle, test that empty review slots adaptively promote
        // based on clean evidence without being bound to the historical 4-New/10-slot ratio.
        var facts = Enumerable.Range(0, 15)
            .Select(i => new ArithmeticFact(ArithmeticOperation.Addition, 0, i))
            .ToArray();
        var customBand = new CurriculumBand(
            ArithmeticOperation.Addition,
            0,
            new CurriculumBandId("ADD-TEST"),
            CurriculumBandKind.Dense,
            facts);
        var customAddition = new OperationCurriculum(ArithmeticOperation.Addition, [customBand]);

        var baseCurriculum = new ArithmeticCurriculum();
        var curricula = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => op == ArithmeticOperation.Addition ? customAddition : baseCurriculum.GetCurriculum(op));

        // Start with only 1 materialized fact that is mastered and non-due (no useful review work).
        var initialMaterialized = facts[0];
        var materializedList = new List<ArithmeticFact> { initialMaterialized };
        var itemStates = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [initialMaterialized.Id] = new ItemLearningState
            {
                FactId = initialMaterialized.Id,
                Operation = initialMaterialized.Operation,
                LeftOperand = initialMaterialized.LeftOperand,
                RightOperand = initialMaterialized.RightOperand,
                IsProvisionallyMastered = true
            }
        };
        var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [initialMaterialized.Id] = CreateDefaultCard(initialMaterialized, duePosition: 1000, lastReviewPosition: 500)
        };

        var selector = new AdaptivePracticeSelector();
        var resolvedRoles = new List<PracticeSelectionRole>();

        var expectedOperations = Enum.GetValues<ArithmeticOperation>();
        var progressions = expectedOperations.ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));

        for (var ordinal = 1L; ordinal <= 10L; ordinal++)
        {
            var candidateIndex = new PracticeCandidateIndex(materializedList, itemStates, fsrsStates);
            var context = new PracticeSelectionContext(
                prospectivePracticePosition: 510 + ordinal,
                currentSessionOrder: 0,
                operationProgressions: progressions,
                curricula: curricula,
                candidateIndex: candidateIndex,
                recentAcceptedFactsOldestToNewest: [],
                scheduledOperationAttemptOrdinal: ordinal,
                enabledOperations: [ArithmeticOperation.Addition],
                guidedNumberSpaceGate: null,
                hasBroadWeakness: false);

            var result = selector.SelectTargetFact(context);
            resolvedRoles.Add(result.ResolvedRole);

            if (result.IsNewIntroduction)
            {
                materializedList.Add(result.Fact);
                var newState = ItemLearningState.CreateNew(result.Fact);
                newState.IsProvisionallyMastered = true; // immediately mastered to keep frontier clean
                itemStates[result.Fact.Id] = newState;
                fsrsStates[result.Fact.Id] = CreateDefaultCard(result.Fact, duePosition: 1000, lastReviewPosition: 510 + ordinal);
            }
        }

        // Nominal 10-slot cycle has exactly 4 nominal New slots: ordinals 1, 3, 6, 8.
        // Under Evidence-Adaptive Discovery, since all review slots have zero useful work,
        // clean evidence promotes all review slots to New introductions as long as material remains.
        var newCount = resolvedRoles.Count(r => r == PracticeSelectionRole.New);
        Assert.True(newCount > 4, $"Expected more than 4 New resolutions in 10 turns with empty review work, but got {newCount}.");
        Assert.Equal(10, newCount); // All 10 turns promoted to New!
    }

    [Fact]
    public void DiscoveryPromotion_FrontierWithBothMasteredAndUnmastered_PreservesFrontier()
    {
        // Fixture:
        // Nominal requested role = Frontier (ordinal 5).
        // Frontier contains two materialized facts: one mastered, one unmastered.
        // Since unmastered learning work exists, useful work is present, so Frontier must NOT promote.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var masteredFact = frontier[0];
        var unmasteredFact = frontier[1];

        const long prospectivePosition = 100;
        var masteredCard = CreateDefaultCard(masteredFact, duePosition: 200, lastReviewPosition: 90);
        var unmasteredCard = CreateDefaultCard(unmasteredFact, duePosition: 200, lastReviewPosition: 90);

        var masteredState = ItemLearningState.CreateNew(masteredFact);
        masteredState.IsProvisionallyMastered = true;

        var unmasteredState = ItemLearningState.CreateNew(unmasteredFact);
        unmasteredState.IsProvisionallyMastered = false;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [masteredFact, unmasteredFact],
            itemStates: new Dictionary<string, ItemLearningState>
            {
                [masteredFact.Id] = masteredState,
                [unmasteredFact.Id] = unmasteredState
            },
            fsrsStates: new Dictionary<string, FsrsCardState>
            {
                [masteredFact.Id] = masteredCard,
                [unmasteredFact.Id] = unmasteredCard
            },
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Contains(result.Fact.Id, new[] { masteredFact.Id, unmasteredFact.Id });
    }

    [Fact]
    public void DiscoveryPromotion_RemediationTakesPrecedenceOverPromotion()
    {
        // Fixture:
        // Nominal requested role = Due (ordinal 2).
        // Zero useful Due work (Due card is far in future).
        // Unmaterialized material exists in band 0.
        // A materialized fact needs remediation and its spacing cooldown has elapsed.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var remediationFact = frontier[0];

        const long prospectivePosition = 100;
        var remCard = CreateDefaultCard(remediationFact, duePosition: 200, lastReviewPosition: 90); // 100 >= 90 + 4
        var remState = ItemLearningState.CreateNew(remediationFact);
        remState.NeedsRemediation = true;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [remediationFact],
            itemStates: new Dictionary<string, ItemLearningState> { [remediationFact.Id] = remState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [remediationFact.Id] = remCard },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(remediationFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_BoundedEvidence_EmptyDue_CleanLearner_PromotesToNew()
    {
        // Fixture using PracticeSelectionEvidence with HasBoundedSemanticPools = true
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var materializedFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(materializedFact, duePosition: 200, lastReviewPosition: 90);
        var itemState = ItemLearningState.CreateNew(materializedFact);
        var cand = new PracticeSelectionCandidate(materializedFact, itemState, card);

        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePosition,
            currentBandCandidates: [cand],
            dueCandidates: [], // Empty due candidates!
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: [cand]);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [materializedFact],
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false,
            boundedEvidence: evidence);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(materializedFact.Id, result.Fact.Id);
    }

    [Fact]
    public void DiscoveryPromotion_BoundedEvidence_MasteredFrontier_CleanLearner_PromotesToNew()
    {
        // Fixture using PracticeSelectionEvidence where currentBandCandidates are all mastered
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var masteredFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(masteredFact, duePosition: 200, lastReviewPosition: 90);
        var itemState = ItemLearningState.CreateNew(masteredFact);
        itemState.IsProvisionallyMastered = true;
        var cand = new PracticeSelectionCandidate(masteredFact, itemState, card);

        var evidence = new PracticeSelectionEvidence(
            ArithmeticOperation.Addition,
            prospectivePosition,
            currentBandCandidates: [cand],
            dueCandidates: [],
            maintenanceCandidates: [],
            remediationCandidates: [],
            earlyReviewCandidates: [cand]);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [masteredFact],
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false,
            boundedEvidence: evidence);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(masteredFact.Id, result.Fact.Id);
    }
}
