namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Issue #114 / ADR-0013: deterministic synthetic learner personas driving a real CurriculumManaged
/// <see cref="TrainingSession"/> against isolated temporary SQLite stores and a fake clock. Answers are scripted
/// per fact encounter, so every run is reproducible without random seeds. Pinned positions record measured
/// behavior of the D10 proficiency unlock policy; they are not a balance study of real learners.
/// </summary>
public sealed class Issue114ProficiencyUnlockPersonaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue114_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public Issue114ProficiencyUnlockPersonaTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly Func<ArithmeticFact, int, bool> AlwaysCorrect = (_, _) => true;

    // =========================================================================
    // 1. FRESH ADDITION-ONLY LEARNER / 4. FAST FLUENT ADULT / 6-9. D10 BOUNDARIES
    // =========================================================================

    [Fact]
    public async Task Persona01_FastFluentAdult_UnlocksEachOperationOnlyAfterD10MasteryWithoutUnseenFacts()
    {
        var result = await RunPersonaAsync(new Persona("FastFluent", 3000, LatencyMs: 800, AlwaysCorrect), stopAtStage4: true);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        Assert.Equal(CurriculumStage.Stage4_Division, result.FinalStage);
        Assert.Equal(0, result.Incorrect);

        // The first attempts belong to Addition only, through every Dense band up to and including D10.
        var stage2 = result.TransitionAt(CurriculumStage.Stage2_Subtraction);
        Assert.All(result.Trace.Take(stage2), turn => Assert.Equal(ArithmeticOperation.Addition, turn.Operation));
        Assert.True(stage2 >= CurriculumUnlockPolicy.GetMasteryFactIds(ArithmeticOperation.Addition).Count);

        AssertTransitionEvidence(result);
        Assert.Equal(PinnedFastFluentTransitions, result.TransitionsText);
    }

    [Theory]
    [InlineData("add:10+10", CurriculumStage.Stage2_Subtraction)]
    [InlineData("sub:10-9", CurriculumStage.Stage3_Multiplication)]
    [InlineData("sub:10-10", CurriculumStage.Stage3_Multiplication)]
    [InlineData("mul:10*10", CurriculumStage.Stage4_Division)]
    public async Task Persona06To08_BoundaryFactMissedTwice_DelaysUnlockUntilBoundaryRecovered(string boundaryFactId, CurriculumStage blockedStage)
    {
        var persona = new Persona(
            "BoundaryMiss_" + boundaryFactId,
            3000,
            LatencyMs: 900,
            (fact, encounter) => fact.Id != boundaryFactId || encounter > 2);
        var result = await RunPersonaAsync(persona, stopAtStage4: true);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        AssertTransitionEvidence(result);
        Assert.Equal(CurriculumStage.Stage4_Division, result.FinalStage);

        var unlock = result.TransitionAt(blockedStage);
        var boundaryTurns = result.Trace.Where(turn => turn.FactId == boundaryFactId).ToList();
        Assert.True(boundaryTurns.Count >= 3, "A twice-missed boundary fact needs a later correct recovery retrieval.");
        Assert.False(boundaryTurns[0].IsCorrect);
        Assert.False(boundaryTurns[1].IsCorrect);
        Assert.True(boundaryTurns[2].IsCorrect);
        Assert.True(boundaryTurns[2].Attempt <= unlock, "The stage must not unlock before the boundary fact is recovered.");
        Assert.True(boundaryTurns[2].Attempt - boundaryTurns[1].Attempt > 1, "Recovery must come from a later, spaced presentation.");

        var fastline = await RunPersonaAsync(new Persona("FastFluentReference", 3000, LatencyMs: 900, AlwaysCorrect), stopAtStage4: true);
        Assert.True(unlock > fastline.TransitionAt(blockedStage), "A missed boundary fact must delay the dependent unlock relative to an error-free learner.");
    }

    // =========================================================================
    // 2. BEGINNER REPEATEDLY MISSING 4 + 1
    // =========================================================================

    [Fact]
    public async Task Persona02_BeginnerAlwaysMissing4Plus1_StaysInAccessibleAdditionWithTargetedRemediation()
    {
        var persona = new Persona(
            "Beginner4Plus1",
            400,
            LatencyMs: 7000,
            (fact, _) => fact.Id != "add:4+1");
        var result = await RunPersonaAsync(persona);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        Assert.Equal(CurriculumStage.Stage1_Addition, result.FinalStage);
        Assert.Empty(result.Transitions);
        Assert.All(result.Trace, turn => Assert.Equal(ArithmeticOperation.Addition, turn.Operation));

        // ADD-D04 (BandIndex 3) owns 4+1 and has nine facts, so it needs 100% latest correctness: the learner stays there.
        Assert.Equal(3, result.FinalBandIndex[ArithmeticOperation.Addition]);
        Assert.True(result.Trace.Count(turn => turn.FactId == "add:4+1") >= 40, "The precise weak fact must keep being revisited.");
        Assert.Contains(result.Trace, turn => turn.Role == nameof(PracticeSelectionRole.Remediation));

        // Accessible, already-correct facts (including earlier bands) stay in the mix next to the weak frontier.
        var ownership = new AcquisitionOwnershipResolver(new ArithmeticCurriculum().Addition);
        var lateAccessible = result.Trace.Where(turn => turn.Attempt > 200 && turn.FactId != "add:4+1").ToList();
        Assert.True(lateAccessible.Select(turn => turn.FactId).Distinct().Count() >= 5);
        Assert.All(lateAccessible, turn => Assert.True(turn.IsCorrect));
        Assert.Contains(lateAccessible, turn => ownership.TryGetOwner(turn.FactId, 3, out var owner) && owner < 3);
        _output.WriteLine($"late accessible distinct facts={lateAccessible.Select(turn => turn.FactId).Distinct().Count()} share4Plus1={result.Trace.Count(turn => turn.FactId == "add:4+1")}/{result.Trace.Count}");

        Assert.Equal(PinnedBeginner4Plus1Metrics, result.MetricsText);
    }

    [Fact]
    public async Task Persona02b_BeginnerWhoRecovers4Plus1_EventuallyEarnsSubtraction()
    {
        var persona = new Persona(
            "Beginner4Plus1Recovers",
            3000,
            LatencyMs: 7000,
            (fact, encounter) => fact.Id != "add:4+1" || encounter > 6);
        var result = await RunPersonaAsync(persona, stopAtStage: CurriculumStage.Stage2_Subtraction);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        AssertTransitionEvidence(result);
        Assert.Equal(CurriculumStage.Stage2_Subtraction, result.FinalStage);

        var lastMiss = result.Trace.Last(turn => turn.FactId == "add:4+1" && !turn.IsCorrect).Attempt;
        Assert.True(result.TransitionAt(CurriculumStage.Stage2_Subtraction) > lastMiss);
        Assert.Equal(PinnedBeginnerRecoveryTransitions, result.TransitionsText);
    }

    // =========================================================================
    // 3. SLOW BUT CONSISTENTLY CORRECT LEARNER
    // =========================================================================

    [Fact]
    public async Task Persona03_SlowButCorrectLearner_IsNotPenalizedForLatency()
    {
        var slow = await RunPersonaAsync(new Persona("SlowCorrect", 3000, LatencyMs: 9000, AlwaysCorrect), stopAtStage4: true);
        var fast = await RunPersonaAsync(new Persona("FastCorrect", 3000, LatencyMs: 800, AlwaysCorrect), stopAtStage4: true);
        _output.WriteLine(slow.Describe());
        _output.WriteLine(fast.Describe());

        AssertCommonInvariants(slow);
        AssertTransitionEvidence(slow);
        Assert.Equal(0, slow.Incorrect);
        Assert.Equal(CurriculumStage.Stage4_Division, slow.FinalStage);
        // 9 s versus 0.8 s per correct answer: identical unlock positions and identical presented facts.
        Assert.Equal(PinnedFastFluentTransitions, slow.TransitionsText);
        Assert.Equal(fast.TransitionsText, slow.TransitionsText);
        Assert.Equal(fast.Trace.Select(turn => turn.FactId), slow.Trace.Select(turn => turn.FactId));
    }

    // =========================================================================
    // 5. PERSISTENT WEAKNESS NEAR D10 (90% DENSE RULE GAP)
    // =========================================================================

    [Fact]
    public async Task Persona05_PersistentWeaknessInsideD10_AdditionBandAdvancesButSubtractionWaits()
    {
        const string weakFact = "add:10+7";
        var persona = new Persona(
            "WeakNearD10",
            3000,
            LatencyMs: 1500,
            (fact, encounter) => fact.Id != weakFact || encounter > 20);
        var result = await RunPersonaAsync(persona, stopAtStage: CurriculumStage.Stage2_Subtraction);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        AssertTransitionEvidence(result);

        // ADD-D10 has 21 facts and advances at 19 latest-correct, so the Dense rule alone leaves 10+7 unresolved.
        var addBand10Reached = result.Trace.First(turn => turn.BandIndexAfter[ArithmeticOperation.Addition] >= 10).Attempt;
        var weakTurns = result.Trace.Where(turn => turn.FactId == weakFact).ToList();
        var stage2 = result.TransitionAt(CurriculumStage.Stage2_Subtraction);

        _output.WriteLine($"addBand10Reached={addBand10Reached} stage2={stage2} weakTurns={string.Join(",", weakTurns.Select(turn => $"{turn.Attempt}{(turn.IsCorrect ? "+" : "x")}"))}");
        Assert.True(addBand10Reached < stage2, "The Dense 90% rule advanced Addition past D10 while 10+7 was unresolved.");
        Assert.True(weakTurns.Count(turn => !turn.IsCorrect && turn.Attempt <= addBand10Reached) >= 1);
        Assert.True(weakTurns.Last(turn => turn.Attempt <= stage2).IsCorrect);
        Assert.Equal(PinnedWeakNearD10Transitions, result.TransitionsText);
    }

    // =========================================================================
    // 9. DIVISION STARTS AT ITS OWN FOUNDATIONAL BAND
    // =========================================================================

    [Fact]
    public async Task Persona09_DivisionAndEachNewOperation_StartFromTheirOwnIntroductoryBand()
    {
        var result = await RunPersonaAsync(new Persona("FastFluentDivision", 3100, LatencyMs: 800, AlwaysCorrect), stopAfterStage4Attempts: 40);
        _output.WriteLine(result.Describe());
        AssertCommonInvariants(result);

        var curriculum = new ArithmeticCurriculum();
        foreach (var (operation, stage) in new[]
        {
            (ArithmeticOperation.Subtraction, CurriculumStage.Stage2_Subtraction),
            (ArithmeticOperation.Multiplication, CurriculumStage.Stage3_Multiplication),
            (ArithmeticOperation.Division, CurriculumStage.Stage4_Division)
        })
        {
            var unlockTurn = result.Trace[result.TransitionAt(stage) - 1];
            Assert.Equal(0, unlockTurn.BandIndexAfter[operation]);

            var firstTurn = result.Trace.First(turn => turn.Operation == operation);
            var ownership = new AcquisitionOwnershipResolver(curriculum.GetCurriculum(operation));
            Assert.True(ownership.TryGetOwner(firstTurn.FactId, 0, out var ownerBand));
            Assert.Equal(0, ownerBand);
        }

        // Earlier operations remain scheduled after Division unlocks.
        var afterDivision = result.Trace.Where(turn => turn.Attempt > result.TransitionAt(CurriculumStage.Stage4_Division)).ToList();
        Assert.Equal(40, afterDivision.Count);
        foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Contains(afterDivision, turn => turn.Operation == operation);
        }
    }

    // =========================================================================
    // 10. EXISTING LEARNERS AT STAGES 2, 3 AND 4
    // =========================================================================

    [Theory]
    [InlineData(CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage3_Multiplication)]
    [InlineData(CurriculumStage.Stage4_Division)]
    public async Task Persona10_ExistingEarnedStage_IsRetainedWithoutD10EvidenceAndKeepsAllUnlockedOperations(CurriculumStage earnedStage)
    {
        var path = Path.Combine(_directory, $"existing_{earnedStage}.db");
        using (var seed = new SqliteLearnerStore(path))
        {
            await seed.InitializeAsync();
            var progression = LearnerProgression.CreateFresh();
            progression.CurriculumStage = earnedStage;
            await seed.SaveProgressionAsync(progression);
        }

        // Every third answer is wrong: no prerequisite mastery evidence can exist, yet the earned stage stays.
        var persona = new Persona("Existing_" + earnedStage, 90, LatencyMs: 2000, (fact, encounter) => encounter % 3 != 0);
        var result = await RunPersonaAsync(persona, databasePath: path);
        _output.WriteLine(result.Describe());

        Assert.Equal(earnedStage, result.InitialStage);
        Assert.Equal(earnedStage, result.FinalStage);
        Assert.Empty(result.Transitions);
        var unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(earnedStage);
        Assert.All(result.Trace, turn => Assert.Contains(turn.Operation, unlocked));
        foreach (var operation in unlocked)
        {
            Assert.Contains(result.Trace, turn => turn.Operation == operation);
        }
    }

    // =========================================================================
    // 11. RESTART AND RECOVERY
    // =========================================================================

    [Fact]
    public async Task Persona11_RestartingEveryHundredAttempts_PreservesStagesAndStillEarnsAllOperations()
    {
        var path = Path.Combine(_directory, "restart.db");
        var persona = new Persona("Restarting", 3000, LatencyMs: 1100, (fact, encounter) => !(fact.Operation == ArithmeticOperation.Subtraction && encounter == 1 && fact.LeftOperand == 7));
        var stageBeforeRestart = CurriculumStage.Stage1_Addition;
        var totalAttempts = 0;
        var stages = new List<CurriculumStage>();
        var transitions = new List<string>();

        for (var segment = 0; segment < 40 && stageBeforeRestart != CurriculumStage.Stage4_Division; segment++)
        {
            var result = await RunPersonaAsync(persona with { Attempts = 100 }, databasePath: path, stopAtStage4: true);
            Assert.Equal(stageBeforeRestart, result.InitialStage);
            Assert.True(result.FinalStage >= stageBeforeRestart);
            stages.Add(result.FinalStage);
            AssertTransitionEvidence(result);
            transitions.AddRange(result.Transitions.Select(t => $"{t.Stage}@{totalAttempts + t.Attempt}"));
            stageBeforeRestart = result.FinalStage;
            totalAttempts += result.Trace.Count;
            SqliteConnection.ClearAllPools();
        }

        _output.WriteLine($"restartSegments={stages.Count} totalAttempts={totalAttempts} transitions=[{string.Join(",", transitions)}]");
        Assert.Equal(CurriculumStage.Stage4_Division, stageBeforeRestart);
        Assert.Equal(PinnedRestartTransitions, string.Join(",", transitions));

        using var reopened = new SqliteLearnerStore(path);
        var snapshot = await reopened.LoadSnapshotAsync();
        Assert.Equal(CurriculumStage.Stage4_Division, snapshot.Progression.CurriculumStage);
        Assert.True(snapshot.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex >= CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);
        Assert.True(snapshot.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex >= CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);
        Assert.True(snapshot.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex >= CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);
        Assert.Equal(0, snapshot.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    // =========================================================================
    // 12. CUSTOM PRACTICE COMPATIBILITY
    // =========================================================================

    [Fact]
    public async Task Persona12_CustomPractice_RemainsIndependentOfCurriculumStage()
    {
        var path = Path.Combine(_directory, "custom.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var preferences = new FixedOperationsPreferenceStore(PracticeOperationPreferencePolicy.AllOperations);
        var session = new TrainingSession(store, new PersonaClock(), preferenceStore: preferences, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync(startTiming: false);

        var seen = new HashSet<ArithmeticOperation>();
        for (var i = 0; i < 40; i++)
        {
            seen.Add(session.CurrentFact.Operation);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            await AdvanceSessionTurnAsync(session);
        }

        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations.ToHashSet(), seen);
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // 14. NO STARVATION BETWEEN NEW, DUE AND REMEDIATION
    // =========================================================================

    [Fact]
    public async Task Persona14_MixedLearner_KeepsNewReviewAndRemediationRolesAndAllUnlockedOperationsAlive()
    {
        // Misses each fact on its first encounter when (left + right) is divisible by 7, recovering afterwards.
        var persona = new Persona(
            "MixedLearner",
            3000,
            LatencyMs: 2500,
            (fact, encounter) => encounter > 1 || (fact.LeftOperand + fact.RightOperand) % 7 != 0);
        var result = await RunPersonaAsync(persona, stopAfterStage4Attempts: 100);
        _output.WriteLine(result.Describe());

        AssertCommonInvariants(result);
        AssertTransitionEvidence(result);
        Assert.Equal(CurriculumStage.Stage4_Division, result.FinalStage);

        var roles = result.Trace.Select(turn => turn.Role).ToHashSet();
        Assert.Contains(nameof(PracticeSelectionRole.New), roles);
        Assert.Contains(nameof(PracticeSelectionRole.Remediation), roles);
        Assert.True(roles.Contains(nameof(PracticeSelectionRole.Due)) || roles.Contains(nameof(PracticeSelectionRole.EarlyReview)));

        // Every operation unlocked at a turn is scheduled again within the following 30 attempts (no operation starvation).
        for (var index = 0; index + 30 <= result.Trace.Count; index += 30)
        {
            var window = result.Trace.Skip(index).Take(30).ToList();
            foreach (var operation in CurriculumUnlockPolicy.GetUnlockedOperations(window[0].StageBefore))
            {
                Assert.Contains(window, turn => turn.Operation == operation);
            }
        }

        // Every miss (except in the final 20 attempts) was revisited and recovered: remediation is not starved by New material.
        foreach (var miss in result.Trace.Where(turn => !turn.IsCorrect && turn.Attempt <= result.Trace.Count - 20))
        {
            Assert.Contains(result.Trace, turn => turn.FactId == miss.FactId && turn.IsCorrect && turn.Attempt > miss.Attempt);
        }

        Assert.Equal(PinnedMixedLearnerTransitions, result.TransitionsText);
    }

    [Fact]
    public async Task Personas_RepeatedRuns_AreBitwiseDeterministic()
    {
        var persona = new Persona("Determinism", 600, LatencyMs: 1300, (fact, encounter) => encounter > 1 || fact.LeftOperand != 6);
        var first = await RunPersonaAsync(persona);
        var second = await RunPersonaAsync(persona);

        Assert.Equal(first.Trace.Select(turn => turn.FactId), second.Trace.Select(turn => turn.FactId));
        Assert.Equal(first.TransitionsText, second.TransitionsText);
        Assert.Equal(first.MetricsText, second.MetricsText);
    }

    // =========================================================================
    // PINNED MEASUREMENTS
    // =========================================================================

    // Measured on the ADR-0013 policy at its introduction (Issue #114). A change here is a learning-policy change.
    // Issue #126 (owner-approved) re-measured the two personas whose post-D10 Addition practice now draws from the
    // incremental ADD-D11+ frontiers instead of ADD-P1 tens anchors: WeakNearD10 160 -> 162, MixedLearner Stage4 723 -> 753.
    private const string PinnedFastFluentTransitions = "Stage2_Subtraction@121,Stage3_Multiplication@254,Stage4_Division@618";
    private const string PinnedBeginner4Plus1Metrics = "attempts=400;correct=212;incorrect=188;transitions=[];operations=[Addition:400];bands=[Addition:3,Subtraction:0,Multiplication:0,Division:0]";
    private const string PinnedBeginnerRecoveryTransitions = "Stage2_Subtraction@134";
    private const string PinnedWeakNearD10Transitions = "Stage2_Subtraction@162";
    private const string PinnedMixedLearnerTransitions = "Stage2_Subtraction@148,Stage3_Multiplication@312,Stage4_Division@753";
    private const string PinnedRestartTransitions = "Stage2_Subtraction@121,Stage3_Multiplication@272,Stage4_Division@632";

    // =========================================================================
    // HARNESS
    // =========================================================================

    private static void AssertCommonInvariants(PersonaResult result)
    {
        var stages = new[] { result.InitialStage }.Concat(result.Transitions.Select(t => t.Stage)).ToArray();
        for (var i = 1; i < stages.Length; i++)
        {
            Assert.True(stages[i] > stages[i - 1], $"Curriculum stage regressed or repeated: {stages[i - 1]} -> {stages[i]}.");
        }

        foreach (var turn in result.Trace)
        {
            Assert.Contains(turn.Operation, CurriculumUnlockPolicy.GetUnlockedOperations(turn.StageBefore));
        }
    }

    /// <summary>
    /// Each transition happened on the first committed attempt whose resulting durable state satisfied the D10
    /// mastery predicate for the stage being left, and every D01..D10 prerequisite fact was actually attempted.
    /// </summary>
    private static void AssertTransitionEvidence(PersonaResult result)
    {
        foreach (var (attempt, stage) in result.Transitions)
        {
            var turn = result.Trace[attempt - 1];
            var leftStage = turn.StageBefore;
            Assert.Equal(stage, leftStage + 1);
            Assert.NotNull(turn.MasteryAfter);
            Assert.True(turn.MasteryAfter!.IsSatisfied);
            Assert.True(turn.MasteryAfter.BandIndex >= CurriculumUnlockPolicy.PrerequisiteMasteryBandCount);
            Assert.Empty(turn.MasteryAfter.UnresolvedBoundaryFactIds);
            Assert.Equal(turn.MasteryAfter.MasteryFactCount, turn.AttemptedMasteryFactsAfter);

            foreach (var earlier in result.Trace.Where(t => t.Attempt < attempt && t.StageBefore == leftStage))
            {
                Assert.False(earlier.MasteryAfter?.IsSatisfied == true && !earlier.HadBroadWeaknessAfter,
                    $"Mastery for {leftStage} was satisfied at attempt {earlier.Attempt} but the unlock waited until {attempt}.");
            }
        }
    }

    private async Task<PersonaResult> RunPersonaAsync(
        Persona persona,
        bool stopAtStage4 = false,
        CurriculumStage? stopAtStage = null,
        int? stopAfterStage4Attempts = null,
        string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(_directory, $"{persona.Name}_{Guid.NewGuid():N}.db");
        var clock = new PersonaClock { CurrentTurnLatency = TimeSpan.FromMilliseconds(persona.LatencyMs) };

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var session = new TrainingSession(store, clock, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        var result = new PersonaResult(persona.Name, session.Progression.CurriculumStage);
        var stageReachedAt = (int?)null;

        for (var attempt = 1; attempt <= persona.Attempts; attempt++)
        {
            var fact = session.CurrentFact;
            var stageBefore = session.Progression.CurriculumStage;

            // Encounters are counted from durable attempts so scripted answers survive restarts.
            var prior = await store.LoadItemStatesAsync([fact.Id]);
            var encounter = (prior.TryGetValue(fact.Id, out var priorState) ? priorState.TotalAttempts : 0) + 1;

            clock.AdvanceMs(persona.LatencyMs);
            var isCorrect = persona.Answer(fact, encounter);
            var evaluation = session.SubmitAnswer(isCorrect ? fact.CorrectResult : fact.CorrectResult + 1);
            var role = evaluation.ChangeSet?.Attempt.ResolvedRole ?? string.Empty;
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

            var stageAfter = session.Progression.CurriculumStage;
            PrerequisiteMasteryEvidence? mastery = null;
            var attemptedMastery = 0;
            if (CurriculumUnlockPolicy.GetPrerequisiteOperation(stageBefore) is { } prerequisite)
            {
                // Evaluate against durable store rows, not the session's lazily filled selection cache.
                var durable = await store.LoadItemStatesAsync(CurriculumUnlockPolicy.GetMasteryFactIds(prerequisite));
                mastery = CurriculumUnlockPolicy.EvaluatePrerequisiteMastery(stageBefore, durable, session.Progression.OperationProgressions);
                attemptedMastery = durable.Values.Count(state => state.TotalAttempts > 0);
            }

            result.Trace.Add(new PersonaTurn(
                attempt,
                fact.Id,
                fact.Operation,
                isCorrect,
                role,
                stageBefore,
                session.Progression.OperationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value.BandIndex),
                mastery,
                attemptedMastery,
                session.DeriveHasBroadWeakness()));
            if (isCorrect) result.Correct++; else result.Incorrect++;

            if (stageAfter != stageBefore)
            {
                result.Transitions.Add((attempt, stageAfter));
                if (stageAfter == CurriculumStage.Stage4_Division)
                {
                    stageReachedAt = attempt;
                }
            }

            await AdvanceSessionTurnAsync(session);

            if (stopAtStage is { } target && stageAfter >= target)
            {
                break;
            }

            if (stopAtStage4 && stageAfter == CurriculumStage.Stage4_Division && stopAfterStage4Attempts is null)
            {
                break;
            }

            if (stopAfterStage4Attempts is { } extra && stageReachedAt is { } reached && attempt - reached >= extra)
            {
                break;
            }
        }

        result.FinalStage = session.Progression.CurriculumStage;
        foreach (var (operation, progression) in session.Progression.OperationProgressions)
        {
            result.FinalBandIndex[operation] = progression.BandIndex;
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

    private sealed record Persona(string Name, int Attempts, int LatencyMs, Func<ArithmeticFact, int, bool> Answer);

    private sealed record PersonaTurn(
        int Attempt,
        string FactId,
        ArithmeticOperation Operation,
        bool IsCorrect,
        string Role,
        CurriculumStage StageBefore,
        IReadOnlyDictionary<ArithmeticOperation, int> BandIndexAfter,
        PrerequisiteMasteryEvidence? MasteryAfter,
        int AttemptedMasteryFactsAfter,
        bool HadBroadWeaknessAfter);

    private sealed class PersonaResult(string name, CurriculumStage initialStage)
    {
        public string Name { get; } = name;
        public CurriculumStage InitialStage { get; } = initialStage;
        public CurriculumStage FinalStage { get; set; }
        public List<PersonaTurn> Trace { get; } = [];
        public List<(int Attempt, CurriculumStage Stage)> Transitions { get; } = [];
        public Dictionary<ArithmeticOperation, int> FinalBandIndex { get; } = [];
        public int Correct { get; set; }
        public int Incorrect { get; set; }

        public int TransitionAt(CurriculumStage stage) =>
            Transitions.Single(transition => transition.Stage == stage).Attempt;

        public string TransitionsText => string.Join(",", Transitions.Select(t => $"{t.Stage}@{t.Attempt}"));

        public string OperationCountsText => string.Join(
            ",",
            Trace.GroupBy(turn => turn.Operation).OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}"));

        public string MetricsText => string.Create(
            CultureInfo.InvariantCulture,
            $"attempts={Trace.Count};correct={Correct};incorrect={Incorrect};transitions=[{TransitionsText}];operations=[{OperationCountsText}];bands=[{string.Join(",", FinalBandIndex.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"))}]");

        public string Describe()
        {
            var builder = new StringBuilder();
            builder.AppendLine(CultureInfo.InvariantCulture, $"persona={Name} initialStage={InitialStage} finalStage={FinalStage}");
            builder.AppendLine(MetricsText);
            foreach (var (attempt, stage) in Transitions)
            {
                var turn = Trace[attempt - 1];
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {stage}@{attempt}: correctSoFar={Trace.Take(attempt).Count(t => t.IsCorrect)} incorrectSoFar={Trace.Take(attempt).Count(t => !t.IsCorrect)} bands=[{string.Join(",", turn.BandIndexAfter.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value}"))}]");
            }

            var roles = Trace.GroupBy(turn => turn.Role).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}:{group.Count()}");
            builder.Append(CultureInfo.InvariantCulture, $"roles=[{string.Join(",", roles)}]");
            return builder.ToString();
        }
    }

    private sealed class PersonaClock : IClock
    {
        private long _timestamp = 10_000_000;
        public TimeSpan CurrentTurnLatency { get; set; } = TimeSpan.FromMilliseconds(1000);
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => CurrentTurnLatency;
        public void AdvanceMs(long ms) => _timestamp += ms;
    }

    private sealed class FixedOperationsPreferenceStore(IReadOnlyList<ArithmeticOperation> operations) : IPreferenceStore
    {
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => operations.Contains(operation);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => operations;
        public void SetEnabledOperations(IReadOnlyList<ArithmeticOperation> operations) { }
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }
}
