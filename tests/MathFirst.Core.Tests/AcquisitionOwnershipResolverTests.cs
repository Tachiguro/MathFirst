namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class AcquisitionOwnershipResolverTests
{
    [Fact]
    public void IsEligible_Multiplication_2x8_IsIneligibleAtBand1And2_EligibleAtBand7()
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        Assert.False(resolver.IsEligible("mul:2*8", 1));
        Assert.False(resolver.IsEligible("mul:2*8", 2));
        Assert.True(resolver.IsEligible("mul:2*8", 7));
    }

    [Fact]
    public void IsEligible_Addition_TensFact_IsIneligibleAtDenseBand0_EligibleAtStructuredBand()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var resolver = new AcquisitionOwnershipResolver(addition);

        Assert.False(resolver.IsEligible("add:10+20", 0));
        Assert.True(resolver.IsEligible("add:10+20", 10));
    }

    [Fact]
    public void IsEligible_Subtraction_InverseFact_IsIneligibleAtDenseBand0_EligibleAtInverseBand()
    {
        var subtraction = new ArithmeticCurriculum().Subtraction;
        var resolver = new AcquisitionOwnershipResolver(subtraction);

        Assert.False(resolver.IsEligible("sub:20-10", 0));
        Assert.True(resolver.IsEligible("sub:20-10", 19));
    }

    [Fact]
    public void IsEligible_Division_LargeDivisor_IsIneligibleAtBand1_EligibleAtDivBand()
    {
        var division = new ArithmeticCurriculum().Division;
        var resolver = new AcquisitionOwnershipResolver(division);

        Assert.False(resolver.IsEligible("div:64/8", 1));
        Assert.True(resolver.IsEligible("div:64/8", 7));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("mul:")]
    [InlineData("mul:2*")]
    [InlineData("xyz:1+1")]
    public void IsEligible_MalformedOrWhitespaceFactId_FailsClosedReturningFalse(string? malformedFactId)
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        Assert.False(resolver.IsEligible(malformedFactId!, 7));
    }

    [Fact]
    public void IsEligible_CrossOperationFactId_FailsClosedReturningFalse()
    {
        var curriculum = new ArithmeticCurriculum();
        var additionResolver = new AcquisitionOwnershipResolver(curriculum.Addition);
        var multiplicationResolver = new AcquisitionOwnershipResolver(curriculum.Multiplication);

        Assert.False(additionResolver.IsEligible("mul:2*8", 10));
        Assert.False(additionResolver.IsEligible("sub:5-2", 10));
        Assert.False(additionResolver.IsEligible("div:4/2", 10));

        Assert.False(multiplicationResolver.IsEligible("add:2+8", 10));
        Assert.False(multiplicationResolver.IsEligible("sub:8-2", 10));
        Assert.False(multiplicationResolver.IsEligible("div:8/2", 10));
    }

    [Theory]
    [InlineData("div:5/0")]
    [InlineData("div:0/0")]
    [InlineData("add:-1+2")]
    [InlineData("sub:2-5")]
    [InlineData("mul:99999*99999")]
    [InlineData("div:10/3")]
    public void IsEligible_CurriculumInvalidOrDivisionByZeroFactId_FailsClosedReturningFalse(string invalidFactId)
    {
        var curriculum = new ArithmeticCurriculum();
        var divisionResolver = new AcquisitionOwnershipResolver(curriculum.Division);
        var additionResolver = new ArithmeticCurriculum().Addition;
        var additionOwnership = new AcquisitionOwnershipResolver(additionResolver);
        var subtractionResolver = new AcquisitionOwnershipResolver(curriculum.Subtraction);
        var multiplicationResolver = new AcquisitionOwnershipResolver(curriculum.Multiplication);

        Assert.False(divisionResolver.IsEligible(invalidFactId, 10));
        Assert.False(additionOwnership.IsEligible(invalidFactId, 10));
        Assert.False(subtractionResolver.IsEligible(invalidFactId, 10));
        Assert.False(multiplicationResolver.IsEligible(invalidFactId, 10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(33)]
    [InlineData(1000)]
    public void IsEligible_NegativeOrBeyondCurriculumBandIndex_FailsClosedReturningFalse(int invalidBandIndex)
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        Assert.False(resolver.IsEligible("mul:2*2", invalidBandIndex));
    }
}
