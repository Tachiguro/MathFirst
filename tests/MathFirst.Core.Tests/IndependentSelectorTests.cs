namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class IndependentSelectorTests
{
    [Fact]
    public void ProspectivePosition_DeterminesIndependentOperationAndRoleSchedule()
    {
        var expectedOperations = new[]
        {
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Division,
            ArithmeticOperation.Addition
        };

        Assert.Equal(
            expectedOperations,
            Enumerable.Range(1, 5).Select(position => AdaptivePracticeSelector.GetScheduledOperation(position)));
        Assert.Equal(2, AdaptivePracticeSelector.GetOperationAttemptOrdinal(5));
        Assert.Equal(PracticeSelectionRole.Due, AdaptivePracticeSelector.GetRequestedRole(5));

        for (var position = 1; position <= 160; position++)
        {
            var expectedOperation = expectedOperations[(position - 1) % 4];
            var expectedOrdinal = ((position - 1) / 4) + 1;
            Assert.Equal(expectedOperation, AdaptivePracticeSelector.GetScheduledOperation(position));
            Assert.Equal(expectedOrdinal, AdaptivePracticeSelector.GetOperationAttemptOrdinal(position));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePracticeSelector.GetScheduledOperation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePracticeSelector.GetRequestedRole(0));
    }

    [Fact]
    public void EachOperation_RepeatsTheExactTenAttemptRoleCycle()
    {
        var expected = new[]
        {
            PracticeSelectionRole.New,
            PracticeSelectionRole.Due,
            PracticeSelectionRole.New,
            PracticeSelectionRole.Maintenance,
            PracticeSelectionRole.Frontier,
            PracticeSelectionRole.New,
            PracticeSelectionRole.Due,
            PracticeSelectionRole.New,
            PracticeSelectionRole.Due,
            PracticeSelectionRole.Frontier
        };

        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            var offset = (int)operation - 1;
            var actual = Enumerable.Range(0, 20)
                .Select(index => AdaptivePracticeSelector.GetRequestedRole((index * 4L) + offset + 1))
                .ToArray();
            Assert.Equal(expected.Concat(expected), actual);
            Assert.Equal(4, actual.Take(10).Count(role => role == PracticeSelectionRole.New));
            Assert.Equal(3, actual.Take(10).Count(role => role == PracticeSelectionRole.Due));
            Assert.Equal(1, actual.Take(10).Count(role => role == PracticeSelectionRole.Maintenance));
            Assert.Equal(2, actual.Take(10).Count(role => role == PracticeSelectionRole.Frontier));
        }
    }

    [Fact]
    public void EmptyDuePool_FallsBackToMaterializedFrontierWithoutIntroducing()
    {
        var curriculum = new ArithmeticCurriculum();
        var frontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var context = CreateContext(5, curriculum, Materialize(frontier));

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);
        var expected = DeterministicFactRanker.Order(
            frontier,
            ArithmeticOperation.Addition,
            curriculum.Addition.Bands[0].Id,
            FactSelectionRole.Frontier,
            5)[0];

        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.Equal(expected.Id, result.Fact.Id);
        Assert.True(result.IsMaterialized);
        Assert.False(result.IsNewIntroduction);
    }

    [Fact]
    public void DueSameSessionRemediation_OverridesNormalRoleDeterministically()
    {
        var curriculum = new ArithmeticCurriculum();
        var remediationFact = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0)[0];
        var state = ItemLearningState.CreateNew(remediationFact);
        state.NeedsRemediation = true;
        state.RemediationDueOrder = 7;
        var materialized = new MaterializedState(
            new[] { remediationFact },
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [remediationFact.Id] = state },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal));
        var context = CreateContext(1, curriculum, materialized, currentSessionOrder: 7);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.Equal(remediationFact.Id, result.Fact.Id);
        Assert.True(result.IsMaterialized);
    }

    [Theory]
    [InlineData(1, "new", PracticeSelectionRole.New)]
    [InlineData(1, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(1, "due", PracticeSelectionRole.Due)]
    [InlineData(1, "maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(1, "any", PracticeSelectionRole.AnyMaterialized)]
    [InlineData(5, "due", PracticeSelectionRole.Due)]
    [InlineData(5, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(5, "maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(5, "any", PracticeSelectionRole.AnyMaterialized)]
    [InlineData(13, "maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(13, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(13, "due", PracticeSelectionRole.Due)]
    [InlineData(13, "any", PracticeSelectionRole.AnyMaterialized)]
    [InlineData(17, "frontier", PracticeSelectionRole.Frontier)]
    [InlineData(17, "due", PracticeSelectionRole.Due)]
    [InlineData(17, "maintenance", PracticeSelectionRole.Maintenance)]
    [InlineData(17, "any", PracticeSelectionRole.AnyMaterialized)]
    public void EveryFallbackTransition_UsesTheFirstNonEmptyPoolWithoutSwitchingOperation(
        long position,
        string availablePool,
        PracticeSelectionRole expectedResolvedRole)
    {
        var context = CreateFallbackContext(position, availablePool);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(expectedResolvedRole, result.ResolvedRole);
        Assert.Equal(expectedResolvedRole != PracticeSelectionRole.New, result.IsMaterialized);
        Assert.Equal(expectedResolvedRole == PracticeSelectionRole.New, result.IsNewIntroduction);
    }

    [Fact]
    public void DenseAndStructuredNew_IntroduceOnlyTheirRequiredUnmaterializedFrontiers()
    {
        var curriculum = new ArithmeticCurriculum();
        var empty = EmptyMaterialized();
        var dense = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(1, curriculum, empty));

        var structuredProgressions = CreateProgressions((ArithmeticOperation.Addition, 10));
        var structuredContext = CreateContext(
            1,
            curriculum,
            empty,
            operationProgressions: structuredProgressions);
        var structured = new AdaptivePracticeSelector().SelectTargetFact(structuredContext);
        var owned = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(10);
        var band = GetBand(curriculum.Addition, 10);
        var sample = DeterministicFactRanker.SelectStructuredSample(owned, ArithmeticOperation.Addition, band.Id);

        Assert.Equal(PracticeSelectionRole.New, dense.ResolvedRole);
        Assert.False(dense.IsMaterialized);
        Assert.True(dense.IsNewIntroduction);
        Assert.Contains(structured.Fact.Id, sample.Select(fact => fact.Id));
        Assert.True(owned.Count > sample.Count);

        var exhaustedContext = CreateContext(
            1,
            curriculum,
            Materialize(sample),
            operationProgressions: structuredProgressions);
        var exhausted = new AdaptivePracticeSelector().SelectTargetFact(exhaustedContext);
        Assert.Equal(PracticeSelectionRole.Frontier, exhausted.ResolvedRole);
        Assert.True(exhausted.IsMaterialized);
        Assert.False(exhausted.IsNewIntroduction);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 10, 1)]
    [InlineData(ArithmeticOperation.Multiplication, 13, 19)]
    public void Frontier_UsesAcquisitionOwnershipAndRejectsDenseOwnedStructuredOverlap(
        ArithmeticOperation operation,
        int bandIndex,
        long position)
    {
        var curriculum = new ArithmeticCurriculum();
        var operationCurriculum = curriculum.GetCurriculum(operation);
        var band = GetBand(operationCurriculum, bandIndex);
        var resolver = new AcquisitionOwnershipResolver(operationCurriculum);
        var owned = resolver.GetOwnedFrontier(bandIndex);
        var earlierOwned = band.Frontier.First(fact => !owned.Any(item => item.Id == fact.Id));
        var currentOwned = owned[0];
        var progressions = CreateProgressions((operation, bandIndex));
        var rolePosition = operation == ArithmeticOperation.Addition ? 17 : position;
        var context = CreateContext(
            rolePosition,
            curriculum,
            Materialize(new[] { earlierOwned, currentOwned }),
            operationProgressions: progressions);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
        Assert.Equal(currentOwned.Id, result.Fact.Id);
        Assert.NotEqual(earlierOwned.Id, result.Fact.Id);
    }

    [Fact]
    public void PresentationDirections_RemainIndependentMaterializationCandidates()
    {
        var forward = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var reverse = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var custom = new OperationCurriculum(
            ArithmeticOperation.Addition,
            new[] { new CurriculumBand(ArithmeticOperation.Addition, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, new[] { forward, reverse }) });
        var curriculum = new ArithmeticCurriculum();
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var context = CreateContext(1, curriculum, Materialize(new[] { forward }), curricula: curricula);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(reverse.Id, result.Fact.Id);
        Assert.True(result.IsNewIntroduction);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(17)]
    public void DueRemediation_OverridesEveryRequestedRoleWithoutChangingOperation(long position)
    {
        var curriculum = new ArithmeticCurriculum();
        var remediationFact = curriculum.Addition.Bands[0].Frontier[0];
        var materialized = Materialize(new[] { remediationFact }, state =>
        {
            state.NeedsRemediation = true;
            state.RemediationDueOrder = 4;
        });
        var context = CreateContext(position, curriculum, materialized, currentSessionOrder: 4);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.Equal(remediationFact.Id, result.Fact.Id);
        Assert.True(result.IsMaterialized);
        Assert.False(result.IsNewIntroduction);
    }

    [Fact]
    public void RemediationForAnotherOperation_NeverConsumesTheScheduledSlot()
    {
        var curriculum = new ArithmeticCurriculum();
        var addition = curriculum.Addition.Bands[0].Frontier[0];
        var materialized = Materialize(new[] { addition }, state =>
        {
            state.NeedsRemediation = true;
            state.RemediationDueOrder = 1;
        });

        var result = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(2, curriculum, materialized, currentSessionOrder: 5));

        Assert.Equal(ArithmeticOperation.Subtraction, result.ScheduledOperation);
        Assert.NotEqual(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.Equal(ArithmeticOperation.Subtraction, result.Fact.Operation);
    }

    [Fact]
    public void Remediation_UsesEarliestDueOrderThenDeterministicDueRanking()
    {
        var curriculum = new ArithmeticCurriculum();
        var facts = curriculum.Addition.Bands[0].Frontier.Take(3).ToArray();
        var materialized = Materialize(facts, state =>
        {
            state.NeedsRemediation = true;
            state.RemediationDueOrder = state.FactId == facts[2].Id ? 6 : 5;
        });
        var expected = DeterministicFactRanker.Order(
            facts.Take(2),
            ArithmeticOperation.Addition,
            curriculum.Addition.Bands[0].Id,
            FactSelectionRole.Due,
            1)[0];

        var first = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(1, curriculum, materialized, currentSessionOrder: 7));
        var reversed = ReverseMaterialized(materialized);
        var second = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(1, new ArithmeticCurriculum(), reversed, currentSessionOrder: 7));

        Assert.Equal(expected.Id, first.Fact.Id);
        Assert.Equal(first.Fact.Id, second.Fact.Id);
        Assert.NotEqual(facts[2].Id, first.Fact.Id);
    }

    [Fact]
    public void Maintenance_ExcludesDueFactsAndUsesOnlyMaterializedCandidates()
    {
        var context = CreateEmptyOwnedContext(13, "mixed");

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Maintenance, result.ResolvedRole);
        Assert.Equal("add:0+0", result.Fact.Id);
        Assert.True(result.IsMaterialized);
    }

    [Fact]
    public void ExactCooldownUsesLatestThreeAndHistoryOrderIsOldestToNewest()
    {
        var curriculum = new ArithmeticCurriculum();
        var frontier = curriculum.Addition.Bands[0].Frontier;
        var rankedFirst = DeterministicFactRanker.Order(
            frontier,
            ArithmeticOperation.Addition,
            curriculum.Addition.Bands[0].Id,
            FactSelectionRole.Frontier,
            17)[0];
        var otherHistory = new ArithmeticFact[]
        {
            new(ArithmeticOperation.Subtraction, 0, 0),
            new(ArithmeticOperation.Multiplication, 0, 0),
            new(ArithmeticOperation.Division, 0, 1)
        };

        var outside = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(
            17,
            curriculum,
            Materialize(frontier),
            recentFacts: new[] { rankedFirst }.Concat(otherHistory)));
        var inside = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(
            17,
            curriculum,
            Materialize(frontier),
            recentFacts: otherHistory.Concat(new[] { rankedFirst })));

        Assert.Equal(rankedFirst.Id, outside.Fact.Id);
        Assert.NotEqual(rankedFirst.Id, inside.Fact.Id);
    }

    [Fact]
    public void CooldownRelaxation_IsMirrorThenExactAndNeverChangesSemanticPool()
    {
        var forward = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var reverse = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var mirrorContext = CreateSingleBandContext(17, new[] { forward, reverse }, new[] { forward });
        var mirror = new AdaptivePracticeSelector().SelectTargetFact(mirrorContext);

        var exactContext = CreateSingleBandContext(17, new[] { forward }, new[] { forward });
        var exact = new AdaptivePracticeSelector().SelectTargetFact(exactContext);

        var curriculum = new ArithmeticCurriculum();
        var dueFact = curriculum.Addition.Bands[0].Frontier[0];
        var dueMaterialized = Materialize(curriculum.Addition.Bands[0].Frontier, fsrsDuePosition: 1, dueOnlyFactId: dueFact.Id);
        var due = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(
            5,
            curriculum,
            dueMaterialized,
            recentFacts: new[] { dueFact }));

        Assert.Equal(reverse.Id, mirror.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Mirror, mirror.CooldownRelaxation);
        Assert.Equal(forward.Id, exact.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, exact.CooldownRelaxation);
        Assert.Equal(PracticeSelectionRole.Due, due.ResolvedRole);
        Assert.Equal(dueFact.Id, due.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, due.CooldownRelaxation);
    }

    [Fact]
    public void MirrorCooldown_AppliesOnlyToAdditionAndMultiplication()
    {
        var addForward = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var addReverse = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
        var mulForward = new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 1);
        var mulReverse = new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 0);
        var subtraction = new ArithmeticFact(ArithmeticOperation.Subtraction, 1, 0);
        var division = new ArithmeticFact(ArithmeticOperation.Division, 1, 1);

        Assert.Equal(
            AdaptivePracticeSelector.GetCanonicalMirrorKey(addForward),
            AdaptivePracticeSelector.GetCanonicalMirrorKey(addReverse));
        Assert.Equal(
            AdaptivePracticeSelector.GetCanonicalMirrorKey(mulForward),
            AdaptivePracticeSelector.GetCanonicalMirrorKey(mulReverse));
        Assert.Equal(subtraction.Id, AdaptivePracticeSelector.GetCanonicalMirrorKey(subtraction));
        Assert.Equal(division.Id, AdaptivePracticeSelector.GetCanonicalMirrorKey(division));
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 17)]
    [InlineData(ArithmeticOperation.Multiplication, 19)]
    public void TargetMirrorCooldown_ExcludesBothRecentFactAndItsMirrorWhenAnAlternativeExists(
        ArithmeticOperation operation,
        long position)
    {
        var forward = new ArithmeticFact(operation, 0, 1);
        var reverse = new ArithmeticFact(operation, 1, 0);
        var alternative = new ArithmeticFact(operation, 1, 1);
        var context = CreateSingleBandContext(
            position,
            new[] { forward, reverse, alternative },
            new[] { forward });

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(alternative.Id, result.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.None, result.CooldownRelaxation);
    }

    [Fact]
    public void EquivalentStateAndReversedSources_ProduceTheSameTargetSelection()
    {
        var firstCurriculum = new ArithmeticCurriculum();
        var facts = firstCurriculum.Addition.Bands[0].Frontier;
        var first = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(17, firstCurriculum, Materialize(facts)));
        var secondCurriculum = new ArithmeticCurriculum();
        var second = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(17, secondCurriculum, ReverseMaterialized(Materialize(facts))));

        Assert.Equal(first.ScheduledOperation, second.ScheduledOperation);
        Assert.Equal(first.RequestedRole, second.RequestedRole);
        Assert.Equal(first.ResolvedRole, second.ResolvedRole);
        Assert.Equal(first.Fact.Id, second.Fact.Id);
    }

    [Fact]
    public void Selector_IsTotalForFreshOngoingAndMigratedLikeState_AndFailsClosedForImpossibleState()
    {
        var curriculum = new ArithmeticCurriculum();
        var empty = EmptyMaterialized();
        for (var position = 1; position <= 4; position++)
        {
            var fresh = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(position, curriculum, empty));
            Assert.Equal(PracticeSelectionRole.New, fresh.ResolvedRole);
            Assert.True(fresh.IsNewIntroduction);
        }

        var ongoing = new AdaptivePracticeSelector().SelectTargetFact(
            CreateContext(5, curriculum, Materialize(curriculum.Addition.Bands[0].Frontier)));
        Assert.Equal(PracticeSelectionRole.Frontier, ongoing.ResolvedRole);

        var owned = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(10);
        var migrated = new AdaptivePracticeSelector().SelectTargetFact(CreateContext(
            5,
            curriculum,
            Materialize(new[] { owned[0], new ArithmeticFact(ArithmeticOperation.Addition, 10, 10) }),
            operationProgressions: CreateProgressions((ArithmeticOperation.Addition, 10))));
        Assert.Equal(PracticeSelectionRole.Frontier, migrated.ResolvedRole);

        var impossible = CreateEmptyOwnedContext(1, "none");
        var firstError = Assert.Throws<InvalidOperationException>(() => new AdaptivePracticeSelector().SelectTargetFact(impossible));
        var secondError = Assert.Throws<InvalidOperationException>(() => new AdaptivePracticeSelector().SelectTargetFact(impossible));
        Assert.Equal(firstError.Message, secondError.Message);
        Assert.Contains("Addition", firstError.Message);
    }

    [Fact]
    public void EveryOperationRetainsEveryFourthSlotDespiteRemediationAndFallbacks()
    {
        var curriculum = new ArithmeticCurriculum();
        var allInitialFacts = Enum.GetValues<ArithmeticOperation>()
            .SelectMany(operation => curriculum.GetCurriculum(operation).Bands[0].Frontier)
            .ToArray();
        var remediationId = curriculum.Addition.Bands[0].Frontier[0].Id;
        var materialized = Materialize(allInitialFacts, state =>
        {
            if (state.FactId == remediationId)
            {
                state.NeedsRemediation = true;
                state.RemediationDueOrder = 1;
            }
        });
        var counts = Enum.GetValues<ArithmeticOperation>().ToDictionary(operation => operation, _ => 0);

        for (var position = 1; position <= 80; position++)
        {
            var result = new AdaptivePracticeSelector().SelectTargetFact(
                CreateContext(position, curriculum, materialized, currentSessionOrder: 100));
            var expected = (ArithmeticOperation)(((position - 1) % 4) + 1);
            Assert.Equal(expected, result.ScheduledOperation);
            Assert.Equal(expected, result.Fact.Operation);
            if (expected != ArithmeticOperation.Addition)
            {
                Assert.NotEqual(PracticeSelectionRole.Remediation, result.ResolvedRole);
            }
            counts[expected]++;
        }

        Assert.All(counts.Values, count => Assert.Equal(20, count));
    }

    private static PracticeSelectionContext CreateContext(
        long position,
        ArithmeticCurriculum curriculum,
        MaterializedState materialized,
        int currentSessionOrder = 0,
        IEnumerable<ArithmeticFact>? recentFacts = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum>? curricula = null) => new(
        position,
        currentSessionOrder,
        operationProgressions ?? CreateProgressions(),
        curricula ?? CreateCurricula(curriculum),
        new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
        recentFacts ?? Array.Empty<ArithmeticFact>());

    private static MaterializedState Materialize(
        IEnumerable<ArithmeticFact> facts,
        Action<ItemLearningState>? configureState = null,
        long? fsrsDuePosition = null,
        string? dueOnlyFactId = null)
    {
        var factArray = facts.ToArray();
        var states = factArray.ToDictionary(fact => fact.Id, ItemLearningState.CreateNew, StringComparer.Ordinal);
        foreach (var state in states.Values)
        {
            configureState?.Invoke(state);
        }
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        if (fsrsDuePosition.HasValue)
        {
            foreach (var fact in factArray.Where(fact => dueOnlyFactId is null || fact.Id == dueOnlyFactId))
            {
                cards[fact.Id] = CreateCard(fact, fsrsDuePosition.Value);
            }
        }
        return new MaterializedState(
            factArray,
            states,
            cards);
    }

    private static PracticeSelectionContext CreateFallbackContext(long position, string availablePool) =>
        availablePool switch
        {
            "new" => CreateContext(position, new ArithmeticCurriculum(), EmptyMaterialized()),
            "frontier" => CreateFrontierFallbackContext(position),
            "due" => CreateEmptyOwnedContext(position, "due"),
            "maintenance" => CreateEmptyOwnedContext(position, "maintenance"),
            "any" => CreateEmptyOwnedContext(position, "any"),
            _ => throw new ArgumentOutOfRangeException(nameof(availablePool), availablePool, null)
        };

    private static PracticeSelectionContext CreateFrontierFallbackContext(long position)
    {
        var curriculum = new ArithmeticCurriculum();
        var frontierOnly = Materialize(curriculum.Addition.Bands[0].Frontier, state =>
        {
            state.NeedsRemediation = true;
            state.RemediationDueOrder = 100;
        });
        return CreateContext(position, curriculum, frontierOnly);
    }

    private static PracticeSelectionContext CreateEmptyOwnedContext(long position, string stateKind)
    {
        var curriculum = new ArithmeticCurriculum();
        var earlier = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var custom = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, earlier);
        var curricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, custom));
        var progressions = CreateProgressions((ArithmeticOperation.Addition, 1));
        if (stateKind == "none")
        {
            return CreateContext(position, curriculum, EmptyMaterialized(), operationProgressions: progressions, curricula: curricula);
        }

        if (stateKind == "mixed")
        {
            var maintenance = earlier;
            var due = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
            var customMixed = CreateRepeatedFactCurriculum(ArithmeticOperation.Addition, maintenance, due);
            var mixedCurricula = CreateCurricula(curriculum, (ArithmeticOperation.Addition, customMixed));
            var materializedMixed = Materialize(new[] { maintenance, due });
            var cards = materializedMixed.FsrsStates.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            cards[due.Id] = CreateCard(due, 1);
            return CreateContext(
                position,
                curriculum,
                materializedMixed with { FsrsStates = cards },
                operationProgressions: progressions,
                curricula: mixedCurricula);
        }

        var materialized = stateKind switch
        {
            "due" => Materialize(new[] { earlier }, fsrsDuePosition: 1),
            "maintenance" => Materialize(new[] { earlier }),
            "any" => Materialize(new[] { earlier }, state =>
            {
                state.NeedsRemediation = true;
                state.RemediationDueOrder = 100;
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(stateKind), stateKind, null)
        };
        return CreateContext(
            position,
            curriculum,
            materialized,
            operationProgressions: progressions,
            curricula: curricula);
    }

    private static PracticeSelectionContext CreateSingleBandContext(
        long position,
        IReadOnlyList<ArithmeticFact> frontier,
        IReadOnlyList<ArithmeticFact> recent)
    {
        var operation = frontier[0].Operation;
        var custom = new OperationCurriculum(
            operation,
            new[] { new CurriculumBand(operation, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, frontier) });
        var curriculum = new ArithmeticCurriculum();
        return CreateContext(
            position,
            curriculum,
            Materialize(frontier),
            recentFacts: recent,
            curricula: CreateCurricula(curriculum, (operation, custom)));
    }

    private static OperationCurriculum CreateRepeatedFactCurriculum(
        ArithmeticOperation operation,
        params ArithmeticFact[] facts)
    {
        var first = new CurriculumBand(operation, 0, new CurriculumBandId("TEST-D01"), CurriculumBandKind.Dense, facts);
        var repeated = new CurriculumBand(operation, 1, new CurriculumBandId("TEST-S01"), CurriculumBandKind.Structured, facts);
        return new OperationCurriculum(operation, new[] { first, repeated });
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> CreateProgressions(
        params (ArithmeticOperation Operation, int BandIndex)[] overrides)
    {
        var bands = overrides.ToDictionary(item => item.Operation, item => item.BandIndex);
        return Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, bands.GetValueOrDefault(operation), 0));
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> CreateCurricula(
        ArithmeticCurriculum curriculum,
        params (ArithmeticOperation Operation, OperationCurriculum Curriculum)[] overrides)
    {
        var result = new Dictionary<ArithmeticOperation, OperationCurriculum>
        {
            [ArithmeticOperation.Addition] = curriculum.Addition,
            [ArithmeticOperation.Subtraction] = curriculum.Subtraction,
            [ArithmeticOperation.Multiplication] = curriculum.Multiplication,
            [ArithmeticOperation.Division] = curriculum.Division
        };
        foreach (var item in overrides)
        {
            result[item.Operation] = item.Curriculum;
        }
        return result;
    }

    private static MaterializedState EmptyMaterialized() => new(
        Array.Empty<ArithmeticFact>(),
        new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
        new Dictionary<string, FsrsCardState>(StringComparer.Ordinal));

    private static MaterializedState ReverseMaterialized(MaterializedState source)
    {
        var facts = source.Facts.Reverse().ToArray();
        return new MaterializedState(
            facts,
            facts.ToDictionary(fact => fact.Id, fact => source.ItemStates[fact.Id], StringComparer.Ordinal),
            source.FsrsStates.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static FsrsCardState CreateCard(ArithmeticFact fact, long duePosition) =>
        new(fact.Id, Guid.Empty, 1, null, 1, 1, duePosition, null, FsrsRating.Good);

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        return band!;
    }

    private sealed record MaterializedState(
        IReadOnlyList<ArithmeticFact> Facts,
        IReadOnlyDictionary<string, ItemLearningState> ItemStates,
        IReadOnlyDictionary<string, FsrsCardState> FsrsStates);
}
