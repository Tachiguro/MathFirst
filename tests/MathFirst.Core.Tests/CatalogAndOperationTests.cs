namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using Xunit;

public sealed class CatalogAndOperationTests
{
    [Theory]
    [InlineData(0, 1)]     // 0+0
    [InlineData(1, 4)]     // 2x2 = 4
    [InlineData(2, 9)]     // 3x3 = 9
    [InlineData(10, 121)]  // 11x11 = 121
    public void AdditionCatalog_GeneratesCorrectFactCounts(int maxOperand, int expectedCount)
    {
        var facts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Addition, maxOperand);
        Assert.Equal(expectedCount, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(ArithmeticOperation.Addition, f.Operation);
            Assert.True(f.LeftOperand <= maxOperand);
            Assert.True(f.RightOperand <= maxOperand);
            Assert.Equal(f.LeftOperand + f.RightOperand, f.CorrectResult);
            Assert.Equal("+", f.DisplaySymbol);
        });
    }

    [Fact]
    public void AdditionCatalog_DistinctOrderedPairsHaveDistinctIdentities()
    {
        var fact3Plus4 = new ArithmeticFact(ArithmeticOperation.Addition, 3, 4);
        var fact4Plus3 = new ArithmeticFact(ArithmeticOperation.Addition, 4, 3);

        Assert.Equal("add:3+4", fact3Plus4.Id);
        Assert.Equal("add:4+3", fact4Plus3.Id);
        Assert.NotEqual(fact3Plus4.Id, fact4Plus3.Id);
        Assert.Equal(7, fact3Plus4.CorrectResult);
        Assert.Equal(7, fact4Plus3.CorrectResult);
    }

    [Theory]
    [InlineData(0, 1)]    // 0-0
    [InlineData(1, 3)]    // 0-0, 1-0, 1-1
    [InlineData(2, 6)]    // (3*4)/2 = 6
    [InlineData(10, 66)]  // (11*12)/2 = 66
    public void SubtractionCatalog_NeverProducesNegativeResults(int maxOperand, int expectedCount)
    {
        var facts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Subtraction, maxOperand);
        Assert.Equal(expectedCount, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(ArithmeticOperation.Subtraction, f.Operation);
            Assert.True(f.LeftOperand >= f.RightOperand, $"Left {f.LeftOperand} must be >= Right {f.RightOperand}");
            Assert.True(f.CorrectResult >= 0, "Subtraction result must be non-negative");
            Assert.Equal(f.LeftOperand - f.RightOperand, f.CorrectResult);
            Assert.Equal("\u2212", f.DisplaySymbol);
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 4)]
    [InlineData(2, 9)]
    [InlineData(10, 121)]
    public void MultiplicationCatalog_GeneratesExactFacts(int maxOperand, int expectedCount)
    {
        var facts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Multiplication, maxOperand);
        Assert.Equal(expectedCount, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(ArithmeticOperation.Multiplication, f.Operation);
            Assert.Equal(f.LeftOperand * f.RightOperand, f.CorrectResult);
            Assert.Equal("\u00D7", f.DisplaySymbol);
        });
    }

    [Theory]
    [InlineData(1, 2)]    // R=1, Q in {0, 1} -> 0/1, 1/1 (2 facts)
    [InlineData(2, 6)]    // R=1 (0/1,1/1,2/1), R=2 (0/2,2/2,4/2) (6 facts)
    [InlineData(10, 110)] // 10 divisors * 11 quotients = 110 facts
    public void DivisionCatalog_NeverDividesByZeroAndAlwaysExact(int maxOperand, int expectedCount)
    {
        var facts = ArithmeticCatalog.GetFacts(ArithmeticOperation.Division, maxOperand);
        Assert.Equal(expectedCount, facts.Count);
        Assert.All(facts, f =>
        {
            Assert.Equal(ArithmeticOperation.Division, f.Operation);
            Assert.True(f.RightOperand >= 1, "Divisor must never be zero");
            Assert.True(f.LeftOperand % f.RightOperand == 0, "Division fact must have zero remainder");
            Assert.Equal(f.LeftOperand / f.RightOperand, f.CorrectResult);
            Assert.Equal("\u00F7", f.DisplaySymbol);
        });
    }

    [Fact]
    public void NewlyUnlockedFacts_FiltersOutPriorOperands()
    {
        var newlyUnlocked = ArithmeticCatalog.GetNewlyUnlockedFacts(ArithmeticOperation.Addition, 2);
        // Prior was 0..1 (4 facts). Up to 2 is 9 facts. New facts involving operand 2 = 5 facts:
        // (0+2, 1+2, 2+0, 2+1, 2+2)
        Assert.Equal(5, newlyUnlocked.Count);
        Assert.All(newlyUnlocked, f => Assert.True(f.LeftOperand == 2 || f.RightOperand == 2));
    }
}
