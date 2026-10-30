namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Deterministic offline headless simulations with three synthetic learner personas (Issue #112).
/// Each persona drives a real <see cref="TrainingSession"/> in fresh CurriculumManaged mode against isolated
/// temporary learner and gameplay SQLite stores, a fake clock and a seeded answer generator, and applies
/// committed attempts to Cyber Defense combat with synthetic timestamps.
/// The tests pin the measured outcome of each seeded run; they are a progression-safety probe, not a balance assessment.
/// </summary>
public sealed class Issue112SyntheticPersonaSimulationTests : IDisposable
{
    private static readonly DateTimeOffset SyntheticEpoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly PersonaProfile BeginnerPersona = new("A-Beginner", Seed: 42, Attempts: 50, CorrectProbability: 0.55, MinLatencyMs: 4500, MaxLatencyMs: 8000, PersistentWeakFactId: null);
    private static readonly PersonaProfile IntermediatePersona = new("B-Intermediate", Seed: 1337, Attempts: 75, CorrectProbability: 0.85, MinLatencyMs: 1800, MaxLatencyMs: 3500, PersistentWeakFactId: "add:1+1");
    private static readonly PersonaProfile FluentPersona = new("C-Fluent", Seed: 20261030, Attempts: 80, CorrectProbability: 0.98, MinLatencyMs: 700, MaxLatencyMs: 1400, PersistentWeakFactId: null);

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MathFirstPersonaSim_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public Issue112SyntheticPersonaSimulationTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // The pinned metrics below characterize the current ADR-0012 tolerant introduction gate (prerequisite facts
    // attempted, at most one remediation, no broad weakness). They record observed pacing; they do not endorse it.

    // =========================================================================
    // PERSONA A — BEGINNER (slow responses, 55% answer probability)
    // =========================================================================

    [Fact]
    public async Task PersonaA_Beginner_ProgressesOnlyThroughStageGatedOperations_AndSurvives()
    {
        var result = await RunPersonaAsync(BeginnerPersona);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(BeginnerPersona, result);
        Assert.True(result.BroadWeaknessObservations > 0, "A struggling beginner should trigger broad-weakness remediation.");
        Assert.True(result.TeachingInterventions > 0, "A struggling beginner should receive teaching interventions.");

        Assert.Equal(
            new PersonaMetrics(
                Correct: 32, Incorrect: 18,
                Transitions: "Stage2_Subtraction@4,Stage3_Multiplication@9,Stage4_Division@39",
                OperationCounts: "Addition:19,Subtraction:16,Multiplication:13,Division:2",
                TimingEligibleAttempts: 50, TeachingInterventions: 5, BroadWeaknessObservations: 35,
                OpponentsDefeated: 11, GameOvers: 0, FinalPlayerHp: 32, PlayerLevel: 3, CumulativeXp: 264),
            result.Metrics);
    }

    // =========================================================================
    // PERSONA B — INTERMEDIATE (moderate pace, 85% answer probability, always misses add:1+1)
    // =========================================================================

    [Fact]
    public async Task PersonaB_Intermediate_ProgressesWithoutDeadlockFromPersistentWeakFact()
    {
        var result = await RunPersonaAsync(IntermediatePersona);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(IntermediatePersona, result);
        Assert.True(result.StageTransitions.Count > 0, "One persistently missed prerequisite fact must not deadlock stage advancement.");
        Assert.True(result.TeachingInterventions > 0);

        Assert.Equal(
            new PersonaMetrics(
                Correct: 47, Incorrect: 28,
                Transitions: "Stage2_Subtraction@7,Stage3_Multiplication@17,Stage4_Division@30",
                OperationCounts: "Addition:28,Subtraction:20,Multiplication:16,Division:11",
                TimingEligibleAttempts: 75, TeachingInterventions: 10, BroadWeaknessObservations: 34,
                OpponentsDefeated: 13, GameOvers: 1, FinalPlayerHp: 94, PlayerLevel: 4, CumulativeXp: 368),
            result.Metrics);
    }

    // =========================================================================
    // PERSONA C — FLUENT (fast responses, 98% answer probability)
    // =========================================================================

    [Fact]
    public async Task PersonaC_Fluent_AdvancesMonotonically_EarnsSkillPoints_AndCanPurchaseUpgrade()
    {
        var result = await RunPersonaAsync(FluentPersona, purchaseAttackUpgrade: true);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(FluentPersona, result);
        Assert.True(result.EarnedSkillPoints > 0);
        Assert.Equal(UpgradePurchaseStatus.Success, result.AttackPurchaseStatus);

        Assert.Equal(
            new PersonaMetrics(
                Correct: 79, Incorrect: 1,
                Transitions: "Stage2_Subtraction@4,Stage3_Multiplication@9,Stage4_Division@19",
                OperationCounts: "Addition:24,Subtraction:22,Multiplication:19,Division:15",
                TimingEligibleAttempts: 80, TeachingInterventions: 0, BroadWeaknessObservations: 0,
                OpponentsDefeated: 24, GameOvers: 0, FinalPlayerHp: 100, PlayerLevel: 6, CumulativeXp: 621),
            result.Metrics);
    }

    // =========================================================================
    // REPRODUCIBILITY
    // =========================================================================

    [Fact]
    public async Task Personas_RepeatedSeededRuns_ProduceIdenticalTraces()
    {
        foreach (var persona in new[] { BeginnerPersona, IntermediatePersona, FluentPersona })
        {
            var first = await RunPersonaAsync(persona);
            var second = await RunPersonaAsync(persona);

            Assert.Equal(first.FactTrace, second.FactTrace);
            Assert.Equal(first.Metrics, second.Metrics);
            Assert.Equal(first.StageTransitions, second.StageTransitions);
            Assert.Equal(first.FinalPlayerHp, second.FinalPlayerHp);
            Assert.Equal(first.CumulativeXp, second.CumulativeXp);
        }
    }

    // =========================================================================
    // SIMULATION
    // =========================================================================

    private static void AssertCommonInvariants(PersonaProfile persona, PersonaResult result)
    {
        Assert.Equal(persona.Attempts, result.Correct + result.Incorrect);
        Assert.Equal(CurriculumStage.Stage1_Addition, result.InitialStage);
        Assert.Equal(ArithmeticOperation.Addition, result.FirstOperation);

        var stages = new[] { result.InitialStage }.Concat(result.StageTransitions.Select(t => t.Stage)).ToArray();
        for (int i = 1; i < stages.Length; i++)
        {
            Assert.True(stages[i] > stages[i - 1], $"Curriculum stage regressed or repeated: {stages[i - 1]} -> {stages[i]}.");
        }

        // ADR-0012 weakness boundary: no stage was earned on an attempt that left broad weakness active.
        Assert.DoesNotContain(result.StageTransitions, t => result.BroadWeaknessAttempts.Contains(t.Attempt));

        // Operations appear only after the stage that unlocks them.
        foreach (var (operation, unlockingStage) in new[]
        {
            (ArithmeticOperation.Subtraction, CurriculumStage.Stage2_Subtraction),
            (ArithmeticOperation.Multiplication, CurriculumStage.Stage3_Multiplication),
            (ArithmeticOperation.Division, CurriculumStage.Stage4_Division)
        })
        {
            if (!result.FirstAttemptByOperation.TryGetValue(operation, out var firstAttempt))
            {
                continue;
            }
            var unlock = result.StageTransitions.FirstOrDefault(t => t.Stage == unlockingStage);
            Assert.True(unlock.Attempt > 0 && unlock.Attempt < firstAttempt, $"{operation} appeared at attempt {firstAttempt} before {unlockingStage} unlocked.");
        }

        if (result.FinalStage < CurriculumStage.Stage3_Multiplication)
        {
            Assert.False(result.AttemptsPerOperation.ContainsKey(ArithmeticOperation.Multiplication), "Multiplication must not be presented before Stage 3.");
        }
        if (result.FinalStage < CurriculumStage.Stage4_Division)
        {
            Assert.False(result.AttemptsPerOperation.ContainsKey(ArithmeticOperation.Division), "Division must not be presented before Stage 4.");
        }

        Assert.Equal(persona.Attempts, result.AppliedCombatReceipts);
    }

    private async Task<PersonaResult> RunPersonaAsync(PersonaProfile persona, bool purchaseAttackUpgrade = false)
    {
        var rng = new Random(persona.Seed);
        var clock = new PersonaSimulationClock();
        string runId = Guid.NewGuid().ToString("N");

        using var learnerStore = new SqliteLearnerStore(Path.Combine(_tempDir, $"{persona.Name}_{runId}_learner.db"));
        await learnerStore.InitializeAsync();
        using var gameplayStore = new SqliteGameplayStore(Path.Combine(_tempDir, $"{persona.Name}_{runId}_gameplay.db"));
        await gameplayStore.InitializeAsync();

        var consumer = new CyberDefenseSubmissionConsumer(gameplayStore, learnerStore);
        var combatCoordinator = new CyberDefenseCombatCoordinator(gameplayStore, consumer);
        await combatCoordinator.InitializeAsync();

        var session = new TrainingSession(learnerStore, clock, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var result = new PersonaResult
        {
            InitialStage = session.Progression.CurriculumStage,
            FirstOperation = session.CurrentFact.Operation
        };
        var currentStage = result.InitialStage;

        for (int attempt = 1; attempt <= persona.Attempts; attempt++)
        {
            var fact = session.CurrentFact;
            Assert.NotNull(fact);
            Assert.Contains(fact.Operation, CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage));
            result.FactTrace.Add(fact.Id);
            result.AttemptsPerOperation[fact.Operation] = result.AttemptsPerOperation.GetValueOrDefault(fact.Operation) + 1;
            result.FirstAttemptByOperation.TryAdd(fact.Operation, attempt);

            int latency = rng.Next(persona.MinLatencyMs, persona.MaxLatencyMs);
            clock.CurrentTurnLatency = TimeSpan.FromMilliseconds(latency);
            clock.AdvanceMs(latency);

            bool isPersistentWeakFact = fact.Id == persona.PersistentWeakFactId;
            bool isCorrect = !isPersistentWeakFact && rng.NextDouble() < persona.CorrectProbability;
            session.SubmitAnswer(isCorrect ? fact.CorrectResult : fact.CorrectResult + 1);
            if (isCorrect) result.Correct++; else result.Incorrect++;

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);

            if (session.InteractionState == SessionInteractionState.TeachingIntervention) result.TeachingInterventions++;
            if (session.HasBroadWeakness) result.BroadWeaknessAttempts.Add(attempt);

            if (session.LastEvaluation?.ChangeSet is { } committed)
            {
                if (committed.Attempt.IsTimingEligible) result.TimingEligibleAttempts++;
                var boundDamage = await combatCoordinator.GetOrBindEffectiveAttackDamageAsync(session.FactInstanceRevision);
                var intent = new CyberDefensePendingIntentRecord(
                    committed.SubmissionId,
                    committed.Attempt.FactId,
                    committed.Attempt.IsCorrect,
                    committed.Attempt.IsTimingEligible,
                    latency,
                    0L,
                    SyntheticEpoch.AddMilliseconds(clock.GetTimestamp()),
                    effectiveAttackDamage: boundDamage,
                    boundCriticalStrikeRank: 0,
                    boundOverdriveRank: 0,
                    practicePosition: committed.Attempt.PracticePosition);

                await gameplayStore.StagePendingIntentAsync(intent);
                var receipt = await gameplayStore.ApplyAttemptTransactionAsync(intent);
                if (receipt.ReceiptKind == CyberDefenseReceiptKind.Applied && receipt.TransitionResult is { } transition)
                {
                    result.AppliedCombatReceipts++;
                    if (transition.IsOpponentDefeated) result.OpponentsDefeated++;
                    if (transition.IsGameOver) result.GameOvers++;
                }
            }

            if (session.Progression.CurriculumStage != currentStage)
            {
                currentStage = session.Progression.CurriculumStage;
                result.StageTransitions.Add((attempt, currentStage));
            }

            await AdvanceSessionTurnAsync(session);
        }

        var runState = await gameplayStore.GetRunStateAsync();
        var progression = await gameplayStore.GetProgressionAsync();
        result.FinalStage = session.Progression.CurriculumStage;
        result.FinalPlayerHp = runState.PlayerCurrentHp;
        result.FinalSector = runState.Sector;
        result.FinalOpponentIndex = runState.OpponentIndex;
        result.PlayerLevel = progression.PlayerLevel;
        result.CumulativeXp = progression.CumulativeXp;
        result.EarnedSkillPoints = progression.EarnedSkillPoints;

        if (purchaseAttackUpgrade)
        {
            using var terminal = new UpgradeTerminalViewModel(gameplayStore);
            await terminal.InitializeAsync();
            Assert.Equal(progression.AvailableSkillPoints, terminal.AvailableSkillPoints);
            Assert.True(terminal.CanPurchase, "A fluent learner with earned SP must be able to buy Attack rank 1.");
            result.AttackPurchaseStatus = (await terminal.PurchaseUpgradeAsync()).Status;
        }

        return result;
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

    private sealed record PersonaProfile(
        string Name,
        int Seed,
        int Attempts,
        double CorrectProbability,
        int MinLatencyMs,
        int MaxLatencyMs,
        string? PersistentWeakFactId);

    private sealed record PersonaMetrics(
        int Correct,
        int Incorrect,
        string Transitions,
        string OperationCounts,
        int TimingEligibleAttempts,
        int TeachingInterventions,
        int BroadWeaknessObservations,
        int OpponentsDefeated,
        int GameOvers,
        int FinalPlayerHp,
        int PlayerLevel,
        long CumulativeXp);

    private sealed class PersonaResult
    {
        public CurriculumStage InitialStage { get; init; }
        public ArithmeticOperation FirstOperation { get; init; }
        public CurriculumStage FinalStage { get; set; }
        public List<(int Attempt, CurriculumStage Stage)> StageTransitions { get; } = [];
        public Dictionary<ArithmeticOperation, int> AttemptsPerOperation { get; } = [];
        public Dictionary<ArithmeticOperation, int> FirstAttemptByOperation { get; } = [];
        public HashSet<int> BroadWeaknessAttempts { get; } = [];
        public List<string> FactTrace { get; } = [];
        public int Correct { get; set; }
        public int Incorrect { get; set; }
        public int TimingEligibleAttempts { get; set; }
        public int TeachingInterventions { get; set; }
        public int BroadWeaknessObservations => BroadWeaknessAttempts.Count;
        public int AppliedCombatReceipts { get; set; }
        public int OpponentsDefeated { get; set; }
        public int GameOvers { get; set; }
        public int FinalPlayerHp { get; set; }
        public int FinalSector { get; set; }
        public int FinalOpponentIndex { get; set; }
        public int PlayerLevel { get; set; }
        public long CumulativeXp { get; set; }
        public int EarnedSkillPoints { get; set; }
        public UpgradePurchaseStatus? AttackPurchaseStatus { get; set; }

        public string TransitionsText => string.Join(",", StageTransitions.Select(t => $"{t.Stage}@{t.Attempt}"));
        public string OperationCountsText => string.Join(",", AttemptsPerOperation.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"));

        public PersonaMetrics Metrics => new(
            Correct, Incorrect, TransitionsText, OperationCountsText, TimingEligibleAttempts, TeachingInterventions,
            BroadWeaknessObservations, OpponentsDefeated, GameOvers, FinalPlayerHp, PlayerLevel, CumulativeXp);

        public string Describe()
        {
            var builder = new StringBuilder();
            builder.AppendLine(CultureInfo.InvariantCulture, $"correct={Correct} incorrect={Incorrect} accuracy={(double)Correct / (Correct + Incorrect):P1}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"initialStage={InitialStage} finalStage={FinalStage} transitions=[{TransitionsText}]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"attemptsPerOperation=[{OperationCountsText}] firstAttempt=[{string.Join(",", FirstAttemptByOperation.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"))}]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"timingEligible={TimingEligibleAttempts} teachingInterventions={TeachingInterventions} broadWeaknessObservations={BroadWeaknessObservations}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"combatReceipts={AppliedCombatReceipts} opponentsDefeated={OpponentsDefeated} gameOvers={GameOvers} finalHp={FinalPlayerHp} sector={FinalSector} opponentIndex={FinalOpponentIndex}");
            builder.Append(CultureInfo.InvariantCulture, $"playerLevel={PlayerLevel} cumulativeXp={CumulativeXp} earnedSp={EarnedSkillPoints} attackPurchase={AttackPurchaseStatus?.ToString() ?? "n/a"}");
            return builder.ToString();
        }
    }

    private sealed class PersonaSimulationClock : IClock
    {
        private long _timestamp = 10_000_000;
        public TimeSpan CurrentTurnLatency { get; set; } = TimeSpan.FromMilliseconds(1000);

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) => CurrentTurnLatency;

        public void AdvanceMs(long ms) => _timestamp += ms;
    }
}
