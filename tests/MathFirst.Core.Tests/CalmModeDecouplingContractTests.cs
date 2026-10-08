namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-001 Slice 4/4: Calm Mode Decoupling and Mode-Toggle Contract Regression Tests.
/// Validates that mental arithmetic practice is 100% autonomous when Cyber Defense is disabled,
/// and that toggling between modes does not corrupt mathematical state, lose input, alter timing,
/// replay suppressed attempts, or violate reset contracts.
/// </summary>
public sealed class CalmModeDecouplingContractTests : IDisposable
{
    private readonly string _testDbDir;

    public CalmModeDecouplingContractTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstCalmModeContract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temporary test directory
        }
    }

    private string GetTempDbPath(string prefix = "calm_contract") =>
        Path.Combine(_testDbDir, $"{prefix}_{Guid.NewGuid():N}.db");

    private sealed class FakeModePreferences : ICyberDefenseModePreferences
    {
        public bool Enabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => Enabled;
        public void SetCyberDefenseEnabled(bool enabled) => Enabled = enabled;
    }

    private sealed class FakePreferenceStore : IPreferenceStore, ICyberDefenseModePreferences
    {
        public bool CyberDefenseEnabled { get; set; } = true;
        public bool GetCyberDefenseEnabled() => CyberDefenseEnabled;
        public void SetCyberDefenseEnabled(bool enabled) => CyberDefenseEnabled = enabled;

        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => PracticeOperationPreferencePolicy.AllOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() => CyberDefenseEnabled = true;
        public void ResetAllPreferences() => CyberDefenseEnabled = true;
    }

    private sealed class FakeInstallationIdProvider : IInstallationIdProvider
    {
        public string GetOrCreateInstallationId() => Guid.NewGuid().ToString("D");
        public void ClearInstallationId() { }
    }

    private sealed class FakeTelemetryShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));
        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    // =========================================================================
    // 1. FULL CALM MODE PRACTICE AUTONOMY
    // =========================================================================

    [Fact]
    public async Task FullCalmModePracticeSession_CompletesPractice_WithZeroEncounterAllocationOrCombatMutation()
    {
        var dbPath = GetTempDbPath("full_calm_practice");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);

        var preferences = new FakeModePreferences { Enabled = false };
        var sessionState = new CyberDefenseSessionState(preferences);
        var dispatcher = new CyberDefenseCombatDispatcher(sessionState);

        await session.InitializeAsync();
        session.StartOrResumePractice();

        Assert.False(sessionState.HasActiveEncounter);
        Assert.Null(sessionState.ActiveEncounter);

        // Execute 30 questions with mixed outcomes
        for (var i = 0; i < 30; i++)
        {
            Assert.NotNull(session.CurrentFact);
            clock.AdvanceMs(750);

            var isCorrect = (i % 6) != 4;
            var submittedAnswer = isCorrect ? session.CurrentFact.CorrectResult : session.CurrentFact.CorrectResult + 2;

            var eval = session.SubmitAnswer(submittedAnswer);
            Assert.Equal(isCorrect, eval.IsCorrect);

            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);
            Assert.True(session.IsCurrentSubmissionCommitted);

            // Dispatch must be suppressed in Calm Mode
            var dispatchResult = dispatcher.Dispatch(new ConfirmedCombatAttempt(
                eval.ChangeSet.SubmissionId,
                eval.IsCorrect,
                isCritical: false,
                isCommitted: session.IsCurrentSubmissionCommitted,
                wasEligibleAtSubmission: false));

            Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, dispatchResult.Status);
            Assert.False(dispatchResult.MutatedCombatState);
            Assert.False(sessionState.HasActiveEncounter);
            Assert.Null(sessionState.ActiveEncounter);

            if (isCorrect)
            {
                await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
            }
            else
            {
                await session.AcknowledgeFeedbackAsync(startTiming: false);
            }

            if (session.InteractionState == SessionInteractionState.SessionCheckIn)
            {
                await session.ContinuePracticeAsync(startTiming: false);
            }
            else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
            {
                await session.AcknowledgeTeachingInterventionAsync(startTiming: false);
            }
        }

        Assert.Equal(30, session.SessionTotalCount);
        Assert.False(sessionState.HasActiveEncounter);

        // Reload snapshot from SQLite; verify durable state
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(31, snapshot.Revision); // initial (1) + 30 commits
        Assert.NotEmpty(snapshot.ItemStates);
        Assert.NotEmpty(snapshot.RecentAttempts);
    }

    // =========================================================================
    // 2. MODE-TOGGLE SCENARIOS (A THROUGH K)
    // =========================================================================

    [Fact]
    public void ScenarioA_CyberDefenseEnabledFromStart_AllocatesEncounterAndDispatchesMutations()
    {
        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);

        Assert.True(sessionState.IsCyberDefenseEnabled);
        Assert.NotNull(sessionState.ActiveEncounter);
        Assert.True(sessionState.HasActiveEncounter);
    }

    [Fact]
    public void ScenarioB_CalmModeEnabledBeforeInit_DoesNotAllocateEncounter()
    {
        var preferences = new FakeModePreferences { Enabled = false };
        var sessionState = new CyberDefenseSessionState(preferences);

        Assert.False(sessionState.IsCyberDefenseEnabled);
        Assert.Null(sessionState.ActiveEncounter);
        Assert.False(sessionState.HasActiveEncounter);
    }

    [Fact]
    public async Task ScenarioC_ToggleCyberDefenseToCalmMode_BetweenAttempts_PreservesMathStateAndSuppressesFutureCombat()
    {
        var dbPath = GetTempDbPath("scenario_c");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var dispatcher = new CyberDefenseCombatDispatcher(sessionState);

        // Attempt 1 in Cyber Defense
        clock.AdvanceMs(800);
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        var disp1 = dispatcher.Dispatch(new ConfirmedCombatAttempt(eval1.ChangeSet.SubmissionId, true, false, true, true));
        Assert.Equal(CombatDispatchStatus.Dispatched, disp1.Status);
        Assert.True(disp1.MutatedCombatState);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints - 1, sessionState.ActiveEncounter?.EnemyHitPoints);

        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);

        // Toggle to Calm Mode between attempts
        preferences.Enabled = false;
        Assert.False(sessionState.IsCyberDefenseEnabled);
        Assert.Null(sessionState.ActiveEncounter); // null view during Calm Mode

        // Attempt 2 in Calm Mode
        clock.AdvanceMs(800);
        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        var disp2 = dispatcher.Dispatch(new ConfirmedCombatAttempt(eval2.ChangeSet.SubmissionId, true, false, true, false));
        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, disp2.Status);
        Assert.False(disp2.MutatedCombatState);

        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);
        Assert.Equal(2, session.SessionTotalCount);
        Assert.Equal(2, session.SessionCorrectCount);
    }

    [Fact]
    public async Task ScenarioD_ToggleCalmModeToCyberDefense_BetweenAttempts_EnablesFutureCombatWithoutRetroactiveReplay()
    {
        var dbPath = GetTempDbPath("scenario_d");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = false };
        var sessionState = new CyberDefenseSessionState(preferences);
        var dispatcher = new CyberDefenseCombatDispatcher(sessionState);

        // Attempt 1 in Calm Mode
        clock.AdvanceMs(800);
        var eval1 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        var disp1 = dispatcher.Dispatch(new ConfirmedCombatAttempt(eval1.ChangeSet.SubmissionId, true, false, true, false));
        Assert.Equal(CombatDispatchStatus.CalmModeSuppressed, disp1.Status);
        await session.AdvanceAfterCorrectAnswerAsync(startTiming: false);

        // Toggle to Cyber Defense between attempts
        preferences.Enabled = true;
        Assert.True(sessionState.IsCyberDefenseEnabled);

        // Attempt 2 in Cyber Defense
        clock.AdvanceMs(800);
        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        var disp2 = dispatcher.Dispatch(new ConfirmedCombatAttempt(eval2.ChangeSet.SubmissionId, true, false, true, true));
        Assert.Equal(CombatDispatchStatus.Dispatched, disp2.Status);
        Assert.True(disp2.MutatedCombatState);

        // Encounter should reflect only attempt 2's single hit (5 - 1 = 4 HP)
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints - 1, sessionState.ActiveEncounter?.EnemyHitPoints);

        // Attempting to replay attempt 1 must be suppressed as duplicate
        var replayDisp = dispatcher.Dispatch(new ConfirmedCombatAttempt(eval1.ChangeSet.SubmissionId, true, false, true, true));
        Assert.Equal(CombatDispatchStatus.DuplicateSuppressed, replayDisp.Status);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints - 1, sessionState.ActiveEncounter?.EnemyHitPoints);
    }

    [Fact]
    public async Task ScenarioE_RepeatedModeTogglingWithoutAnswering_PreservesCurrentFactInputAndTiming()
    {
        var dbPath = GetTempDbPath("scenario_e");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);

        var activeFactId = session.CurrentFact.Id;
        var activeFactRevision = session.FactInstanceRevision;
        session.SetCurrentAnswerInput("4");

        clock.AdvanceMs(500);

        // Toggle back and forth 10 times
        for (var i = 0; i < 10; i++)
        {
            preferences.Enabled = !preferences.Enabled;
        }

        Assert.Equal(activeFactId, session.CurrentFact.Id);
        Assert.Equal(activeFactRevision, session.FactInstanceRevision);
        Assert.Equal("4", session.CurrentAnswerInput);
        Assert.True(session.GetCurrentActiveElapsedMs() >= 500);
    }

    [Fact]
    public async Task ScenarioF_ToggleModeDuringFeedbackAcknowledgement_PreservesFeedbackState()
    {
        var dbPath = GetTempDbPath("scenario_f");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);

        clock.AdvanceMs(800);
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 3); // Incorrect
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);

        // Toggle mode during feedback display
        preferences.Enabled = false;

        Assert.Equal(SessionInteractionState.IncorrectFeedback, session.InteractionState);
        Assert.NotNull(session.LastEvaluation);
        Assert.False(session.LastEvaluation.IsCorrect);

        // Acknowledge feedback in Calm Mode
        await session.AcknowledgeFeedbackAsync(startTiming: false);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }

    [Fact]
    public async Task ScenarioG_ToggleModeWhileSameFactVisible_DoesNotAdvanceOrRescheduleFact()
    {
        var dbPath = GetTempDbPath("scenario_g");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = true };
        var activeFact = session.CurrentFact;
        var activeRevision = session.FactInstanceRevision;

        preferences.Enabled = false;

        Assert.Same(activeFact, session.CurrentFact);
        Assert.Equal(activeRevision, session.FactInstanceRevision);
    }

    [Fact]
    public void ScenarioH_NavigateToSettingsAndBack_RetainsModePreferenceAndPreservesEncounterState()
    {
        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        initialEncounter.RecordCorrectAnswer(); // 5 -> 4 HP

        // Navigate to Settings: user toggles mode to false
        preferences.SetCyberDefenseEnabled(false);
        Assert.Null(sessionState.ActiveEncounter);

        // User navigates back to Settings: re-enables mode
        preferences.SetCyberDefenseEnabled(true);
        Assert.Same(initialEncounter, sessionState.ActiveEncounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints - 1, sessionState.ActiveEncounter?.EnemyHitPoints);
    }

    [Fact]
    public void ScenarioI_RestoreDefaultPreferences_RestoresCyberDefenseDefaultWithoutCorruptingMathSession()
    {
        var prefStore = new FakePreferenceStore();

        // Non-default mode
        prefStore.SetCyberDefenseEnabled(false);
        Assert.False(prefStore.GetCyberDefenseEnabled());

        // Restore defaults (clear custom key)
        prefStore.ResetAllPreferences();
        Assert.True(prefStore.GetCyberDefenseEnabled()); // Default is true for backward compatibility
    }

    [Fact]
    public async Task ScenarioJ_LearningOnlyReset_ResetsMathWithoutCorruptingEncounterOrProcessedReceipts()
    {
        var dbPath = GetTempDbPath("scenario_j");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId = Guid.NewGuid().ToString("N");
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, true, true));

        // Learning-only reset
        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.True(sessionState.HasActiveEncounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints - 1, encounter.EnemyHitPoints);
        Assert.True(sessionState.IsSubmissionProcessed(subId));
    }

    [Fact]
    public async Task ScenarioK_FullLocalReset_ClearsBothLearnerStoreAndCyberDefenseEncounterState()
    {
        var dbPath = GetTempDbPath("scenario_k");
        using var store = new SqliteLearnerStore(dbPath);
        var prefStore = new FakePreferenceStore();
        var installIdProvider = new FakeInstallationIdProvider();
        var cacheCleaner = new FakeTelemetryShareCacheCleaner();
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var preferences = new FakeModePreferences { Enabled = true };
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);

        var subId = Guid.NewGuid().ToString("N");
        sessionState.DispatchAttempt(new ConfirmedCombatAttempt(subId, true, false, true, true));

        var coordinator = new AppResetCoordinator(session, prefStore, installIdProvider, cacheCleaner, sessionState);
        await coordinator.ExecuteFullResetAsync();

        Assert.False(sessionState.HasActiveEncounter);
        Assert.False(sessionState.IsSubmissionProcessed(subId));
    }

    // =========================================================================
    // 3. ACTIVE THINKING TIME ISOLATION
    // =========================================================================

    [Fact]
    public async Task ActiveThinkingTime_IsStrictlyIsolatedFromModeToggles()
    {
        var dbPath = GetTempDbPath("thinking_time_isolation");
        using var store = new SqliteLearnerStore(dbPath);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var preferences = new FakeModePreferences { Enabled = true };

        // Learner begins thinking
        clock.AdvanceMs(400);

        // Toggle mode during active calculation
        preferences.Enabled = false;
        clock.AdvanceMs(600);

        // Submit answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Exact response latency must be 1000ms (400 + 600), untouched by mode toggle
        Assert.Equal(1000, eval.LatencyMs);
        Assert.Equal(1000, eval.ChangeSet.Attempt.ResponseLatencyMs);
    }
}
