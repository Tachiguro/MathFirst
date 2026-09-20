namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class GuidedNumberSpaceGateTests
{
    [Fact]
    public void AllFourOperations_ActivateGuidedMode()
    {
        Assert.True(GuidedNumberSpaceGate.IsGuidedMode(PracticeOperationPreferencePolicy.AllOperations));
    }

    [Fact]
    public void EveryProperNonEmptyOperationSubset_RemainsCustomMode()
    {
        var operations = PracticeOperationPreferencePolicy.AllOperations;
        var fullMask = (1 << operations.Count) - 1;

        for (var mask = 1; mask < fullMask; mask++)
        {
            var subset = operations
                .Where((_, index) => (mask & (1 << index)) != 0)
                .ToArray();

            Assert.False(GuidedNumberSpaceGate.IsGuidedMode(subset));
        }
    }

    [Fact]
    public void OperationOrdering_DoesNotChangeGuidedIdentity()
    {
        Assert.True(GuidedNumberSpaceGate.IsGuidedMode(
        [
            ArithmeticOperation.Division,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Addition
        ]));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 6)]
    public void CanonicalAdditionPrefix_ProducesExpectedCeiling(int bandIndex, int expectedCeiling)
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, bandIndex);

        Assert.True(gate.IsActive);
        Assert.Equal(expectedCeiling, gate.AdditionCeiling);
    }

    [Fact]
    public void NonMonotonicAdditionCurriculum_UsesCompletePrefixMaximum()
    {
        var high = new ArithmeticFact(ArithmeticOperation.Addition, 50, 50);
        var low = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var curriculum = new OperationCurriculum(
            ArithmeticOperation.Addition,
            [
                CreateBand(0, "TEST-ADD-HIGH", high),
                CreateBand(1, "TEST-ADD-LOW", low)
            ]);

        var gate = GuidedNumberSpaceGate.ForGuided(curriculum, 1);

        Assert.Equal(100, gate.AdditionCeiling);
    }

    [Fact]
    public void GuidedConstruction_RejectsNonAdditionCurriculum()
    {
        Assert.Throws<ArgumentException>(() =>
            GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Multiplication, 0));
    }

    [Fact]
    public void GuidedConstruction_RejectsNegativeBandIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, -1));
    }

    [Fact]
    public void GuidedConstruction_RejectsUnresolvableClaimedPrefix()
    {
        var first = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var third = new ArithmeticFact(ArithmeticOperation.Addition, 2, 2);
        var curriculum = new OperationCurriculum(
            ArithmeticOperation.Addition,
            [CreateBand(0, "TEST-ADD-0", first)],
            bandIndex => bandIndex == 2 ? CreateBand(2, "TEST-ADD-2", third) : null);

        Assert.Throws<InvalidOperationException>(() => GuidedNumberSpaceGate.ForGuided(curriculum, 2));
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 3, false)]
    public void GuidedMultiplication_UsesProductCeiling(int left, int right, bool expected)
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0);

        Assert.Equal(expected, gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, left, right)));
    }

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(4, 2, false)]
    public void GuidedDivision_UsesDividendCeiling(int dividend, int divisor, bool expected)
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0);

        Assert.Equal(expected, gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, dividend, divisor)));
    }

    [Fact]
    public void GuidedGate_DoesNotRestrictAdditionOrSubtraction()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0);

        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Addition, 100, 100)));
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Subtraction, 100, 99)));
    }

    [Fact]
    public void UnrestrictedGate_AllowsEveryOperation()
    {
        var gate = GuidedNumberSpaceGate.Unrestricted;
        var facts = new[]
        {
            new ArithmeticFact(ArithmeticOperation.Addition, 100, 100),
            new ArithmeticFact(ArithmeticOperation.Subtraction, 100, 99),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 100, 100),
            new ArithmeticFact(ArithmeticOperation.Division, 100, 1)
        };

        Assert.False(gate.IsActive);
        Assert.Null(gate.AdditionCeiling);
        Assert.All(facts, fact => Assert.True(gate.Allows(fact)));
    }

    [Fact]
    public void GuidedIdentity_RejectsUnknownOperation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedNumberSpaceGate.IsGuidedMode([(ArithmeticOperation)999]));
    }

    private static CurriculumBand CreateBand(int bandIndex, string id, params ArithmeticFact[] facts) =>
        new(
            ArithmeticOperation.Addition,
            bandIndex,
            new CurriculumBandId(id),
            CurriculumBandKind.Dense,
            facts);
}
