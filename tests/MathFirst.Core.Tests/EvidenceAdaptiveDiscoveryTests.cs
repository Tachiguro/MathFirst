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
        PracticeSelectionEvidence? boundedEvidence = null,
        IEnumerable<ArithmeticFact>? recentAcceptedFacts = null)
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
            recentAcceptedFacts ?? [],
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
        // Option-B reconciliation: Under Option B, a Due candidate whose latest outcome is Correct
        // and clean does not block New. To genuinely preserve the Due role during active band acquisition,
        // the Due candidate must have actionable repair evidence (non-Correct latest outcome).
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 50,
            LastRating: FsrsRating.Again);
        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 0;

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
        // Option-B reconciliation: Under Option B, provisional unmastery alone (TotalAttempts < 3)
        // does not block New if latest outcome was Correct. To preserve Frontier during active acquisition,
        // the candidate must have actionable repair evidence (non-Correct latest outcome).
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var unmasteredFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            unmasteredFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 200,
            LastReviewPracticePosition: 95,
            LastRating: FsrsRating.Again);
        var itemState = ItemLearningState.CreateNew(unmasteredFact);
        itemState.TotalAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 0;
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
        // Option-B reconciliation: Under Option B, a stale Maintenance candidate does not block New
        // while unmaterialized material exists for a clean learner. When unmaterialized material is
        // exhausted (newPool empty) or broad weakness exists, Maintenance role preserves Maintenance.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var staleFact = frontier[0];

        const long prospectivePosition = 100;
        var card = CreateDefaultCard(staleFact, duePosition: 200, lastReviewPosition: 50); // 100 - 50 = 50 >= 40
        var itemState = ItemLearningState.CreateNew(staleFact);

        // All frontier facts materialized so newPool is empty
        var allMaterialized = frontier;
        var itemStates = frontier.ToDictionary(f => f.Id, f => ItemLearningState.CreateNew(f), StringComparer.Ordinal);
        var fsrsStates = frontier.ToDictionary(
            f => f.Id,
            f => f.Id == staleFact.Id ? card : CreateDefaultCard(f, duePosition: 200, lastReviewPosition: 90),
            StringComparer.Ordinal);

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: allMaterialized,
            itemStates: itemStates,
            fsrsStates: fsrsStates,
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
        // Option-B reconciliation: Under Option B, a frontier candidate does not block New merely
        // because IsProvisionallyMastered == false if its latest outcome was Correct.
        // When unmastered frontier work has actionable repair evidence (non-Correct latest outcome),
        // Frontier role is preserved and not promoted to New.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var masteredFact = frontier[0];
        var unmasteredFact = frontier[1];

        const long prospectivePosition = 100;
        var masteredCard = CreateDefaultCard(masteredFact, duePosition: 200, lastReviewPosition: 90);
        var unmasteredCard = new FsrsCardState(
            unmasteredFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 200,
            LastReviewPracticePosition: 90,
            LastRating: FsrsRating.Again);

        var masteredState = ItemLearningState.CreateNew(masteredFact);
        masteredState.TotalAttempts = 3;
        masteredState.CorrectAttempts = 3;
        masteredState.ConsecutiveCorrectStreak = 3;
        masteredState.IsProvisionallyMastered = true;

        var unmasteredState = ItemLearningState.CreateNew(unmasteredFact);
        unmasteredState.TotalAttempts = 1;
        unmasteredState.IncorrectAttempts = 1;
        unmasteredState.ConsecutiveCorrectStreak = 0;
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

    [Fact]
    public void EvidenceAdaptiveDiscovery_DueCorrectCleanCandidate_DoesNotBlockNewPromotion()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // Candidate is Due (DuePracticePosition <= prospectivePosition).
        // Candidate latest outcome is Correct (TotalAttempts = 1, ConsecutiveCorrectStreak = 1, LastRating = Good).
        // Candidate NeedsRemediation == false.
        // Candidate is not immediate predecessor.
        // Eligible unmaterialized newPool is non-empty.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];
        var unmaterializedFacts = frontier.Skip(1).ToArray();
        Assert.NotEmpty(unmaterializedFacts);

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 50,
            LastRating: FsrsRating.Good);

        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.NeedsRemediation = false;

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
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.Contains(result.Fact.Id, unmaterializedFacts.Select(f => f.Id));
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_FrontierCorrectUnmasteredCandidate_DoesNotBlockNewPromotion()
    {
        // Fixture:
        // Requested role = Frontier (ordinal 5).
        // Materialized frontier candidate: IsProvisionallyMastered == false.
        // Authoritative latest outcome Correct (TotalAttempts = 1, ConsecutiveCorrectStreak = 1, LastRating = Good).
        // NeedsRemediation == false.
        // Eligible newPool non-empty.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var frontierFact = frontier[0];
        var unmaterializedFacts = frontier.Skip(1).ToArray();
        Assert.NotEmpty(unmaterializedFacts);

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            frontierFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 200,
            LastReviewPracticePosition: 95,
            LastRating: FsrsRating.Good);

        var itemState = ItemLearningState.CreateNew(frontierFact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.IsProvisionallyMastered = false;
        itemState.NeedsRemediation = false;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [frontierFact],
            itemStates: new Dictionary<string, ItemLearningState> { [frontierFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [frontierFact.Id] = card },
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.Contains(result.Fact.Id, unmaterializedFacts.Select(f => f.Id));
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_MaintenanceCorrectCleanCandidate_DoesNotBlockNewPromotion()
    {
        // Fixture:
        // Requested role = Maintenance (ordinal 4).
        // Candidate is stale (prospectivePosition - lastReviewPosition >= 40, Due > prospectivePosition).
        // Authoritative latest outcome Correct (TotalAttempts = 1, ConsecutiveCorrectStreak = 1, LastRating = Good).
        // NeedsRemediation == false.
        // Eligible newPool non-empty.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var staleFact = frontier[0];
        var unmaterializedFacts = frontier.Skip(1).ToArray();
        Assert.NotEmpty(unmaterializedFacts);

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            staleFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 200,
            LastReviewPracticePosition: 50, // 100 - 50 = 50 >= 40
            LastRating: FsrsRating.Good);

        var itemState = ItemLearningState.CreateNew(staleFact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.NeedsRemediation = false;

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
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.Contains(result.Fact.Id, unmaterializedFacts.Select(f => f.Id));
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_DueIncorrectCandidate_BlocksNewPromotion()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // Due candidate exists (DuePracticePosition <= prospectivePosition).
        // Latest outcome is non-Correct (LastRating = Again, streak = 0).
        // newPool non-empty.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 50,
            LastRating: FsrsRating.Again);

        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.IncorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 0;
        itemState.NeedsRemediation = false;

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
    public void EvidenceAdaptiveDiscovery_FrontierNonCorrectCandidate_BlocksNewPromotion()
    {
        // Fixture:
        // Requested role = Frontier (ordinal 5).
        // Materialized frontier candidate with non-Correct latest outcome.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var frontierFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            frontierFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 200,
            LastReviewPracticePosition: 95,
            LastRating: FsrsRating.Again);

        var itemState = ItemLearningState.CreateNew(frontierFact);
        itemState.TotalAttempts = 1;
        itemState.IncorrectAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 0;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [frontierFact],
            itemStates: new Dictionary<string, ItemLearningState> { [frontierFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [frontierFact.Id] = card },
            scheduledOperationOrdinal: 5, // Frontier
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(frontierFact.Id, result.Fact.Id);
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_RemediationCandidate_BlocksNewPromotion()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // Due candidate exists with NeedsRemediation == true.
        // LastReview was recent so remediation override spacing hasn't elapsed,
        // but within the Due pool, NeedsRemediation == true acts as acquisition-blocking work.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 98, // Spacing: 100 - 98 = 2 < 4 (isolated spacing not elapsed)
            LastRating: FsrsRating.Again);

        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.NeedsRemediation = true;

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
    public void EvidenceAdaptiveDiscovery_BroadWeakness_SuppressesCleanReviewPromotion()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // Due candidate is clean and Correct.
        // Eligible newPool non-empty.
        // HasBroadWeakness = true.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 50,
            LastRating: FsrsRating.Good);

        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 1;
        itemState.NeedsRemediation = false;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [dueFact],
            itemStates: new Dictionary<string, ItemLearningState> { [dueFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [dueFact.Id] = card },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: true);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.NotEqual(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
        Assert.Equal(dueFact.Id, result.Fact.Id);
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_NoUnmaterializedMaterial_PreservesRequestedReview()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // All facts in band materialized (newPool empty).
        // Due candidate is clean and Correct.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;

        const long prospectivePosition = 100;
        var itemStates = frontier.ToDictionary(f => f.Id, f =>
        {
            var s = ItemLearningState.CreateNew(f);
            s.TotalAttempts = 1;
            s.ConsecutiveCorrectStreak = 1;
            return s;
        }, StringComparer.Ordinal);
        var fsrsStates = frontier.ToDictionary(
            f => f.Id,
            f => new FsrsCardState(f.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, DuePracticePosition: 90, LastReviewPracticePosition: 50, LastRating: FsrsRating.Good),
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
        Assert.Equal(PracticeSelectionRole.Due, result.ResolvedRole);
        Assert.False(result.IsNewIntroduction);
        Assert.True(result.IsMaterialized);
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_NominalNew_PreservedWithUnmaterializedMaterial()
    {
        // Fixture:
        // Requested role = New (ordinal 1).
        // newPool non-empty.
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
            hasBroadWeakness: false);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.New, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
    }

    [Fact]
    public void EvidenceAdaptiveDiscovery_ImmediatePredecessorOnlyReview_DoesNotBlockNewPromotion()
    {
        // Fixture:
        // Requested role = Due (ordinal 2).
        // The ONLY due candidate is the immediate predecessor (even with repair evidence).
        // Since immediate predecessor cannot legally be selected under No-Immediate-Fact-Repetition invariant,
        // it must not count as actionable acquisition-blocking work.
        // Eligible newPool non-empty.
        // HasBroadWeakness = false.
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var dueFact = frontier[0];
        var unmaterializedFacts = frontier.Skip(1).ToArray();
        Assert.NotEmpty(unmaterializedFacts);

        const long prospectivePosition = 100;
        var card = new FsrsCardState(
            dueFact.Id,
            Guid.NewGuid(),
            State: 1,
            Step: null,
            Stability: 1.0,
            Difficulty: 1.0,
            DuePracticePosition: 90,
            LastReviewPracticePosition: 99,
            LastRating: FsrsRating.Again);

        var itemState = ItemLearningState.CreateNew(dueFact);
        itemState.TotalAttempts = 1;
        itemState.ConsecutiveCorrectStreak = 0;
        itemState.NeedsRemediation = true;

        var context = CreateContext(
            prospectivePosition: prospectivePosition,
            curriculum: curriculum,
            materializedFacts: [dueFact],
            itemStates: new Dictionary<string, ItemLearningState> { [dueFact.Id] = itemState },
            fsrsStates: new Dictionary<string, FsrsCardState> { [dueFact.Id] = card },
            scheduledOperationOrdinal: 2, // Due
            hasBroadWeakness: false,
            recentAcceptedFacts: [dueFact]);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.True(result.IsNewIntroduction);
        Assert.False(result.IsMaterialized);
        Assert.NotEqual(dueFact.Id, result.Fact.Id);
        Assert.Contains(result.Fact.Id, unmaterializedFacts.Select(f => f.Id));
    }
}
