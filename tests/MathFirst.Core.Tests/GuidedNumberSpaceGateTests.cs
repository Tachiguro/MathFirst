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

    [Fact]
    public void GuidedGate_CustomMode_RemainsUnrestricted()
    {
        var gate = GuidedNumberSpaceGate.Unrestricted;

        Assert.False(gate.IsActive);
        Assert.Null(gate.AdditionCeiling);
        Assert.False(gate.IsMultiplicationDecoupled);
        Assert.False(gate.IsDivisionDecoupled);

        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Addition, 100, 100)));
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Subtraction, 100, 99)));
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 100, 100)));
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 100, 1)));
    }

    [Fact]
    public void GuidedGate_Addition_RemainsUnrestricted()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0);

        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Addition, 100, 100)));
    }

    [Fact]
    public void GuidedGate_Subtraction_RemainsUnrestricted()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 0);

        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Subtraction, 100, 99)));
    }

    [Fact]
    public void GuidedGate_Multiplication_Band0_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, multiplicationBandIndex: 0);

        Assert.False(gate.IsMultiplicationDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Multiplication_Band1_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, multiplicationBandIndex: 1);

        Assert.False(gate.IsMultiplicationDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Multiplication_Band2_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, multiplicationBandIndex: 2);

        Assert.False(gate.IsMultiplicationDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Multiplication_Band3_IsDecoupled()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, multiplicationBandIndex: 3);

        Assert.True(gate.IsMultiplicationDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Division_Band0_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, divisionBandIndex: 0);

        Assert.False(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 4, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Division_Band1_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, divisionBandIndex: 1);

        Assert.False(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 4, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Division_Band2_IsAdditionCeilingGated()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, divisionBandIndex: 2);

        Assert.False(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 4, 2))); // 4 <= 4
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_Division_Band3_IsDecoupled()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(new ArithmeticCurriculum().Addition, 1, divisionBandIndex: 3);

        Assert.True(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4
    }

    [Fact]
    public void GuidedGate_MultiplicationBand3_DoesNotDecoupleDivisionBand2()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(
            new ArithmeticCurriculum().Addition,
            1,
            multiplicationBandIndex: 3,
            divisionBandIndex: 2);

        Assert.True(gate.IsMultiplicationDecoupled);
        Assert.False(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);

        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4 (MUL decoupled)
        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4 (DIV gated)
    }

    [Fact]
    public void GuidedGate_DivisionBand3_DoesNotDecoupleMultiplicationBand2()
    {
        var gate = GuidedNumberSpaceGate.ForGuided(
            new ArithmeticCurriculum().Addition,
            1,
            multiplicationBandIndex: 2,
            divisionBandIndex: 3);

        Assert.False(gate.IsMultiplicationDecoupled);
        Assert.True(gate.IsDivisionDecoupled);
        Assert.Equal(4, gate.AdditionCeiling);

        Assert.False(gate.Allows(new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 3))); // 6 > 4 (MUL gated)
        Assert.True(gate.Allows(new ArithmeticFact(ArithmeticOperation.Division, 6, 2))); // 6 > 4 (DIV decoupled)
    }

    [Fact]
    public void GuidedGate_DecoupledCanonicalFact_AllowedWithoutMagicCeiling()
    {
        var additionCurriculum = new ArithmeticCurriculum().Addition;
        var multiplicationCurriculum = new ArithmeticCurriculum().Multiplication;

        var gateBeforeDecoupling = GuidedNumberSpaceGate.ForGuided(additionCurriculum, 0, multiplicationBandIndex: 2);
        var gateAfterDecoupling = GuidedNumberSpaceGate.ForGuided(additionCurriculum, 0, multiplicationBandIndex: 3);

        Assert.True(multiplicationCurriculum.TryGetBand(3, out var band3));
        var canonicalFact = band3!.Frontier.First(f => f.CorrectResult > 2);

        Assert.False(gateBeforeDecoupling.Allows(canonicalFact));
        Assert.True(gateAfterDecoupling.Allows(canonicalFact));
        Assert.Equal(2, gateAfterDecoupling.AdditionCeiling);
    }

    [Fact]
    public void GuidedConstruction_RejectsNegativeMultiplicationOrDivisionBandIndex()
    {
        var additionCurriculum = new ArithmeticCurriculum().Addition;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedNumberSpaceGate.ForGuided(additionCurriculum, 0, multiplicationBandIndex: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GuidedNumberSpaceGate.ForGuided(additionCurriculum, 0, divisionBandIndex: -1));
    }

    [Fact]
    public void GuidedConstruction_WithProgressionsDictionary_ConstructsExpectedGate()
    {
        var additionCurriculum = new ArithmeticCurriculum().Addition;
        var progressions = new Dictionary<ArithmeticOperation, OperationProgression>
        {
            [ArithmeticOperation.Addition] = new(ArithmeticOperation.Addition, 1, 0),
            [ArithmeticOperation.Multiplication] = new(ArithmeticOperation.Multiplication, 3, 0),
            [ArithmeticOperation.Division] = new(ArithmeticOperation.Division, 2, 0)
        };

        var gate = GuidedNumberSpaceGate.ForGuided(additionCurriculum, progressions);

        Assert.Equal(4, gate.AdditionCeiling);
        Assert.True(gate.IsMultiplicationDecoupled);
        Assert.False(gate.IsDivisionDecoupled);
    }

    private static CurriculumBand CreateBand(int bandIndex, string id, params ArithmeticFact[] facts) =>
        new(
            ArithmeticOperation.Addition,
            bandIndex,
            new CurriculumBandId(id),
            CurriculumBandKind.Dense,
            facts);
}
