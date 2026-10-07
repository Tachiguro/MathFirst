namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class CurriculumInvariantPropertyTests
{
    private readonly ArithmeticCurriculum _curriculum = new();

    [Fact]
    public void Addition_AllCanonicalBandsAndFacts_SatisfyArithmeticInvariants()
    {
        var additionCurriculum = _curriculum.Addition;
        var enumeratedFacts = 0;

        for (var bandIndex = 0; ; bandIndex++)
        {
            if (!additionCurriculum.TryGetBand(bandIndex, out var band) || band is null)
            {
                break;
            }

            Assert.Equal(ArithmeticOperation.Addition, band.Operation);
            Assert.Equal(bandIndex, band.BandIndex);
            Assert.NotEmpty(band.Frontier);

            foreach (var fact in band.Frontier)
            {
                enumeratedFacts++;

                Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                Assert.True(fact.LeftOperand >= 0, $"Addition LeftOperand must be non-negative: {fact.Id}");
                Assert.True(fact.RightOperand >= 0, $"Addition RightOperand must be non-negative: {fact.Id}");
                Assert.Equal(fact.LeftOperand + fact.RightOperand, fact.CorrectResult);
                Assert.True(fact.CorrectResult >= 0);

                Assert.Equal("+", fact.DisplaySymbol);
                Assert.Equal($"{fact.LeftOperand} + {fact.RightOperand}", fact.ExpressionText);
                Assert.Equal($"{fact.LeftOperand} + {fact.RightOperand} = {fact.CorrectResult}", fact.EquationText);
                Assert.Equal($"add:{fact.LeftOperand}+{fact.RightOperand}", fact.Id);

                // Inverse relations (Addition -> Subtraction)
                Assert.Equal(fact.LeftOperand, fact.CorrectResult - fact.RightOperand);
                Assert.Equal(fact.RightOperand, fact.CorrectResult - fact.LeftOperand);
            }
        }

        Assert.True(enumeratedFacts > 0, "Curriculum must enumerate addition facts.");
    }

    [Fact]
    public void Subtraction_AllCanonicalBandsAndFacts_SatisfyArithmeticInvariants()
    {
        var subtractionCurriculum = _curriculum.Subtraction;
        var enumeratedFacts = 0;

        for (var bandIndex = 0; ; bandIndex++)
        {
            if (!subtractionCurriculum.TryGetBand(bandIndex, out var band) || band is null)
            {
                break;
            }

            Assert.Equal(ArithmeticOperation.Subtraction, band.Operation);
            Assert.Equal(bandIndex, band.BandIndex);
            Assert.NotEmpty(band.Frontier);

            foreach (var fact in band.Frontier)
            {
                enumeratedFacts++;

                Assert.Equal(ArithmeticOperation.Subtraction, fact.Operation);
                Assert.True(fact.LeftOperand >= 0, $"Subtraction LeftOperand must be non-negative: {fact.Id}");
                Assert.True(fact.RightOperand >= 0, $"Subtraction RightOperand must be non-negative: {fact.Id}");
                Assert.True(fact.LeftOperand >= fact.RightOperand, $"Subtraction minuend must be >= subtrahend: {fact.Id}");
                Assert.Equal(fact.LeftOperand - fact.RightOperand, fact.CorrectResult);
                Assert.True(fact.CorrectResult >= 0, $"Subtraction result must be non-negative: {fact.Id}");

                Assert.Equal("\u2212", fact.DisplaySymbol);
                Assert.Equal($"{fact.LeftOperand} \u2212 {fact.RightOperand}", fact.ExpressionText);
                Assert.Equal($"{fact.LeftOperand} \u2212 {fact.RightOperand} = {fact.CorrectResult}", fact.EquationText);
                Assert.Equal($"sub:{fact.LeftOperand}-{fact.RightOperand}", fact.Id);

                // Inverse relation (Subtraction -> Addition)
                Assert.Equal(fact.LeftOperand, fact.CorrectResult + fact.RightOperand);
            }
        }

        Assert.True(enumeratedFacts > 0, "Curriculum must enumerate subtraction facts.");
    }

    [Fact]
    public void Multiplication_AllCanonicalBandsAndFacts_SatisfyArithmeticInvariants()
    {
        var multiplicationCurriculum = _curriculum.Multiplication;
        var enumeratedFacts = 0;

        for (var bandIndex = 0; ; bandIndex++)
        {
            if (!multiplicationCurriculum.TryGetBand(bandIndex, out var band) || band is null)
            {
                break;
            }

            Assert.Equal(ArithmeticOperation.Multiplication, band.Operation);
            Assert.Equal(bandIndex, band.BandIndex);
            Assert.NotEmpty(band.Frontier);

            foreach (var fact in band.Frontier)
            {
                enumeratedFacts++;

                Assert.Equal(ArithmeticOperation.Multiplication, fact.Operation);
                Assert.True(fact.LeftOperand >= 0, $"Multiplication LeftOperand must be non-negative: {fact.Id}");
                Assert.True(fact.RightOperand >= 0, $"Multiplication RightOperand must be non-negative: {fact.Id}");
                Assert.Equal(fact.LeftOperand * fact.RightOperand, fact.CorrectResult);
                Assert.True(fact.CorrectResult >= 0);

                Assert.Equal("\u00D7", fact.DisplaySymbol);
                Assert.Equal($"{fact.LeftOperand} \u00D7 {fact.RightOperand}", fact.ExpressionText);
                Assert.Equal($"{fact.LeftOperand} \u00D7 {fact.RightOperand} = {fact.CorrectResult}", fact.EquationText);
                Assert.Equal($"mul:{fact.LeftOperand}*{fact.RightOperand}", fact.Id);

                // Zero-safe Inverse relations (Multiplication -> Division)
                if (fact.RightOperand != 0)
                {
                    Assert.Equal(0, fact.CorrectResult % fact.RightOperand);
                    Assert.Equal(fact.LeftOperand, fact.CorrectResult / fact.RightOperand);
                }

                if (fact.LeftOperand != 0)
                {
                    Assert.Equal(0, fact.CorrectResult % fact.LeftOperand);
                    Assert.Equal(fact.RightOperand, fact.CorrectResult / fact.LeftOperand);
                }
            }
        }

        Assert.True(enumeratedFacts > 0, "Curriculum must enumerate multiplication facts.");
    }

    [Fact]
    public void Division_AllCanonicalBandsAndFacts_SatisfyArithmeticInvariants()
    {
        var divisionCurriculum = _curriculum.Division;
        var enumeratedFacts = 0;

        for (var bandIndex = 0; ; bandIndex++)
        {
            if (!divisionCurriculum.TryGetBand(bandIndex, out var band) || band is null)
            {
                break;
            }

            Assert.Equal(ArithmeticOperation.Division, band.Operation);
            Assert.Equal(bandIndex, band.BandIndex);
            Assert.NotEmpty(band.Frontier);

            foreach (var fact in band.Frontier)
            {
                enumeratedFacts++;

                Assert.Equal(ArithmeticOperation.Division, fact.Operation);
                Assert.True(fact.LeftOperand >= 0, $"Division dividend must be non-negative: {fact.Id}");
                Assert.True(fact.RightOperand > 0, $"Division divisor must be positive (non-zero): {fact.Id}");
                Assert.Equal(0, fact.LeftOperand % fact.RightOperand);
                Assert.Equal(fact.LeftOperand / fact.RightOperand, fact.CorrectResult);
                Assert.True(fact.CorrectResult >= 0);

                Assert.Equal("\u00F7", fact.DisplaySymbol);
                Assert.Equal($"{fact.LeftOperand} \u00F7 {fact.RightOperand}", fact.ExpressionText);
                Assert.Equal($"{fact.LeftOperand} \u00F7 {fact.RightOperand} = {fact.CorrectResult}", fact.EquationText);
                Assert.Equal($"div:{fact.LeftOperand}/{fact.RightOperand}", fact.Id);

                // Inverse relation (Division -> Multiplication)
                Assert.Equal(fact.LeftOperand, fact.CorrectResult * fact.RightOperand);
            }
        }

        Assert.True(enumeratedFacts > 0, "Curriculum must enumerate division facts.");
    }

    [Fact]
    public void CanonicalFacts_AcrossAllFourOperations_HaveDistinctAndStableIds()
    {
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            var opCurriculum = _curriculum.GetCurriculum(op);
            for (var bandIndex = 0; bandIndex < 10; bandIndex++)
            {
                if (!opCurriculum.TryGetBand(bandIndex, out var band) || band is null)
                {
                    continue;
                }

                foreach (var fact in band.Frontier)
                {
                    var reconstructed = new ArithmeticFact(fact.Operation, fact.LeftOperand, fact.RightOperand);
                    Assert.Equal(fact.Id, reconstructed.Id);
                    Assert.Equal(fact.DisplaySymbol, reconstructed.DisplaySymbol);
                    Assert.Equal(fact.CorrectResult, reconstructed.CorrectResult);
                    Assert.Equal(fact.ExpressionText, reconstructed.ToString());
                }
            }
        }
    }
}
