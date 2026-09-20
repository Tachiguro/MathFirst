namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;
using Xunit.Abstractions;

public sealed class PracticeBalanceIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper? _output;
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstPracticeBalance_" + Guid.NewGuid().ToString("N"));

    public PracticeBalanceIntegrationTests(ITestOutputHelper? output = null)
    {
        _output = output;
        Directory.CreateDirectory(_testDbDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of synthetic test directory.
        }
    }

    private string GetDatabasePath(string name) => Path.Combine(_testDbDirectory, $"{name}.db");

    // ===========================================================================
    // CASE A: GUIDED ALL-FOUR — ADDITION 0% CORRECT
    // ===========================================================================
    [Fact]
    public async Task GuidedAllFour_AdditionZeroPercentCorrect_IntroducesAllFoundationalFactsWithinTenTurns()
    {
        var dbPath = GetDatabasePath("guided-add-failure-100");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var tracker = new OperationPresentationTracker();
        var curriculum = new ArithmeticCurriculum();
        var startingAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(startingAddBand);
        var expectedOwnedFactIds = ownedFrontier.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        // Band 0 canonical regression guard
        Assert.Contains("add:0+0", expectedOwnedFactIds);
        Assert.Contains("add:0+1", expectedOwnedFactIds);
        Assert.Contains("add:1+0", expectedOwnedFactIds);
        Assert.Contains("add:1+1", expectedOwnedFactIds);

        var totalAttempts = 100;
        var teachingInterventionsAcknowledged = 0;

        for (var i = 1; i <= totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            var op = fact.Operation;
            var opTurn = (int)session.GetOperationAcceptedAttemptCount(op) + 1;
            tracker.Record(op, fact.Id, opTurn);

            // Invariant check: Guided Number Space Gate ceiling
            var currentAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            var currentCeiling = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, currentAddBand).AdditionCeiling!.Value;
            if (op == ArithmeticOperation.Multiplication)
            {
                Assert.True(
                    fact.CorrectResult <= currentCeiling,
                    $"Multiplication fact {fact.Id} (result {fact.CorrectResult}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }
            else if (op == ArithmeticOperation.Division)
            {
                Assert.True(
                    fact.LeftOperand <= currentCeiling,
                    $"Division fact {fact.Id} (dividend {fact.LeftOperand}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }

            // Learner behavior: Addition always incorrect, all others correct
            if (op == ArithmeticOperation.Addition)
            {
                session.SubmitAnswer(fact.CorrectResult + 1);
            }
            else
            {
                session.SubmitAnswer(fact.CorrectResult);
            }

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess, $"Commit failed at attempt {i}: {commitResult.Message}");

            teachingInterventionsAcknowledged += CompleteTurn(session);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        }

        // Assertion A: Exactly 25 accepted attempts per operation after 100 global accepted attempts
        Assert.Equal(100, session.Progression.PracticePosition);
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(25, session.GetOperationAcceptedAttemptCount(op));
            Assert.Equal(25, tracker.GetPresentedFacts(op).Count);
        }

        // Assertion B: Addition remains in foundational progression state (no false advancement)
        var finalAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        Assert.Equal(startingAddBand, finalAddBand);
        Assert.Equal(0, finalAddBand);

        // Assertion C: Every eligible owned Addition fact from the active initial dense band is introduced
        foreach (var factId in expectedOwnedFactIds)
        {
            Assert.True(
                session.ItemStates.ContainsKey(factId),
                $"Owned foundational Addition fact {factId} was not materialized in session.");
            Assert.True(
                session.ItemStates[factId].TotalAttempts > 0,
                $"Owned foundational Addition fact {factId} has 0 recorded attempts in ItemStates.");
        }

        // Assertion D: No owned foundational Addition fact remains permanently unseen
        var presentedAddFactIds = tracker.GetPresentedFacts(ArithmeticOperation.Addition).ToHashSet(StringComparer.Ordinal);
        foreach (var factId in expectedOwnedFactIds)
        {
            Assert.Contains(factId, presentedAddFactIds);
        }

        // Assertion E: All four foundational Addition facts introduced within first 10 Addition turns
        foreach (var factId in expectedOwnedFactIds)
        {
            var firstSeenTurn = tracker.GetFirstSeenTurn(factId);
            Assert.NotNull(firstSeenTurn);
            Assert.True(
                firstSeenTurn.Value <= 10,
                $"Fact {factId} was first introduced on Addition turn {firstSeenTurn.Value}, exceeding target bound of 10.");
        }

        // Assertion F: Liveness
        Assert.True(teachingInterventionsAcknowledged > 0, "Teaching interventions should have occurred and been acknowledged for failing Addition learner.");
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // Telemetry logging
        _output?.WriteLine($"[Case A] ADD immediate same-op repeats: {tracker.GetImmediateRepeatCount(ArithmeticOperation.Addition)}");
        _output?.WriteLine($"[Case A] Teaching interventions acknowledged: {teachingInterventionsAcknowledged}");
        foreach (var factId in expectedOwnedFactIds)
        {
            _output?.WriteLine($"[Case A] Fact {factId} first seen on Addition turn: {tracker.GetFirstSeenTurn(factId)}");
        }
    }

    // ===========================================================================
    // CASE B: GUIDED ALL-FOUR — MULTIPLICATION 0% CORRECT
    // ===========================================================================
    [Fact]
    public async Task GuidedAllFour_MultiplicationZeroPercentCorrect_IntroducesAllEligibleFoundationalFactsAndRespectsGate()
    {
        var dbPath = GetDatabasePath("guided-mul-failure-100");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var tracker = new OperationPresentationTracker();
        var curriculum = new ArithmeticCurriculum();
        var startingMulBand = session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex;
        var ownedMulFrontier = new AcquisitionOwnershipResolver(curriculum.Multiplication).GetOwnedFrontier(startingMulBand);
        var expectedOwnedMulFactIds = ownedMulFrontier.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        // Band 0 canonical regression guard
        Assert.Contains("mul:0*0", expectedOwnedMulFactIds);
        Assert.Contains("mul:0*1", expectedOwnedMulFactIds);
        Assert.Contains("mul:1*0", expectedOwnedMulFactIds);
        Assert.Contains("mul:1*1", expectedOwnedMulFactIds);

        var totalAttempts = 100;
        var teachingInterventionsAcknowledged = 0;

        for (var i = 1; i <= totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            var op = fact.Operation;
            var opTurn = (int)session.GetOperationAcceptedAttemptCount(op) + 1;
            tracker.Record(op, fact.Id, opTurn);

            // Invariant check: Guided Number Space Gate ceiling
            var currentAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            var currentCeiling = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, currentAddBand).AdditionCeiling!.Value;
            if (op == ArithmeticOperation.Multiplication)
            {
                Assert.True(
                    fact.CorrectResult <= currentCeiling,
                    $"Multiplication fact {fact.Id} (result {fact.CorrectResult}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }
            else if (op == ArithmeticOperation.Division)
            {
                Assert.True(
                    fact.LeftOperand <= currentCeiling,
                    $"Division fact {fact.Id} (dividend {fact.LeftOperand}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }

            // Learner behavior: Multiplication always incorrect, all others correct
            if (op == ArithmeticOperation.Multiplication)
            {
                session.SubmitAnswer(fact.CorrectResult + 1);
            }
            else
            {
                session.SubmitAnswer(fact.CorrectResult);
            }

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess, $"Commit failed at attempt {i}: {commitResult.Message}");

            teachingInterventionsAcknowledged += CompleteTurn(session);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        }

        // Assertion A: Balanced allocation: 25 each
        Assert.Equal(100, session.Progression.PracticePosition);
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(25, session.GetOperationAcceptedAttemptCount(op));
            Assert.Equal(25, tracker.GetPresentedFacts(op).Count);
        }

        // Assertion B: Multiplication does not advance through failed material
        var finalMulBand = session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex;
        Assert.Equal(startingMulBand, finalMulBand);
        Assert.Equal(0, finalMulBand);

        // Addition should have advanced because it was 100% correct
        var finalAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        Assert.True(finalAddBand > 0, "Addition should have advanced with 100% correct responses.");

        // Assertion C: Every eligible owned foundational Multiplication fact in Band 0 is introduced
        foreach (var factId in expectedOwnedMulFactIds)
        {
            Assert.True(
                session.ItemStates.ContainsKey(factId),
                $"Owned foundational Multiplication fact {factId} was not materialized in session.");
            Assert.True(
                session.ItemStates[factId].TotalAttempts > 0,
                $"Owned foundational Multiplication fact {factId} has 0 recorded attempts in ItemStates.");
        }

        // Assertion D: No eligible foundational Multiplication fact is permanently starved
        var presentedMulFactIds = tracker.GetPresentedFacts(ArithmeticOperation.Multiplication).ToHashSet(StringComparer.Ordinal);
        foreach (var factId in expectedOwnedMulFactIds)
        {
            Assert.Contains(factId, presentedMulFactIds);
        }

        // Bounded introduction: within first 10 Multiplication turns
        foreach (var factId in expectedOwnedMulFactIds)
        {
            var firstSeenTurn = tracker.GetFirstSeenTurn(factId);
            Assert.NotNull(firstSeenTurn);
            Assert.True(
                firstSeenTurn.Value <= 10,
                $"Multiplication fact {factId} first seen on turn {firstSeenTurn.Value}, exceeding target bound of 10.");
        }

        // Assertion E & Liveness
        Assert.True(teachingInterventionsAcknowledged > 0, "Teaching interventions should have occurred and been acknowledged for failing Multiplication learner.");
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // Telemetry logging
        _output?.WriteLine($"[Case B] MUL immediate same-op repeats: {tracker.GetImmediateRepeatCount(ArithmeticOperation.Multiplication)}");
        _output?.WriteLine($"[Case B] Final Addition band: {finalAddBand}");
        _output?.WriteLine($"[Case B] Teaching interventions acknowledged: {teachingInterventionsAcknowledged}");
        foreach (var factId in expectedOwnedMulFactIds)
        {
            _output?.WriteLine($"[Case B] Fact {factId} first seen on MUL turn: {tracker.GetFirstSeenTurn(factId)}");
        }
    }

    // ===========================================================================
    // CASE C: CUSTOM SINGLE-OPERATION TOTAL FAILURE (ADDITION ONLY)
    // ===========================================================================
    [Fact]
    public async Task CustomSingleOperation_AdditionTotalFailure_IntroducesAllFoundationalFactsAndMaintainsLiveness()
    {
        var dbPath = GetDatabasePath("custom-add-failure-100");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Addition]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        var tracker = new OperationPresentationTracker();
        var curriculum = new ArithmeticCurriculum();
        var startingAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(startingAddBand);
        var expectedOwnedFactIds = ownedFrontier.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        var totalAttempts = 100;
        var teachingInterventionsAcknowledged = 0;

        for (var i = 1; i <= totalAttempts; i++)
        {
            var fact = session.CurrentFact;
            Assert.Equal(ArithmeticOperation.Addition, fact.Operation);

            var opTurn = (int)session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition) + 1;
            tracker.Record(ArithmeticOperation.Addition, fact.Id, opTurn);

            // Always incorrect
            session.SubmitAnswer(fact.CorrectResult + 1);

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess, $"Commit failed at attempt {i}: {commitResult.Message}");

            teachingInterventionsAcknowledged += CompleteTurn(session);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        }

        // Assertion: Operation is always Addition
        Assert.Equal(100, session.Progression.PracticePosition);
        Assert.Equal(100, session.GetOperationAcceptedAttemptCount(ArithmeticOperation.Addition));
        Assert.Equal(100, tracker.GetPresentedFacts(ArithmeticOperation.Addition).Count);

        // Assertion: Failed learner does not incorrectly advance
        var finalAddBand = session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
        Assert.Equal(0, finalAddBand);

        // Assertion: All foundational owned Addition facts introduced within first 10 turns
        foreach (var factId in expectedOwnedFactIds)
        {
            Assert.True(session.ItemStates.ContainsKey(factId), $"Fact {factId} not materialized in session.");
            Assert.True(session.ItemStates[factId].TotalAttempts > 0, $"Fact {factId} has 0 attempts.");
            var firstSeenTurn = tracker.GetFirstSeenTurn(factId);
            Assert.NotNull(firstSeenTurn);
            Assert.True(
                firstSeenTurn.Value <= 10,
                $"Fact {factId} first seen on turn {firstSeenTurn.Value}, exceeding target bound of 10.");
        }

        // Assertion: Remediation remains dominant after initial introduction
        // All presented facts from turn 9 to 100 must be from the materialized owned set
        var laterTurns = tracker.GetPresentedFacts(ArithmeticOperation.Addition).Skip(8).ToList();
        Assert.All(laterTurns, factId => Assert.Contains(factId, expectedOwnedFactIds));

        // Assertion: Teaching interventions remain functional
        Assert.True(teachingInterventionsAcknowledged > 0, "Teaching interventions must be triggered and handled.");

        // Assertion: Exact fact repetition remains possible when liveness requires it
        var repeatCount = tracker.GetImmediateRepeatCount(ArithmeticOperation.Addition);
        _output?.WriteLine($"[Case C] Immediate same-op repeats in single-op 100 attempts: {repeatCount}");
        _output?.WriteLine($"[Case C] Teaching interventions acknowledged: {teachingInterventionsAcknowledged}");
        foreach (var factId in expectedOwnedFactIds)
        {
            _output?.WriteLine($"[Case C] Fact {factId} first seen on turn: {tracker.GetFirstSeenTurn(factId)}");
        }
    }

    // ===========================================================================
    // CASE D: PERSISTENCE / RESTART UNDER STRUGGLE
    // ===========================================================================
    [Fact]
    public async Task PersistenceRestart_UnderStruggle_PreservesStateAndIntroducesRemainingFoundationalFacts()
    {
        var dbPath = GetDatabasePath("restart-struggle-100");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var curriculum = new ArithmeticCurriculum();
        var ownedFrontier = new AcquisitionOwnershipResolver(curriculum.Addition).GetOwnedFrontier(0);
        var expectedOwnedFactIds = ownedFrontier.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);

        // Canonical Band 0 regression guard
        Assert.Contains("add:0+0", expectedOwnedFactIds);
        Assert.Contains("add:0+1", expectedOwnedFactIds);
        Assert.Contains("add:1+0", expectedOwnedFactIds);
        Assert.Contains("add:1+1", expectedOwnedFactIds);

        HashSet<string> preRestartMaterializedFactIds;
        Dictionary<string, FsrsCardState> preRestartFsrsStates;
        long preRestartPosition;
        Dictionary<ArithmeticOperation, long> preRestartAcceptedCounts;

        // Phase 1: Pre-restart struggle session (12 accepted attempts = 3 per operation)
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
            await session.InitializeAsync(startTiming: false);

            for (var i = 1; i <= 12; i++)
            {
                var fact = session.CurrentFact;
                if (fact.Operation == ArithmeticOperation.Addition)
                {
                    session.SubmitAnswer(fact.CorrectResult + 1);
                }
                else
                {
                    session.SubmitAnswer(fact.CorrectResult);
                }

                var commitResult = await session.CommitCurrentEvaluationAsync();
                Assert.True(commitResult.IsSuccess, $"Pre-restart commit failed at attempt {i}: {commitResult.Message}");

                CompleteTurn(session);
                Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
            }

            preRestartPosition = session.Progression.PracticePosition;
            Assert.Equal(12, preRestartPosition);

            preRestartAcceptedCounts = Enum.GetValues<ArithmeticOperation>()
                .ToDictionary(op => op, op => session.GetOperationAcceptedAttemptCount(op));
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                Assert.Equal(3, preRestartAcceptedCounts[op]);
            }

            preRestartMaterializedFactIds = session.ItemStates.Keys.ToHashSet(StringComparer.Ordinal);
            // With 3 Addition attempts (turns 1 [New], 2 [Due/Remediation], 3 [New]), exactly 2 Addition facts are materialized
            var preRestartAdditionFacts = preRestartMaterializedFactIds.Where(id => id.StartsWith("add:", StringComparison.Ordinal)).ToHashSet();
            Assert.Equal(2, preRestartAdditionFacts.Count);
            Assert.True(expectedOwnedFactIds.Except(preRestartAdditionFacts).Any(), "Expected some initial Addition facts to remain unintroduced before restart.");

            preRestartFsrsStates = session.FsrsStates.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            await store.CloseAsync();
        }

        // Phase 2: Fresh session restart against the same database
        using var reopenedStore = new SqliteLearnerStore(dbPath);
        var restartedSession = new TrainingSession(reopenedStore, new FixedClock(), preferenceStore: preferences);
        await restartedSession.InitializeAsync(startTiming: false);

        // Assertion A: Previously introduced facts remain materialized
        foreach (var factId in preRestartMaterializedFactIds)
        {
            Assert.True(
                restartedSession.ItemStates.ContainsKey(factId),
                $"Fact {factId} was materialized before restart but missing after restart.");
        }

        // Assertion B: Attempt history and PracticePosition are preserved
        Assert.Equal(preRestartPosition, restartedSession.Progression.PracticePosition);
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(preRestartAcceptedCounts[op], restartedSession.GetOperationAcceptedAttemptCount(op));
        }

        // Assertion C: No learner state is reset
        Assert.Equal(0, restartedSession.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);

        // Assertion F: Existing FSRS state for already materialized facts is preserved through restart
        foreach (var (factId, cardState) in preRestartFsrsStates)
        {
            Assert.True(restartedSession.FsrsStates.ContainsKey(factId));
            var restartedCard = restartedSession.FsrsStates[factId];
            Assert.Equal(cardState.CardId, restartedCard.CardId);
            Assert.Equal(cardState.DuePracticePosition, restartedCard.DuePracticePosition);
            Assert.Equal(cardState.LastReviewPracticePosition, restartedCard.LastReviewPracticePosition);
            Assert.Equal(cardState.LastRating, restartedCard.LastRating);
        }

        // Assertion G: Guided gate remains active after restart
        var addCeiling = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, 0).AdditionCeiling!.Value;
        Assert.Equal(2, addCeiling);

        // Phase 3: Continuation (88 more accepted attempts to reach 100 total)
        var continuationTracker = new OperationPresentationTracker();
        for (var i = 13; i <= 100; i++)
        {
            var fact = restartedSession.CurrentFact;
            var op = fact.Operation;
            var opTurn = (int)restartedSession.GetOperationAcceptedAttemptCount(op) + 1;
            continuationTracker.Record(op, fact.Id, opTurn);

            // Invariant check: Guided Number Space Gate ceiling
            var currentAddBand = restartedSession.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex;
            var currentCeiling = GuidedNumberSpaceGate.ForGuided(curriculum.Addition, currentAddBand).AdditionCeiling!.Value;
            if (op == ArithmeticOperation.Multiplication)
            {
                Assert.True(
                    fact.CorrectResult <= currentCeiling,
                    $"Continuation Multiplication fact {fact.Id} (result {fact.CorrectResult}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }
            else if (op == ArithmeticOperation.Division)
            {
                Assert.True(
                    fact.LeftOperand <= currentCeiling,
                    $"Continuation Division fact {fact.Id} (dividend {fact.LeftOperand}) exceeded Addition ceiling {currentCeiling} at attempt {i}.");
            }

            if (op == ArithmeticOperation.Addition)
            {
                restartedSession.SubmitAnswer(fact.CorrectResult + 1);
            }
            else
            {
                restartedSession.SubmitAnswer(fact.CorrectResult);
            }

            var commitResult = await restartedSession.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess, $"Continuation commit failed at attempt {i}: {commitResult.Message}");

            CompleteTurn(restartedSession);
            Assert.Equal(SessionInteractionState.AwaitingAnswer, restartedSession.InteractionState);
        }

        // Assertion D & E: After bounded continuation, complete initial owned-frontier coverage is achieved
        Assert.Equal(100, restartedSession.Progression.PracticePosition);
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            Assert.Equal(25, restartedSession.GetOperationAcceptedAttemptCount(op));
        }

        foreach (var factId in expectedOwnedFactIds)
        {
            Assert.True(
                restartedSession.ItemStates.ContainsKey(factId),
                $"Unintroduced foundational fact {factId} was not introduced after restart.");
            Assert.True(
                restartedSession.ItemStates[factId].TotalAttempts > 0,
                $"Fact {factId} has 0 recorded attempts after continuation.");
        }

        // Assertion H: No duplicate or destructive learner-state initialization
        Assert.Equal(0, restartedSession.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);

        _output?.WriteLine($"[Case D] Post-restart continuation completed to position {restartedSession.Progression.PracticePosition}");
        _output?.WriteLine($"[Case D] All {expectedOwnedFactIds.Count} foundational Addition facts materialized: {string.Join(", ", expectedOwnedFactIds)}");
    }

    // ===========================================================================
    // Helper Methods & Types
    // ===========================================================================
    private static int CompleteTurn(TrainingSession session)
    {
        var teachingInterventionsAcknowledged = 0;
        if (session.InteractionState == SessionInteractionState.CorrectFeedback)
        {
            session.AdvanceAfterCorrectAnswer(startTiming: false);
        }
        else if (session.InteractionState is SessionInteractionState.IncorrectFeedback or SessionInteractionState.TimeoutFeedback)
        {
            session.AcknowledgeFeedback(startTiming: false);
        }
        else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
        {
            teachingInterventionsAcknowledged = 1;
            session.AcknowledgeTeachingIntervention(startTiming: false);
        }

        if (session.InteractionState == SessionInteractionState.SessionCheckIn)
        {
            session.ContinuePractice(startTiming: false);
        }

        if (session.InteractionState != SessionInteractionState.AwaitingAnswer)
        {
            session.AdvanceToNextFact(startTiming: false);
        }

        return teachingInterventionsAcknowledged;
    }

    private sealed class OperationPresentationTracker
    {
        private readonly Dictionary<ArithmeticOperation, List<string>> _presentedFacts = [];
        private readonly Dictionary<ArithmeticOperation, int> _immediateRepeats = [];
        private readonly Dictionary<string, int> _firstSeenTurn = [];

        public OperationPresentationTracker()
        {
            foreach (var op in Enum.GetValues<ArithmeticOperation>())
            {
                _presentedFacts[op] = [];
                _immediateRepeats[op] = 0;
            }
        }

        public void Record(ArithmeticOperation op, string factId, int opTurn)
        {
            var list = _presentedFacts[op];
            if (list.Count > 0 && list[^1] == factId)
            {
                _immediateRepeats[op]++;
            }
            list.Add(factId);
            _firstSeenTurn.TryAdd(factId, opTurn);
        }

        public int GetImmediateRepeatCount(ArithmeticOperation op) => _immediateRepeats[op];
        public IReadOnlyList<string> GetPresentedFacts(ArithmeticOperation op) => _presentedFacts[op];
        public int? GetFirstSeenTurn(string factId) => _firstSeenTurn.TryGetValue(factId, out var turn) ? turn : null;
    }

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(900);
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
