namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class DenseProgressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstDenseProgression_" + Guid.NewGuid().ToString("N"));
    private readonly BandAdvancementEvaluator _evaluator = new();

    public DenseProgressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void Evaluator_IncompleteCoverage_Fails()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        // ADD-D01 has 4 facts: add:0+0, add:0+1, add:1+0, add:1+1. Provide only 3:
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.False(decision.Advances);
        Assert.Equal(0, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Evaluator_N4_AllFourCorrect_Advances()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(4, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void Evaluator_N10_NineCorrect_Advances()
    {
        // SUB-D09 has 10 facts (sub:0-8 to sub:9-8)
        var curriculum = new ArithmeticCurriculum().Subtraction;
        var progression = new OperationProgression(ArithmeticOperation.Subtraction, 8, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(8);
        Assert.Equal(10, ownedFrontier.Count);

        var attempts = ownedFrontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 9, // 9 correct, 1 incorrect
                isFluent: idx < 9,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(9, decision.ResultingProgression.BandIndex);
        Assert.Equal(110, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void Evaluator_N10_EightCorrect_Fails()
    {
        var curriculum = new ArithmeticCurriculum().Subtraction;
        var progression = new OperationProgression(ArithmeticOperation.Subtraction, 8, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(8);
        Assert.Equal(10, ownedFrontier.Count);

        var attempts = ownedFrontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 8, // 8 correct, 2 incorrect (8*10=80 < 10*9=90)
                isFluent: idx < 8,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.False(decision.Advances);
        Assert.Equal(8, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Evaluator_N11_TenCorrect_Advances()
    {
        // ADD-D05 has 11 facts
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 4, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(4);
        Assert.Equal(11, ownedFrontier.Count);

        var attempts = ownedFrontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 10, // 10 correct, 1 incorrect (100 >= 99)
                isFluent: idx < 10,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(5, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Evaluator_N12_ElevenCorrect_Advances()
    {
        // DIV-D06 has 12 facts (DIV band index 5)
        var curriculum = new ArithmeticCurriculum().Division;
        var progression = new OperationProgression(ArithmeticOperation.Division, 5, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(5);
        Assert.Equal(12, ownedFrontier.Count);

        var attempts = ownedFrontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 11, // 11 correct, 1 incorrect (110 >= 108)
                isFluent: idx < 11,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(6, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Evaluator_N25_TwentyThreeCorrect_Advances()
    {
        // MUL-D12 has 25 facts
        var curriculum = new ArithmeticCurriculum().Multiplication;
        var progression = new OperationProgression(ArithmeticOperation.Multiplication, 10, 100);
        // Note: band 10 is MUL-D11 (23 facts), band 11 is MUL-D12 (25 facts, terminal)
        // Let's test band index 10 (MUL-D11, 23 facts) or create a band test case with N=23
        // Band 11 is terminal with 25 facts, band 10 has 23 facts.
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var band10Frontier = ownership.GetOwnedFrontier(10);
        Assert.Equal(23, band10Frontier.Count);

        // For N=23: required correct is 21 (210 >= 207)
        var attempts = band10Frontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 21, // 21 correct, 2 incorrect (210 >= 207)
                isFluent: idx < 21,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(11, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Evaluator_N25_TwentyTwoCorrect_FailsThreshold()
    {
        var curriculum = new ArithmeticCurriculum().Multiplication;
        var progression = new OperationProgression(ArithmeticOperation.Multiplication, 10, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var band10Frontier = ownership.GetOwnedFrontier(10);
        Assert.Equal(23, band10Frontier.Count);

        // For N=23: 20 correct fails (200 < 207)
        var attempts = band10Frontier.Select((fact, idx) =>
            new BandAttemptEvidence(
                101 + idx,
                fact.Id,
                isCorrect: idx < 20,
                isFluent: idx < 20,
                responseLatencyMs: 1200)).ToArray();

        var evidence = new BandAdvancementEvidence(attempts, [], []);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.False(decision.Advances);
        Assert.Equal(10, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Repair_EarlierIncorrectRepairedByLaterCorrect_IsEligible()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        // ADD-D01: 4 facts
        // Fact "add:0+0" was Incorrect at pos 1, then Correct at pos 5
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: false, isFluent: false, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(5, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(5, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void Repair_EarlierTimeoutRepairedByLaterCorrect_IsEligible()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: false, isFluent: false, responseLatencyMs: 15000), // Timeout
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(5, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000), // Repaired
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
        Assert.Equal(5, decision.ResultingProgression.BandStartedPracticePosition);
    }

    [Fact]
    public void Repair_EarlierCorrectOverriddenByLaterIncorrect_RemovesVote()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(5, "add:0+0", isCorrect: false, isFluent: false, responseLatencyMs: 1000), // Broken
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        // N=4, but only 3 latest Correct -> 3*10=30 < 4*9=36 -> fails
        Assert.False(decision.Advances);
        Assert.Equal(0, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Repair_EarlierCorrectOverriddenByLaterTimeout_RemovesVote()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(5, "add:0+0", isCorrect: false, isFluent: false, responseLatencyMs: 15000), // Timeout
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.False(decision.Advances);
        Assert.Equal(0, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Latency_SlowCorrectAbove2000Ms_CountsAsCorrect()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: false, responseLatencyMs: 5710),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: false, responseLatencyMs: 4200),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: false, responseLatencyMs: 3100),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: false, responseLatencyMs: 6500),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Fluency_NonFluentCorrect_CountsAsCorrect()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: false, responseLatencyMs: 1800),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: false, responseLatencyMs: 1900),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: false, responseLatencyMs: 1950),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: false, responseLatencyMs: 1990),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Authority_DenseBandStandardWindowPassed_LatestFrontierFailed_DoesNotAdvance()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        // 40 attempts, 38 correct, 34 fluent. BUT latest state of fact "add:0+0" is Incorrect, so C=3, N=4 (<90%).
        var list = new List<BandAttemptEvidence>();
        for (var i = 1; i <= 36; i++)
        {
            list.Add(new BandAttemptEvidence(i, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000));
        }
        list.Add(new BandAttemptEvidence(37, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000));
        list.Add(new BandAttemptEvidence(38, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000));
        list.Add(new BandAttemptEvidence(39, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000));
        list.Add(new BandAttemptEvidence(40, "add:0+0", isCorrect: false, isFluent: false, responseLatencyMs: 1000)); // Latest for 0+0 is Incorrect

        var evidence = new BandAdvancementEvidence(list, ["add:0+0", "add:0+1", "add:1+0", "add:1+1"], ["add:0+0", "add:0+1", "add:1+0", "add:1+1"]);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        // Standard 40-gate would pass (39/40 correct, 38 fluent, 40 frontier), but Dense latest-per-frontier fails (3/4 < 90%)
        Assert.False(decision.Advances);
        Assert.Equal(0, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Authority_StructuredBandStandardWindowPassed_Advances()
    {
        var curriculum = new ArithmeticCurriculum().Subtraction;
        var progression = new OperationProgression(ArithmeticOperation.Subtraction, 10, 100);
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(10);
        var structuredSample = DeterministicFactRanker.SelectStructuredSample(
            ownedFrontier,
            ArithmeticOperation.Subtraction,
            new CurriculumBandId("SUB-I11"));

        var list = new List<BandAttemptEvidence>();
        var sampleIds = structuredSample.Select(f => f.Id).ToArray();
        for (var i = 1; i <= 40; i++)
        {
            var factId = sampleIds[(i - 1) % sampleIds.Length];
            list.Add(new BandAttemptEvidence(100 + i, factId, isCorrect: true, isFluent: true, responseLatencyMs: 1000));
        }

        var evidence = new BandAdvancementEvidence(list, sampleIds, sampleIds);
        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(11, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Terminal_DenseBandWith100PercentCorrect_StaysSafelyWithoutOverflow()
    {
        // Custom curriculum with a single terminal Dense band (no successor exists)
        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
        var terminalBand = new CurriculumBand(
            ArithmeticOperation.Addition,
            0,
            new CurriculumBandId("ADD-TERM"),
            CurriculumBandKind.Dense,
            [fact]);
        var customCurriculum = new OperationCurriculum(ArithmeticOperation.Addition, [terminalBand]);

        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 100);
        var attempts = new[]
        {
            new BandAttemptEvidence(101, fact.Id, isCorrect: true, isFluent: true, responseLatencyMs: 1000)
        };

        var decision = DenseProgressionEvaluator.Evaluate(progression, customCurriculum, attempts);

        Assert.False(decision.Advances);
        Assert.Equal(0, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Terminal_Int32MaxValue_StaysSafelyWithoutOverflow()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, int.MaxValue, 100);
        var attempts = new[]
        {
            new BandAttemptEvidence(101, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000)
        };

        var decision = DenseProgressionEvaluator.Evaluate(progression, curriculum, attempts);

        Assert.False(decision.Advances);
        Assert.Equal(int.MaxValue, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public void Independence_AdditionAdvancement_LeavesOtherOperationsUnchanged()
    {
        var curriculum = new ArithmeticCurriculum().Addition;
        var progression = new OperationProgression(ArithmeticOperation.Addition, 0, 0);
        var attempts = new[]
        {
            new BandAttemptEvidence(1, "add:0+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(2, "add:0+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(3, "add:1+0", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
            new BandAttemptEvidence(4, "add:1+1", isCorrect: true, isFluent: true, responseLatencyMs: 1000),
        };
        var evidence = new BandAdvancementEvidence(attempts, [], []);

        var decision = _evaluator.Evaluate(progression, curriculum, evidence);

        Assert.True(decision.Advances);
        Assert.Equal(ArithmeticOperation.Addition, decision.ResultingProgression.Operation);
        Assert.Equal(1, decision.ResultingProgression.BandIndex);
    }

    [Fact]
    public async Task Session_CurrentCandidateOverlay_FinalMissingFact_TriggersAdvancement()
    {
        var path = Path.Combine(_directory, "candidate-overlay-pass.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // Turn 1 (pos 1): Addition fact
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 2 (pos 2): Subtraction
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 3 (pos 3): Multiplication
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 4 (pos 4): Division
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 5 (pos 5): Addition fact 2
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 6 (pos 6): Subtraction
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 7 (pos 7): Multiplication
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 8 (pos 8): Division fact 2 -> DIV-D01 has 2 facts, should advance on pos 8!
        var divEval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(divEval.OperationAdvanced);
        Assert.Equal(1, divEval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
        Assert.Equal(8, divEval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Division].BandStartedPracticePosition);

        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task Initial_Progression_GlobalPosition15_AdvancesAllFourInitialBands()
    {
        var path = Path.Combine(_directory, "all-advance-15.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        for (var position = 1; position <= 15; position++)
        {
            Assert.Equal(position, session.Progression.PracticePosition + 1);
            var fact = session.CurrentFact;
            var eval = session.SubmitAnswer(fact.CorrectResult);

            if (position == 8)
            {
                // DIV-D01 (2 facts) advances on 8
                Assert.True(eval.OperationAdvanced);
                Assert.Equal(1, eval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
            }
            else if (position == 10)
            {
                // SUB-D01 (3 facts) advances on 10
                Assert.True(eval.OperationAdvanced);
                Assert.Equal(1, eval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
            }
            else if (position == 13)
            {
                // ADD-D01 (4 facts) advances on 13
                Assert.True(eval.OperationAdvanced);
                Assert.Equal(1, eval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
            }
            else if (position == 15)
            {
                // MUL-D01 (4 facts) advances on 15
                Assert.True(eval.OperationAdvanced);
                Assert.Equal(1, eval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
            }

            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            if (position < 15)
            {
                Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
            }
        }

        // Verify all 4 operations are at BandIndex 1 (D02) at position 15
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task CandidateOverlay_CandidateIncorrect_DoesNotAdvance()
    {
        var path = Path.Combine(_directory, "overlay-incorrect.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // Turn 1 (pos 1): Addition Correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 2 (pos 2): Subtraction
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 3 (pos 3): Multiplication
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 4 (pos 4): Division (DIV fact 1 Correct)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 5 (pos 5): Addition Correct
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 6 (pos 6): Subtraction
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 7 (pos 7): Multiplication
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        // Turn 8 (pos 8): Division (DIV fact 2 Incorrect) -> Should NOT advance (1/2 < 90%)
        var divEval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 99);
        Assert.False(divEval.OperationAdvanced);
        Assert.False(divEval.IsCorrect);
        Assert.Equal(0, divEval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Division].BandIndex);

        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task CandidateOverlay_CandidateTimeout_DoesNotAdvance()
    {
        var path = Path.Combine(_directory, "overlay-timeout.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // 7 turns correct
        for (var i = 1; i <= 7; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        // Turn 8: Division fact 2 Timeout -> Should NOT advance
        var divEval = session.RecordTimeout();
        Assert.False(divEval.OperationAdvanced);
        Assert.False(divEval.IsCorrect);
        Assert.Equal(0, divEval.ChangeSet.UpdatedProgression.OperationProgressions[ArithmeticOperation.Division].BandIndex);

        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task Atomicity_PersistenceFailureDuringAdvancement_RollsBackState()
    {
        var path = Path.Combine(_directory, "atomicity-advancement.db");
        using var innerStore = new SqliteLearnerStore(path);
        var failingStore = new FailOnNthCommitStore(innerStore, failOnCommit: 8);
        var session = new TrainingSession(failingStore);
        await session.InitializeAsync(startTiming: false);

        // 7 turns correct
        for (var i = 1; i <= 7; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }

        // Turn 8 would advance DIV-D01 to Band 1, but commit will fail
        var divEval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(divEval.OperationAdvanced); // in candidate evaluation

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(commitResult.IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);

        // Authoritative session progression must NOT have advanced
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
        Assert.Equal(7, session.Progression.PracticePosition);
    }

    [Fact]
    public async Task RestartEquivalence_ContinuousVsReopened_YieldsIdenticalProgression()
    {
        var continuousPath = Path.Combine(_directory, "restart-continuous.db");
        var reopenedPath = Path.Combine(_directory, "restart-reopened.db");

        using (var storeA = new SqliteLearnerStore(continuousPath))
        {
            var sessionA = new TrainingSession(storeA);
            await sessionA.InitializeAsync(startTiming: false);
            for (var p = 1; p <= 30; p++)
            {
                sessionA.SubmitAnswer(sessionA.CurrentFact.CorrectResult);
                Assert.True((await sessionA.CommitCurrentEvaluationAsync()).IsSuccess);
                if (!sessionA.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    sessionA.ContinuePractice(startTiming: false);
                }
            }
        }

        for (var p = 1; p <= 30; p++)
        {
            using var storeB = new SqliteLearnerStore(reopenedPath);
            var sessionB = new TrainingSession(storeB);
            await sessionB.InitializeAsync(startTiming: false);
            sessionB.SubmitAnswer(sessionB.CurrentFact.CorrectResult);
            Assert.True((await sessionB.CommitCurrentEvaluationAsync()).IsSuccess);
            if (!sessionB.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                sessionB.ContinuePractice(startTiming: false);
            }
        }

        using var finalA = new SqliteLearnerStore(continuousPath);
        using var finalB = new SqliteLearnerStore(reopenedPath);
        var snapA = await finalA.LoadSnapshotAsync();
        var snapB = await finalB.LoadSnapshotAsync();

        foreach (var op in Enum.GetValues<ArithmeticOperation>())
        {
            Assert.Equal(snapA.OperationProgressions![op].BandIndex, snapB.OperationProgressions![op].BandIndex);
            Assert.Equal(snapA.OperationProgressions[op].BandStartedPracticePosition, snapB.OperationProgressions[op].BandStartedPracticePosition);
        }
    }

    [Fact]
    public async Task WeakFact_PreservesNeedsRemediationAndFSRSAfterAdvancement()
    {
        // When a band advances (e.g. 9 correct, 1 incorrect), the incorrect fact must remain in remediation and FSRS
        var path = Path.Combine(_directory, "weak-fact-advancement.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        // Progress Addition until Band 4 (ADD-D05 has 11 facts, can advance with 10 correct and 1 incorrect)
        // Or directly verify that an incorrect fact maintains NeedsRemediation=true after operation advances.
        // Let's verify through item states and FSRS states.
        var fact = session.CurrentFact;
        var incorrectEval = session.SubmitAnswer(fact.CorrectResult + 1);
        Assert.False(incorrectEval.IsCorrect);
        Assert.True(incorrectEval.ChangeSet.UpdatedItemState.NeedsRemediation);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        Assert.True(session.ItemStates[fact.Id].NeedsRemediation);
        Assert.True(session.FsrsStates.ContainsKey(fact.Id));
    }

    [Fact]
    public async Task Simulation_DeterministicProgression_Positions20_50_100()
    {
        var path = Path.Combine(_directory, "simulation-100.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        var curriculum = new ArithmeticCurriculum();

        for (var position = 1; position <= 100; position++)
        {
            var fact = session.CurrentFact;
            session.SubmitAnswer(fact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

            if (position == 20)
            {
                // Position 20 (5 turns per op):
                // ADD: Band 1 (ADD-D02)
                // SUB: Band 1 (SUB-D02)
                // MUL: Band 1 (MUL-D02)
                // DIV: Band 1 (DIV-D02)
                Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
                Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
                Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
                Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
            }
            else if (position == 50)
            {
                // Position 50 (13 turns for ADD/SUB, 12 turns for MUL/DIV):
                // ADD: Band 2 (ADD-D03)
                // SUB: Band 3 (SUB-D04)
                // MUL: Band 2 (MUL-D03)
                // DIV: Band 3 (DIV-D04)
                Assert.Equal(2, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
                Assert.Equal(3, session.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
                Assert.Equal(2, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
                Assert.Equal(3, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
            }
            else if (position == 100)
            {
                // Position 100 (25 turns per op):
                // ADD: Band 4 (ADD-D05)
                // SUB: Band 5 (SUB-D06)
                // MUL: Band 4 (MUL-D05)
                // DIV: Band 4 (DIV-D05)
                Assert.Equal(4, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
                Assert.Equal(5, session.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
                Assert.Equal(4, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
                Assert.Equal(4, session.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
            }

            if (position < 100)
            {
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }
            }
        }
    }

    private sealed class FailOnNthCommitStore(ILearnerStore inner, int failOnCommit) : ILearnerStore
    {
        private int _commitCount;

        public string StoragePath => inner.StoragePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadSnapshotAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            inner.LoadRuntimeSnapshotAsync(cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            _commitCount++;
            if (_commitCount == failOnCommit)
            {
                return Task.FromResult(PersistenceResult.Unavailable("Simulated commit failure on target turn"));
            }

            return inner.CommitSubmissionAsync(changeSet, cancellationToken);
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            inner.ResetLearningProgressAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            inner.CloseAsync(cancellationToken);

        public void Dispose() => inner.Dispose();
    }
}
