namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class StructuredCurriculumTests
{
    [Fact]
    public void AdditionAfterD10_ContinuesIncrementalDenseBandsInsteadOfStructuredFamilies()
    {
        // Issue #126 replaced the ADD-P{k} structured families with one-operand-at-a-time Dense frontiers.
        var addition = new ArithmeticCurriculum().Addition;
        var bands = GetBands(addition, 10, 20);

        Assert.Equal(Enumerable.Range(11, 20).Select(n => $"ADD-D{n:00}"), bands.Select(band => band.Id.Value));
        Assert.All(bands, band => Assert.Equal(CurriculumBandKind.Dense, band.Kind));
        Assert.Equal(Enumerable.Range(11, 20).Select(n => (2 * n) + 1), bands.Select(band => band.Frontier.Count));
        Assert.Equal(
            new[] { "add:0+11", "add:1+11", "add:2+11", "add:3+11", "add:4+11" },
            GetBand(addition, 10).Frontier.Take(5).Select(fact => fact.Id));
        Assert.All(bands, AssertCompleteUniqueBand);
        Assert.Equal(
            bands.SelectMany(band => band.Frontier).Select(fact => fact.Id),
            GetBands(new ArithmeticCurriculum().Addition, 10, 20).SelectMany(band => band.Frontier).Select(fact => fact.Id));
    }

    [Fact]
    public void SubtractionStructuredBands_AreExactDeterministicAdditionInverses()
    {
        var curriculum = new ArithmeticCurriculum();
        var additionBands = CreateStructuredAdditionCoreBands();
        var subtractionBands = GetBands(curriculum.Subtraction, 20, 11);

        Assert.Equal(
            new[]
            {
                "SUB-P1-ANCHOR", "SUB-P1-T0", "SUB-P1-R0", "SUB-P1-D0",
                "SUB-P2-ANCHOR", "SUB-P2-T1", "SUB-P2-R1", "SUB-P2-D1",
                "SUB-P2-T0", "SUB-P2-R0", "SUB-P2-D0"
            },
            subtractionBands.Select(band => band.Id.Value));

        Assert.Equal(subtractionBands.Length, additionBands.Count);
        for (var index = 0; index < additionBands.Count; index++)
        {
            var expected = CreateSubtractionInverseIds(additionBands[index]);
            Assert.Equal(expected, subtractionBands[index].Frontier.Select(fact => fact.Id));
            AssertCompleteUniqueBand(subtractionBands[index]);
        }

        Assert.All(subtractionBands.SelectMany(band => band.Frontier), fact =>
        {
            Assert.True(fact.LeftOperand >= fact.RightOperand);
            Assert.Equal(fact.LeftOperand - fact.RightOperand, fact.CorrectResult);
        });

        var restart = GetBands(new ArithmeticCurriculum().Subtraction, 20, 11);
        Assert.Equal(
            subtractionBands.SelectMany(band => band.Frontier).Select(fact => fact.Id),
            restart.SelectMany(band => band.Frontier).Select(fact => fact.Id));
    }

    [Fact]
    public void MultiplicationStructuredBands_HaveCanonicalOrderCountsAndExactSequence()
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;

        AssertBands(
            multiplication,
            12,
            new[]
            {
                ("MUL-2D", 144),
                ("MUL-P1-ROUND", 144),
                ("MUL-P1-SHIFT", 174),
                ("MUL-P1-SCALED", 144),
                ("MUL-P2-ROUND", 144),
                ("MUL-P2-SHIFT", 198),
                ("MUL-P2-SCALED", 144),
                ("MUL-P3-ROUND", 144),
                ("MUL-P3-SHIFT", 198),
                ("MUL-P3-SCALED", 144)
            });

        Assert.Equal(new[] { "mul:71*2", "mul:2*71" }, GetBand(multiplication, 12).Frontier.Take(2).Select(fact => fact.Id));
        Assert.Equal(new[] { "mul:10*2", "mul:2*10" }, GetBand(multiplication, 13).Frontier.Take(2).Select(fact => fact.Id));
        Assert.Equal(new[] { "mul:13*10", "mul:10*13" }, GetBand(multiplication, 14).Frontier.Take(2).Select(fact => fact.Id));
        Assert.Equal(new[] { "mul:710*2", "mul:2*710" }, GetBand(multiplication, 15).Frontier.Take(2).Select(fact => fact.Id));

        var bands = GetBands(multiplication, 12, 10);
        Assert.All(bands, AssertCompleteUniqueBand);
        Assert.All(bands.SelectMany(band => band.Frontier), fact =>
            Assert.Equal(checked(fact.LeftOperand * fact.RightOperand), fact.CorrectResult));
        Assert.Equal(
            bands.SelectMany(band => band.Frontier).Select(fact => fact.Id),
            GetBands(new ArithmeticCurriculum().Multiplication, 12, 10)
                .SelectMany(band => band.Frontier)
                .Select(fact => fact.Id));
    }

    [Fact]
    public void DivisionStructuredBands_AreExactDeterministicMultiplicationInverses()
    {
        var curriculum = new ArithmeticCurriculum();
        var multiplicationBands = GetBands(curriculum.Multiplication, 12, 7);
        var divisionBands = GetBands(curriculum.Division, 12, 7);

        Assert.Equal(
            new[]
            {
                "DIV-2D", "DIV-P1-ROUND", "DIV-P1-SHIFT", "DIV-P1-SCALED",
                "DIV-P2-ROUND", "DIV-P2-SHIFT", "DIV-P2-SCALED"
            },
            divisionBands.Select(band => band.Id.Value));

        for (var index = 0; index < multiplicationBands.Length; index++)
        {
            var expected = CreateDivisionInverseIds(multiplicationBands[index].Frontier);
            Assert.Equal(expected, divisionBands[index].Frontier.Select(fact => fact.Id));
            AssertCompleteUniqueBand(divisionBands[index]);
        }

        Assert.Equal(new[] { "div:142/71", "div:142/2" }, divisionBands[0].Frontier.Take(2).Select(fact => fact.Id));
        Assert.All(divisionBands.SelectMany(band => band.Frontier), fact =>
        {
            Assert.True(fact.RightOperand > 0);
            Assert.True(fact.LeftOperand >= 0);
            Assert.Equal(0, fact.LeftOperand % fact.RightOperand);
            Assert.Equal(fact.LeftOperand / fact.RightOperand, fact.CorrectResult);
        });
        Assert.Equal(
            divisionBands.SelectMany(band => band.Frontier).Select(fact => fact.Id),
            GetBands(new ArithmeticCurriculum().Division, 12, 7)
                .SelectMany(band => band.Frontier)
                .Select(fact => fact.Id));
    }

    [Fact]
    public void CompleteBandSafety_StopsBeforeAnyPartialInt32UnsafeBand()
    {
        var curriculum = new ArithmeticCurriculum();

        // Issue #126: Addition stops at its explicit incremental resource bound instead of the structured Int32 limit.
        AssertAllAvailable(curriculum.Addition, 101, 126);
        Assert.True(curriculum.Addition.TryGetBand(IncrementalAdditionCurriculum.MaximumBandIndex, out _));
        Assert.False(curriculum.Addition.TryGetBand(IncrementalAdditionCurriculum.MaximumBandIndex + 1, out var unsafeAddition));
        Assert.Null(unsafeAddition);

        AssertAllAvailable(curriculum.Subtraction, 111, 135);
        Assert.False(curriculum.Subtraction.TryGetBand(136, out var unsafeSubtraction));
        Assert.Null(unsafeSubtraction);

        Assert.True(curriculum.Multiplication.TryGetBand(31, out var safeRound));
        Assert.Equal("MUL-P7-ROUND", safeRound!.Id.Value);
        Assert.True(curriculum.Multiplication.TryGetBand(32, out var safeShift));
        Assert.Equal("MUL-P7-SHIFT", safeShift!.Id.Value);
        Assert.False(curriculum.Multiplication.TryGetBand(33, out var unsafeScaled));
        Assert.Null(unsafeScaled);
        Assert.False(curriculum.Multiplication.TryGetBand(33, out _));

        Assert.True(curriculum.Division.TryGetBand(32, out var safeDivisionShift));
        Assert.Equal("DIV-P7-SHIFT", safeDivisionShift!.Id.Value);
        Assert.False(curriculum.Division.TryGetBand(33, out var unsafeDivisionScaled));
        Assert.Null(unsafeDivisionScaled);
    }

    [Fact]
    public void ProceduralLookup_IsBoundedToTheRequestedBand()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var multiplication = new ArithmeticCurriculum().Multiplication;

        Assert.True(addition.TryGetBand(10, out _));
        Assert.False(addition.TryGetBand(int.MaxValue, out _));
        Assert.True(multiplication.TryGetBand(12, out _));
        Assert.False(multiplication.TryGetBand(int.MaxValue, out _));
    }

    private static void AssertBands(
        OperationCurriculum curriculum,
        int startIndex,
        IReadOnlyList<(string Id, int Count)> expected)
    {
        for (var offset = 0; offset < expected.Count; offset++)
        {
            var band = GetBand(curriculum, startIndex + offset);
            Assert.Equal(expected[offset].Id, band.Id.Value);
            Assert.Equal(expected[offset].Count, band.Frontier.Count);
            Assert.Equal(startIndex + offset, band.BandIndex);
            Assert.Equal(CurriculumBandKind.Structured, band.Kind);
        }
    }

    private static CurriculumBand[] GetBands(OperationCurriculum curriculum, int startIndex, int count) =>
        Enumerable.Range(startIndex, count).Select(index => GetBand(curriculum, index)).ToArray();

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        return band!;
    }

    private static void AssertCompleteUniqueBand(CurriculumBand band)
    {
        Assert.NotEmpty(band.Frontier);
        Assert.Equal(
            band.Frontier.Count,
            band.Frontier.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count());
    }

    private static string[] CreateSubtractionInverseIds(IEnumerable<ArithmeticFact> additions)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addition in additions)
        {
            var sum = checked(addition.LeftOperand + addition.RightOperand);
            AddId(ids, seen, $"sub:{sum}-{addition.LeftOperand}");
            AddId(ids, seen, $"sub:{sum}-{addition.RightOperand}");
        }

        return ids.ToArray();
    }

    private static string[] CreateDivisionInverseIds(IEnumerable<ArithmeticFact> multiplications)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var multiplication in multiplications)
        {
            var product = checked(multiplication.LeftOperand * multiplication.RightOperand);
            AddId(ids, seen, $"div:{product}/{multiplication.LeftOperand}");
            AddId(ids, seen, $"div:{product}/{multiplication.RightOperand}");
        }

        return ids.ToArray();
    }

    /// <summary>
    /// Independent oracle for the ADR-0003 structured additive core pairs (magnitudes 1 and 2) that the Subtraction
    /// structured bands invert. Addition itself no longer uses these families after Issue #126.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<ArithmeticFact>> CreateStructuredAdditionCoreBands()
    {
        var bands = new List<IReadOnlyList<ArithmeticFact>>();
        for (var magnitude = 1; magnitude <= 2; magnitude++)
        {
            var power = magnitude == 1 ? 10 : 100;
            var anchor = new List<ArithmeticFact>();
            for (var u = 1; u <= 9; u++)
            {
                for (var v = u; v <= 9; v++)
                {
                    AddBothDirections(anchor, u * power, v * power);
                }
            }

            bands.Add(anchor);
            for (var lowerPlace = magnitude - 1; lowerPlace >= 0; lowerPlace--)
            {
                var lowerPower = lowerPlace == 0 ? 1 : 10;
                var transfer = new List<ArithmeticFact>();
                var round = new List<ArithmeticFact>();
                for (var u = 1; u <= 9; u++)
                {
                    for (var v = 1; v <= 9; v++)
                    {
                        var c = 1 + (((3 * u) + (5 * v) + magnitude + lowerPlace) % 9);
                        AddBothDirections(transfer, (c * power) + (u * lowerPower), v * lowerPower);
                        AddBothDirections(round, (c * power) + (u * lowerPower), v * power);
                    }
                }

                var decomposition = new List<ArithmeticFact>();
                for (var u = 1; u <= 9; u++)
                {
                    for (var v = u; v <= 9; v++)
                    {
                        var a = 1 + (((2 * u) + (3 * v) + magnitude + lowerPlace) % 9);
                        var b0 = 1 + (((5 * u) + (7 * v) + magnitude + lowerPlace) % 9);
                        var b = u == v && a == b0 ? 1 + (b0 % 9) : b0;
                        AddBothDirections(decomposition, (a * power) + (u * lowerPower), (b * power) + (v * lowerPower));
                    }
                }

                bands.Add(transfer);
                bands.Add(round);
                bands.Add(decomposition);
            }
        }

        return bands;
    }

    private static void AddBothDirections(ICollection<ArithmeticFact> facts, int left, int right)
    {
        facts.Add(new ArithmeticFact(ArithmeticOperation.Addition, left, right));
        if (left != right)
        {
            facts.Add(new ArithmeticFact(ArithmeticOperation.Addition, right, left));
        }
    }

    private static void AddId(ICollection<string> ids, ISet<string> seen, string id)
    {
        if (seen.Add(id))
        {
            ids.Add(id);
        }
    }

    private static void AssertAllAvailable(OperationCurriculum curriculum, int first, int last)
    {
        for (var index = first; index <= last; index++)
        {
            Assert.True(curriculum.TryGetBand(index, out var band));
            Assert.NotNull(band);
        }
    }
}
