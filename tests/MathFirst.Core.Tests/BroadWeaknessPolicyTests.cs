namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class BroadWeaknessPolicyTests
{
    private readonly ArithmeticCurriculum _curriculum = new();

    #region Argument Validation

    [Fact]
    public void CountEligibleWeakFacts_NullArguments_ThrowsArgumentNullException()
    {
        var itemStates = Array.Empty<ItemLearningState>();
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.CountEligibleWeakFacts(null!, activeOps, progressions, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.CountEligibleWeakFacts(itemStates, null!, progressions, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.CountEligibleWeakFacts(itemStates, activeOps, null!, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.CountEligibleWeakFacts(itemStates, activeOps, progressions, null!, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.CountEligibleWeakFacts(itemStates, activeOps, progressions, _curriculum, null!));
    }

    [Fact]
    public void HasBroadWeakness_NullArguments_ThrowsArgumentNullException()
    {
        var itemStates = Array.Empty<ItemLearningState>();
        var activeOps = new[] { ArithmeticOperation.Addition };
        var progressions = CreateDefaultProgressions();
        var gate = GuidedNumberSpaceGate.Unrestricted;

        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.HasBroadWeakness(null!, activeOps, progressions, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.HasBroadWeakness(itemStates, null!, progressions, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.HasBroadWeakness(itemStates, activeOps, null!, _curriculum, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.HasBroadWeakness(itemStates, activeOps, progressions, null!, gate));
        Assert.Throws<ArgumentNullException>(() =>
            BroadWeaknessPolicy.HasBroadWeakness(itemStates, activeOps, progressions, _curriculum, null!));
    }

    #endregion

    #region CountEligibleWeakFacts Scenarios

    [Fact]
    public void CountEligibleWeakFacts_EmptyCollection_ReturnsZero()
    {
        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            [],
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_SingleEligibleRemediationFact_ReturnsOne()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_NonRemediationFact_IsIgnored()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: false),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, needsRemediation: false)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_FactFromInactiveOperation_IsIgnored()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Subtraction, 0, 0, needsRemediation: true)
        };

        // Only Addition is active; Subtraction must be ignored
        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_FactWithoutProgression_IsIgnored()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 0, 0, needsRemediation: true)
        };

        // Multiplication is active, but missing from progressions dictionary
        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            progressions,
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_FutureIneligibleOwnedFact_IsIgnored()
    {
        // BandIndex is 0 for Addition. 2+2 belongs to ADD-D02 (Band 1), so ineligible at Band 0
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 2, 2, needsRemediation: true)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_FactExcludedByEffectiveGuidedGate_IsIgnored()
    {
        // ADD-D01 has ceiling = 1. mul:5*5 has CorrectResult = 25 > 1, so gate excludes it
        var gate = GuidedNumberSpaceGate.ForGuided(
            _curriculum.Addition,
            unlockedAdditionBandIndex: 0,
            multiplicationBandIndex: 0,
            divisionBandIndex: 0);

        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 5, 5, needsRemediation: true)
        };

        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 0, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 4, 0)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            progressions,
            _curriculum,
            gate);

        Assert.Equal(1, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_MultipleActiveOperations_CountsAllEligibleWeakFacts()
    {
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Subtraction, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Multiplication, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Division, 0, 1, needsRemediation: true)
        };

        var activeOps = new[]
        {
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Division
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            activeOps,
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(4, count);
    }

    [Fact]
    public void CountEligibleWeakFacts_ReturnsExactAggregateCount_NotCappedAtThreshold()
    {
        // ADD-D01 contains: 0+0, 0+1, 1+0, 1+1. All 4 in remediation.
        var itemStates = new[]
        {
            CreateItemState(ArithmeticOperation.Addition, 0, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 0, 1, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 1, 0, needsRemediation: true),
            CreateItemState(ArithmeticOperation.Addition, 1, 1, needsRemediation: true)
        };

        var count = BroadWeaknessPolicy.CountEligibleWeakFacts(
            itemStates,
            [ArithmeticOperation.Addition],
            CreateDefaultProgressions(),
            _curriculum,
            GuidedNumberSpaceGate.Unrestricted);

        Assert.Equal(4, count);
    }

    #endregion

    #region Helpers

    private static ItemLearningState CreateItemState(
        ArithmeticOperation operation,
        int left,
        int right,
        bool needsRemediation = false)
    {
        var fact = new ArithmeticFact(operation, left, right);
        var state = ItemLearningState.CreateNew(fact);
        state.TotalAttempts = 1;
        state.NeedsRemediation = needsRemediation;
        return state;
    }

    private static Dictionary<ArithmeticOperation, OperationProgression> CreateDefaultProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => new OperationProgression(op, 0, 0));

    #endregion
}
