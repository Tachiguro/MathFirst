namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;

public sealed class FastAcquisitionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstSlice3_" + Guid.NewGuid().ToString("N"));

    public FastAcquisitionTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void CaseA_BasicSuccess_AdvancesDenseBandWhenAllOwnedFactsQualify()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var band0 = curriculum.Bands[0];
        Assert.Equal(CurriculumBandKind.Dense, band0.Kind);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        Assert.True(ownedFrontier.Count is >= 1 and <= 12);
        var n = ownedFrontier.Count;

        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1500L, n).ToArray());

        var decision = Evaluate(progression, curriculum, attempts);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(attempts[^1].PracticePosition, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Theory]
    [InlineData(2000, true)]
    [InlineData(2001, false)]
    public void CaseB_AbsoluteSpeedBoundary_RequiresLatencyLessThanOrEqualToTwoThousand(
        long firstEncounterLatency,
        bool expectedAdvancement)
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var n = ownedFrontier.Count;

        var latencies = Enumerable.Repeat(1500L, n).ToArray();
        latencies[0] = firstEncounterLatency;

        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: latencies);

        var decision = Evaluate(progression, curriculum, attempts);

        Assert.Equal(expectedAdvancement, decision.Advances);
    }

    [Fact]
    public void CaseC_AdaptiveFluencyIndependence_UsesAbsoluteLatencyRegardlessOfIsFluent()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var n = ownedFrontier.Count;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        // Case C1: 1900ms with isFluent: false => passes Fast Acquisition
        var attemptsPassing = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1900L, n).ToArray(),
            isFluent: false);
        var decisionPassing = Evaluate(progression, curriculum, attemptsPassing);
        Assert.True(decisionPassing.Advances);

        // Case C2: 2100ms with isFluent: true => fails Fast Acquisition
        var attemptsFailing = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(2100L, n).ToArray(),
            isFluent: true);
        var decisionFailing = Evaluate(progression, curriculum, attemptsFailing);
        Assert.False(decisionFailing.Advances);
    }

    [Fact]
    public void CaseD_IncorrectFirstEncounter_PermanentlyFailsEvenWithLaterFastCorrectRepetition()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());

        // Replace the first attempt (first encounter of fact 0) with Incorrect
        attempts[0] = new BandAttemptEvidence(
            attempts[0].PracticePosition,
            attempts[0].FactId,
            isCorrect: false,
            isFluent: false,
            responseLatencyMs: 1200);

        // Append a later fast correct repetition for fact 0
        var nextOpPos = GetNextOperationPosition(ArithmeticOperation.Addition, attempts[^1].PracticePosition);
        var extendedAttempts = attempts.Append(new BandAttemptEvidence(
            nextOpPos,
            attempts[0].FactId,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 800)).ToArray();

        var decision = Evaluate(progression, curriculum, extendedAttempts);
        Assert.False(decision.Advances);
    }

    [Fact]
    public void CaseE_TimeoutFirstEncounter_PermanentlyFailsEvenWithLaterFastCorrectRepetition()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());

        // Replace first encounter with Timeout
        attempts[0] = new BandAttemptEvidence(
            attempts[0].PracticePosition,
            attempts[0].FactId,
            isCorrect: false,
            isFluent: false,
            responseLatencyMs: 9000);

        // Append a later fast correct repetition for fact 0
        var nextOpPos = GetNextOperationPosition(ArithmeticOperation.Addition, attempts[^1].PracticePosition);
        var extendedAttempts = attempts.Append(new BandAttemptEvidence(
            nextOpPos,
            attempts[0].FactId,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 800)).ToArray();

        var decision = Evaluate(progression, curriculum, extendedAttempts);
        Assert.False(decision.Advances);
    }

    [Fact]
    public void CaseF_ErrorElsewhereInPrefix_FailsFastAcquisitionEvenIfAllOwnedFactsQualify()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());

        // Introduce an error on an intermediate non-frontier / maintenance opportunity
        var intermediateIndex = attempts.Length / 2;
        attempts[intermediateIndex] = new BandAttemptEvidence(
            attempts[intermediateIndex].PracticePosition,
            attempts[intermediateIndex].FactId,
            isCorrect: false,
            isFluent: false,
            responseLatencyMs: 3000);

        var decision = Evaluate(progression, curriculum, attempts);
        Assert.False(decision.Advances);
    }

    [Fact]
    public void CaseG_MissingOwnedFact_FailsFastAcquisitionWhenNotAllOwnedFactsAreEncountered()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        // Create attempts where the last owned fact is never encountered
        var subsetFrontier = ownedFrontier.Take(ownedFrontier.Count - 1).ToArray();
        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            subsetFrontier,
            latencies: Enumerable.Repeat(1200L, subsetFrontier.Length).ToArray());

        var decision = Evaluate(progression, curriculum, attempts);
        Assert.False(decision.Advances);
    }

    [Theory]
    [InlineData(ArithmeticOperation.Addition, 0)]
    [InlineData(ArithmeticOperation.Addition, 7)]
    [InlineData(ArithmeticOperation.Multiplication, 12)]
    [InlineData(ArithmeticOperation.Subtraction, 23)]
    [InlineData(ArithmeticOperation.Division, 41)]
    public void CaseH_PhaseAwareHorizon_DerivedFromRealSchedulerAcrossVariousStartingPhases(
        ArithmeticOperation operation,
        long bandStart)
    {
        var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var n = Math.Min(4, ownedFrontier.Count);
        var targetFrontier = ownedFrontier.Take(n).ToArray();

        var progression = new OperationProgression(operation, 0, bandStart);
        var attempts = CreateQualifyingFastAcquisitionAttempts(
            operation,
            bandStart,
            targetFrontier,
            latencies: Enumerable.Repeat(1200L, n).ToArray());

        var decision = Evaluate(progression, curriculum, attempts);
        Assert.True(decision.Advances);
    }

    [Fact]
    public void CaseI_HorizonBound_NthNewHorizonOccursWithinAtMostThirtyOperationOpportunities()
    {
        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            // Test 100 different starting positions to cover all possible starting phases
            for (long startPos = 0; startPos < 100; startPos++)
            {
                for (int n = 1; n <= 12; n++)
                {
                    var horizonPos = FastAcquisitionEvaluator.CalculateRequestedNewHorizon(operation, startPos, n);
                    var operationOpportunitiesCount = FastAcquisitionEvaluator
                        .EnumerateOperationPositions(operation, startPos, horizonPos)
                        .Count();

                    Assert.True(
                        operationOpportunitiesCount <= 30,
                        $"Horizon for operation {operation}, startPos {startPos}, N {n} took {operationOpportunitiesCount} opportunities (expected <= 30).");
                }
            }
        }
    }

    [Fact]
    public void CaseJ_PrefixCompleteness_FailsClosedIfEvidenceRowIsMissingOrCorrupted()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition,
            bandStart: 0,
            ownedFrontier,
            latencies: Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());

        if (attempts.Length > 2)
        {
            var missingAttempts = attempts.Where((_, index) => index != 1).ToArray();
            var decision = Evaluate(progression, curriculum, missingAttempts);
            Assert.False(decision.Advances);
        }
    }

    [Fact]
    public void CaseK_NullPracticePosition_LegacyAttemptsDoNotSatisfyFastAcquisition()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var emptyEvidence = new BandAdvancementEvidence([], ownedFrontier.Select(f => f.Id), []);
        var evaluator = new BandAdvancementEvaluator();
        var decision = evaluator.Evaluate(progression, curriculum, emptyEvidence);
        Assert.False(decision.Advances);
    }

    [Fact]
    public void CaseL_DenseEligibility_StructuredBandsAndOversizedFrontiersNeverQualify()
    {
        var subCurriculum = new ArithmeticCurriculum().Subtraction;
        var subBand0 = subCurriculum.Bands[0];
        if (subBand0.Kind == CurriculumBandKind.Structured)
        {
            var subProg = new OperationProgression(ArithmeticOperation.Subtraction, 0, 0);
            var subOwnership = new AcquisitionOwnershipResolver(subCurriculum);
            var subFrontier = subOwnership.GetOwnedFrontier(0);
            var subAttempts = CreateQualifyingFastAcquisitionAttempts(
                ArithmeticOperation.Subtraction, 0, subFrontier, Enumerable.Repeat(1000L, subFrontier.Count).ToArray());
            var decision = Evaluate(subProg, subCurriculum, subAttempts);
            Assert.False(decision.Advances);
        }

        var addCurriculum = new ArithmeticCurriculum().Addition;
        var oversizedFrontier = Enumerable.Range(0, 13)
            .Select(i => new ArithmeticFact(ArithmeticOperation.Addition, i, i))
            .ToArray();
        var oversizedAttempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition, 0, oversizedFrontier, Enumerable.Repeat(1000L, 13).ToArray());
        var oversizedProg = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        Assert.False(FastAcquisitionEvaluator.TryEvaluate(
            oversizedProg, addCurriculum, new BandAdvancementEvidence(oversizedAttempts, [], []), out _));
    }

    [Fact]
    public void CaseM_ExistingLearnerPartialCompatibility_CanQualifyOnFutureAcceptedAttempt()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var attemptsPartial = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition, 0, ownedFrontier.Take(2).ToArray(), Enumerable.Repeat(1200L, 2).ToArray());
        var decisionPartial = Evaluate(progression, curriculum, attemptsPartial);
        Assert.False(decisionPartial.Advances);

        var attemptsComplete = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition, 0, ownedFrontier, Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());
        var decisionComplete = Evaluate(progression, curriculum, attemptsComplete);
        Assert.True(decisionComplete.Advances);
    }

    [Fact]
    public void CaseN_ExistingLearnerExpiredHorizon_DoesNotRetroactivelyAdvance()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var horizon = FastAcquisitionEvaluator.CalculateRequestedNewHorizon(ArithmeticOperation.Addition, 0, ownedFrontier.Count);
        var beyondHorizonPos = GetNextOperationPosition(ArithmeticOperation.Addition, horizon);

        var partialAttempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition, 0, ownedFrontier.Take(1).ToArray(), [1200L]);
        var extendedAttempts = partialAttempts.Append(new BandAttemptEvidence(
            beyondHorizonPos, "add:999+999", true, true, 1200)).ToArray();

        var decision = Evaluate(progression, curriculum, extendedAttempts);
        Assert.False(decision.Advances);
    }

    [Fact]
    public void CaseO_MulD01FallbackPreservation_OrdinaryFallbackGateStillAdvancesWhenFastAcquisitionFails()
    {
        var curriculum = new ArithmeticCurriculum().Multiplication;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Multiplication, 0, 0);

        var fallbackAttempts = Enumerable.Range(0, 12)
            .Select(index =>
            {
                var actualPos = GetNthOperationPosition(ArithmeticOperation.Multiplication, 0, index + 1);
                var fact = index < 8 ? ownedFrontier[index % ownedFrontier.Count] : ownedFrontier[0];
                var isCorrect = index < 11;
                return new BandAttemptEvidence(actualPos, fact.Id, isCorrect, isCorrect, isCorrect ? 2400 : 3000);
            })
            .ToArray();

        var decision = Evaluate(progression, curriculum, fallbackAttempts);
        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void CaseP_StandardFallbackPreservation_StandardGateStillAdvancesWhenFastAcquisitionFails()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);

        var standardAttempts = Enumerable.Range(0, 40)
            .Select(index =>
            {
                var actualPos = GetNthOperationPosition(ArithmeticOperation.Addition, 0, index + 1);
                var fact = index < 20 ? ownedFrontier[index % Math.Min(16, ownedFrontier.Count)] : new ArithmeticFact(ArithmeticOperation.Addition, 99, 99);
                return new BandAttemptEvidence(actualPos, fact.Id, isCorrect: true, isFluent: true, responseLatencyMs: 2400);
            })
            .ToArray();

        var decision = Evaluate(progression, curriculum, standardAttempts);
        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public async Task CaseQ_AtomicPersistenceAndRestart_FastAcquisitionPersistsAtomicallyAndSurvivesRestart()
    {
        var dbPath = Path.Combine(_directory, "fast-acq-restart.db");
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var session = new TrainingSession(store);
            await session.InitializeAsync();

            var op = session.CurrentFact.Operation;
            Assert.Equal(ArithmeticOperation.Addition, op);
            Assert.Equal(0, session.Progression.OperationProgressions[op].BandIndex);

            for (var step = 0; step < 30; step++)
            {
                var isAddition = session.CurrentFact.Operation == ArithmeticOperation.Addition;
                var answer = isAddition ? session.CurrentFact.CorrectResult : checked(session.CurrentFact.CorrectResult + 1);
                var evaluation = session.SubmitAnswer(answer);
                await session.CommitCurrentEvaluationAsync();

                if (session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex == 1)
                {
                    Assert.True(evaluation.OperationAdvanced);
                    break;
                }

                session.AdvanceToNextFact();
            }

            Assert.Equal(1, session.Progression.OperationProgressions[op].BandIndex);
            await store.CloseAsync();
        }

        using (var reopenedStore = new SqliteLearnerStore(dbPath))
        {
            var snapshot = await reopenedStore.LoadSnapshotAsync();
            Assert.NotNull(snapshot.OperationProgressions);
            Assert.Equal(1, snapshot.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
            Assert.True(snapshot.OperationProgressions[ArithmeticOperation.Addition].BandStartedPracticePosition > 0);
            Assert.Equal(0, snapshot.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
        }
    }

    [Fact]
    public void CaseR_OperationIndependence_AdvancementInOneOperationDoesNotAlterOtherOperations()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(0);

        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = CreateQualifyingFastAcquisitionAttempts(
            ArithmeticOperation.Addition, 0, ownedFrontier, Enumerable.Repeat(1200L, ownedFrontier.Count).ToArray());

        var decision = Evaluate(progression, curriculum, attempts);
        Assert.True(decision.Advances);
        Assert.Equal(ArithmeticOperation.Addition, decision.ResultingProgression.Operation);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch { }
    }

    private static BandAdvancementDecision Evaluate(
        OperationProgression progression,
        OperationCurriculum curriculum,
        IEnumerable<BandAttemptEvidence> attempts)
    {
        var evidenceList = attempts.ToArray();
        var lifetimeFacts = evidenceList.Select(a => a.FactId).ToHashSet(StringComparer.Ordinal);
        var introducedFacts = evidenceList.Select(a => a.FactId).ToHashSet(StringComparer.Ordinal);
        var evaluator = new BandAdvancementEvaluator();
        return evaluator.Evaluate(
            progression,
            curriculum,
            new BandAdvancementEvidence(evidenceList, lifetimeFacts, introducedFacts));
    }

    private static BandAttemptEvidence[] CreateQualifyingFastAcquisitionAttempts(
        ArithmeticOperation operation,
        long bandStart,
        IReadOnlyList<ArithmeticFact> ownedFrontier,
        IReadOnlyList<long> latencies,
        bool isFluent = true)
    {
        var attempts = new List<BandAttemptEvidence>();
        var frontierIndex = 0;

        for (var pos = checked(bandStart + 1); ; pos++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(pos) == operation)
            {
                var role = AdaptivePracticeSelector.GetRequestedRole(pos);
                if (role == PracticeSelectionRole.New && frontierIndex < ownedFrontier.Count)
                {
                    var fact = ownedFrontier[frontierIndex];
                    var latency = latencies[frontierIndex];
                    attempts.Add(new BandAttemptEvidence(pos, fact.Id, isCorrect: true, isFluent: isFluent, responseLatencyMs: latency));
                    frontierIndex++;
                    if (frontierIndex == ownedFrontier.Count)
                    {
                        break;
                    }
                }
                else
                {
                    var fact = ownedFrontier[Math.Max(0, frontierIndex - 1)];
                    attempts.Add(new BandAttemptEvidence(pos, fact.Id, isCorrect: true, isFluent: isFluent, responseLatencyMs: 1000));
                }
            }
        }

        return attempts.ToArray();
    }

    private static long GetNextOperationPosition(ArithmeticOperation operation, long currentPosition)
    {
        for (var pos = checked(currentPosition + 1); ; pos++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(pos) == operation)
            {
                return pos;
            }
        }
    }

    private static long GetNthOperationPosition(ArithmeticOperation operation, long startPosition, int n)
    {
        var count = 0;
        for (var pos = checked(startPosition + 1); ; pos++)
        {
            if (AdaptivePracticeSelector.GetScheduledOperation(pos) == operation)
            {
                count++;
                if (count == n)
                {
                    return pos;
                }
            }
        }
    }
}
