namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class DeterministicFactRankerTests
{
    [Fact]
    public void DigestVectors_FixUtf8NulDomainsAndEncoding()
    {
        var selection = DeterministicFactRanker.ComputeSelectionDigest(
            ArithmeticOperation.Addition,
            new CurriculumBandId("ADD-P1-T0"),
            FactSelectionRole.New,
            42,
            "add:11+1");
        var sample = DeterministicFactRanker.ComputeStructuredSampleDigest(
            ArithmeticOperation.Multiplication,
            new CurriculumBandId("MUL-2D"),
            "mul:71*2");

        Assert.Equal(
            "43df63e5561b02b092011e030e746fb1aa6ca9c054ecd76880e64c3f6e1ba472",
            Convert.ToHexString(selection).ToLowerInvariant());
        Assert.Equal(
            "258b07ff32aed83ba67d7a2da90bbbf95dd83f14c07978a3e3f5293e7763ae01",
            Convert.ToHexString(sample).ToLowerInvariant());
    }

    [Fact]
    public void SelectionDigest_ChangesForEveryCanonicalInputDimension()
    {
        var band = new CurriculumBandId("ADD-P1-T0");
        var baseline = Hex(DeterministicFactRanker.ComputeSelectionDigest(
            ArithmeticOperation.Addition,
            band,
            FactSelectionRole.New,
            42,
            "add:11+1"));

        var variants = new[]
        {
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Subtraction, band, FactSelectionRole.New, 42, "add:11+1")),
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Addition, new CurriculumBandId("ADD-P1-R0"), FactSelectionRole.New, 42, "add:11+1")),
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Addition, band, FactSelectionRole.Due, 42, "add:11+1")),
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Addition, band, FactSelectionRole.New, 43, "add:11+1")),
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Addition, band, FactSelectionRole.New, 42, "add:1+11")),
            Hex(DeterministicFactRanker.ComputeSelectionDigest(ArithmeticOperation.Addition, null, FactSelectionRole.New, 42, "add:11+1"))
        };

        Assert.DoesNotContain(baseline, variants);
        Assert.Equal(variants.Length, variants.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RankComparison_UsesBigEndianThenRemainingBytesThenOrdinalFactId()
    {
        var highBigEndian = new byte[32];
        var lowBigEndian = new byte[32];
        highBigEndian[0] = 1;
        lowBigEndian[1] = byte.MaxValue;
        Assert.True(DeterministicFactRanker.CompareRankKeys(
            highBigEndian, "add:1+1", lowBigEndian, "add:1+1") > 0);

        var lowerRemainder = new byte[32];
        var higherRemainder = new byte[32];
        lowerRemainder[8] = 1;
        higherRemainder[8] = 2;
        Assert.True(DeterministicFactRanker.CompareRankKeys(
            lowerRemainder, "add:1+1", higherRemainder, "add:1+1") < 0);

        var tiedDigest = new byte[32];
        Assert.True(DeterministicFactRanker.CompareRankKeys(
            tiedDigest, "add:10+1", tiedDigest, "add:2+1") < 0);
    }

    [Fact]
    public void GeneralRanking_IsRestartEquivalentAndIndependentOfInputOrder()
    {
        var facts = Enumerable.Range(1, 30)
            .Select(value => new ArithmeticFact(ArithmeticOperation.Addition, value, 1))
            .ToArray();
        var reverse = facts.Reverse().ToArray();
        var band = new CurriculumBandId("ADD-P1-T0");

        var first = DeterministicFactRanker.Order(
            facts,
            ArithmeticOperation.Addition,
            band,
            FactSelectionRole.Frontier,
            1234);
        var second = DeterministicFactRanker.Order(
            reverse,
            ArithmeticOperation.Addition,
            band,
            FactSelectionRole.Frontier,
            1234);
        var restart = DeterministicFactRanker.Order(
            facts,
            ArithmeticOperation.Addition,
            band,
            FactSelectionRole.Frontier,
            1234);

        Assert.Equal(first.Select(fact => fact.Id), second.Select(fact => fact.Id));
        Assert.Equal(first.Select(fact => fact.Id), restart.Select(fact => fact.Id));
    }

    [Fact]
    public void GeneralRanking_HasAFixedKnownVector()
    {
        var facts = Enumerable.Range(1, 5)
            .Select(value => new ArithmeticFact(ArithmeticOperation.Addition, value, 1));

        var ranked = DeterministicFactRanker.Order(
            facts,
            ArithmeticOperation.Addition,
            new CurriculumBandId("ADD-P1-T0"),
            FactSelectionRole.Frontier,
            1234);

        Assert.Equal(
            new[] { "add:1+1", "add:2+1", "add:4+1", "add:5+1", "add:3+1" },
            ranked.Select(fact => fact.Id));
    }

    [Fact]
    public void StructuredSample_SelectsExactlySixteenDistinctOwnedFactsDeterministically()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var owned = new AcquisitionOwnershipResolver(addition).GetOwnedFrontier(11);
        var band = GetBand(addition, 11);

        var first = DeterministicFactRanker.SelectStructuredSample(
            owned,
            ArithmeticOperation.Addition,
            band.Id);
        var reverse = DeterministicFactRanker.SelectStructuredSample(
            owned.Reverse(),
            ArithmeticOperation.Addition,
            band.Id);
        var restart = DeterministicFactRanker.SelectStructuredSample(
            new AcquisitionOwnershipResolver(new ArithmeticCurriculum().Addition).GetOwnedFrontier(11),
            ArithmeticOperation.Addition,
            band.Id);

        Assert.Equal(16, first.Count);
        Assert.Equal(16, first.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(first.Select(fact => fact.Id), reverse.Select(fact => fact.Id));
        Assert.Equal(first.Select(fact => fact.Id), restart.Select(fact => fact.Id));
    }

    [Fact]
    public void StructuredSample_UsesMinimumOfSixteenAndDistinctFrontierSize()
    {
        var facts = Enumerable.Range(1, 5)
            .Select(value => new ArithmeticFact(ArithmeticOperation.Multiplication, value, 2))
            .ToArray();
        var withDuplicates = facts.Concat(facts.Reverse());

        var sample = DeterministicFactRanker.SelectStructuredSample(
            withDuplicates,
            ArithmeticOperation.Multiplication,
            new CurriculumBandId("MUL-2D"));

        Assert.Equal(5, sample.Count);
        Assert.Equal(5, sample.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RankingRejectsInvalidStateInsteadOfUsingProcessSpecificFallbacks()
    {
        var addition = new[] { new ArithmeticFact(ArithmeticOperation.Addition, 1, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicFactRanker.Order(
            addition,
            ArithmeticOperation.Addition,
            null,
            FactSelectionRole.Any,
            -1));
        Assert.Throws<ArgumentException>(() => DeterministicFactRanker.Order(
            addition,
            ArithmeticOperation.Subtraction,
            null,
            FactSelectionRole.Any,
            0));
    }

    private static string Hex(byte[] digest) => Convert.ToHexString(digest);

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        return band!;
    }
}
