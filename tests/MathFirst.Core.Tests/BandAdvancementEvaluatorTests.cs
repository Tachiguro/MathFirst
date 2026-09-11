namespace MathFirst.Core.Tests;

using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class BandAdvancementEvaluatorTests
{
    private readonly BandAdvancementEvaluator _evaluator = new();

    [Fact]
    public void OperationProgression_IsImmutableAndValidatesItsOperationAndPositions()
    {
        var progression = new OperationProgression(ArithmeticOperation.Addition, 2, 123);

        Assert.Equal(ArithmeticOperation.Addition, progression.Operation);
        Assert.Equal(2, progression.BandIndex);
        Assert.Equal(123, progression.BandStartedPracticePosition);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OperationProgression((ArithmeticOperation)999, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OperationProgression(ArithmeticOperation.Addition, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OperationProgression(ArithmeticOperation.Addition, 0, -1));
    }

    [Fact]
    public void AttemptEvidence_RequiresPositivePositionAndNonNegativeLatency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BandAttemptEvidence(0, "add:1+1", true, true, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BandAttemptEvidence(1, "add:1+1", true, true, -1));
    }

    [Fact]
    public void StructuredBand_FullyQualifyingFortyAttemptLearner_Advances()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 10);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.True(decision.Advances);
        Assert.Equal(11, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void StructuredBand_CorrectnessThreshold_DistinguishesThirtyEightFromThirtySeven()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, correctCount: 38, fluentCount: 34);
        var failing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, correctCount: 37, fluentCount: 34);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_Advancement_CreatesExactlyTheNextIndependentStateAtTheTriggeringPosition()
    {
        var testCase = CreateCase(ArithmeticOperation.Subtraction, bandIndex: 10, bandStart: 400);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.Equal(11, decision.ResultingProgression.BandIndex);
        Assert.Equal(440, decision.ResultingProgression.BandStartedPracticePosition);
        Assert.Equal(testCase.Progression.Operation, decision.ResultingProgression.Operation);
        Assert.Equal(10, testCase.Progression.BandIndex);
        Assert.Equal(400, testCase.Progression.BandStartedPracticePosition);
    }

    [Fact]
    public void StructuredBand_FewerThanFortyPostStartAttempts_DoNotAdvance()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, attemptCount: 39);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.False(decision.Advances);
        Assert.Same(testCase.Progression, decision.ResultingProgression);
    }

    [Fact]
    public void StructuredBand_AttemptsAtBandStartAreExcludedAndAttemptsAfterBandStartAreIncluded()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, bandStart: 100);
        var shiftedAttempts = passing.Evidence.AcceptedAttempts
            .Select(attempt => new BandAttemptEvidence(
                attempt.PracticePosition - 1,
                attempt.FactId,
                attempt.IsCorrect,
                attempt.IsFluent,
                attempt.ResponseLatencyMs));
        var equalityIncluded = new BandAdvancementEvidence(
            shiftedAttempts,
            passing.Evidence.LifetimeAttemptedFactIds,
            passing.Evidence.CurrentBandIntroducedFactIds);

        Assert.False(_evaluator.Evaluate(passing.Progression, passing.Curriculum, equalityIncluded).Advances);
        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_MoreThanFortyAttempts_UseOnlyTheLatestFortyByPracticePosition()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10);
        var oldFailures = Enumerable.Range(101, 10)
            .Select(position => new BandAttemptEvidence(position, "add:999+999", false, false, 5000));
        var shiftedPassing = ShiftAttempts(passing.Evidence.AcceptedAttempts, 10);
        var withOldFailures = ReplaceAttempts(passing, oldFailures.Concat(shiftedPassing));

        Assert.True(_evaluator.Evaluate(
            withOldFailures.Progression,
            withOldFailures.Curriculum,
            withOldFailures.Evidence).Advances);

        var failing = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 10,
            correctCount: 37,
            fluentCount: 34);
        var oldSuccesses = Enumerable.Range(101, 10)
            .Select(position => new BandAttemptEvidence(position, "add:10+10", true, true, 1000));
        var shiftedFailing = ShiftAttempts(failing.Evidence.AcceptedAttempts, 10);
        var withOldSuccesses = ReplaceAttempts(failing, oldSuccesses.Concat(shiftedFailing));

        Assert.False(_evaluator.Evaluate(
            withOldSuccesses.Progression,
            withOldSuccesses.Curriculum,
            withOldSuccesses.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_UnorderedInput_IsNormalizedByPracticePosition()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 10);
        var reversed = ReplaceAttempts(testCase, testCase.Evidence.AcceptedAttempts.Reverse());

        Assert.True(_evaluator.Evaluate(reversed.Progression, reversed.Curriculum, reversed.Evidence).Advances);
        Assert.Equal(140, _evaluator.Evaluate(
            reversed.Progression,
            reversed.Curriculum,
            reversed.Evidence).ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void DuplicatePositivePracticePositions_AreRejected()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 10);
        var duplicate = testCase.Evidence.AcceptedAttempts
            .Append(new BandAttemptEvidence(140, "add:999+999", true, true, 1000));
        var invalid = ReplaceAttempts(testCase, duplicate);

        Assert.Throws<ArgumentException>(() =>
            _evaluator.Evaluate(invalid.Progression, invalid.Curriculum, invalid.Evidence));
    }

    [Fact]
    public void StructuredBand_FluencyThreshold_DistinguishesThirtyFourFromThirtyThree()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, fluentCount: 34);
        var failing = CreateCase(ArithmeticOperation.Addition, bandIndex: 10, fluentCount: 33);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_SlowCorrectAnswers_CountAsCorrectButNotFluent()
    {
        var passing = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 10,
            correctCount: 38,
            fluentCount: 34);
        var tooSlow = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 10,
            correctCount: 38,
            fluentCount: 33);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(tooSlow.Progression, tooSlow.Curriculum, tooSlow.Evidence).Advances);
        Assert.Equal(38, tooSlow.Evidence.AcceptedAttempts.Count(attempt => attempt.IsCorrect));
    }

    [Fact]
    public void StructuredBand_FrontierAttemptThreshold_DistinguishesTwentyFromNineteen()
    {
        const int structuredBandIndex = 10;
        var passing = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            frontierAttemptCount: 20,
            distinctFrontierCount: 16);
        var failing = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            frontierAttemptCount: 19,
            distinctFrontierCount: 16);

        Assert.Equal(21, failing.Evidence.AcceptedAttempts.Count(attempt => attempt.FactId == "add:10+10"));
        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_DistinctFrontierThreshold_DistinguishesSixteenFromFifteen()
    {
        const int structuredBandIndex = 10;
        var passing = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            distinctFrontierCount: 16);
        var failing = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            distinctFrontierCount: 15);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_Coverage_RequiresTheDeterministicRepresentativeSample()
    {
        const int structuredBandIndex = 10;
        var passing = CreateCase(ArithmeticOperation.Addition, structuredBandIndex);
        var failing = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            completeIntroductions: false);

        var band = GetBand(passing.Curriculum, structuredBandIndex);
        var owned = new AcquisitionOwnershipResolver(passing.Curriculum).GetOwnedFrontier(structuredBandIndex);
        var required = DeterministicFactRanker.SelectStructuredSample(
            owned,
            ArithmeticOperation.Addition,
            band.Id);

        Assert.Equal(16, required.Count);
        Assert.Equal(15, failing.Evidence.CurrentBandIntroducedFactIds.Count);
        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void StructuredBand_EarlierOwnedOrUnrelatedIntroductions_DoNotReplaceAStructuredSampleFact()
    {
        const int structuredBandIndex = 10;
        var testCase = CreateCase(
            ArithmeticOperation.Addition,
            structuredBandIndex,
            completeIntroductions: false);
        var evidence = new BandAdvancementEvidence(
            testCase.Evidence.AcceptedAttempts,
            testCase.Evidence.LifetimeAttemptedFactIds,
            testCase.Evidence.CurrentBandIntroducedFactIds.Concat(new[] { "add:10+10", "add:999+999" }));

        Assert.False(_evaluator.Evaluate(testCase.Progression, testCase.Curriculum, evidence).Advances);
    }

    [Fact]
    public void StructuredBand_UnseenNonRequiredStructuredCandidates_AreNotAdvancementDebt()
    {
        const int structuredBandIndex = 10;
        var testCase = CreateCase(ArithmeticOperation.Addition, structuredBandIndex);
        var owned = new AcquisitionOwnershipResolver(testCase.Curriculum).GetOwnedFrontier(structuredBandIndex);

        Assert.True(owned.Count > testCase.Evidence.CurrentBandIntroducedFactIds.Count);
        Assert.True(_evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence).Advances);
    }

    [Fact]
    public void FinalSafeBand_RemainsInMaintenanceWhenNoCompleteNextBandExists()
    {
        const int finalSafeBandIndex = 32;
        var testCase = CreateCase(ArithmeticOperation.Multiplication, finalSafeBandIndex);

        Assert.True(testCase.Curriculum.TryGetBand(finalSafeBandIndex, out _));
        Assert.False(testCase.Curriculum.TryGetBand(finalSafeBandIndex + 1, out _));

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.False(decision.Advances);
        Assert.Same(testCase.Progression, decision.ResultingProgression);
    }

    [Fact]
    public void InvalidOrMismatchedCurrentCurriculumState_IsRejected()
    {
        var curriculum = new ArithmeticCurriculum();
        var evidence = CreateCase(ArithmeticOperation.Addition, bandIndex: 10).Evidence;

        Assert.Throws<ArgumentException>(() => _evaluator.Evaluate(
            new OperationProgression(ArithmeticOperation.Addition, int.MaxValue, 0),
            curriculum.Addition,
            evidence));
        Assert.Throws<ArgumentException>(() => _evaluator.Evaluate(
            new OperationProgression(ArithmeticOperation.Subtraction, 0, 0),
            curriculum.Addition,
            evidence));
    }

    [Fact]
    public void WeakStructuredOperation_DoesNotBlockThreeIndependentStrongOperations()
    {
        var cases = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(
                operation => operation,
                operation => CreateCase(
                    operation,
                    bandIndex: operation switch
                    {
                        ArithmeticOperation.Addition => 10,
                        ArithmeticOperation.Subtraction => 10,
                        ArithmeticOperation.Multiplication => 12,
                        ArithmeticOperation.Division => 12,
                        _ => 10
                    },
                    attemptCount: operation == ArithmeticOperation.Addition ? 39 : 40));

        var decisions = cases.ToDictionary(
            pair => pair.Key,
            pair => _evaluator.Evaluate(
                pair.Value.Progression,
                pair.Value.Curriculum,
                pair.Value.Evidence));

        Assert.False(decisions[ArithmeticOperation.Addition].Advances);
        Assert.Equal(10, decisions[ArithmeticOperation.Addition].ResultingProgression.BandIndex);
        Assert.All(
            new[]
            {
                ArithmeticOperation.Subtraction,
                ArithmeticOperation.Multiplication,
                ArithmeticOperation.Division
            },
            operation =>
            {
                var expectedBand = operation switch
                {
                    ArithmeticOperation.Subtraction => 11,
                    ArithmeticOperation.Multiplication => 13,
                    ArithmeticOperation.Division => 13,
                    _ => 11
                };
                Assert.True(decisions[operation].Advances);
                Assert.Equal(expectedBand, decisions[operation].ResultingProgression.BandIndex);
            });
    }

    [Fact]
    public void WeakLaterEvidence_NeverRollsBackOrResetsTheCurrentProgression()
    {
        var testCase = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 10,
            bandStart: 900,
            attemptCount: 39);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.False(decision.Advances);
        Assert.Same(testCase.Progression, decision.ResultingProgression);
        Assert.Equal(10, decision.ResultingProgression.BandIndex);
        Assert.Equal(900, decision.ResultingProgression.BandStartedPracticePosition);
    }

    private static AdvancementCase CreateCase(
        ArithmeticOperation operation,
        int bandIndex,
        long bandStart = 100,
        int attemptCount = 40,
        int correctCount = 40,
        int fluentCount = 40,
        int frontierAttemptCount = 40,
        int? distinctFrontierCount = null,
        bool completeCoverage = true,
        bool completeIntroductions = true)
    {
        if (fluentCount > correctCount)
        {
            throw new ArgumentOutOfRangeException(nameof(fluentCount));
        }

        var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
        var band = GetBand(curriculum, bandIndex);
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum).GetOwnedFrontier(bandIndex);
        var requiredDistinctCount = Math.Min(16, ownedFrontier.Count);
        var usedDistinctCount = distinctFrontierCount ?? requiredDistinctCount;
        var usedFrontier = ownedFrontier.Take(usedDistinctCount).ToArray();
        var nonFrontierId = GetNonFrontierFactId(operation, bandIndex);

        var attempts = Enumerable.Range(0, attemptCount)
            .Select(index =>
            {
                var isFrontier = index < frontierAttemptCount && usedFrontier.Length > 0;
                var factId = isFrontier
                    ? usedFrontier[index % usedFrontier.Length].Id
                    : nonFrontierId;
                var isCorrect = index < correctCount;
                var latency = isCorrect && index < fluentCount ? 2500 : 2501;
                return new BandAttemptEvidence(
                    bandStart + index + 1,
                    factId,
                    isCorrect,
                    isCorrect && index < fluentCount,
                    latency);
            })
            .ToArray();

        var lifetime = completeCoverage
            ? ownedFrontier.Select(fact => fact.Id)
            : ownedFrontier.Take(Math.Max(0, ownedFrontier.Count - 1)).Select(fact => fact.Id);
        var structuredSample = band.Kind == CurriculumBandKind.Structured
            ? DeterministicFactRanker.SelectStructuredSample(ownedFrontier, operation, band.Id)
            : Array.Empty<ArithmeticFact>();
        var introduced = completeIntroductions
            ? structuredSample.Select(fact => fact.Id)
            : structuredSample.Take(Math.Max(0, structuredSample.Count - 1)).Select(fact => fact.Id);

        return new AdvancementCase(
            new OperationProgression(operation, bandIndex, bandStart),
            curriculum,
            new BandAdvancementEvidence(attempts, lifetime, introduced));
    }

    private static AdvancementCase ReplaceAttempts(
        AdvancementCase testCase,
        IEnumerable<BandAttemptEvidence> attempts) =>
        testCase with
        {
            Evidence = new BandAdvancementEvidence(
                attempts,
                testCase.Evidence.LifetimeAttemptedFactIds,
                testCase.Evidence.CurrentBandIntroducedFactIds)
        };

    private static IEnumerable<BandAttemptEvidence> ShiftAttempts(
        IEnumerable<BandAttemptEvidence> attempts,
        long offset) => attempts.Select(attempt => new BandAttemptEvidence(
            attempt.PracticePosition + offset,
            attempt.FactId,
            attempt.IsCorrect,
            attempt.IsFluent,
            attempt.ResponseLatencyMs));

    private static CurriculumBand GetBand(OperationCurriculum curriculum, int bandIndex)
    {
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        return band!;
    }

    private static string GetNonFrontierFactId(ArithmeticOperation operation, int bandIndex) =>
        (operation, bandIndex) switch
        {
            (ArithmeticOperation.Addition, 10) => "add:10+10",
            (ArithmeticOperation.Subtraction, 10) => "sub:11-1",
            (ArithmeticOperation.Subtraction, 20) => "sub:20-10",
            (ArithmeticOperation.Multiplication, 12) => "mul:10*10",
            (ArithmeticOperation.Division, 12) => "div:20/10",
            (ArithmeticOperation.Addition, _) => "add:999+999",
            (ArithmeticOperation.Subtraction, _) => "sub:999-1",
            (ArithmeticOperation.Multiplication, _) => "mul:999*999",
            (ArithmeticOperation.Division, _) => "div:999/1",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

    private sealed record AdvancementCase(
        OperationProgression Progression,
        OperationCurriculum Curriculum,
        BandAdvancementEvidence Evidence);
}
