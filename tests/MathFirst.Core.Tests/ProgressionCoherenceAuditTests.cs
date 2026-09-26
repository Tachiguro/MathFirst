namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Progression;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;
using Xunit.Abstractions;

public sealed class ProgressionCoherenceAuditTests
{
    private readonly ITestOutputHelper _output;

    public ProgressionCoherenceAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ===========================================================================
    // TEST 1: STAGE COMPARABILITY AUDIT
    // ===========================================================================
    [Fact]
    public void PresentationStages_AreNotCrossOperationProgressPercentages()
    {
        var curriculum = new ArithmeticCurriculum();

        // 1. Measure total dense facts and dense band counts per operation
        var addOwnership = new AcquisitionOwnershipResolver(curriculum.Addition);
        var subOwnership = new AcquisitionOwnershipResolver(curriculum.Subtraction);
        var mulOwnership = new AcquisitionOwnershipResolver(curriculum.Multiplication);
        var divOwnership = new AcquisitionOwnershipResolver(curriculum.Division);

        var addDenseFacts = Enumerable.Range(0, 10).Sum(b => addOwnership.GetOwnedFrontier(b).Count);
        var subDenseFacts = Enumerable.Range(0, 20).Sum(b => subOwnership.GetOwnedFrontier(b).Count);
        var mulDenseFacts = Enumerable.Range(0, 12).Sum(b => mulOwnership.GetOwnedFrontier(b).Count);
        var divDenseFacts = Enumerable.Range(0, 12).Sum(b => divOwnership.GetOwnedFrontier(b).Count);

        // Core architectural truths
        Assert.Equal(121, addDenseFacts);
        Assert.Equal(121, subDenseFacts);
        Assert.Equal(169, mulDenseFacts);
        Assert.Equal(156, divDenseFacts);
        Assert.Equal(567, addDenseFacts + subDenseFacts + mulDenseFacts + divDenseFacts);

        // 2. Cumulative owned facts at representative displayed stages (Stage = BandIndex + 1)
        var addStage10Facts = Enumerable.Range(0, 10).Sum(b => addOwnership.GetOwnedFrontier(b).Count);
        var subStage10Facts = Enumerable.Range(0, 10).Sum(b => subOwnership.GetOwnedFrontier(b).Count);
        var subStage20Facts = Enumerable.Range(0, 20).Sum(b => subOwnership.GetOwnedFrontier(b).Count);
        var mulStage5Facts = Enumerable.Range(0, 5).Sum(b => mulOwnership.GetOwnedFrontier(b).Count);
        var divStage5Facts = Enumerable.Range(0, 5).Sum(b => divOwnership.GetOwnedFrontier(b).Count);

        // Anomaly A: Addition Stage 10 and Subtraction Stage 20 represent EQUAL curriculum coverage
        Assert.Equal(121, addStage10Facts);
        Assert.Equal(121, subStage20Facts);
        Assert.Equal(addStage10Facts, subStage20Facts);

        // Anomaly B: Addition Stage 10 and Subtraction Stage 10 represent VASTLY DIFFERENT coverage
        Assert.Equal(121, addStage10Facts);
        Assert.Equal(66, subStage10Facts); // Subtraction Stage 10 only covers minuends <= 10 (54.5% of dense)

        // Anomaly C: Multiplication Stage 5 and Division Stage 5 represent a small fraction of dense
        Assert.Equal(36, mulStage5Facts); // 21.3% of 169
        Assert.Equal(30, divStage5Facts); // 19.2% of 156

        _output.WriteLine("=== STAGE COMPARABILITY FINDINGS ===");
        _output.WriteLine($"Addition Dense Foundation:       10 bands, {addDenseFacts} facts. Stage 10 = {addStage10Facts} facts (100.0%)");
        _output.WriteLine($"Subtraction Dense Foundation:    20 bands, {subDenseFacts} facts. Stage 10 = {subStage10Facts} facts (54.5%), Stage 20 = {subStage20Facts} facts (100.0%)");
        _output.WriteLine($"Multiplication Dense Foundation: 12 bands, {mulDenseFacts} facts. Stage 5  = {mulStage5Facts} facts (21.3%), Stage 12 = {mulDenseFacts} facts (100.0%)");
        _output.WriteLine($"Division Dense Foundation:       12 bands, {divDenseFacts} facts. Stage 5  = {divStage5Facts} facts (19.2%), Stage 12 = {divDenseFacts} facts (100.0%)");
        _output.WriteLine("CONCLUSION: Learner-facing Stage is BandIndex + 1 and is structurally non-comparable across operations.");
    }

    // ===========================================================================
    // TEST 2: NUMBER-SPACE CLIFF MEASUREMENT & STAGE 10 -> 11
    // ===========================================================================
    [Fact]
    public void AdditionStage10To11_MeasuresNumberSpaceUnlock()
    {
        var curriculum = new ArithmeticCurriculum();

        // Band 9 is Stage 10 (ADD-D10)
        var gateBefore = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, 9);
        var ceilingBefore = gateBefore.AdditionCeiling!.Value;

        // Band 10 is Stage 11 (ADD-P1-ANCHOR)
        var gateAfter = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, 10);
        var ceilingAfter = gateAfter.AdditionCeiling!.Value;

        var unlockDelta = ceilingAfter - ceilingBefore;

        // Structural assertions
        Assert.Equal(20, ceilingBefore);
        Assert.Equal(180, ceilingAfter);
        Assert.Equal(160, unlockDelta);

        // Fact eligibility checks
        var mul5x5 = new ArithmeticFact(ArithmeticOperation.Multiplication, 5, 5);
        var div25d5 = new ArithmeticFact(ArithmeticOperation.Division, 25, 5);

        Assert.False(gateBefore.Allows(mul5x5), "5x5=25 must be gated at Addition Stage 10 (ceiling 20).");
        Assert.False(gateBefore.Allows(div25d5), "25/5=5 must be gated at Addition Stage 10 (ceiling 20).");

        Assert.True(gateAfter.Allows(mul5x5), "5x5=25 must be eligible at Addition Stage 11 (ceiling 180).");
        Assert.True(gateAfter.Allows(div25d5), "25/5=5 must be eligible at Addition Stage 11 (ceiling 180).");

        _output.WriteLine("=== NUMBER-SPACE CLIFF MEASUREMENT ===");
        _output.WriteLine($"Addition Ceiling before Stage 11 (Stage 10, ADD-D10):       {ceilingBefore}");
        _output.WriteLine($"Addition Ceiling after Stage 11 (Stage 11, ADD-P1-ANCHOR):   {ceilingAfter}");
        _output.WriteLine($"Measured Number-Space Unlock Delta:                        +{unlockDelta} (+{((double)unlockDelta / ceilingBefore) * 100:0.0}%)");
    }

    // ===========================================================================
    // TEST 3: MULTIPLICATION STAGE 5 COVERAGE BLOCKING
    // ===========================================================================
    [Fact]
    public void GuidedMode_MultiplicationStage5_IsCoverageBlockedAtCeiling20()
    {
        var curriculum = new ArithmeticCurriculum();
        var mulCurriculum = curriculum.Multiplication;
        Assert.True(mulCurriculum.TryGetBand(4, out var band));
        Assert.Equal("MUL-D05", band!.Id.Value);

        var ownership = new AcquisitionOwnershipResolver(mulCurriculum);
        var frontier = ownership.GetOwnedFrontier(4);
        Assert.Equal(11, frontier.Count);

        var gateAtCeiling20 = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, 9);
        Assert.Equal(20, gateAtCeiling20.AdditionCeiling);

        var allowedFacts = frontier.Where(f => gateAtCeiling20.Allows(f)).ToList();
        var gatedFacts = frontier.Where(f => !gateAtCeiling20.Allows(f)).ToList();

        Assert.Equal(10, allowedFacts.Count);
        Assert.Single(gatedFacts);
        Assert.Equal("mul:5*5", gatedFacts[0].Id);
        Assert.Equal(25, gatedFacts[0].CorrectResult);

        // Simulate complete correctness across all 10 presentable facts
        var progression = new OperationProgression(ArithmeticOperation.Multiplication, 4, 100);
        var evidenceAttempts = allowedFacts
            .Select((fact, idx) => new BandAttemptEvidence(101 + idx, fact.Id, isCorrect: true, isFluent: true, responseLatencyMs: 800))
            .ToList();

        var decision = DenseProgressionEvaluator.Evaluate(progression, mulCurriculum, evidenceAttempts);
        Assert.False(decision.Advances, "Multiplication Stage 5 must NOT advance when 5*5=25 has not been attempted.");

        var blockingReason = AnalyzeDenseBlockingReason(progression, mulCurriculum, evidenceAttempts, gateAtCeiling20);
        _output.WriteLine($"Multiplication Stage 5 Blocking Analysis: {blockingReason}");
        Assert.Contains("BlockedByGuidedNumberSpaceGate", blockingReason);
        Assert.Contains("mul:5*5", blockingReason);
    }

    // ===========================================================================
    // TEST 4: DIVISION STAGE 5 COVERAGE BLOCKING
    // ===========================================================================
    [Fact]
    public void GuidedMode_DivisionStage5_IsCoverageBlockedAtCeiling20()
    {
        var curriculum = new ArithmeticCurriculum();
        var divCurriculum = curriculum.Division;
        Assert.True(divCurriculum.TryGetBand(4, out var band));
        Assert.Equal("DIV-D05", band!.Id.Value);

        var ownership = new AcquisitionOwnershipResolver(divCurriculum);
        var frontier = ownership.GetOwnedFrontier(4);
        Assert.Equal(10, frontier.Count);

        var gateAtCeiling20 = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, 9);
        Assert.Equal(20, gateAtCeiling20.AdditionCeiling);

        var allowedFacts = frontier.Where(f => gateAtCeiling20.Allows(f)).ToList();
        var gatedFacts = frontier.Where(f => !gateAtCeiling20.Allows(f)).ToList();

        Assert.Equal(9, allowedFacts.Count);
        Assert.Single(gatedFacts);
        Assert.Equal("div:25/5", gatedFacts[0].Id);
        Assert.Equal(25, gatedFacts[0].LeftOperand); // dividend

        // Simulate complete correctness across all 9 presentable facts
        var progression = new OperationProgression(ArithmeticOperation.Division, 4, 100);
        var evidenceAttempts = allowedFacts
            .Select((fact, idx) => new BandAttemptEvidence(101 + idx, fact.Id, isCorrect: true, isFluent: true, responseLatencyMs: 800))
            .ToList();

        var decision = DenseProgressionEvaluator.Evaluate(progression, divCurriculum, evidenceAttempts);
        Assert.False(decision.Advances, "Division Stage 5 must NOT advance when 25/5=5 has not been attempted.");

        var blockingReason = AnalyzeDenseBlockingReason(progression, divCurriculum, evidenceAttempts, gateAtCeiling20);
        _output.WriteLine($"Division Stage 5 Blocking Analysis: {blockingReason}");
        Assert.Contains("BlockedByGuidedNumberSpaceGate", blockingReason);
        Assert.Contains("div:25/5", blockingReason);
    }

    // ===========================================================================
    // TEST 5: PROFILE A — PERFECT LEARNER DETERMINISTIC MILESTONES & PLATEAU
    // ===========================================================================
    [Fact]
    public async Task PerfectLearner_ProgressionMilestones_AreDeterministic()
    {
        var run1 = await ExecuteSimulationAsync(new SimulationConfig(
            ProfileName: "Profile A (Run 1)",
            MaxAttempts: 2500,
            LatencyMs: 800,
            SubmitAnswerFn: (fact, attemptNumber, opAttemptNumber) => fact.CorrectResult));

        var run2 = await ExecuteSimulationAsync(new SimulationConfig(
            ProfileName: "Profile A (Run 2)",
            MaxAttempts: 2500,
            LatencyMs: 800,
            SubmitAnswerFn: (fact, attemptNumber, opAttemptNumber) => fact.CorrectResult));

        // 1. Verify 100% bitwise determinism between repeated runs
        Assert.Equal(run1.Milestones.Count, run2.Milestones.Count);
        foreach (var (key, pos1) in run1.Milestones)
        {
            Assert.True(run2.Milestones.TryGetValue(key, out var pos2));
            Assert.Equal(pos1, pos2);
        }
        Assert.Equal(run1.FirstPlateauPosition, run2.FirstPlateauPosition);
        Assert.Equal(run1.LastPlateauPosition, run2.LastPlateauPosition);
        Assert.Equal(run1.PlateauDuration, run2.PlateauDuration);

        // 2. Verify all required milestones were observed
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Addition, 2)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Addition, 8)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Addition, 10)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Addition, 11)));

        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Subtraction, 10)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Subtraction, 20)));

        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Multiplication, 4)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Multiplication, 5)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Multiplication, 6)));

        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Division, 4)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Division, 5)));
        Assert.True(run1.Milestones.ContainsKey((ArithmeticOperation.Division, 6)));

        // 3. Verify the +10 / -20 / x5 / /5 plateau occurred
        Assert.NotNull(run1.FirstPlateauPosition);
        Assert.NotNull(run1.LastPlateauPosition);
        Assert.True(run1.PlateauDuration > 0);

        _output.WriteLine("=== PROFILE A (STRONG LEARNER) MEASURED MILESTONES ===");
        _output.WriteLine($"Addition Stage 2 reached at global attempt:       {run1.Milestones[(ArithmeticOperation.Addition, 2)]}");
        _output.WriteLine($"Addition Stage 8 reached at global attempt:       {run1.Milestones[(ArithmeticOperation.Addition, 8)]}");
        _output.WriteLine($"Addition Stage 10 reached at global attempt:      {run1.Milestones[(ArithmeticOperation.Addition, 10)]}");
        _output.WriteLine($"Addition Stage 11 reached at global attempt:      {run1.Milestones[(ArithmeticOperation.Addition, 11)]}");
        _output.WriteLine($"Subtraction Stage 10 reached at global attempt:   {run1.Milestones[(ArithmeticOperation.Subtraction, 10)]}");
        _output.WriteLine($"Subtraction Stage 20 reached at global attempt:   {run1.Milestones[(ArithmeticOperation.Subtraction, 20)]}");
        if (run1.Milestones.TryGetValue((ArithmeticOperation.Subtraction, 21), out var sub21))
        {
            _output.WriteLine($"Subtraction Stage 21 reached at global attempt:   {sub21}");
        }
        _output.WriteLine($"Multiplication Stage 4 reached at global attempt: {run1.Milestones[(ArithmeticOperation.Multiplication, 4)]}");
        _output.WriteLine($"Multiplication Stage 5 reached at global attempt: {run1.Milestones[(ArithmeticOperation.Multiplication, 5)]}");
        _output.WriteLine($"Multiplication Stage 6 reached at global attempt: {run1.Milestones[(ArithmeticOperation.Multiplication, 6)]}");
        _output.WriteLine($"Division Stage 4 reached at global attempt:       {run1.Milestones[(ArithmeticOperation.Division, 4)]}");
        _output.WriteLine($"Division Stage 5 reached at global attempt:       {run1.Milestones[(ArithmeticOperation.Division, 5)]}");
        _output.WriteLine($"Division Stage 6 reached at global attempt:       {run1.Milestones[(ArithmeticOperation.Division, 6)]}");
        _output.WriteLine("------------------------------------------------------");
        _output.WriteLine($"First attempt entering +10 / -20 / x5 / /5:       {run1.FirstPlateauPosition}");
        _output.WriteLine($"Last attempt in +10 / -20 / x5 / /5:              {run1.LastPlateauPosition}");
        _output.WriteLine($"Plateau Duration (Global Accepted Attempts):      {run1.PlateauDuration}");
        _output.WriteLine($"Plateau Duration (Addition Accepted Attempts):    {run1.AdditionAttemptsInPlateau}");
        _output.WriteLine($"Addition Transition Unblocking 5x5 and 25/5:      Addition Stage 10 -> 11 (Position {run1.Milestones[(ArithmeticOperation.Addition, 11)]})");
        _output.WriteLine($"Multiplication Stage 6 Transition Position:       Position {run1.Milestones[(ArithmeticOperation.Multiplication, 6)]}");
        _output.WriteLine($"Division Stage 6 Transition Position:             Position {run1.Milestones[(ArithmeticOperation.Division, 6)]}");
    }

    // ===========================================================================
    // TEST 6: PROFILE B — NORMAL LEARNER AUDIT
    // ===========================================================================
    [Fact]
    public async Task NormalLearner_PlateauEventuallyResolves()
    {
        // 87.5% correct: deterministic error every 8th turn for each operation
        var result = await ExecuteSimulationAsync(new SimulationConfig(
            ProfileName: "Profile B (Normal Learner)",
            MaxAttempts: 3500,
            LatencyMs: 2200,
            SubmitAnswerFn: (fact, attemptNumber, opAttemptNumber) =>
            {
                if (opAttemptNumber % 8 == 0)
                {
                    return fact.CorrectResult + 1; // deliberate deterministic mistake
                }
                return fact.CorrectResult;
            }));

        Assert.NotNull(result.FirstPlateauPosition);
        Assert.NotNull(result.LastPlateauPosition);
        Assert.True(result.PlateauDuration > 0);

        // Verify plateau eventually resolved without manual intervention
        Assert.True(result.Milestones.ContainsKey((ArithmeticOperation.Addition, 11)));
        Assert.True(result.Milestones.ContainsKey((ArithmeticOperation.Multiplication, 6)));
        Assert.True(result.Milestones.ContainsKey((ArithmeticOperation.Division, 6)));

        _output.WriteLine("=== PROFILE B (NORMAL LEARNER) AUDIT ===");
        _output.WriteLine($"Plateau +10 / -20 / x5 / /5 Entered At:      Attempt {result.FirstPlateauPosition}");
        _output.WriteLine($"Plateau Exited At:                           Attempt {result.LastPlateauPosition + 1}");
        _output.WriteLine($"Plateau Duration (Global Attempts):          {result.PlateauDuration}");
        _output.WriteLine($"Plateau Duration (Addition Attempts):        {result.AdditionAttemptsInPlateau}");
        _output.WriteLine($"Errors Occurred During Plateau:              {result.ErrorsInPlateau}");
        _output.WriteLine($"Teaching Interventions During Plateau:       {result.TeachingInterventionsInPlateau}");
        _output.WriteLine($"Eventual Resolution:                        Addition reached Stage 11 at attempt {result.Milestones[(ArithmeticOperation.Addition, 11)]}, unblocking Mul/Div to Stage 6.");
    }

    // ===========================================================================
    // TEST 7: PROFILE C — SLOW CORRECT LEARNER AUDIT
    // ===========================================================================
    [Fact]
    public async Task SlowCorrectLearner_DenseAndStructuredFluencyBehavior()
    {
        // 100% correct, slow latency (4500ms). Adaptive pace ceiling is clamp(1.25 * P_fact, 1500, 4000) = 4000ms.
        // At 4500ms latency, responses are Correct but NOT fluent (rated Hard).
        var result = await ExecuteSimulationAsync(new SimulationConfig(
            ProfileName: "Profile C (Slow Correct)",
            MaxAttempts: 50,
            LatencyMs: 4500,
            SubmitAnswerFn: (fact, attemptNumber, opAttemptNumber) => fact.CorrectResult));

        // Verify all 50 attempts are Correct, but IsFluent is false
        Assert.Equal(50, result.Telemetry.Count);
        Assert.All(result.Telemetry, t =>
        {
            Assert.Equal(AttemptOutcome.Correct, t.Outcome);
            Assert.Equal(4500, t.LatencyMs);
            Assert.False(t.IsFluent, $"Expected attempt {t.PracticePosition} at 4500ms to be non-fluent.");
        });

        // Verify Dense progression advanced despite non-fluent latency
        var addBand = result.FinalProgression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        var subBand = result.FinalProgression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex;
        Assert.True(addBand > 0, "Addition dense progression must advance based on correctness alone.");
        Assert.True(subBand > 0, "Subtraction dense progression must advance based on correctness alone.");

        // Verify Structured progression requirement: Structured bands require >= 34 fluent in rolling 40
        Assert.True(new ArithmeticCurriculum().Addition.TryGetBand(10, out var structuredBand));
        var nonFluentEvidence = Enumerable.Range(1, 40)
            .Select(i => new BandAttemptEvidence(i, "add:10+10", isCorrect: true, isFluent: false, responseLatencyMs: 4500))
            .ToList();

        var structuredEvidence = new BandAdvancementEvidence(
            nonFluentEvidence,
            ["add:10+10"],
            new HashSet<string>(["add:10+10"], StringComparer.Ordinal));

        var structuredDecision = new BandAdvancementEvaluator().Evaluate(
            new OperationProgression(ArithmeticOperation.Addition, 10, 0),
            new ArithmeticCurriculum().Addition,
            structuredEvidence);

        Assert.False(structuredDecision.Advances, "Structured band MUST NOT advance when fluent attempt count is below 34.");

        _output.WriteLine("=== PROFILE C (SLOW CORRECT LEARNER) AUDIT ===");
        _output.WriteLine("1. Slow correct responses (4500ms) are evaluated as AttemptOutcome.Correct.");
        _output.WriteLine("2. AttemptRecord.IsFluent is FALSE for all 50 attempts (rated Hard due to exceeding adaptive fluency threshold).");
        _output.WriteLine("3. Dense band progression advances normally (DenseProgressionEvaluator requires latest-attempt correctness only).");
        _output.WriteLine("4. Structured band progression is strictly BLOCKED (BandAdvancementEvaluator requires >= 34 fluent attempts in rolling 40).");
    }

    // ===========================================================================
    // TEST 8: PROFILE D — TARGETED WEAK FACTS EXTENDS MUL/DIV PLATEAU
    // ===========================================================================
    [Fact]
    public async Task TargetedAdditionWeakness_ExtendsMulDivPlateauWithoutProvingPermanentDeadlock()
    {
        // Profile D:
        // Subtraction, Multiplication, Division: 100% correct.
        // Addition: Correct when sum < 15, Incorrect when sum >= 15.
        // In ADD-D08 (Stage 8), 3 facts have sum >= 15 (8+7=15, 7+8=15, 8+8=16).
        // Since 14*10 = 140 < 17*9 = 153, Addition CANNOT advance past Stage 8 under this rule.
        var result = await ExecuteSimulationAsync(new SimulationConfig(
            ProfileName: "Profile D (Addition Weakness)",
            MaxAttempts: 2000,
            LatencyMs: 800,
            SubmitAnswerFn: (fact, attemptNumber, opAttemptNumber) =>
            {
                if (fact.Operation == ArithmeticOperation.Addition && fact.CorrectResult >= 15)
                {
                    return fact.CorrectResult + 1; // deliberate failure on large addition
                }
                return fact.CorrectResult;
            }));

        var mulProgression = result.FinalProgression.OperationProgressions[ArithmeticOperation.Multiplication];
        var divProgression = result.FinalProgression.OperationProgressions[ArithmeticOperation.Division];
        var addProgression = result.FinalProgression.OperationProgressions[ArithmeticOperation.Addition];
        var subProgression = result.FinalProgression.OperationProgressions[ArithmeticOperation.Subtraction];

        // 1. Multiplication and Division reached Stage 5 and stayed frozen at Stage 5
        Assert.Equal(4, mulProgression.BandIndex); // Stage 5
        Assert.Equal(4, divProgression.BandIndex); // Stage 5

        // 2. Subtraction continued advancing independently through dense foundation
        Assert.True(subProgression.BandIndex >= 19, $"Expected Subtraction to reach at least Stage 20, but was {subProgression.BandIndex + 1}");

        // 3. Addition is blocked BEFORE Stage 10, specifically at Stage 8 (BandIndex 7, ADD-D08)
        Assert.Equal(7, addProgression.BandIndex); // Stage 8 (ADD-D08)

        // 4. Mul/Div selector fallback roles: once in Stage 5 and eligible facts introduced,
        // newPool is empty because mul:5*5 and div:25/5 are gated by Addition ceiling 16.
        // Verify late attempts for Mul/Div (>= 1000) are non-new and use Due or Maintenance roles.
        var lateMulTelemetry = result.Telemetry
            .Where(t => t.Operation == ArithmeticOperation.Multiplication && t.PracticePosition >= 1000)
            .ToList();
        var lateDivTelemetry = result.Telemetry
            .Where(t => t.Operation == ArithmeticOperation.Division && t.PracticePosition >= 1000)
            .ToList();

        Assert.NotEmpty(lateMulTelemetry);
        Assert.NotEmpty(lateDivTelemetry);
        Assert.DoesNotContain(lateMulTelemetry, t => t.FactId == "mul:5*5");
        Assert.DoesNotContain(lateDivTelemetry, t => t.FactId == "div:25/5");
        Assert.All(lateMulTelemetry, t =>
        {
            Assert.False(t.IsNewIntroduction, "No new Multiplication facts may be introduced while 5*5 is gated.");
            Assert.True(t.IsGateEligible, "Presented Multiplication facts must be gate-eligible.");
        });
        Assert.All(lateDivTelemetry, t =>
        {
            Assert.False(t.IsNewIntroduction, "No new Division facts may be introduced while 25/5 is gated.");
            Assert.True(t.IsGateEligible, "Presented Division facts must be gate-eligible.");
        });

        _output.WriteLine("=== PROFILE D (TARGETED WEAK ADDITION) AUDIT ===");
        _output.WriteLine($"Final Addition Stage:             Stage {addProgression.BandIndex + 1} (ADD-D08) — BLOCKED BEFORE STAGE 10");
        _output.WriteLine($"Final Subtraction Stage:          Stage {subProgression.BandIndex + 1} (SUB-P1-ANCHOR) — INDEPENDENT ADVANCEMENT");
        _output.WriteLine($"Final Multiplication Stage:       Stage {mulProgression.BandIndex + 1} (MUL-D05) — FROZEN");
        _output.WriteLine($"Final Division Stage:             Stage {divProgression.BandIndex + 1} (DIV-D05) — FROZEN");
        _output.WriteLine($"Audited Attempt Horizon:          {result.FinalProgression.PracticePosition} attempts (Unresolved within horizon)");
        _output.WriteLine("Finding: Addition is blocked before Stage 10 at Stage 8 due to 8+7=15, 7+8=15, 8+8=16 failing deterministically.");
        _output.WriteLine("Finding: GuidedNumberSpaceGate ceiling remains at 16, gating 5*5=25 and 25/5=5.");
        _output.WriteLine("Finding: Multiplication and Division cannot progress past Stage 5 without Addition advancing.");
        _output.WriteLine("Finding: Subtraction progresses completely independently through Stage 20.");
        _output.WriteLine("Finding: Selector gracefully falls back to Due and Maintenance roles when newPool is empty.");
    }

    // ===========================================================================
    // TEST SIMULATION HARNESS & HELPERS
    // ===========================================================================

    private sealed record SimulationConfig(
        string ProfileName,
        int MaxAttempts,
        long LatencyMs,
        Func<ArithmeticFact, int, int, int> SubmitAnswerFn);

    private sealed class SimulationResult
    {
        public required string ProfileName { get; init; }
        public required Dictionary<(ArithmeticOperation, int), long> Milestones { get; init; }
        public required List<AttemptTelemetry> Telemetry { get; init; }
        public required LearnerProgression FinalProgression { get; init; }
        public long? FirstPlateauPosition { get; set; }
        public long? LastPlateauPosition { get; set; }
        public long PlateauDuration => (LastPlateauPosition.HasValue && FirstPlateauPosition.HasValue)
            ? LastPlateauPosition.Value - FirstPlateauPosition.Value + 1
            : 0;
        public int AdditionAttemptsInPlateau { get; set; }
        public int ErrorsInPlateau { get; set; }
        public int TeachingInterventionsInPlateau { get; set; }
    }

    private sealed record AttemptTelemetry(
        long PracticePosition,
        ArithmeticOperation Operation,
        long OperationAttemptOrdinal,
        PracticeSelectionRole RequestedRole,
        string FactId,
        int LeftOperand,
        int RightOperand,
        int CorrectResult,
        int SubmittedAnswer,
        AttemptOutcome Outcome,
        long LatencyMs,
        bool IsFluent,
        bool IsNewIntroduction,
        IReadOnlyDictionary<ArithmeticOperation, int> BandIndices,
        IReadOnlyDictionary<ArithmeticOperation, int> Stages,
        int AdditionCeiling,
        bool IsGateEligible,
        bool OperationAdvanced);

    private static async Task<SimulationResult> ExecuteSimulationAsync(SimulationConfig config)
    {
        using var store = new InMemoryAuditStore();
        var clock = new DeterministicAuditClock { ElapsedPerTurn = TimeSpan.FromMilliseconds(config.LatencyMs) };
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var session = new TrainingSession(store, clock, preferenceStore: preferences);
        await session.InitializeAsync(startTiming: true);

        var milestones = new Dictionary<(ArithmeticOperation, int), long>();
        var telemetry = new List<AttemptTelemetry>(config.MaxAttempts);
        var curriculum = new ArithmeticCurriculum();

        // Record initial stages
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            milestones[(op, 1)] = 0;
        }

        long? firstPlateauPos = null;
        long? lastPlateauPos = null;
        var addAttemptsInPlateau = 0;
        var errorsInPlateau = 0;
        var teachingInterventionsInPlateau = 0;

        for (var pos = 1; pos <= config.MaxAttempts; pos++)
        {
            var fact = session.CurrentFact;
            var op = fact.Operation;
            var opOrdinal = session.GetOperationAcceptedAttemptCount(op) + 1;
            var requestedRole = AdaptivePracticeSelector.GetRequestedRole(opOrdinal);

            var addBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            var currentGate = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, addBand);
            var additionCeiling = currentGate.AdditionCeiling!.Value;
            var isGateEligible = currentGate.Allows(fact);

            var isNewIntroduction = !session.ItemStates.ContainsKey(fact.Id) || session.ItemStates[fact.Id].TotalAttempts == 0;

            var submittedAnswer = config.SubmitAnswerFn(fact, pos, (int)opOrdinal);
            session.SubmitAnswer(submittedAnswer);

            var commitResult = await session.CommitCurrentEvaluationAsync();
            if (!commitResult.IsSuccess)
            {
                throw new InvalidOperationException($"Commit failed at position {pos}: {commitResult.Message}");
            }

            var eval = session.LastEvaluation!;
            var attemptRecord = eval.ChangeSet.Attempt;

            // Track band changes
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                var bandIndex = session.Progression.OperationProgressions[operation].BandIndex;
                var stage = bandIndex + 1;
                if (!milestones.ContainsKey((operation, stage)))
                {
                    milestones[(operation, stage)] = pos;
                }
            }

            // Plateau detection: +10 / -20 / x5 / /5
            var currentStages = session.Progression.OperationProgressions.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.BandIndex + 1);

            var inPlateau = currentStages[ArithmeticOperation.Addition] == 10
                && currentStages[ArithmeticOperation.Subtraction] >= 20
                && currentStages[ArithmeticOperation.Multiplication] == 5
                && currentStages[ArithmeticOperation.Division] == 5;

            if (inPlateau)
            {
                firstPlateauPos ??= pos;
                lastPlateauPos = pos;
                if (op == ArithmeticOperation.Addition)
                {
                    addAttemptsInPlateau++;
                }
                if (!eval.IsCorrect)
                {
                    errorsInPlateau++;
                }
            }

            telemetry.Add(new AttemptTelemetry(
                PracticePosition: pos,
                Operation: op,
                OperationAttemptOrdinal: opOrdinal,
                RequestedRole: requestedRole,
                FactId: fact.Id,
                LeftOperand: fact.LeftOperand,
                RightOperand: fact.RightOperand,
                CorrectResult: fact.CorrectResult,
                SubmittedAnswer: submittedAnswer,
                Outcome: eval.Outcome,
                LatencyMs: eval.LatencyMs,
                IsFluent: attemptRecord.IsFluent,
                IsNewIntroduction: isNewIntroduction,
                BandIndices: session.Progression.OperationProgressions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BandIndex),
                Stages: currentStages,
                AdditionCeiling: additionCeiling,
                IsGateEligible: isGateEligible,
                OperationAdvanced: eval.OperationAdvanced));

            var interventions = CompleteTurn(session);
            if (inPlateau)
            {
                teachingInterventionsInPlateau += interventions;
            }

            // Early exit condition for Profile A / B once all desired targets are passed
            if (currentStages[ArithmeticOperation.Addition] >= 11
                && currentStages[ArithmeticOperation.Multiplication] >= 6
                && currentStages[ArithmeticOperation.Division] >= 6
                && currentStages[ArithmeticOperation.Subtraction] >= 20)
            {
                break;
            }
        }

        return new SimulationResult
        {
            ProfileName = config.ProfileName,
            Milestones = milestones,
            Telemetry = telemetry,
            FinalProgression = session.Progression,
            FirstPlateauPosition = firstPlateauPos,
            LastPlateauPosition = lastPlateauPos,
            AdditionAttemptsInPlateau = addAttemptsInPlateau,
            ErrorsInPlateau = errorsInPlateau,
            TeachingInterventionsInPlateau = teachingInterventionsInPlateau
        };
    }

    private static int CompleteTurn(TrainingSession session)
    {
        var interventions = 0;
        if (session.InteractionState == SessionInteractionState.CorrectFeedback)
        {
            session.AdvanceAfterCorrectAnswer(startTiming: true);
        }
        else if (session.InteractionState is SessionInteractionState.IncorrectFeedback or SessionInteractionState.TimeoutFeedback)
        {
            session.AcknowledgeFeedback(startTiming: true);
        }
        else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
        {
            interventions = 1;
            session.AcknowledgeTeachingIntervention(startTiming: true);
        }

        if (session.InteractionState == SessionInteractionState.SessionCheckIn)
        {
            session.ContinuePractice(startTiming: true);
        }

        if (session.InteractionState != SessionInteractionState.AwaitingAnswer)
        {
            session.AdvanceToNextFact(startTiming: true);
        }

        return interventions;
    }

    private static string AnalyzeDenseBlockingReason(
        OperationProgression progression,
        OperationCurriculum curriculum,
        IReadOnlyList<BandAttemptEvidence> evidenceAttempts,
        GuidedNumberSpaceGate gate)
    {
        var ownership = new AcquisitionOwnershipResolver(curriculum);
        var ownedFrontier = ownership.GetOwnedFrontier(progression.BandIndex);
        var frontierCount = ownedFrontier.Count;

        var gatedFacts = ownedFrontier.Where(f => !gate.Allows(f)).ToList();
        if (gatedFacts.Count > 0)
        {
            return $"BlockedByGuidedNumberSpaceGate: {gatedFacts.Count} facts gated ({string.Join(", ", gatedFacts.Select(f => f.Id))})";
        }

        var attemptedFactIds = evidenceAttempts
            .Where(a => a.PracticePosition > progression.BandStartedPracticePosition)
            .Select(a => a.FactId)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ownedFrontier.Where(f => !attemptedFactIds.Contains(f.Id)).ToList();
        if (missing.Count > 0)
        {
            return $"MissingFrontierCoverage: {missing.Count} of {frontierCount} facts unattempted ({string.Join(", ", missing.Select(f => f.Id))})";
        }

        var correctCount = evidenceAttempts.Count(a => a.IsCorrect);
        if (checked(correctCount * 10) < checked(frontierCount * 9))
        {
            return $"CorrectnessBelowThreshold: {correctCount}/{frontierCount} correct";
        }

        if (!curriculum.TryGetBand(progression.BandIndex + 1, out _))
        {
            return "NoSuccessorBand";
        }

        return "AdvancementReady";
    }

    // ===========================================================================
    // TEST STORE & PREFERENCE IMPLEMENTATIONS
    // ===========================================================================

    private sealed class DeterministicAuditClock : IClock
    {
        private long _timestamp = 10_000_000L;
        public TimeSpan ElapsedPerTurn { get; set; } = TimeSpan.FromMilliseconds(800);

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) => ElapsedPerTurn;
    }

    private sealed class InMemoryAuditStore : ILearnerStore
    {
        private readonly Dictionary<string, ItemLearningState> _items = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FsrsCardState> _fsrs = new(StringComparer.Ordinal);
        private readonly List<AttemptRecord> _attempts = [];
        private readonly HashSet<string> _submissionIds = new(StringComparer.Ordinal);
        private LearnerProgression _progression = LearnerProgression.CreateFresh();
        private long _revision = 1;

        public string StoragePath => "inmemory://audit";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new LearnerSnapshot(_progression, _items, _fsrs, _attempts, _revision, LearnerProgression.DefaultSchemaVersion, _progression.OperationProgressions));

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_attempts, operation, bandStartedPracticePosition, frontierFactIds));

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            if (_submissionIds.Contains(changeSet.SubmissionId))
            {
                return Task.FromResult(PersistenceResult.Success(_revision));
            }
            if (changeSet.ExpectedRevision != _revision)
            {
                return Task.FromResult(PersistenceResult.Conflict("synthetic stale revision"));
            }

            _submissionIds.Add(changeSet.SubmissionId);
            _attempts.Add(changeSet.Attempt);
            _items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            if (changeSet.UpdatedFsrsState is not null)
            {
                _fsrs[changeSet.UpdatedFsrsState.FactId] = changeSet.UpdatedFsrsState;
            }
            _progression = changeSet.UpdatedProgression;
            _revision++;
            return Task.FromResult(PersistenceResult.Success(_revision));
        }
    }

    private sealed class TestPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operationPreferences = [];

        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _operationPreferences.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _operationPreferences[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            var enabled = operations.ToHashSet();
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                SetOperationEnabled(operation, enabled.Contains(operation));
            }
        }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operationPreferences.Clear();
        public void ResetAllPreferences() => _operationPreferences.Clear();
    }
}
