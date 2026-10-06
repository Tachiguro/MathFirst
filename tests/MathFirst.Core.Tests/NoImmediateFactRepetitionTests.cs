namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class NoImmediateFactRepetitionTests : IDisposable
{
    private readonly string _testDirectory;

    public NoImmediateFactRepetitionTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstSlice3Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    // -----------------------------------------------------------------------------------------
    // Test A: NoImmediateRepeat_SelectTargetCandidate_NeverRelaxesImmediatePredecessor
    // Force deeper relaxation. Assert selected FactId != previous FactId.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_SelectTargetCandidate_NeverRelaxesImmediatePredecessor()
    {
        var band = new CurriculumBandId("ADD-D01");
        var factA = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // add:0+0
        var factB = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1); // add:1+1
        var pool = new[] { factA, factB };

        // Recent sequence has factB then factA (factA is the immediate predecessor).
        // Both are in the exact cooldown window (distance = 2 <= 3).
        var recent = new[] { factB, factA };

        // Calling SelectTargetCandidate with position 2 forces Tier 3 relaxation because both are in exact cooldown.
        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            pool,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.Due,
            2);

        // Under current defective code, Tier 3 relaxes exact cooldown on entire pool and returns factA (t-1).
        // Under Slice 3, factA is hard-excluded from all tiers, so factB must be returned.
        Assert.NotEqual(factA.Id, selected.Fact.Id);
        Assert.Equal(factB.Id, selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, selected.Relaxation);
    }

    // -----------------------------------------------------------------------------------------
    // Test B: NoImmediateRepeat_LadderAlternativeBeatsExactImmediateRepeat
    // Only choices: previous Fact A, ladder Fact B. Assert B.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_LadderAlternativeBeatsExactImmediateRepeat()
    {
        var band = new CurriculumBandId("ADD-D01");
        var factA = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // add:0+0
        var factB = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1); // add:0+1 (ladder of 0+0)
        var pool = new[] { factA, factB };

        // Both in exact cooldown, factA is immediate predecessor
        var recent = new[] { factB, factA };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            pool,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.Due,
            2);

        // Immediate repeat prevention outranks anti-ladder aesthetics:
        // Fact A cannot be selected, so ladder Fact B must be selected despite being a ladder continuation.
        Assert.NotEqual(factA.Id, selected.Fact.Id);
        Assert.Equal(factB.Id, selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, selected.Relaxation);
    }

    // -----------------------------------------------------------------------------------------
    // Test C: NoImmediateRepeat_OlderExactCooldownCanRelaxWhileImmediatePredecessorRemainsBlocked
    // Recent sequence includes older B and immediate A. Assert B may become eligible, A never.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_OlderExactCooldownCanRelaxWhileImmediatePredecessorRemainsBlocked()
    {
        var band = new CurriculumBandId("ADD-D01");
        var factA = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0); // add:0+0 (immediate)
        var factB = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1); // add:1+1 (older exact)
        var pool = new[] { factA, factB };

        var recent = new[] { factB, factA };

        var selected = AdaptivePracticeSelector.SelectTargetCandidate(
            pool,
            recent,
            ArithmeticOperation.Addition,
            band,
            PracticeSelectionRole.Due,
            2);

        Assert.Equal(factB.Id, selected.Fact.Id);
        Assert.Equal(PracticeCooldownRelaxation.Exact, selected.Relaxation);

        // A pool containing ONLY immediate predecessor factA must fail closed
        var poolOnlyA = new[] { factA };
        Assert.Throws<InvalidOperationException>(() =>
            AdaptivePracticeSelector.SelectTargetCandidate(
                poolOnlyA,
                recent,
                ArithmeticOperation.Addition,
                band,
                PracticeSelectionRole.Due,
                2));
    }

    // -----------------------------------------------------------------------------------------
    // Test D: NoImmediateRepeat_DuePoolOnlyPreviousFact_UsesAlternativeSemanticPool
    // Assert valid same-operation fallback is selected.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_DuePoolOnlyPreviousFact_UsesAlternativeSemanticPool()
    {
        var curriculum = new ArithmeticCurriculum();
        var factA = curriculum.Addition.Bands[0].Frontier[0]; // add:0+0
        var factB = curriculum.Addition.Bands[0].Frontier[1]; // add:0+1

        // State: factA is Due (DuePracticePosition <= 2). factB is Frontier (not Due, DuePracticePosition = 500).
        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [factA.Id] = ItemLearningState.CreateNew(factA),
            [factB.Id] = ItemLearningState.CreateNew(factB)
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [factA.Id] = new FsrsCardState(factA.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 1, 1, FsrsRating.Good),
            [factB.Id] = new FsrsCardState(factB.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good)
        };

        var materialized = new MaterializedState([factA, factB], states, cards);

        // Requested role: Due (ordinal 2). Position 2.
        // Recent contains factA (factA is immediate predecessor).
        // Broad weakness is true to prevent review-to-New discovery promotion.
        var context = CreateContext(
            position: 2,
            curriculum,
            materialized,
            recentFacts: [factA],
            hasBroadWeakness: true,
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: [ArithmeticOperation.Addition]);

        var selector = new AdaptivePracticeSelector();
        var result = selector.SelectTargetFact(context);

        // Due pool only has factA. Because factA is immediate predecessor, Due pool is treated as unavailable.
        // The selector continues through legitimate semantic alternatives (fallback to Frontier) for the same operation.
        Assert.NotEqual(factA.Id, result.Fact.Id);
        Assert.Equal(factB.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.Frontier, result.ResolvedRole);
    }

    // -----------------------------------------------------------------------------------------
    // Test E: NoImmediateRepeat_RemediationPoolWithAlternative_SelectsDifferentRemediationFact
    // Remediation pool contains previous Fact A and alternative Fact B -> choose B.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_RemediationPoolWithAlternative_SelectsDifferentRemediationFact()
    {
        var curriculum = new ArithmeticCurriculum();
        var factA = curriculum.Addition.Bands[0].Frontier[0]; // add:0+0
        var factB = curriculum.Addition.Bands[0].Frontier[1]; // add:0+1

        var stateA = ItemLearningState.CreateNew(factA); stateA.NeedsRemediation = true;
        var stateB = ItemLearningState.CreateNew(factB); stateB.NeedsRemediation = true;

        // Prospective position is 6. LastReview was 1 (6 >= 1 + 4 -> eligible for both).
        var cardA = new FsrsCardState(factA.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Again);
        var cardB = new FsrsCardState(factB.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Again);

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [factA.Id] = stateA,
            [factB.Id] = stateB
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [factA.Id] = cardA,
            [factB.Id] = cardB
        };

        var materialized = new MaterializedState([factA, factB], states, cards);

        // Recent contains factA as immediate predecessor
        var context = CreateContext(
            position: 6,
            curriculum,
            materialized,
            recentFacts: [factA],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: [ArithmeticOperation.Addition]);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        Assert.Equal(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.NotEqual(factA.Id, result.Fact.Id);
        Assert.Equal(factB.Id, result.Fact.Id);
    }

    // -----------------------------------------------------------------------------------------
    // Test F: NoImmediateRepeat_OnlyRemediationCandidateIsPrevious_FallsBackWithoutClearingRemediation
    // Only remediation candidate is Fact A -> do not choose A, leave NeedsRemediation true, continue to alternatives.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_OnlyRemediationCandidateIsPrevious_FallsBackWithoutClearingRemediation()
    {
        var curriculum = new ArithmeticCurriculum();
        var factA = curriculum.Addition.Bands[0].Frontier[0]; // add:0+0
        var factB = curriculum.Addition.Bands[0].Frontier[1]; // add:0+1

        var stateA = ItemLearningState.CreateNew(factA); stateA.NeedsRemediation = true;
        var stateB = ItemLearningState.CreateNew(factB); stateB.NeedsRemediation = false;

        // Prospective position is 6. factA eligible for remediation (last review 1).
        var cardA = new FsrsCardState(factA.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 100, 1, FsrsRating.Again);
        var cardB = new FsrsCardState(factB.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);

        var states = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
        {
            [factA.Id] = stateA,
            [factB.Id] = stateB
        };
        var cards = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
        {
            [factA.Id] = cardA,
            [factB.Id] = cardB
        };

        var materialized = new MaterializedState([factA, factB], states, cards);

        var context = CreateContext(
            position: 6,
            curriculum,
            materialized,
            recentFacts: [factA],
            scheduledOperationAttemptOrdinal: 2,
            hasBroadWeakness: true,
            enabledOperations: [ArithmeticOperation.Addition]);

        var result = new AdaptivePracticeSelector().SelectTargetFact(context);

        // Previous factA cannot be selected even though it's the only remediation candidate.
        Assert.NotEqual(factA.Id, result.Fact.Id);
        Assert.NotEqual(PracticeSelectionRole.Remediation, result.ResolvedRole);
        Assert.Equal(factB.Id, result.Fact.Id);

        // Remediation state must remain unresolved (not cleared)
        Assert.True(context.CandidateIndex.GetCandidate(factA.Id).ItemState!.NeedsRemediation);
    }

    // -----------------------------------------------------------------------------------------
    // Test G: FreshSingleOperationPractice_NoRepeatInvariantDoesNotStarveSecondTurn
    // Turn 1 introduces Fact A. Turn 2 requests Due, Due has no useful work, promotes to New Fact B.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void FreshSingleOperationPractice_NoRepeatInvariantDoesNotStarveSecondTurn()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();

        // Single operation: Addition
        var enabledOps = new[] { ArithmeticOperation.Addition };

        // Turn 1: Position 1, attempt ordinal 1 (New)
        var context1 = new PracticeSelectionContext(
            prospectivePracticePosition: 1,
            currentSessionOrder: 1,
            CreateProgressions(),
            CreateCurricula(curriculum),
            new PracticeCandidateIndex(Array.Empty<ArithmeticFact>(), new Dictionary<string, ItemLearningState>(), new Dictionary<string, FsrsCardState>()),
            recentAcceptedFactsOldestToNewest: Array.Empty<ArithmeticFact>(),
            scheduledOperationAttemptOrdinal: 1,
            enabledOperations: enabledOps,
            hasBroadWeakness: false);

        var result1 = selector.SelectTargetFact(context1);
        Assert.Equal(PracticeSelectionRole.New, result1.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result1.ResolvedRole);
        Assert.True(result1.IsNewIntroduction);
        var factA = result1.Fact;

        // Turn 2: Position 2, attempt ordinal 2 (Due). Fact A is only materialized fact.
        // Fact A is NOT due (due at e.g. pos 10). Due has no useful work. Unmaterialized facts remain.
        var stateA = ItemLearningState.CreateNew(factA);
        var cardA = new FsrsCardState(factA.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 10, 1, FsrsRating.Good);
        var materialized = new MaterializedState(
            [factA],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [factA.Id] = stateA },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [factA.Id] = cardA });

        var context2 = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            CreateProgressions(),
            CreateCurricula(curriculum),
            new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
            recentAcceptedFactsOldestToNewest: [factA],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: enabledOps,
            hasBroadWeakness: false);

        var result2 = selector.SelectTargetFact(context2);

        // Expected: Slice-2 Discovery promotes Due -> New, selecting Fact B != Fact A
        Assert.NotEqual(factA.Id, result2.Fact.Id);
        Assert.Equal(PracticeSelectionRole.Due, result2.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result2.ResolvedRole);
        Assert.True(result2.IsNewIntroduction);
        Assert.False(result2.IsMaterialized);
    }

    // -----------------------------------------------------------------------------------------
    // -----------------------------------------------------------------------------------------
    // Test H: NoImmediateRepeat_BroadWeaknessWithUnmaterializedCurriculum_UsesTerminalNewFallback
    // Invariant FactId(t + 1) != FactId(t) is strictly preserved, and terminal New fallback provides liveness.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_BroadWeaknessWithUnmaterializedCurriculum_UsesTerminalNewFallback()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();
        var factA = curriculum.Addition.Bands[0].Frontier[0];

        var stateA = ItemLearningState.CreateNew(factA);
        var cardA = new FsrsCardState(factA.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);
        var materialized = new MaterializedState(
            [factA],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [factA.Id] = stateA },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [factA.Id] = cardA });

        // Position 2, attempt ordinal 2 (Due).
        // hasBroadWeakness is TRUE -> suppresses Discovery promotion to New.
        // Due pool has no useful work. Frontier pool has only factA.
        // factA is the immediate predecessor.
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            CreateProgressions(),
            CreateCurricula(curriculum),
            new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
            recentAcceptedFactsOldestToNewest: [factA],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: [ArithmeticOperation.Addition],
            hasBroadWeakness: true);

        // Invariant FactId(t + 1) != FactId(t) is strictly preserved, and terminal New fallback provides liveness
        var result = selector.SelectTargetFact(context);
        Assert.Equal(ArithmeticOperation.Addition, result.ScheduledOperation);
        Assert.Equal(PracticeSelectionRole.Due, result.RequestedRole);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
        Assert.False(result.IsMaterialized);
        Assert.True(result.IsNewIntroduction);
        Assert.NotEqual(factA.Id, result.Fact.Id);
    }

    // -----------------------------------------------------------------------------------------
    // Test I: NoImmediateRepeat_StarvedScheduledOperation_DoesNotStealAnotherOperationTurn
    // Starved scheduled operation fails closed; does not steal another operation's candidates.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void NoImmediateRepeat_StarvedScheduledOperation_DoesNotStealAnotherOperationTurn()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();

        // Enabled operations: Addition and Subtraction
        var enabledOps = new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction };

        // Position 2 scheduled operation is Addition (or whichever is scheduled at pos 2 for [Addition, Subtraction])
        var scheduledOp = AdaptivePracticeSelector.GetScheduledOperation(2, enabledOps);

        var otherOp = scheduledOp == ArithmeticOperation.Addition ? ArithmeticOperation.Subtraction : ArithmeticOperation.Addition;
        var otherFact = curriculum.GetCurriculum(otherOp).Bands[0].Frontier[0];

        // Give scheduledOp a 1-fact custom curriculum with scheduledFact already materialized, so newPool is empty
        var scheduledFact = new ArithmeticFact(scheduledOp, 0, 0);
        var schedCurriculum = new OperationCurriculum(
            scheduledOp,
            [new CurriculumBand(scheduledOp, 0, new CurriculumBandId("SCHED-D01"), CurriculumBandKind.Dense, [scheduledFact])]);

        var curricula = CreateCurricula(curriculum);
        var curriculaWithCustom = Enum.GetValues<ArithmeticOperation>().ToDictionary(
            op => op,
            op => op == scheduledOp ? schedCurriculum : curricula[op]);

        var stateSched = ItemLearningState.CreateNew(scheduledFact);
        var cardSched = new FsrsCardState(scheduledFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);

        var stateOther = ItemLearningState.CreateNew(otherFact);
        var cardOther = new FsrsCardState(otherFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 1, 1, FsrsRating.Good);

        // Materialize scheduledFact (only one for scheduledOp) and otherFact
        var materialized = new MaterializedState(
            [scheduledFact, otherFact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal)
            {
                [scheduledFact.Id] = stateSched,
                [otherFact.Id] = stateOther
            },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal)
            {
                [scheduledFact.Id] = cardSched,
                [otherFact.Id] = cardOther
            });

        // Scheduled op is starved: only scheduledFact exists, scheduledFact is immediate predecessor, and newPool is empty.
        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            CreateProgressions(),
            curriculaWithCustom,
            new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
            recentAcceptedFactsOldestToNewest: [scheduledFact],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: enabledOps,
            hasBroadWeakness: true);

        // Must fail closed with InvalidOperationException for scheduledOp; must NOT steal otherOp!
        var ex = Assert.Throws<InvalidOperationException>(() => selector.SelectTargetFact(context));
        Assert.Contains(scheduledOp.ToString(), ex.Message);
    }

    [Fact]
    public void NoImmediateRepeat_TerminalNewFallback_NeverSelectsImmediatePredecessor()
    {
        var curriculum = new ArithmeticCurriculum();
        var selector = new AdaptivePracticeSelector();
        var immediateFact = curriculum.Addition.Bands[0].Frontier[0]; // add:0+0

        var stateImm = ItemLearningState.CreateNew(immediateFact);
        var cardImm = new FsrsCardState(immediateFact.Id, Guid.NewGuid(), 1, null, 1.0, 1.0, 500, 1, FsrsRating.Good);

        var materialized = new MaterializedState(
            [immediateFact],
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal) { [immediateFact.Id] = stateImm },
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal) { [immediateFact.Id] = cardImm });

        var context = new PracticeSelectionContext(
            prospectivePracticePosition: 2,
            currentSessionOrder: 2,
            CreateProgressions(),
            CreateCurricula(curriculum),
            new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
            recentAcceptedFactsOldestToNewest: [immediateFact],
            scheduledOperationAttemptOrdinal: 2,
            enabledOperations: [ArithmeticOperation.Addition],
            hasBroadWeakness: true);

        var result = selector.SelectTargetFact(context);

        // Even under terminal New fallback, immediate predecessor is NEVER selected
        Assert.NotEqual(immediateFact.Id, result.Fact.Id);
        Assert.Equal(PracticeSelectionRole.New, result.ResolvedRole);
    }

    // -----------------------------------------------------------------------------------------
    // Outcome-Independent Real Session Tests (Section 13)
    // Fast Correct, Slow Correct, Incorrect, Timeout, Teaching-Intervention
    // -----------------------------------------------------------------------------------------
    [Fact]
    public async Task Session_FastCorrect_NeverImmediatelyRepeatsFactId()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var prefStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        var fact1 = session.CurrentFact;
        session.SubmitAnswer(fact1.CorrectResult);
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        session.AdvanceAfterCorrectAnswer(startTiming: false);

        Assert.NotNull(session.CurrentFact);
        Assert.NotEqual(fact1.Id, session.CurrentFact.Id);
    }

    [Fact]
    public async Task Session_SlowCorrect_NeverImmediatelyRepeatsFactId()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var prefStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        var fakeClock = new ManualClock();
        var session = new TrainingSession(store, clock: fakeClock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        var fact1 = session.CurrentFact;
        fakeClock.Advance(TimeSpan.FromSeconds(5)); // Slow correct
        session.SubmitAnswer(fact1.CorrectResult);
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        session.AdvanceToNextFact(startTiming: false);

        Assert.NotNull(session.CurrentFact);
        Assert.NotEqual(fact1.Id, session.CurrentFact.Id);
    }

    [Fact]
    public async Task Session_Incorrect_NeverImmediatelyRepeatsFactId()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var prefStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        var fact1 = session.CurrentFact;
        session.SubmitAnswer(fact1.CorrectResult + 1);
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        session.AdvanceToNextFact(startTiming: false);

        Assert.NotNull(session.CurrentFact);
        Assert.NotEqual(fact1.Id, session.CurrentFact.Id);
    }

    [Fact]
    public async Task Session_Timeout_NeverImmediatelyRepeatsFactId()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var prefStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        var fact1 = session.CurrentFact;
        session.SubmitTimeout();
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        session.AdvanceToNextFact(startTiming: false);

        Assert.NotNull(session.CurrentFact);
        Assert.NotEqual(fact1.Id, session.CurrentFact.Id);
    }

    [Fact]
    public async Task Session_TeachingInterventionAdvance_NeverImmediatelyRepeatsFactId()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var prefStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: false);

        // Submit wrong answers until teaching intervention triggers
        while (session.InteractionState != SessionInteractionState.TeachingIntervention)
        {
            var f = session.CurrentFact;
            session.SubmitAnswer(f.CorrectResult + 1);
            await session.CommitCurrentEvaluationAsync();
            if (session.InteractionState != SessionInteractionState.TeachingIntervention)
            {
                session.AdvanceToNextFact(startTiming: false);
            }
        }

        var taughtFact = session.CurrentFact;
        var ackResult = session.AcknowledgeTeachingIntervention(startTiming: false);
        Assert.True(ackResult);

        Assert.NotNull(session.CurrentFact);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.NotEqual(taughtFact.Id, session.CurrentFact.Id);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------
    private sealed class ManualClock : IClock
    {
        private long _timestamp = 1000;
        private TimeSpan _elapsed = TimeSpan.FromMilliseconds(500);

        public void Advance(TimeSpan duration)
        {
            _elapsed += duration;
            _timestamp += (long)duration.TotalMilliseconds;
        }

        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => _elapsed;
    }

    private sealed class SingleOperationPreferenceStore : IPreferenceStore
    {
        private readonly HashSet<ArithmeticOperation> _enabled;

        public SingleOperationPreferenceStore(params ArithmeticOperation[] enabledOperations)
        {
            _enabled = new HashSet<ArithmeticOperation>(enabledOperations);
        }

        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "de";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => _enabled.Contains(operation);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled)
        {
            if (enabled) _enabled.Add(operation); else _enabled.Remove(operation);
        }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => _enabled.ToList();
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    private static PracticeSelectionContext CreateContext(
        long position,
        ArithmeticCurriculum curriculum,
        MaterializedState materialized,
        IEnumerable<ArithmeticFact>? recentFacts = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression>? operationProgressions = null,
        IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum>? curricula = null,
        long? scheduledOperationAttemptOrdinal = null,
        bool hasBroadWeakness = false,
        IReadOnlyList<ArithmeticOperation>? enabledOperations = null) => new(
        position,
        currentSessionOrder: 0,
        operationProgressions ?? CreateProgressions(),
        curricula ?? CreateCurricula(curriculum),
        new PracticeCandidateIndex(materialized.Facts, materialized.ItemStates, materialized.FsrsStates),
        recentFacts ?? Array.Empty<ArithmeticFact>(),
        scheduledOperationAttemptOrdinal ?? (((position - 1) / 4) + 1),
        enabledOperations: enabledOperations,
        hasBroadWeakness: hasBroadWeakness);

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> CreateProgressions() =>
        Enum.GetValues<ArithmeticOperation>().ToDictionary(
            operation => operation,
            operation => new OperationProgression(operation, 0, 0));

    private static IReadOnlyDictionary<ArithmeticOperation, OperationCurriculum> CreateCurricula(ArithmeticCurriculum curriculum) =>
        new Dictionary<ArithmeticOperation, OperationCurriculum>
        {
            [ArithmeticOperation.Addition] = curriculum.Addition,
            [ArithmeticOperation.Subtraction] = curriculum.Subtraction,
            [ArithmeticOperation.Multiplication] = curriculum.Multiplication,
            [ArithmeticOperation.Division] = curriculum.Division
        };

    private sealed record MaterializedState(
        IReadOnlyList<ArithmeticFact> Facts,
        IReadOnlyDictionary<string, ItemLearningState> ItemStates,
        IReadOnlyDictionary<string, FsrsCardState> FsrsStates);
}
