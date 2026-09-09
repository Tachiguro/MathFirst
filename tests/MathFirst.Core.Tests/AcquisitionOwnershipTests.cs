namespace MathFirst.Core.Tests;

using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class AcquisitionOwnershipTests
{
    [Fact]
    public void AdditionOwnership_HasExactExpectedRawAndOwnedCounts()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var resolver = new AcquisitionOwnershipResolver(addition);

        AssertCount(addition, resolver, 10, 81, 80);
        AssertCount(addition, resolver, 11, 162, 162);
        AssertCount(addition, resolver, 12, 162, 162);
        AssertCount(addition, resolver, 13, 90, 90);
        AssertCount(addition, resolver, 14, 81, 81);
        AssertCount(addition, resolver, 15, 162, 162);
        AssertCount(addition, resolver, 16, 162, 162);
        AssertCount(addition, resolver, 17, 90, 90);
        AssertCount(addition, resolver, 18, 162, 162);
        AssertCount(addition, resolver, 19, 162, 162);
        AssertCount(addition, resolver, 20, 90, 90);

        Assert.Equal(495, Enumerable.Range(10, 4).Sum(index => GetBand(addition, index).Frontier.Count));
        Assert.Equal(494, Enumerable.Range(10, 4).Sum(index => resolver.GetOwnedFrontier(index).Count));
        Assert.Equal(909, Enumerable.Range(14, 7).Sum(index => GetBand(addition, index).Frontier.Count));
        Assert.Equal(909, Enumerable.Range(14, 7).Sum(index => resolver.GetOwnedFrontier(index).Count));
    }

    [Fact]
    public void EarlierDenseFact_RemainsOwnerAndPresentationDirectionsStayIndependent()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var resolver = new AcquisitionOwnershipResolver(addition);

        Assert.True(resolver.TryGetOwner("add:10+10", 10, out var denseOwner));
        Assert.Equal(9, denseOwner);
        Assert.DoesNotContain(resolver.GetOwnedFrontier(10), fact => fact.Id == "add:10+10");

        Assert.True(resolver.TryGetOwner("add:10+20", 10, out var forwardOwner));
        Assert.True(resolver.TryGetOwner("add:20+10", 10, out var reverseOwner));
        Assert.Equal(10, forwardOwner);
        Assert.Equal(10, reverseOwner);
        Assert.Contains(resolver.GetOwnedFrontier(10), fact => fact.Id == "add:10+20");
        Assert.Contains(resolver.GetOwnedFrontier(10), fact => fact.Id == "add:20+10");
    }

    [Fact]
    public void MultiplicationOwnership_HasExactExpectedRawAndOwnedCounts()
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        AssertCount(multiplication, resolver, 12, 144, 144);
        AssertCount(multiplication, resolver, 13, 144, 128);
        AssertCount(multiplication, resolver, 14, 174, 174);
        AssertCount(multiplication, resolver, 15, 144, 144);
        AssertCount(multiplication, resolver, 16, 144, 144);
        AssertCount(multiplication, resolver, 17, 198, 182);
        AssertCount(multiplication, resolver, 18, 144, 144);
        AssertCount(multiplication, resolver, 19, 144, 144);
        AssertCount(multiplication, resolver, 20, 198, 182);
        AssertCount(multiplication, resolver, 21, 144, 144);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(18)]
    [InlineData(21)]
    [InlineData(24)]
    [InlineData(27)]
    [InlineData(30)]
    public void EveryCompleteSafeScaledBand_HasOneHundredFortyFourOwnedFacts(int bandIndex)
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        AssertCount(multiplication, resolver, bandIndex, 144, 144);
    }

    [Fact]
    public void InverseOperationOwnership_IsIndependentAndUnique()
    {
        var curriculum = new ArithmeticCurriculum();
        var subtractionResolver = new AcquisitionOwnershipResolver(curriculum.Subtraction);
        var divisionResolver = new AcquisitionOwnershipResolver(curriculum.Division);

        AssertCount(curriculum.Subtraction, subtractionResolver, 20, 81, 80);
        AssertCount(curriculum.Subtraction, subtractionResolver, 21, 162, 162);
        AssertCount(curriculum.Subtraction, subtractionResolver, 24, 81, 81);
        AssertCount(curriculum.Division, divisionResolver, 12, 144, 144);
        AssertCount(curriculum.Division, divisionResolver, 13, 144, 128);
        AssertCount(curriculum.Division, divisionResolver, 14, 174, 174);
        AssertCount(curriculum.Division, divisionResolver, 16, 144, 144);
        AssertCount(curriculum.Division, divisionResolver, 17, 198, 182);

        Assert.True(subtractionResolver.TryGetOwner("sub:20-10", 20, out var subtractionDenseOwner));
        Assert.Equal(19, subtractionDenseOwner);
        Assert.True(divisionResolver.TryGetOwner("div:20/10", 13, out var divisionDenseOwner));
        Assert.True(divisionDenseOwner < 12);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 30)]
    [InlineData(ArithmeticOperation.Subtraction, 40)]
    [InlineData(ArithmeticOperation.Multiplication, 21)]
    [InlineData(ArithmeticOperation.Division, 21)]
    public void Resolver_ExactlyMatchesIndependentBoundedPrefixScanOracle(
        ArithmeticOperation operation,
        int lastBandIndex)
    {
        var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
        var resolver = new AcquisitionOwnershipResolver(curriculum);
        var oracleOwners = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var bandIndex = 0; bandIndex <= lastBandIndex; bandIndex++)
        {
            var band = GetBand(curriculum, bandIndex);
            var expectedOwnedIds = new List<string>();
            foreach (var fact in band.Frontier)
            {
                if (oracleOwners.TryAdd(fact.Id, bandIndex))
                {
                    expectedOwnedIds.Add(fact.Id);
                }
            }

            var actualOwned = resolver.GetOwnedFrontier(bandIndex);
            Assert.Equal(expectedOwnedIds, actualOwned.Select(fact => fact.Id));
            foreach (var fact in band.Frontier)
            {
                Assert.True(resolver.TryGetOwner(fact.Id, bandIndex, out var actualOwner));
                Assert.Equal(oracleOwners[fact.Id], actualOwner);
            }
        }
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 30)]
    [InlineData(ArithmeticOperation.Subtraction, 40)]
    [InlineData(ArithmeticOperation.Multiplication, 21)]
    [InlineData(ArithmeticOperation.Division, 21)]
    public void FreshResolvers_AreRestartEquivalent(ArithmeticOperation operation, int lastBandIndex)
    {
        var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
        var first = new AcquisitionOwnershipResolver(curriculum);
        var second = new AcquisitionOwnershipResolver(curriculum);

        for (var bandIndex = 0; bandIndex <= lastBandIndex; bandIndex++)
        {
            Assert.Equal(
                first.GetOwnedFrontier(bandIndex).Select(fact => fact.Id),
                second.GetOwnedFrontier(bandIndex).Select(fact => fact.Id));
        }
    }

    [Fact]
    public void OwnershipLookup_FailsWithoutExposingAnUnsafeOrPartialBand()
    {
        var multiplication = new ArithmeticCurriculum().Multiplication;
        var resolver = new AcquisitionOwnershipResolver(multiplication);

        Assert.True(resolver.TryGetOwnedFrontier(32, out var lastSafe));
        Assert.NotNull(lastSafe);
        Assert.False(resolver.TryGetOwnedFrontier(33, out var unsafeFrontier));
        Assert.Null(unsafeFrontier);
        Assert.False(resolver.TryGetOwnedFrontier(33, out _));
    }

    private static void AssertCount(
        OperationCurriculum curriculum,
        AcquisitionOwnershipResolver resolver,
        int bandIndex,
        int expectedRaw,
        int expectedOwned)
    {
        Assert.Equal(expectedRaw, GetBand(curriculum, bandIndex).Frontier.Count);
        Assert.Equal(expectedOwned, resolver.GetOwnedFrontier(bandIndex).Count);
    }

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        return band!;
    }
}
