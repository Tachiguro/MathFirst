namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class GuidedNumberSpaceSessionTests : IDisposable
{
    private readonly string _testDbDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstGuidedSessionTests_" + Guid.NewGuid().ToString("N"));

    public GuidedNumberSpaceSessionTests()
    {
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
    // RED Case 1: All-four TrainingSession restricts MUL/DIV to Addition ceiling
    // ===========================================================================
    [Fact]
    public async Task AllFour_DerivesActiveGuidedGate_RestrictsMultiplicationToAdditionCeiling()
    {
        var dbPath = GetDatabasePath("all-four-guided-gate");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Seed Addition at Band 0 (ceiling = 2) and Multiplication at Band 1.
        // Materialize all low facts of Band 1 so mul:2*2 (product = 4) is the only New fact in Band 1.
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // In All-Four mode, prospective position 1 schedules Multiplication
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        // Under Guided Mode with Addition ceiling = 2, MUL product CANNOT exceed 2.
        // On un-wired code, 2*2=4 is selected because the gate is not active in TrainingSession!
        Assert.True(
            session.CurrentFact.CorrectResult <= 2,
            $"Expected Multiplication fact product <= 2 (Addition ceiling), but got {session.CurrentFact.Id} (product = {session.CurrentFact.CorrectResult})");
    }

    // ===========================================================================
    // RED Case 2: Custom -> Guided high current fact is replaced without mutation
    // ===========================================================================
    [Fact]
    public async Task CustomToGuided_IneligibleHighFact_ReplacedWithoutLearnerMutation()
    {
        var dbPath = GetDatabasePath("custom-to-guided-ineligible");
        var preferences = new TestPreferenceStore();
        // Start in Custom Mode: Multiplication only
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Addition Band 0 (ceiling = 2), Multiplication Band 1
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Current fact is 2*2=4 (valid in Custom Mode)
        Assert.Equal("mul:2*2", session.CurrentFact.Id);
        Assert.Equal(4, session.CurrentFact.CorrectResult);
        var originalRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("4");

        var durableBefore = await store.LoadSnapshotAsync();

        // Switch to all four operations -> Guided Mode becomes active (Addition ceiling = 2)
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        // 2*2=4 is > 2, so it MUST be replaced as transient presentation only!
        // On un-wired code, this returns RetainedCurrentFact!
        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);
        Assert.NotEqual("mul:2*2", session.CurrentFact.Id);
        Assert.True(session.FactInstanceRevision > originalRevision);
        Assert.Equal(string.Empty, session.CurrentAnswerInput);

        // Durable learner state must have zero mutation
        var durableAfter = await store.LoadSnapshotAsync();
        Assert.Equal(durableBefore.Progression.PracticePosition, durableAfter.Progression.PracticePosition);
        Assert.Equal(durableBefore.Revision, durableAfter.Revision);
        Assert.Equal(durableBefore.RecentAttempts.Count, durableAfter.RecentAttempts.Count);
    }

    // ===========================================================================
    // RED Case 3: Evidence cache cannot be reused across gate-state change
    // ===========================================================================
    [Fact]
    public async Task EvidenceCache_DoesNotReuseStaleGateEvidenceAcrossConfigurationChange()
    {
        var dbPath = GetDatabasePath("cache-identity-gate-change");
        var preferences = new TestPreferenceStore();
        // Start in Custom Mode: Multiplication only
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        // In Custom Mode, prospective position 1 is Multiplication
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal("mul:2*2", session.CurrentFact.Id);

        // Switch preferences to all four operations (Guided Mode active, ceiling = 2)
        // Position 1 in All-Four Mode is ALSO Multiplication!
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        // EnsureScheduledEvidenceAsync must detect gate identity mismatch on cache and reload
        await session.EnsureScheduledEvidenceAsync();

        // Advance or reconcile: cache must NOT serve mul:2*2 under Guided Mode
        var reconciliationResult = await session.ReconcilePracticeConfigurationAsync(startTiming: false);
        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, reconciliationResult);
        Assert.NotEqual("mul:2*2", session.CurrentFact.Id);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.True(
            session.CurrentFact.CorrectResult <= 2,
            $"Expected Multiplication product <= 2 under Guided Mode, but stale cache selected {session.CurrentFact.Id} (product = {session.CurrentFact.CorrectResult})");
    }

    // ===========================================================================
    // RED Case 4: Guided restart path derives and applies the gate
    // ===========================================================================
    [Fact]
    public async Task Restart_AllFour_DerivesAndAppliesGuidedGateFromAdditionProgression()
    {
        var dbPath = GetDatabasePath("restart-guided-gate");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
            await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
            await MaterializeMultiplicationBand1LowFactsAsync(dbPath);
            await store.CloseAsync();
        }

        // Reopen in fresh session
        using var reopenedStore = new SqliteLearnerStore(dbPath);
        var restartedSession = new TrainingSession(reopenedStore, new FixedClock(), preferenceStore: preferences);
        await restartedSession.InitializeAsync(startTiming: false);

        // In All-Four mode, position 1 is Multiplication
        Assert.Equal(ArithmeticOperation.Multiplication, restartedSession.CurrentFact.Operation);
        Assert.True(
            restartedSession.CurrentFact.CorrectResult <= 2,
            $"Restarted session must apply Guided gate ceiling = 2, but selected {restartedSession.CurrentFact.Id} (product = {restartedSession.CurrentFact.CorrectResult})");
    }

    // ===========================================================================
    // Section 6: Custom mode short-circuit (MUL + DIV only)
    // ===========================================================================
    [Fact]
    public async Task CustomMode_MulDiv_DerivesUnrestrictedGate_AllowsAboveCeilingFacts()
    {
        var dbPath = GetDatabasePath("custom-mul-div-unrestricted");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication, ArithmeticOperation.Division]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        // Addition is Band 0 (ceiling = 2), but MUL is Band 1 with 2*2=4 as new fact.
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Under Custom mode (proper subset MUL+DIV), gate is Unrestricted.
        // Position 1 is scheduled as Division.
        Assert.Equal(ArithmeticOperation.Division, session.CurrentFact.Operation);

        // Advance to position 2 (scheduled as Multiplication)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        await session.AdvanceToNextFactAsync(startTiming: false);

        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.Equal("mul:2*2", session.CurrentFact.Id);
        Assert.Equal(4, session.CurrentFact.CorrectResult);
    }

    // ===========================================================================
    // Section 11: Addition ceiling advancement without restart
    // ===========================================================================
    [Fact]
    public async Task AdditionCeilingAdvancement_WithoutRestart_ImmediatelyAllowsHigherMultiplicationFacts()
    {
        var dbPath = GetDatabasePath("addition-ceiling-advance-live");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // At Band 0 Addition ceiling = 2, mul:2*2 is blocked, so a low review fact is selected.
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.True(session.CurrentFact.CorrectResult <= 2);

        // Now advance Addition to Band 1 (ceiling = 4) in both durable store and in-memory progression.
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 1);
        session.Progression.OperationProgressions[ArithmeticOperation.Addition] =
            new OperationProgression(ArithmeticOperation.Addition, 1, 0);

        // Answer and commit the current fact
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        // Advance until Multiplication is scheduled again
        do
        {
            await session.AdvanceToNextFactAsync(startTiming: false);
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication)
            {
                break;
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        } while (true);

        // First Multiplication attempt in bag 1 requests role Due (from attempt ordinal 42).
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        // Advance to the next Multiplication attempt (which requests role New at ordinal 43).
        do
        {
            await session.AdvanceToNextFactAsync(startTiming: false);
            if (session.CurrentFact.Operation == ArithmeticOperation.Multiplication)
            {
                break;
            }
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        } while (true);

        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        // Under Addition Band 1 (ceiling = 4), mul:2*2 (product = 4) is now eligible and selected!
        Assert.Equal("mul:2*2", session.CurrentFact.Id);
        Assert.Equal(4, session.CurrentFact.CorrectResult);
    }

    // ===========================================================================
    // Section 14: Custom -> Guided valid fact retention
    // ===========================================================================
    [Fact]
    public async Task CustomToGuided_ValidFactWithinCeiling_RetainedExactly()
    {
        var dbPath = GetDatabasePath("custom-to-guided-valid");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 0);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Band 0 MUL facts all have product <= 1 <= 2 (Addition ceiling).
        var originalFact = session.CurrentFact;
        Assert.True(originalFact.CorrectResult <= 2);
        var originalRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("1");

        var snapshotBefore = await store.LoadSnapshotAsync();

        // Switch to all four operations
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(PracticeConfigurationReconciliationResult.RetainedCurrentFact, result);
        Assert.Equal(originalFact.Id, session.CurrentFact.Id);
        Assert.Equal(originalRevision, session.FactInstanceRevision);
        Assert.Equal("1", session.CurrentAnswerInput);

        var snapshotAfter = await store.LoadSnapshotAsync();
        Assert.Equal(snapshotBefore.Revision, snapshotAfter.Revision);
    }

    // ===========================================================================
    // Section 15: Guided -> Custom fact retention & historical recovery
    // ===========================================================================
    [Fact]
    public async Task GuidedToCustom_CurrentFactRetained_FutureSelectionUnrestricted()
    {
        var dbPath = GetDatabasePath("guided-to-custom");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        var currentFactId = session.CurrentFact.Id;

        // Switch to Custom mode with Multiplication only
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        // Fact remains enabled and eligible in Multiplication curriculum
        Assert.Equal(PracticeConfigurationReconciliationResult.RetainedCurrentFact, result);
        Assert.Equal(currentFactId, session.CurrentFact.Id);

        // Answer current fact and advance to next (position 2 requests Due)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        await session.AdvanceToNextFactAsync(startTiming: false);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);

        // Answer position 2 and advance to position 3 (requests New)
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        await session.AdvanceToNextFactAsync(startTiming: false);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        // Under Custom mode (Unrestricted), mul:2*2 (product 4 > 2) is now eligible and selected!
        Assert.Equal("mul:2*2", session.CurrentFact.Id);
    }

    // ===========================================================================
    // Section 16: Accepted feedback deferral
    // ===========================================================================
    [Theory]
    [InlineData(SessionInteractionState.CorrectFeedback)]
    [InlineData(SessionInteractionState.IncorrectFeedback)]
    public async Task AcceptedFeedback_ReconciliationDeferredUntilNextPreparation(SessionInteractionState targetFeedback)
    {
        var dbPath = GetDatabasePath($"feedback-deferral-{targetFeedback}");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal("mul:2*2", session.CurrentFact.Id);

        // Submit answer based on target feedback
        if (targetFeedback == SessionInteractionState.CorrectFeedback)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
        }
        else
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        }

        Assert.Equal(targetFeedback, session.InteractionState);

        // Switch preferences to all four operations while viewing feedback
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        // MUST be deferred! Feedback must not be erased!
        Assert.Equal(PracticeConfigurationReconciliationResult.DeferredUntilNextPreparation, result);
        Assert.Equal(targetFeedback, session.InteractionState);
        Assert.Equal("mul:2*2", session.CurrentFact.Id);
    }

    // ===========================================================================
    // Section 17: Settings current-fact zero-mutation contract
    // ===========================================================================
    [Fact]
    public async Task ZeroMutation_WhenIneligibleFactReplaced_DurableStateIdentical()
    {
        var dbPath = GetDatabasePath("zero-mutation-contract");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 0);
        await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Multiplication, 1);
        await MaterializeMultiplicationBand1LowFactsAsync(dbPath);

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal("mul:2*2", session.CurrentFact.Id);
        var snapshotBefore = await store.LoadSnapshotAsync();
        var acceptedCountsBefore = Enum.GetValues<ArithmeticOperation>()
            .ToDictionary(op => op, op => session.GetOperationAcceptedAttemptCount(op));

        // Switch to All Four (Guided Mode, ceiling 2) -> 2*2 is replaced
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);
        var result = await session.ReconcilePracticeConfigurationAsync(startTiming: false);

        Assert.Equal(PracticeConfigurationReconciliationResult.ReplacedCurrentFact, result);

        var snapshotAfter = await store.LoadSnapshotAsync();
        Assert.Equal(snapshotBefore.Progression.PracticePosition, snapshotAfter.Progression.PracticePosition);
        Assert.Equal(snapshotBefore.Revision, snapshotAfter.Revision);
        Assert.Equal(snapshotBefore.Progression.StoreRevision, snapshotAfter.Progression.StoreRevision);
        Assert.Equal(snapshotBefore.RecentAttempts.Count, snapshotAfter.RecentAttempts.Count);

        foreach (var op in Enum.GetValues<ArithmeticOperation>())
        {
            Assert.Equal(
                snapshotBefore.Progression.OperationProgressions[op].BandIndex,
                snapshotAfter.Progression.OperationProgressions[op].BandIndex);
            Assert.Equal(
                snapshotBefore.Progression.OperationProgressions[op].BandStartedPracticePosition,
                snapshotAfter.Progression.OperationProgressions[op].BandStartedPracticePosition);
            Assert.Equal(
                acceptedCountsBefore[op],
                session.GetOperationAcceptedAttemptCount(op));
        }

        Assert.Equal(snapshotBefore.ItemStates.Count, snapshotAfter.ItemStates.Count);
        Assert.Equal(snapshotBefore.FsrsStates.Count, snapshotAfter.FsrsStates.Count);
    }

    // ===========================================================================
    // Section 18 & 19: Restart verification & determinism
    // ===========================================================================
    [Fact]
    public async Task RestartSelectionDeterminism_ContinuousVsRestarted_IdenticalFactAndGate()
    {
        var dbPath = GetDatabasePath("restart-determinism");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session1 = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session1.InitializeAsync(startTiming: false);

        // Position 1 fact
        var fact1 = session1.CurrentFact;
        var op1 = fact1.Operation;

        // Reopen new session on same store at position 1
        var session2 = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session2.InitializeAsync(startTiming: false);

        var fact2 = session2.CurrentFact;
        var op2 = fact2.Operation;

        Assert.Equal(op1, op2);
        Assert.Equal(fact1.Id, fact2.Id);
        Assert.Equal(fact1.CorrectResult, fact2.CorrectResult);
    }

    // ===========================================================================
    // Section 20: Historical Build-2 blocker preservation
    // ===========================================================================
    [Fact]
    public async Task HistoricalBuild2Blocker_AdvancedAddition_SwitchToSubtractionOnly_Restart_DoesNotStarve()
    {
        var dbPath = GetDatabasePath("build2-blocker-preservation");
        var preferences = new TestPreferenceStore();

        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            // Addition at Band 5, Subtraction at Band 0
            await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Addition, 5);
            await SetOperationBandIndexAsync(dbPath, ArithmeticOperation.Subtraction, 0);
            await store.CloseAsync();
        }

        // Reopen with Subtraction-only (Custom mode)
        preferences.SetEnabledOperations([ArithmeticOperation.Subtraction]);
        using var reopenedStore = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(reopenedStore, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Subtraction fact must be selected without starvation
        Assert.Equal(ArithmeticOperation.Subtraction, session.CurrentFact.Operation);
        Assert.NotNull(session.CurrentFact);
    }

    // ===========================================================================
    // Section 21: Guided low-ceiling session liveness
    // ===========================================================================
    [Fact]
    public async Task GuidedLowCeilingLiveness_AllFourOperations_RunsMultipleRoundsSuccessfully()
    {
        var dbPath = GetDatabasePath("guided-low-ceiling-liveness");
        var preferences = new TestPreferenceStore();
        preferences.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Complete 16 accepted attempts across the 4-operation rotation at Addition Band 0 (ceiling = 2)
        for (var i = 0; i < 16; i++)
        {
            var fact = session.CurrentFact;
            if (fact.Operation == ArithmeticOperation.Multiplication)
            {
                Assert.True(
                    fact.CorrectResult <= 2,
                    $"Multiplication fact {fact.Id} (result {fact.CorrectResult}) exceeds Addition ceiling 2 at attempt {i + 1}.");
            }
            else if (fact.Operation == ArithmeticOperation.Division)
            {
                Assert.True(
                    fact.LeftOperand <= 2,
                    $"Division fact {fact.Id} (dividend {fact.LeftOperand}) exceeds Addition ceiling 2 at attempt {i + 1}.");
            }

            session.SubmitAnswer(fact.CorrectResult);
            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            await session.AdvanceToNextFactAsync(startTiming: false);
        }

        Assert.Equal(16, session.Progression.PracticePosition);
    }

    // ===========================================================================
    // Helper Methods & Test Types
    // ===========================================================================

    private static async Task SetOperationBandIndexAsync(string dbPath, ArithmeticOperation operation, int bandIndex)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE operation_progression SET band_index = @band WHERE operation = @operation;";
        cmd.Parameters.AddWithValue("@band", bandIndex);
        cmd.Parameters.AddWithValue("@operation", operation.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MaterializeMultiplicationBand1LowFactsAsync(string dbPath)
    {
        // Band 0 facts: 0*0, 0*1, 1*0, 1*1
        // Band 1 facts: 0*2, 1*2, 2*0, 2*1, 2*2
        // Materialize 0*2, 1*2, 2*0, 2*1 as mastered so only 2*2 is New in Band 1.
        var lowFacts = new[]
        {
            new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 0),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 1),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 0),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 1),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 0, 2),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 1, 2),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 0),
            new ArithmeticFact(ArithmeticOperation.Multiplication, 2, 1)
        };

        foreach (var fact in lowFacts)
        {
            await SeedFactStateAsync(dbPath, fact, duePos: 1000, lastReviewPos: 1, isMastered: true);
        }
    }

    private static async Task SeedFactStateAsync(
        string dbPath,
        ArithmeticFact fact,
        long duePos,
        long lastReviewPos,
        bool isMastered = false,
        bool needsRemediation = false)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
INSERT OR REPLACE INTO item_learning_state (
    fact_id, operation, left_operand, right_operand,
    total_attempts, correct_attempts, incorrect_attempts,
    consecutive_correct, last_latency_ms, rolling_latency_ms,
    fluent_streak, is_mastered, needs_remediation,
    remediation_due_order, last_practiced_order, last_practiced_at
) VALUES (
    @fact_id, @operation, @left_operand, @right_operand,
    5, 5, 0,
    5, 800, 800,
    5, @is_mastered, @needs_remediation,
    0, 1, '2026-01-01T00:00:00Z'
);";
            cmd.Parameters.AddWithValue("@fact_id", fact.Id);
            cmd.Parameters.AddWithValue("@operation", fact.Operation.ToString());
            cmd.Parameters.AddWithValue("@left_operand", fact.LeftOperand);
            cmd.Parameters.AddWithValue("@right_operand", fact.RightOperand);
            cmd.Parameters.AddWithValue("@is_mastered", isMastered ? 1 : 0);
            cmd.Parameters.AddWithValue("@needs_remediation", needsRemediation ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
INSERT OR REPLACE INTO fsrs_card_state (
    fact_id, card_id, state, step, stability, difficulty,
    due_practice_position, last_review_practice_position, last_rating
) VALUES (
    @fact_id, @card_id, 2, NULL, 10.0, 3.0,
    @due_pos, @last_review_pos, 3
);";
            cmd.Parameters.AddWithValue("@fact_id", fact.Id);
            cmd.Parameters.AddWithValue("@card_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@due_pos", duePos);
            cmd.Parameters.AddWithValue("@last_review_pos", lastReviewPos);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
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
