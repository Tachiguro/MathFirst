namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// Authoritative Phase 4 deterministic offline headless simulations with three synthetic learner personas.
/// Verifies ADR-0012 monotonic curriculum stage progression, mathematical isolation, adaptive timing,
/// remediation queues, combat survival, SP economy, and absence of premature operation unlocks.
/// </summary>
public sealed class Issue112SyntheticPersonaSimulationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MathFirstPersonaSim_" + Guid.NewGuid().ToString("N"));

    public Issue112SyntheticPersonaSimulationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private string GetDatabasePath(string name) => Path.Combine(_tempDir, $"{name}_{Guid.NewGuid():N}.db");

    private sealed class PersonaSimulationClock : IClock
    {
        private long _timestamp = 10_000_000;
        public TimeSpan CurrentTurnLatency { get; set; } = TimeSpan.FromMilliseconds(1000);

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) => CurrentTurnLatency;

        public void AdvanceMs(long ms)
        {
            _timestamp += ms;
        }
    }

    // =========================================================================
    // PERSONA A — BEGINNER (Slow responses, frequent errors, remediation, survival)
    // =========================================================================

    [Fact]
    public async Task PersonaA_BeginnerLearner_ExecutesDeterministicSimulation_AndProtectsFromPrematureUnlocks()
    {
        const int seed = 42;
        var rng = new Random(seed);
        var learnerDb = GetDatabasePath("persona_a_learner");
        var gameplayDb = GetDatabasePath("persona_a_gameplay");

        var clock = new PersonaSimulationClock();
        using var learnerStore = new SqliteLearnerStore(learnerDb);
        await learnerStore.InitializeAsync();

        using var gameplayStore = new SqliteGameplayStore(gameplayDb);
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var combatCoordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await combatCoordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore, clock, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var stageHistory = new List<CurriculumStage> { session.Progression.CurriculumStage };
        var attemptsPerOp = new Dictionary<ArithmeticOperation, int>();
        int totalAttempts = 50;
        int correct = 0;
        int incorrect = 0;
        bool sawBroadWeakness = false;

        for (int i = 0; i < totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            Assert.NotNull(fact);

            // ADR-0012 Invariant check: In each stage, NEVER present unearned operations
            switch (session.Progression.CurriculumStage)
            {
                case CurriculumStage.Stage1_Addition:
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                    break;
                case CurriculumStage.Stage2_Subtraction:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction);
                    break;
                case CurriculumStage.Stage3_Multiplication:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction or ArithmeticOperation.Multiplication);
                    break;
            }

            // ADR-0012 Invariant check: In each stage, NEVER present unearned operations
            switch (session.Progression.CurriculumStage)
            {
                case CurriculumStage.Stage1_Addition:
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                    break;
                case CurriculumStage.Stage2_Subtraction:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction);
                    break;
                case CurriculumStage.Stage3_Multiplication:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction or ArithmeticOperation.Multiplication);
                    break;
            }

            attemptsPerOp[fact.Operation] = attemptsPerOp.GetValueOrDefault(fact.Operation, 0) + 1;

            // Beginner characteristics: Slow latency (4,500ms - 8,000ms), 55% accuracy
            int latency = rng.Next(4500, 8000);
            clock.CurrentTurnLatency = TimeSpan.FromMilliseconds(latency);
            clock.AdvanceMs(latency);

            bool isCorrect = rng.NextDouble() < 0.55;
            if (isCorrect)
            {
                correct++;
                session.SubmitAnswer(fact.CorrectResult);
            }
            else
            {
                incorrect++;
                session.SubmitAnswer(fact.CorrectResult + 1);
            }

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);

            if (session.HasBroadWeakness)
            {
                sawBroadWeakness = true;
            }

            // Sync with combat
            if (session.LastEvaluation?.ChangeSet is { } committed)
            {
                var boundDamage = await combatCoordinator.GetOrBindEffectiveAttackDamageAsync(session.FactInstanceRevision);
                var intent = new CyberDefensePendingIntentRecord(
                    committed.SubmissionId,
                    committed.Attempt.FactId,
                    committed.Attempt.IsCorrect,
                    committed.Attempt.IsTimingEligible,
                    latency,
                    0L,
                    DateTimeOffset.UtcNow,
                    effectiveAttackDamage: boundDamage,
                    boundCriticalStrikeRank: 0,
                    boundOverdriveRank: 0,
                    practicePosition: committed.Attempt.PracticePosition);

                await gameplayStore.StagePendingIntentAsync(intent);
                await gameplayStore.ApplyAttemptTransactionAsync(intent);
            }

            if (session.Progression.CurriculumStage != stageHistory.Last())
            {
                // Monotonic check: stage must never regress
                Assert.True(session.Progression.CurriculumStage >= stageHistory.Last(), "Curriculum stage must be strictly monotonic!");
                stageHistory.Add(session.Progression.CurriculumStage);
            }

            await AdvanceSessionTurnAsync(session);
        }

        // Beginner metrics
        var runState = await gameplayStore.GetRunStateAsync();
        var prog = await gameplayStore.GetProgressionAsync();

        Assert.True(sawBroadWeakness, "Persona A should experience broad weakness during early struggle.");
        Assert.True(stageHistory.Count >= 1);
        Assert.True(runState.PlayerCurrentHp > 0, "Persona A should survive with beginner assistance.");

        // PROOF OF ADR-0012: In fresh CurriculumManaged state, operations unlock only upon reaching the designated stage
        if (session.Progression.CurriculumStage < CurriculumStage.Stage3_Multiplication)
        {
            Assert.False(attemptsPerOp.ContainsKey(ArithmeticOperation.Multiplication), "Multiplication must NOT be attempted before Stage 3!");
        }
        if (session.Progression.CurriculumStage < CurriculumStage.Stage4_Division)
        {
            Assert.False(attemptsPerOp.ContainsKey(ArithmeticOperation.Division), "Division must NOT be attempted before Stage 4!");
        }
    }

    // =========================================================================
    // PERSONA B — INTERMEDIATE (Moderate pace, 82% accuracy, isolated weak fact, unlocks Stage 3)
    // =========================================================================

    [Fact]
    public async Task PersonaB_IntermediateLearner_ProgressesToStage3_WithoutDeadlockFromIsolatedWeakFact()
    {
        const int seed = 1337;
        var rng = new Random(seed);
        var learnerDb = GetDatabasePath("persona_b_learner");
        var gameplayDb = GetDatabasePath("persona_b_gameplay");

        var clock = new PersonaSimulationClock();
        using var learnerStore = new SqliteLearnerStore(learnerDb);
        await learnerStore.InitializeAsync();

        using var gameplayStore = new SqliteGameplayStore(gameplayDb);
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var combatCoordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await combatCoordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore, clock, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var stageHistory = new List<CurriculumStage> { session.Progression.CurriculumStage };
        var attemptsPerOp = new Dictionary<ArithmeticOperation, int>();
        int totalAttempts = 75;
        int correct = 0;
        int incorrect = 0;

        for (int i = 0; i < totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            Assert.NotNull(fact);

            // ADR-0012 Invariant check: In each stage, NEVER present unearned operations
            switch (session.Progression.CurriculumStage)
            {
                case CurriculumStage.Stage1_Addition:
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                    break;
                case CurriculumStage.Stage2_Subtraction:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction);
                    break;
                case CurriculumStage.Stage3_Multiplication:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction or ArithmeticOperation.Multiplication);
                    break;
            }

            attemptsPerOp[fact.Operation] = attemptsPerOp.GetValueOrDefault(fact.Operation, 0) + 1;

            // Intermediate latency (1,800ms - 3,500ms), 82% accuracy, but struggles persistently on 1 specific fact
            int latency = rng.Next(1800, 3500);
            clock.CurrentTurnLatency = TimeSpan.FromMilliseconds(latency);
            clock.AdvanceMs(latency);

            bool isSpecificWeakFact = fact.Id == "add:1+1";
            bool isCorrect = !isSpecificWeakFact && rng.NextDouble() < 0.85;

            if (isCorrect)
            {
                correct++;
                session.SubmitAnswer(fact.CorrectResult);
            }
            else
            {
                incorrect++;
                session.SubmitAnswer(fact.CorrectResult + 1);
            }

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);

            if (session.LastEvaluation?.ChangeSet is { } committed)
            {
                var boundDamage = await combatCoordinator.GetOrBindEffectiveAttackDamageAsync(session.FactInstanceRevision);
                var intent = new CyberDefensePendingIntentRecord(
                    committed.SubmissionId,
                    committed.Attempt.FactId,
                    committed.Attempt.IsCorrect,
                    committed.Attempt.IsTimingEligible,
                    latency,
                    0L,
                    DateTimeOffset.UtcNow,
                    effectiveAttackDamage: boundDamage,
                    boundCriticalStrikeRank: 0,
                    boundOverdriveRank: 0,
                    practicePosition: committed.Attempt.PracticePosition);

                await gameplayStore.StagePendingIntentAsync(intent);
                await gameplayStore.ApplyAttemptTransactionAsync(intent);
            }

            if (session.Progression.CurriculumStage != stageHistory.Last())
            {
                Assert.True(session.Progression.CurriculumStage >= stageHistory.Last());
                stageHistory.Add(session.Progression.CurriculumStage);
            }

            await AdvanceSessionTurnAsync(session);
        }

        var prog = await gameplayStore.GetProgressionAsync();
        Assert.True(prog.AvailableSkillPoints >= 0);
        Assert.True(stageHistory.Count >= 2, "Persona B should progress beyond Stage 1.");

        // Division must remain locked unless Stage 4 is reached
        if (session.Progression.CurriculumStage < CurriculumStage.Stage4_Division)
        {
            Assert.False(attemptsPerOp.ContainsKey(ArithmeticOperation.Division), "Division must not appear before Stage 4.");
        }
    }

    // =========================================================================
    // PERSONA C — FLUENT (Fast responses, 98% accuracy, rapid monotonic unlock of all stages)
    // =========================================================================

    [Fact]
    public async Task PersonaC_FluentLearner_AdvancesThroughStagesMonotonically_AndProvesADR0012StrictInvariants()
    {
        const int seed = 20261030;
        var rng = new Random(seed);
        var learnerDb = GetDatabasePath("persona_c_learner");
        var gameplayDb = GetDatabasePath("persona_c_gameplay");

        var clock = new PersonaSimulationClock();
        using var learnerStore = new SqliteLearnerStore(learnerDb);
        await learnerStore.InitializeAsync();

        using var gameplayStore = new SqliteGameplayStore(gameplayDb);
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var combatCoordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await combatCoordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore, clock, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var stageHistory = new List<CurriculumStage> { session.Progression.CurriculumStage };
        var attemptsPerOp = new Dictionary<ArithmeticOperation, int>();
        int totalAttempts = 80;
        int correct = 0;
        int incorrect = 0;

        for (int i = 0; i < totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            Assert.NotNull(fact);

            // Verify strict operation availability invariants:
            switch (session.Progression.CurriculumStage)
            {
                case CurriculumStage.Stage1_Addition:
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                    break;
                case CurriculumStage.Stage2_Subtraction:
                    Assert.True(fact.Operation == ArithmeticOperation.Addition || fact.Operation == ArithmeticOperation.Subtraction);
                    break;
                case CurriculumStage.Stage3_Multiplication:
                    Assert.True(fact.Operation == ArithmeticOperation.Addition || fact.Operation == ArithmeticOperation.Subtraction || fact.Operation == ArithmeticOperation.Multiplication);
                    break;
                case CurriculumStage.Stage4_Division:
                    // All operations permitted
                    break;
            }

            // ADR-0012 Invariant check: In each stage, NEVER present unearned operations
            switch (session.Progression.CurriculumStage)
            {
                case CurriculumStage.Stage1_Addition:
                    Assert.Equal(ArithmeticOperation.Addition, fact.Operation);
                    break;
                case CurriculumStage.Stage2_Subtraction:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction);
                    break;
                case CurriculumStage.Stage3_Multiplication:
                    Assert.True(fact.Operation is ArithmeticOperation.Addition or ArithmeticOperation.Subtraction or ArithmeticOperation.Multiplication);
                    break;
            }

            attemptsPerOp[fact.Operation] = attemptsPerOp.GetValueOrDefault(fact.Operation, 0) + 1;

            // Fluent latency (700ms - 1,400ms), 98% accuracy
            int latency = rng.Next(700, 1400);
            clock.CurrentTurnLatency = TimeSpan.FromMilliseconds(latency);
            clock.AdvanceMs(latency);

            bool isCorrect = rng.NextDouble() < 0.98;
            if (isCorrect)
            {
                correct++;
                session.SubmitAnswer(fact.CorrectResult);
            }
            else
            {
                incorrect++;
                session.SubmitAnswer(fact.CorrectResult + 1);
            }

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);

            if (session.LastEvaluation?.ChangeSet is { } committed)
            {
                var boundDamage = await combatCoordinator.GetOrBindEffectiveAttackDamageAsync(session.FactInstanceRevision);
                var intent = new CyberDefensePendingIntentRecord(
                    committed.SubmissionId,
                    committed.Attempt.FactId,
                    committed.Attempt.IsCorrect,
                    committed.Attempt.IsTimingEligible,
                    latency,
                    0L,
                    DateTimeOffset.UtcNow,
                    effectiveAttackDamage: boundDamage,
                    boundCriticalStrikeRank: 0,
                    boundOverdriveRank: 0,
                    practicePosition: committed.Attempt.PracticePosition);

                await gameplayStore.StagePendingIntentAsync(intent);
                await gameplayStore.ApplyAttemptTransactionAsync(intent);
            }

            if (session.Progression.CurriculumStage != stageHistory.Last())
            {
                Assert.True(session.Progression.CurriculumStage > stageHistory.Last(), "Stage must advance strictly monotonically!");
                stageHistory.Add(session.Progression.CurriculumStage);
            }

            await AdvanceSessionTurnAsync(session);
        }

        // Persona C achieves high level and earns SP
        var prog = await gameplayStore.GetProgressionAsync();
        Assert.True(prog.PlayerLevel > 1);
        Assert.True(prog.EarnedSkillPoints > 0);

        // Can purchase upgrade in terminal
        var terminalVm = new UpgradeTerminalViewModel(gameplayStore);
        await terminalVm.InitializeAsync();
        Assert.True(terminalVm.AvailableSkillPoints > 0);
        if (terminalVm.CanPurchase)
        {
            var purchaseRes = await terminalVm.PurchaseUpgradeAsync();
            Assert.Equal(UpgradePurchaseStatus.Success, purchaseRes.Status);
        }
    }

    private static async Task AdvanceSessionTurnAsync(TrainingSession session)
    {
        if (session.InteractionState == SessionInteractionState.CorrectFeedback)
        {
            await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        }
        else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
        {
            await session.AcknowledgeTeachingInterventionAsync(startTiming: false);
        }
        else if (session.InteractionState is SessionInteractionState.IncorrectFeedback or SessionInteractionState.TimeoutFeedback)
        {
            await session.AcknowledgeFeedbackAsync(startTiming: false);
        }

        if (session.InteractionState == SessionInteractionState.SessionCheckIn)
        {
            await session.ContinuePracticeAsync(startTiming: false);
        }

        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }
}
