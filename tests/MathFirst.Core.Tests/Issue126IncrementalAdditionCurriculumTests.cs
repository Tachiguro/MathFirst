namespace MathFirst.Core.Tests;

using System.Security.Cryptography;
using System.Text;
using MathFirst.Application.Progression;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

/// <summary>
/// Issue #126: the Addition operand frontier grows one integer at a time (<c>0..1</c>, <c>0..2</c>, ..., <c>0..10</c>,
/// <c>0..11</c>, ...). Band <c>B</c> owns exactly the ordered pairs with <c>max(a, b) = B + 1</c>; there is no structured
/// tens/hundreds magnitude jump after <c>ADD-D10</c>, and the other three operation curricula are unchanged.
/// </summary>
public sealed class Issue126IncrementalAdditionCurriculumTests
{
    // Baseline digests of the complete pre-#126 curricula on main@9952624c8d55b0e5c9898c7eea7711e0cf27311a, produced by
    // the same serialization as SerializeBands. Equality proves the change left them bit-for-bit identical.
    private const string BaselineSubtractionDigest = "F8973E19FFF37C2EF37053C56CBF2DD8AAD863C6BA979175F50093B31F5A15B6";
    private const string BaselineMultiplicationDigest = "1F3C3146979D0FEF829908A4DE2F7724305F0097952BC6226310A277F87894FD";
    private const string BaselineDivisionDigest = "22709D559C62BB4F93BF0D2AB3A562049B78A2CEE08A6286B9DFA4849A1F15C3";
    private const string BaselineAdditionD01ToD10Digest = "3EBC5D7C3E30CBB3D13E82ED8E5B2555ADBE02C4174A4EBEE2054DE76CFA040D";

    [Fact]
    public void N1AndN2_IncludeZeroAndEveryOrderedPair()
    {
        var addition = new ArithmeticCurriculum().Addition;

        Assert.Equal(
            new[] { "add:0+0", "add:0+1", "add:1+0", "add:1+1" },
            GetBand(addition, 0).Frontier.Select(fact => fact.Id));
        Assert.Equal(
            new[] { "add:0+2", "add:1+2", "add:2+0", "add:2+1", "add:2+2" },
            GetBand(addition, 1).Frontier.Select(fact => fact.Id));
        Assert.Equal(
            IncrementalAdditionCurriculum.CreateFrontier(0).Select(fact => fact.Id),
            GetBand(addition, 0).Frontier.Select(fact => fact.Id));
        Assert.Equal(
            IncrementalAdditionCurriculum.CreateFrontier(1).Select(fact => fact.Id),
            GetBand(addition, 1).Frontier.Select(fact => fact.Id));
    }

    [Fact]
    public void D01ToD10_KeepTheirFactsIdentitiesOrderAndThe121MasteryFacts()
    {
        var addition = new ArithmeticCurriculum().Addition;

        Assert.Equal(BaselineAdditionD01ToD10Digest, SerializeBands(addition, 9));
        for (var bandIndex = 0; bandIndex < 10; bandIndex++)
        {
            Assert.Equal(
                addition.Bands[bandIndex].Frontier.Select(fact => fact.Id),
                IncrementalAdditionCurriculum.CreateFrontier(bandIndex).Select(fact => fact.Id));
            Assert.Equal(IncrementalAdditionCurriculum.CreateBandId(bandIndex), addition.Bands[bandIndex].Id);
        }

        var masteryFacts = CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Addition);
        Assert.Equal(121, masteryFacts.Count);
        Assert.Equal(SquareIds(10).Order(StringComparer.Ordinal), masteryFacts.Order(StringComparer.Ordinal));
        Assert.Equal(10, CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);
        Assert.Equal(new[] { "add:10+10" }, CurriculumUnlockPolicy.GetRequiredBoundaryFactIds(CurriculumStage.Stage1_Addition));
    }

    [Theory]
    [InlineData(10, 11, 23)]
    [InlineData(11, 12, 25)]
    [InlineData(12, 13, 27)]
    [InlineData(13, 14, 29)]
    [InlineData(99, 100, 201)]
    [InlineData(100, 101, 203)]
    [InlineData(999, 1000, 2001)]
    [InlineData(1000, 1001, 2003)]
    public void BandAfterD10_IsTheExactMaxOperandRing(int bandIndex, int maximumOperand, int expectedCount)
    {
        var band = GetBand(new ArithmeticCurriculum().Addition, bandIndex);

        Assert.Equal(FormattableString.Invariant($"ADD-D{maximumOperand:00}"), band.Id.Value);
        Assert.Equal(CurriculumBandKind.Dense, band.Kind);
        Assert.Equal(bandIndex, band.BandIndex);
        Assert.Equal(expectedCount, band.Frontier.Count);
        Assert.Equal(expectedCount, IncrementalAdditionCurriculum.GetFrontierSize(bandIndex));

        var expected = new List<string>();
        for (var left = 0; left <= maximumOperand; left++)
        {
            for (var right = 0; right <= maximumOperand; right++)
            {
                if (Math.Max(left, right) == maximumOperand)
                {
                    expected.Add($"add:{left}+{right}");
                }
            }
        }

        Assert.Equal(expected, band.Frontier.Select(fact => fact.Id));
        Assert.Equal(expectedCount, band.Frontier.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(band.Frontier, fact =>
        {
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
            Assert.Equal(maximumOperand, Math.Max(fact.LeftOperand, fact.RightOperand));
            Assert.Equal(fact.LeftOperand + fact.RightOperand, fact.CorrectResult);
        });
        Assert.Contains(band.Frontier, fact => fact.LeftOperand == 0 && fact.RightOperand == maximumOperand);
        Assert.Contains(band.Frontier, fact => fact.LeftOperand == maximumOperand && fact.RightOperand == 0);
        Assert.Contains(band.Frontier, fact => fact.LeftOperand == maximumOperand && fact.RightOperand == maximumOperand);
    }

    [Fact]
    public void D10ToD13_IntroduceElevenThenTwelveThenThirteenWithoutAnyTensAnchorJump()
    {
        var addition = new ArithmeticCurriculum().Addition;

        Assert.Equal("ADD-D10", GetBand(addition, 9).Id.Value);
        Assert.Equal("ADD-D11", GetBand(addition, 10).Id.Value);
        Assert.Equal("ADD-D12", GetBand(addition, 11).Id.Value);
        Assert.Equal("ADD-D13", GetBand(addition, 12).Id.Value);
        Assert.Contains(GetBand(addition, 10).Frontier, fact => fact.Id == "add:11+11");
        Assert.Contains(GetBand(addition, 10).Frontier, fact => fact.Id == "add:9+11");

        // The rejected pre-#126 ADD-P1-ANCHOR band started with 10+20, 20+10, 10+30 ... 90+90.
        // Under incremental growth each of them first appears only when its larger operand becomes the frontier.
        var throughD19 = Enumerable.Range(0, 19).SelectMany(index => GetBand(addition, index).Frontier).ToArray();
        Assert.DoesNotContain(throughD19, fact => fact.Id is "add:10+20" or "add:20+10" or "add:10+30" or "add:20+80" or "add:60+60" or "add:90+90");
        Assert.Contains(GetBand(addition, 19).Frontier, fact => fact.Id == "add:10+20");
        Assert.Contains(GetBand(addition, 59).Frontier, fact => fact.Id == "add:60+60");
        Assert.Contains(GetBand(addition, 79).Frontier, fact => fact.Id == "add:20+80");
        Assert.All(Enumerable.Range(0, 200), bandIndex =>
            Assert.Equal(bandIndex + 1, GetBand(addition, bandIndex).Frontier.Max(fact => Math.Max(fact.LeftOperand, fact.RightOperand))));
        Assert.DoesNotContain(
            Enumerable.Range(0, 200).Select(index => GetBand(addition, index).Id.Value),
            id => id.Contains("-P", StringComparison.Ordinal));
    }

    [Fact]
    public void CumulativePrefix_IsExactlyTheCompleteSquareWithUniqueCanonicalIds()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var cumulative = new HashSet<string>(StringComparer.Ordinal);

        for (var bandIndex = 0; bandIndex <= 120; bandIndex++)
        {
            foreach (var fact in GetBand(addition, bandIndex).Frontier)
            {
                Assert.True(cumulative.Add(fact.Id), $"{fact.Id} is owned by more than one band.");
                Assert.Equal(new ArithmeticFact(ArithmeticOperation.Addition, fact.LeftOperand, fact.RightOperand).Id, fact.Id);
            }

            var maximumOperand = bandIndex + 1;
            Assert.Equal((maximumOperand + 1) * (maximumOperand + 1), cumulative.Count);
            if (bandIndex is 0 or 1 or 9 or 10 or 11 or 12 or 120)
            {
                Assert.True(cumulative.SetEquals(SquareIds(maximumOperand)));
            }
        }
    }

    [Theory]
    [InlineData(80)]
    public void AnalyticOwnership_MatchesTheMaterializedPrefixScanOracle(int lastBandIndex)
    {
        var addition = new ArithmeticCurriculum().Addition;
        var resolver = new AcquisitionOwnershipResolver(addition);
        var oracle = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var bandIndex = 0; bandIndex <= lastBandIndex; bandIndex++)
        {
            var band = GetBand(addition, bandIndex);
            var expectedOwned = band.Frontier.Where(fact => oracle.TryAdd(fact.Id, bandIndex)).Select(fact => fact.Id).ToArray();

            Assert.Equal(expectedOwned, resolver.GetOwnedFrontier(bandIndex).Select(fact => fact.Id));
            foreach (var fact in band.Frontier)
            {
                Assert.True(resolver.TryGetOwner(fact.Id, bandIndex, out var owner));
                Assert.Equal(oracle[fact.Id], owner);
                Assert.True(resolver.IsEligible(fact.Id, bandIndex));
                Assert.True(resolver.IsEligible(fact.Id, lastBandIndex));
                if (bandIndex > 0)
                {
                    Assert.False(resolver.IsEligible(fact.Id, bandIndex - 1));
                    Assert.False(resolver.TryGetOwner(fact.Id, bandIndex - 1, out _));
                }
            }
        }
    }

    [Theory]
    [InlineData("add:01+2")]
    [InlineData("add:1+02")]
    [InlineData("add:+1+2")]
    [InlineData("add:1+2 ")]
    [InlineData("add: 1+2")]
    [InlineData("add:1+")]
    [InlineData("add:+1")]
    [InlineData("add:1-2")]
    [InlineData("add:-1+2")]
    [InlineData("ADD:1+2")]
    [InlineData("sub:2-1")]
    [InlineData("mul:1*2")]
    [InlineData("add:46340+0")]
    [InlineData("add:0+99999999999")]
    public void AnalyticOwnership_RejectsNonCanonicalForeignAndUnsupportedIds(string factId)
    {
        Assert.False(IncrementalAdditionCurriculum.Instance.TryGetOwnerBandIndex(factId, out var owner));
        Assert.Equal(-1, owner);
        Assert.False(new AcquisitionOwnershipResolver(new ArithmeticCurriculum().Addition).IsEligible(factId, 1_000));
    }

    [Fact]
    public void LegacyPostD10AnchorFacts_AreReinterpretedByTheirOperandsAndAreNotEligibleEarly()
    {
        var resolver = new AcquisitionOwnershipResolver(new ArithmeticCurriculum().Addition);

        // Owner decision (Issue #126): a stored Addition BandIndex >= 10 now means N = BandIndex + 1.
        Assert.False(resolver.IsEligible("add:20+80", 10));
        Assert.False(resolver.IsEligible("add:60+60", 58));
        Assert.True(resolver.IsEligible("add:60+60", 59));
        Assert.True(resolver.TryGetOwner("add:20+80", 79, out var owner));
        Assert.Equal(79, owner);
        Assert.True(resolver.IsEligible("add:10+10", 10));
        Assert.True(resolver.IsEligible("add:4+1", 10));
    }

    [Fact]
    public void GuidedCeiling_IsClosedFormAndMatchesTheCompletePrefixScan()
    {
        var addition = new ArithmeticCurriculum().Addition;
        var scanCeiling = 0;
        for (var bandIndex = 0; bandIndex <= 150; bandIndex++)
        {
            foreach (var fact in GetBand(addition, bandIndex).Frontier)
            {
                scanCeiling = Math.Max(scanCeiling, Math.Max(fact.CorrectResult, Math.Max(fact.LeftOperand, fact.RightOperand)));
            }

            Assert.Equal(scanCeiling, GuidedNumberSpaceGate.ForGuided(addition, bandIndex).AdditionCeiling);
        }

        Assert.Equal(20, GuidedNumberSpaceGate.ForGuided(addition, 9).AdditionCeiling);
        Assert.Equal(22, GuidedNumberSpaceGate.ForGuided(addition, 10).AdditionCeiling);
    }

    [Fact]
    public void AnalyticLookups_GenerateOnlyTheRequestedBandInsteadOfThePrefix()
    {
        var calls = new List<int>();
        var addition = new OperationCurriculum(
            ArithmeticOperation.Addition,
            Enumerable.Range(0, 10).Select(index => IncrementalAdditionCurriculum.TryCreateBand(index)!).ToArray(),
            bandIndex =>
            {
                calls.Add(bandIndex);
                return CurriculumGenerators.TryCreateAdditionBand(bandIndex);
            },
            IncrementalAdditionCurriculum.Instance);

        const int highBandIndex = 5_000;
        var resolver = new AcquisitionOwnershipResolver(addition);
        Assert.Equal(10_003, resolver.GetOwnedFrontier(highBandIndex).Count);
        for (var left = 0; left <= 6_000; left += 7)
        {
            resolver.IsEligible($"add:{left}+3", highBandIndex);
        }

        Assert.Equal(new[] { highBandIndex }, calls);

        calls.Clear();
        Assert.Equal(10_002, GuidedNumberSpaceGate.ForGuided(addition, highBandIndex).AdditionCeiling);
        Assert.Equal(new[] { highBandIndex }, calls);

        calls.Clear();
        Assert.True(new AcquisitionOwnershipResolver(addition).IsEligible("add:5001+0", highBandIndex));
        Assert.False(new AcquisitionOwnershipResolver(addition).IsEligible("add:5002+0", highBandIndex));
        Assert.Equal(new[] { highBandIndex }, calls);
    }

    [Fact]
    public void ResourceBound_IsExplicitOverflowSafeAndKeepsTheOperationInMaintenance()
    {
        Assert.Equal(46_339, IncrementalAdditionCurriculum.MaximumSupportedOperand);
        Assert.True((long)(IncrementalAdditionCurriculum.MaximumSupportedOperand + 1) * (IncrementalAdditionCurriculum.MaximumSupportedOperand + 1) <= int.MaxValue);
        Assert.True((long)(IncrementalAdditionCurriculum.MaximumSupportedOperand + 2) * (IncrementalAdditionCurriculum.MaximumSupportedOperand + 2) > int.MaxValue);
        Assert.Equal(IncrementalAdditionCurriculum.MaximumFrontierSize, DenseProgressionEvaluator.MaxDenseFrontierSize);

        var addition = new ArithmeticCurriculum().Addition;
        var last = GetBand(addition, IncrementalAdditionCurriculum.MaximumBandIndex);
        Assert.Equal("ADD-D46339", last.Id.Value);
        Assert.Equal(IncrementalAdditionCurriculum.MaximumFrontierSize, last.Frontier.Count);
        Assert.Equal(2 * IncrementalAdditionCurriculum.MaximumSupportedOperand, last.Frontier.Max(fact => fact.CorrectResult));
        Assert.False(addition.TryGetBand(IncrementalAdditionCurriculum.MaximumBandIndex + 1, out var beyond));
        Assert.Null(beyond);
        Assert.False(addition.TryGetBand(int.MaxValue, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => IncrementalAdditionCurriculum.CreateFrontier(IncrementalAdditionCurriculum.MaximumBandIndex + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IncrementalAdditionCurriculum.CreateFrontier(-1));

        // A completely correct learner on the last supported band stays there: no successor band, no magnitude jump.
        var progression = new OperationProgression(ArithmeticOperation.Addition, IncrementalAdditionCurriculum.MaximumBandIndex, 0);
        var attempts = last.Frontier.Select((fact, index) => new BandAttemptEvidence(index + 1, fact.Id, true, false, 9_000)).ToArray();
        var decision = DenseProgressionEvaluator.Evaluate(progression, addition, attempts);
        Assert.False(decision.Advances);
        Assert.Equal(progression, decision.ResultingProgression);
    }

    [Fact]
    public void ProceduralBandsMustKeepTheirCurriculumKind()
    {
        var foreignKind = new OperationCurriculum(
            ArithmeticOperation.Addition,
            [IncrementalAdditionCurriculum.TryCreateBand(0)!],
            bandIndex => new CurriculumBand(
                ArithmeticOperation.Addition,
                bandIndex,
                new CurriculumBandId("TEST-STRUCTURED"),
                CurriculumBandKind.Structured,
                IncrementalAdditionCurriculum.CreateFrontier(bandIndex)),
            IncrementalAdditionCurriculum.Instance);

        Assert.Throws<InvalidOperationException>(() => foreignKind.TryGetBand(1, out _));
        Assert.Throws<ArgumentException>(() => new OperationCurriculum(
            ArithmeticOperation.Addition,
            [new CurriculumBand(ArithmeticOperation.Addition, 0, new CurriculumBandId("TEST-WRONG"), CurriculumBandKind.Dense, [new ArithmeticFact(ArithmeticOperation.Addition, 2, 2)])],
            analyticModel: IncrementalAdditionCurriculum.Instance));
    }

    [Fact]
    public void OtherOperationCurricula_AreBitForBitUnchanged()
    {
        var curriculum = new ArithmeticCurriculum();

        Assert.Equal(BaselineSubtractionDigest, SerializeBands(curriculum.Subtraction, 135));
        Assert.Equal(BaselineMultiplicationDigest, SerializeBands(curriculum.Multiplication, 32));
        Assert.Equal(BaselineDivisionDigest, SerializeBands(curriculum.Division, 32));
        Assert.False(curriculum.Subtraction.TryGetBand(136, out _));
        Assert.False(curriculum.Multiplication.TryGetBand(33, out _));
        Assert.False(curriculum.Division.TryGetBand(33, out _));
        Assert.Null(curriculum.Subtraction.AnalyticModel);
        Assert.Null(curriculum.Multiplication.AnalyticModel);
        Assert.Null(curriculum.Division.AnalyticModel);
        Assert.Same(IncrementalAdditionCurriculum.Instance, curriculum.Addition.AnalyticModel);
    }

    [Theory]
    [InlineData(10, 23, 21)]
    [InlineData(11, 25, 23)]
    [InlineData(12, 27, 25)]
    [InlineData(99, 201, 181)]
    public void DenseAdvancement_RequiresFullCoverageAndTheEstablishedNinetyPercentButNoFluency(
        int bandIndex,
        int frontierSize,
        int requiredCorrect)
    {
        var addition = new ArithmeticCurriculum().Addition;
        var frontier = GetBand(addition, bandIndex).Frontier;
        var progression = new OperationProgression(ArithmeticOperation.Addition, bandIndex, 1_000);
        Assert.Equal(frontierSize, frontier.Count);

        BandAttemptEvidence[] Attempts(int count, int correct, long latencyMs) => frontier
            .Take(count)
            .Select((fact, index) => new BandAttemptEvidence(1_001 + index, fact.Id, index < correct, isFluent: false, latencyMs))
            .ToArray();

        // Slow, never-fluent but correct answers advance once every new fact has a current-band attempt.
        var slowComplete = DenseProgressionEvaluator.Evaluate(progression, addition, Attempts(frontierSize, frontierSize, 30_000));
        Assert.True(slowComplete.Advances);
        Assert.Equal(bandIndex + 1, slowComplete.ResultingProgression.BandIndex);
        Assert.Equal(1_000 + frontierSize, slowComplete.ResultingProgression.BandStartedPracticePosition);

        Assert.False(DenseProgressionEvaluator.Evaluate(progression, addition, Attempts(frontierSize - 1, frontierSize - 1, 800)).Advances);
        Assert.True(DenseProgressionEvaluator.Evaluate(progression, addition, Attempts(frontierSize, requiredCorrect, 800)).Advances);
        Assert.False(DenseProgressionEvaluator.Evaluate(progression, addition, Attempts(frontierSize, requiredCorrect - 1, 800)).Advances);

        // Attempts before the band started never count as coverage for the new frontier.
        var stale = frontier.Select((fact, index) => new BandAttemptEvidence(1 + index, fact.Id, true, true, 800)).ToArray();
        Assert.False(DenseProgressionEvaluator.Evaluate(progression, addition, stale).Advances);
    }

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band), $"Band {bandIndex} must exist.");
        return band!;
    }

    private static HashSet<string> SquareIds(int maximumOperand)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var left = 0; left <= maximumOperand; left++)
        {
            for (var right = 0; right <= maximumOperand; right++)
            {
                ids.Add($"add:{left}+{right}");
            }
        }

        return ids;
    }

    private static string SerializeBands(OperationCurriculum curriculum, int lastBandIndex)
    {
        var builder = new StringBuilder();
        for (var bandIndex = 0; bandIndex <= lastBandIndex; bandIndex++)
        {
            if (!curriculum.TryGetBand(bandIndex, out var band))
            {
                builder.Append("<none>\n");
                continue;
            }

            builder.Append(band!.BandIndex).Append('|').Append(band.Id.Value).Append('|').Append(band.Kind).Append('|');
            builder.Append(string.Join(",", band.Frontier.Select(fact => fact.Id))).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
