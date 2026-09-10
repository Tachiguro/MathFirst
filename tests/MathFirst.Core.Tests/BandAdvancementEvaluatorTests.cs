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
            new BandAttemptEvidence(0, "add:1+1", true, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BandAttemptEvidence(1, "add:1+1", true, -1));
    }

    [Fact]
    public void FullyQualifyingFortyAttemptLearner_Advances()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 0);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.True(decision.Advances);
    }

    [Fact]
    public void InitialMultiplicationBootstrap_AdvancesAfterTwelveQualifyingAttempts()
    {
        var testCase = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(112, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void InitialMultiplicationBootstrap_RequiresElevenCorrectAttempts()
    {
        var passing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);
        var failing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 10,
            fluentCount: 10,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void InitialMultiplicationBootstrap_RequiresElevenFluentAttempts()
    {
        var passing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 12,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);
        var failing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 12,
            fluentCount: 10,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void InitialMultiplicationBootstrap_RequiresEightFrontierAttempts()
    {
        var passing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);
        var failing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 7,
            distinctFrontierCount: 4);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void InitialMultiplicationBootstrap_RequiresAllFourFactsForCoverageAndDistinctEvidence()
    {
        var passing = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4);
        var missingCoverage = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 4,
            completeCoverage: false);
        var insufficientDistinctEvidence = CreateCase(
            ArithmeticOperation.Multiplication,
            bandIndex: 0,
            attemptCount: 12,
            correctCount: 11,
            fluentCount: 11,
            frontierAttemptCount: 8,
            distinctFrontierCount: 3);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(
            missingCoverage.Progression,
            missingCoverage.Curriculum,
            missingCoverage.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(
            insufficientDistinctEvidence.Progression,
            insufficientDistinctEvidence.Curriculum,
            insufficientDistinctEvidence.Evidence).Advances);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Multiplication, 1)]
    [InlineData(ArithmeticOperation.Addition, 0)]
    [InlineData(ArithmeticOperation.Division, 0)]
    public void BootstrapThresholds_DoNotApplyOutsideInitialMultiplication(
        ArithmeticOperation operation,
        int bandIndex)
    {
        var twelveAttempts = CreateCase(
            operation,
            bandIndex,
            attemptCount: 12,
            correctCount: 12,
            fluentCount: 12,
            frontierAttemptCount: 12);
        var fortyAttempts = CreateCase(operation, bandIndex);

        Assert.False(_evaluator.Evaluate(
            twelveAttempts.Progression,
            twelveAttempts.Curriculum,
            twelveAttempts.Evidence).Advances);
        Assert.True(_evaluator.Evaluate(
            fortyAttempts.Progression,
            fortyAttempts.Curriculum,
            fortyAttempts.Evidence).Advances);
    }

    [Fact]
    public void CorrectnessThreshold_DistinguishesThirtyEightFromThirtySeven()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, correctCount: 38, fluentCount: 34);
        var failing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, correctCount: 37, fluentCount: 34);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void Advancement_CreatesExactlyTheNextIndependentStateAtTheTriggeringPosition()
    {
        var testCase = CreateCase(ArithmeticOperation.Subtraction, bandIndex: 0, bandStart: 400);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(440, decision.ResultingProgression.BandStartedPracticePosition);
        Assert.Equal(testCase.Progression.Operation, decision.ResultingProgression.Operation);
        Assert.Equal(0, testCase.Progression.BandIndex);
        Assert.Equal(400, testCase.Progression.BandStartedPracticePosition);
    }

    [Fact]
    public void FewerThanFortyPostStartAttempts_DoNotAdvance()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, attemptCount: 39);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.False(decision.Advances);
        Assert.Same(testCase.Progression, decision.ResultingProgression);
    }

    [Fact]
    public void AttemptsAtBandStartAreExcludedAndAttemptsAfterBandStartAreIncluded()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, bandStart: 100);
        var shiftedAttempts = passing.Evidence.AcceptedAttempts
            .Select(attempt => new BandAttemptEvidence(
                attempt.PracticePosition - 1,
                attempt.FactId,
                attempt.IsCorrect,
                attempt.ResponseLatencyMs));
        var equalityIncluded = new BandAdvancementEvidence(
            shiftedAttempts,
            passing.Evidence.LifetimeAttemptedFactIds,
            passing.Evidence.CurrentBandIntroducedFactIds);

        Assert.False(_evaluator.Evaluate(passing.Progression, passing.Curriculum, equalityIncluded).Advances);
        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
    }

    [Fact]
    public void MoreThanFortyAttempts_UseOnlyTheLatestFortyByPracticePosition()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0);
        var oldFailures = Enumerable.Range(101, 10)
            .Select(position => new BandAttemptEvidence(position, "add:999+999", false, 5000));
        var shiftedPassing = ShiftAttempts(passing.Evidence.AcceptedAttempts, 10);
        var withOldFailures = ReplaceAttempts(passing, oldFailures.Concat(shiftedPassing));

        Assert.True(_evaluator.Evaluate(
            withOldFailures.Progression,
            withOldFailures.Curriculum,
            withOldFailures.Evidence).Advances);

        var failing = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 0,
            correctCount: 37,
            fluentCount: 34);
        var oldSuccesses = Enumerable.Range(101, 10)
            .Select(position => new BandAttemptEvidence(position, "add:0+0", true, 1000));
        var shiftedFailing = ShiftAttempts(failing.Evidence.AcceptedAttempts, 10);
        var withOldSuccesses = ReplaceAttempts(failing, oldSuccesses.Concat(shiftedFailing));

        Assert.False(_evaluator.Evaluate(
            withOldSuccesses.Progression,
            withOldSuccesses.Curriculum,
            withOldSuccesses.Evidence).Advances);
    }

    [Fact]
    public void UnorderedInput_IsNormalizedByPracticePosition()
    {
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 0);
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
        var testCase = CreateCase(ArithmeticOperation.Addition, bandIndex: 0);
        var duplicate = testCase.Evidence.AcceptedAttempts
            .Append(new BandAttemptEvidence(140, "add:999+999", true, 1000));
        var invalid = ReplaceAttempts(testCase, duplicate);

        Assert.Throws<ArgumentException>(() =>
            _evaluator.Evaluate(invalid.Progression, invalid.Curriculum, invalid.Evidence));
    }

    [Fact]
    public void FluencyThreshold_DistinguishesThirtyFourFromThirtyThree()
    {
        var passing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, fluentCount: 34);
        var failing = CreateCase(ArithmeticOperation.Addition, bandIndex: 0, fluentCount: 33);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(failing.Progression, failing.Curriculum, failing.Evidence).Advances);
    }

    [Fact]
    public void SlowCorrectAnswers_CountAsCorrectButNotFluent()
    {
        var passing = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 0,
            correctCount: 38,
            fluentCount: 34);
        var tooSlow = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 0,
            correctCount: 38,
            fluentCount: 33);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(tooSlow.Progression, tooSlow.Curriculum, tooSlow.Evidence).Advances);
        Assert.Equal(38, tooSlow.Evidence.AcceptedAttempts.Count(attempt => attempt.IsCorrect));
    }

    [Fact]
    public void FrontierAttemptThreshold_DistinguishesTwentyFromNineteenAndRejectsEarlierOwnership()
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
    public void DistinctFrontierThreshold_DistinguishesSixteenFromFifteen()
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
    public void SmallFrontier_RequiresAllAvailableDistinctFactsAndAllowsRepeatedAttempts()
    {
        var passing = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 0,
            frontierAttemptCount: 20,
            distinctFrontierCount: 4);
        var missingOne = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 0,
            frontierAttemptCount: 40,
            distinctFrontierCount: 3);

        Assert.True(_evaluator.Evaluate(passing.Progression, passing.Curriculum, passing.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(missingOne.Progression, missingOne.Curriculum, missingOne.Evidence).Advances);
    }

    [Fact]
    public void DenseCoverage_UsesPositionlessLifetimeExactFactEvidence()
    {
        const int denseBandIndex = 9;
        var complete = CreateCase(
            ArithmeticOperation.Addition,
            denseBandIndex,
            distinctFrontierCount: 16);
        var missingOne = CreateCase(
            ArithmeticOperation.Addition,
            denseBandIndex,
            distinctFrontierCount: 16,
            completeCoverage: false);

        Assert.Equal(21, complete.Evidence.LifetimeAttemptedFactIds.Count);
        Assert.True(_evaluator.Evaluate(complete.Progression, complete.Curriculum, complete.Evidence).Advances);
        Assert.False(_evaluator.Evaluate(missingOne.Progression, missingOne.Curriculum, missingOne.Evidence).Advances);
    }

    [Fact]
    public void UnrelatedLifetimeFacts_DoNotCompleteDenseCoverage()
    {
        const int denseBandIndex = 9;
        var testCase = CreateCase(
            ArithmeticOperation.Addition,
            denseBandIndex,
            completeCoverage: false);
        var unrelatedCoverage = testCase.Evidence.LifetimeAttemptedFactIds.Append("add:999+999");
        var evidence = new BandAdvancementEvidence(
            testCase.Evidence.AcceptedAttempts,
            unrelatedCoverage,
            testCase.Evidence.CurrentBandIntroducedFactIds);

        Assert.False(_evaluator.Evaluate(testCase.Progression, testCase.Curriculum, evidence).Advances);
    }

    [Fact]
    public void StructuredCoverage_RequiresTheDeterministicRepresentativeSample()
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
    public void EarlierOwnedOrUnrelatedIntroductions_DoNotReplaceAStructuredSampleFact()
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
    public void UnseenNonRequiredStructuredCandidates_AreNotAdvancementDebt()
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
        var evidence = CreateCase(ArithmeticOperation.Addition, bandIndex: 0).Evidence;

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
    public void WeakOperation_DoesNotBlockThreeIndependentStrongOperations()
    {
        var cases = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(
                operation => operation,
                operation => CreateCase(
                    operation,
                    bandIndex: 0,
                    attemptCount: operation == ArithmeticOperation.Addition ? 39 : 40));

        var decisions = cases.ToDictionary(
            pair => pair.Key,
            pair => _evaluator.Evaluate(
                pair.Value.Progression,
                pair.Value.Curriculum,
                pair.Value.Evidence));

        Assert.False(decisions[ArithmeticOperation.Addition].Advances);
        Assert.Equal(0, decisions[ArithmeticOperation.Addition].ResultingProgression.BandIndex);
        Assert.All(
            new[]
            {
                ArithmeticOperation.Subtraction,
                ArithmeticOperation.Multiplication,
                ArithmeticOperation.Division
            },
            operation =>
            {
                Assert.True(decisions[operation].Advances);
                Assert.Equal(1, decisions[operation].ResultingProgression.BandIndex);
            });
    }

    [Fact]
    public void WeakLaterEvidence_NeverRollsBackOrResetsTheCurrentProgression()
    {
        var testCase = CreateCase(
            ArithmeticOperation.Addition,
            bandIndex: 5,
            bandStart: 900,
            attemptCount: 39);

        var decision = _evaluator.Evaluate(testCase.Progression, testCase.Curriculum, testCase.Evidence);

        Assert.False(decision.Advances);
        Assert.Same(testCase.Progression, decision.ResultingProgression);
        Assert.Equal(5, decision.ResultingProgression.BandIndex);
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
                return new BandAttemptEvidence(bandStart + index + 1, factId, isCorrect, latency);
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
