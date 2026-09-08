namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class DenseCurriculumTests
{
    [Fact]
    public void ArithmeticFact_RejectsFactsOutsideTheExactNonNegativeInt32Domain()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArithmeticFact(ArithmeticOperation.Addition, -1, 2));
        Assert.Throws<ArgumentException>(() =>
            new ArithmeticFact(ArithmeticOperation.Subtraction, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArithmeticFact(ArithmeticOperation.Multiplication, 2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArithmeticFact(ArithmeticOperation.Division, -4, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArithmeticFact(ArithmeticOperation.Division, 4, 0));
        Assert.Throws<ArgumentException>(() =>
            new ArithmeticFact(ArithmeticOperation.Division, 5, 2));
        Assert.Throws<OverflowException>(() =>
            new ArithmeticFact(ArithmeticOperation.Addition, int.MaxValue, 1));
        Assert.Throws<OverflowException>(() =>
            new ArithmeticFact(ArithmeticOperation.Multiplication, int.MaxValue, 2));
    }

    [Fact]
    public void DenseBandIdsAndCanonicalOperationOrder_AreStable()
    {
        var curriculum = new ArithmeticCurriculum();

        AssertBandIds(curriculum.Addition, Enumerable.Range(1, 10).Select(n => $"ADD-D{n:00}"));
        AssertBandIds(
            curriculum.Subtraction,
            Enumerable.Range(1, 10).Select(n => $"SUB-D{n:00}")
                .Concat(Enumerable.Range(11, 10).Select(n => $"SUB-I{n:00}")));
        AssertBandIds(curriculum.Multiplication, Enumerable.Range(1, 12).Select(n => $"MUL-D{n:00}"));
        AssertBandIds(curriculum.Division, Enumerable.Range(1, 12).Select(n => $"DIV-D{n:00}"));

        Assert.Same(curriculum.Addition, curriculum.GetCurriculum(ArithmeticOperation.Addition));
        Assert.Same(curriculum.Subtraction, curriculum.GetCurriculum(ArithmeticOperation.Subtraction));
        Assert.Same(curriculum.Multiplication, curriculum.GetCurriculum(ArithmeticOperation.Multiplication));
        Assert.Same(curriculum.Division, curriculum.GetCurriculum(ArithmeticOperation.Division));
    }

    [Fact]
    public void AdditionDenseFrontiers_HaveExactCountsIdentityAndOrdering()
    {
        var bands = new ArithmeticCurriculum().Addition.Bands;

        Assert.Equal(new[] { 4, 5, 7, 9, 11, 13, 15, 17, 19, 21 }, bands.Select(b => b.Frontier.Count));
        Assert.Equal(
            new[] { "add:0+0", "add:0+1", "add:1+0", "add:1+1" },
            bands[0].Frontier.Select(f => f.Id));
        Assert.Equal(
            new[] { "add:0+2", "add:1+2", "add:2+0", "add:2+1", "add:2+2" },
            bands[1].Frontier.Select(f => f.Id));
        AssertOperationFoundation(bands, ArithmeticOperation.Addition, 121);
    }

    [Fact]
    public void SubtractionDenseFrontiers_CompleteTheNonNegativeInverseFoundation()
    {
        var bands = new ArithmeticCurriculum().Subtraction.Bands;

        Assert.Equal(new[] { 3, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, bands.Take(10).Select(b => b.Frontier.Count));
        Assert.Equal(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, bands.Skip(10).Select(b => b.Frontier.Count));
        Assert.Equal(55, bands.Skip(10).Sum(b => b.Frontier.Count));
        Assert.Equal(
            new[] { "sub:0-0", "sub:1-0", "sub:1-1" },
            bands[0].Frontier.Select(f => f.Id));
        Assert.Equal(
            Enumerable.Range(1, 10).Select(b => $"sub:11-{b}"),
            bands[10].Frontier.Select(f => f.Id));
        Assert.Equal(new[] { "sub:20-10" }, bands[19].Frontier.Select(f => f.Id));
        Assert.All(bands.SelectMany(b => b.Frontier), fact =>
        {
            Assert.True(fact.LeftOperand >= fact.RightOperand);
            Assert.Equal(fact.LeftOperand - fact.RightOperand, fact.CorrectResult);
        });
        AssertOperationFoundation(bands, ArithmeticOperation.Subtraction, 121);
    }

    [Fact]
    public void MultiplicationDenseFrontiers_HaveExactCountsIdentityAndOrdering()
    {
        var bands = new ArithmeticCurriculum().Multiplication.Bands;

        Assert.Equal(new[] { 4, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25 }, bands.Select(b => b.Frontier.Count));
        Assert.Equal(
            new[] { "mul:0*2", "mul:1*2", "mul:2*0", "mul:2*1", "mul:2*2" },
            bands[1].Frontier.Select(f => f.Id));
        AssertOperationFoundation(bands, ArithmeticOperation.Multiplication, 169);
    }

    [Fact]
    public void DivisionDenseFrontiers_HaveExactCountsIdentityAndOrdering()
    {
        var bands = new ArithmeticCurriculum().Division.Bands;

        Assert.Equal(new[] { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24 }, bands.Select(b => b.Frontier.Count));
        Assert.Equal(new[] { "div:0/1", "div:1/1" }, bands[0].Frontier.Select(f => f.Id));
        Assert.Equal(
            new[] { "div:2/1", "div:0/2", "div:2/2", "div:4/2" },
            bands[1].Frontier.Select(f => f.Id));
        Assert.All(bands.SelectMany(b => b.Frontier), fact =>
        {
            Assert.True(fact.RightOperand > 0);
            Assert.True(fact.LeftOperand >= 0);
            Assert.Equal(0, fact.LeftOperand % fact.RightOperand);
            Assert.Equal(fact.LeftOperand / fact.RightOperand, fact.CorrectResult);
        });
        AssertOperationFoundation(bands, ArithmeticOperation.Division, 156);
    }

    [Fact]
    public void CompleteDenseFoundation_ContainsExactly567Facts()
    {
        var curriculum = new ArithmeticCurriculum();

        var total = curriculum.Addition.Bands.Sum(b => b.Frontier.Count)
            + curriculum.Subtraction.Bands.Sum(b => b.Frontier.Count)
            + curriculum.Multiplication.Bands.Sum(b => b.Frontier.Count)
            + curriculum.Division.Bands.Sum(b => b.Frontier.Count);

        Assert.Equal(567, total);
    }

    [Fact]
    public void DenseCurriculumConstruction_IsRestartDeterministic()
    {
        var first = new ArithmeticCurriculum();
        var second = new ArithmeticCurriculum();

        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            var firstBands = first.GetCurriculum(operation).Bands;
            var secondBands = second.GetCurriculum(operation).Bands;

            Assert.Equal(firstBands.Select(b => b.Id.Value), secondBands.Select(b => b.Id.Value));
            Assert.Equal(
                firstBands.SelectMany(b => b.Frontier).Select(f => f.Id),
                secondBands.SelectMany(b => b.Frontier).Select(f => f.Id));
        }
    }

    [Fact]
    public void OperationCurriculum_LookupExtendsPastDenseBandsAndFailsSafelyOutsideCompleteBands()
    {
        var addition = new ArithmeticCurriculum().Addition;

        Assert.True(addition.TryGetBand(0, out var first));
        Assert.Equal("ADD-D01", first!.Id.Value);
        Assert.False(addition.TryGetBand(-1, out var beforeFirst));
        Assert.Null(beforeFirst);
        Assert.True(addition.TryGetBand(addition.Bands.Count, out var firstStructured));
        Assert.Equal("ADD-P1-ANCHOR", firstStructured!.Id.Value);
        Assert.False(addition.TryGetBand(int.MaxValue, out var unavailable));
        Assert.Null(unavailable);
    }

    [Fact]
    public void CurriculumBandId_RejectsUnstableRepresentations()
    {
        Assert.Throws<ArgumentException>(() => new CurriculumBandId(""));
        Assert.Throws<ArgumentException>(() => new CurriculumBandId("add-d01"));
        Assert.Throws<ArgumentException>(() => new CurriculumBandId("ADD D01"));
    }

    [Fact]
    public void V4Catalog_RemainsTheSame418CanonicalFacts()
    {
        var addition = ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, 10);
        var subtraction = ArithmeticCatalog.GetFacts(ArithmeticOperation.Subtraction, 10);
        var multiplication = ArithmeticCatalog.GetFacts(ArithmeticOperation.Multiplication, 10);
        var division = ArithmeticCatalog.GetFacts(ArithmeticOperation.Division, 10);

        Assert.Equal(121, addition.Count);
        Assert.Equal(66, subtraction.Count);
        Assert.Equal(121, multiplication.Count);
        Assert.Equal(110, division.Count);
        Assert.Equal(418, addition.Count + subtraction.Count + multiplication.Count + division.Count);

        Assert.Equal("add:0+0", addition[0].Id);
        Assert.Equal("add:10+10", addition[^1].Id);
        Assert.Equal("sub:0-0", subtraction[0].Id);
        Assert.Equal("sub:10-10", subtraction[^1].Id);
        Assert.Equal("mul:0*0", multiplication[0].Id);
        Assert.Equal("mul:10*10", multiplication[^1].Id);
        Assert.Equal("div:0/1", division[0].Id);
        Assert.Equal("div:100/10", division[^1].Id);
    }

    private static void AssertBandIds(OperationCurriculum curriculum, IEnumerable<string> expectedIds)
    {
        Assert.Equal(expectedIds, curriculum.Bands.Select(b => b.Id.Value));
        Assert.All(curriculum.Bands, band =>
        {
            Assert.Equal(curriculum.Operation, band.Operation);
            Assert.Equal(CurriculumBandKind.Dense, band.Kind);
            Assert.Equal(band, curriculum.Bands[band.BandIndex]);
        });
    }

    private static void AssertOperationFoundation(
        IReadOnlyList<CurriculumBand> bands,
        ArithmeticOperation operation,
        int expectedCount)
    {
        var facts = bands.SelectMany(b => b.Frontier).ToArray();

        Assert.Equal(expectedCount, facts.Length);
        Assert.Equal(expectedCount, facts.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(facts, fact => Assert.Equal(operation, fact.Operation));
    }
}
