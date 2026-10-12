namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class UpgradeTerminalHomeIntegrationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_UpgradeHomeIntegration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 10_000_000;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceMs(long ms)
        {
            _timestamp += ms;
        }
    }

    private sealed class InMemoryLearnerStore : ILearnerStore
    {
        public string StoragePath => "inmemory://learner";

        private LearnerSnapshot _snapshot;

        public InMemoryLearnerStore(LearnerSnapshot? initialSnapshot = null)
        {
            _snapshot = initialSnapshot ?? new(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion);
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(_snapshot);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(_snapshot.Revision + 1));

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }

    private static TrainingSession CreateSession(FakeClock clock, ILearnerStore? store = null)
    {
        var learnerStore = store ?? new InMemoryLearnerStore();
        return new TrainingSession(learnerStore, clock);
    }

    private static async Task<TrainingSession> CreateAndStartSessionAsync(FakeClock clock, ILearnerStore? store = null)
    {
        var session = CreateSession(clock, store);
        await session.InitializeAsync(startTiming: false);
        session.ShowInitialReadyGate();
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();
        return session;
    }

    // =========================================================================
    // PART C & D: Behavioral Regression Tests (Timing & Exact Answer Safety)
    // =========================================================================

    [Fact]
    public async Task UpgradeTerminal_OpenFromRunning_SynchronouslyPausesActiveTime_AndExcludesInterruptionDuration()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        Assert.True(session.IsTimingActive, "Timing should be active upon StartOrResumePractice.");
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        // 1. Learner actively thinks for 1,200 ms
        clock.AdvanceMs(1200);
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());

        // 2. Open Upgrade Terminal from Running:
        // Synchronously record whether timing was active and pause timing
        bool wasTimingSuspendedByTerminal = session.IsTimingActive;
        session.PauseItemTiming();

        Assert.True(wasTimingSuspendedByTerminal);
        Assert.False(session.IsTimingActive, "Timing must be inactive while terminal is open.");
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());

        // 3. User spends 15,000 ms inside the terminal exploring cards and purchasing
        clock.AdvanceMs(15000);

        // Active elapsed time must remain exactly 1,200 ms
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsTimingActive);

        // 4. Close Upgrade Terminal:
        // Restores only the timing/activity state that the terminal suspended
        if (wasTimingSuspendedByTerminal &&
            session.IsAppForeground &&
            session.PracticeGate == PracticeGateState.Running &&
            session.InteractionState == SessionInteractionState.AwaitingAnswer)
        {
            session.ResumeItemTiming();
        }

        Assert.True(session.IsTimingActive, "Timing must be resumed after closing terminal.");
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());

        // 5. Learner resumes thinking for another 800 ms
        clock.AdvanceMs(800);
        Assert.Equal(2000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task UpgradeTerminal_OpenAndClose_PreservesCurrentFactIdentityAndInstanceRevision()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        var initialFact = session.CurrentFact;
        Assert.NotNull(initialFact);
        var initialRevision = session.FactInstanceRevision;
        var initialFactId = initialFact.Id;
        var initialLeft = initialFact.LeftOperand;
        var initialRight = initialFact.RightOperand;

        // Open terminal
        bool suspended = session.IsTimingActive;
        session.PauseItemTiming();

        clock.AdvanceMs(5000);

        // Close terminal
        if (suspended)
        {
            session.ResumeItemTiming();
        }

        Assert.NotNull(session.CurrentFact);
        Assert.Equal(initialFactId, session.CurrentFact.Id);
        Assert.Equal(initialLeft, session.CurrentFact.LeftOperand);
        Assert.Equal(initialRight, session.CurrentFact.RightOperand);
        Assert.Equal(initialRevision, session.FactInstanceRevision);
    }

    [Fact]
    public async Task UpgradeTerminal_OpenAndClose_PreservesPartialAnswerInput()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        // Learner enters partial input "7"
        session.SetCurrentAnswerInput("7");
        Assert.Equal("7", session.CurrentAnswerInput);

        // Open terminal
        bool suspended = session.IsTimingActive;
        session.PauseItemTiming();

        clock.AdvanceMs(3000);

        // Close terminal
        if (suspended)
        {
            session.ResumeItemTiming();
        }

        // Partial answer must be completely unchanged
        Assert.Equal("7", session.CurrentAnswerInput);
    }

    [Fact]
    public async Task UpgradeTerminal_OpenFromManualPause_StaysManuallyPaused_AndClosingDoesNotResumeTiming()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        clock.AdvanceMs(1000);

        // Learner pauses manually
        session.PausePractice();
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);

        // Open terminal from ManualPause
        bool wasPracticeSurfaceActive = session.IsPracticeSurfaceActive;
        bool wasTimingSuspendedByTerminal = session.IsTimingActive;
        Assert.False(wasTimingSuspendedByTerminal, "Timing was already inactive before terminal opened.");
        Assert.True(wasPracticeSurfaceActive, "Practice surface was active before terminal opened.");
        session.PauseItemTiming();

        clock.AdvanceMs(10000);

        // Close terminal: restore practice surface state that terminal suspended
        if (wasPracticeSurfaceActive)
        {
            session.SetPracticeSurfaceActive(true);
        }

        // Must remain in ManualPause, timing must remain inactive
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(1000, session.GetCurrentActiveElapsedMs());

        clock.AdvanceMs(5000);
        Assert.Equal(1000, session.GetCurrentActiveElapsedMs());

        // When learner later explicitly clicks Resume in the pause menu
        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public async Task UpgradeTerminal_BackgroundTransitionWhileOpen_TransitionsToResumeGate_AndDoesNotForceResumeOnForeground()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        clock.AdvanceMs(500);

        // Open terminal
        bool wasTimingSuspendedByTerminal = session.IsTimingActive;
        session.PauseItemTiming();

        // App moves to background while terminal is open
        session.SetAppForeground(false);
        Assert.False(session.IsAppForeground);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);

        // App returns to foreground while terminal is still open
        session.SetAppForeground(true);
        Assert.True(session.IsAppForeground);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);

        // Terminal is closed
        if (wasTimingSuspendedByTerminal &&
            session.IsAppForeground &&
            session.PracticeGate == PracticeGateState.Running &&
            session.InteractionState == SessionInteractionState.AwaitingAnswer)
        {
            session.ResumeItemTiming();
        }

        // Because PracticeGate is BackgroundResumeGate, timing MUST NOT be active
        Assert.False(session.IsTimingActive);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        Assert.Equal(500, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task UpgradeTerminal_PurchasesInTerminal_DoNotAlterActiveQuestionBoundDamage()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_upgrade_test.db");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var coordinator = new CyberDefenseCombatCoordinator(store);
        await coordinator.InitializeAsync();

        // Active question dispatch binds damage for revision 1 (default attack rank 0 -> damage 1)
        long currentQuestionRevision = 1;
        var boundDamage = await coordinator.GetOrBindEffectiveAttackDamageAsync(currentQuestionRevision);
        Assert.Equal(1, boundDamage);

        // Player has SP and purchases Attack upgrade in the terminal
        // Simulate progression having 2 SP, rank 0
        var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // Directly set progression to 5 SP to allow purchase
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        }

        await vm.RefreshAsync();
        Assert.True(vm.CanPurchase);

        var purchaseResult = await vm.PurchaseUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, purchaseResult.Status);
        Assert.Equal(1, vm.CurrentAttackRank);
        Assert.Equal(2, vm.CurrentBaseDamage);

        // The ACTIVE question must retain its bound damage (1)
        var damageForActiveQuestion = await coordinator.GetOrBindEffectiveAttackDamageAsync(currentQuestionRevision);
        Assert.Equal(1, damageForActiveQuestion);

        // Future question revision 2 gets the newly purchased damage (2)
        long nextQuestionRevision = 2;
        var damageForNextQuestion = await coordinator.GetOrBindEffectiveAttackDamageAsync(nextQuestionRevision);
        Assert.Equal(2, damageForNextQuestion);
    }

    [Fact]
    public async Task UpgradeTerminal_InterruptionTelemetry_SetsAttemptInterruptedFlag()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        Assert.False(session.IsCurrentAttemptInterrupted);

        session.PauseItemTiming();

        Assert.True(session.IsCurrentAttemptInterrupted,
            "Opening the terminal and pausing timing must set IsCurrentAttemptInterrupted for learner telemetry.");
    }

    // =========================================================================
    // PART A, B, E, F: Home and CyberDefenseHud Structural Contract Tests
    // =========================================================================

    [Fact]
    public void Home_Contract_InjectsGameplayStore_AndWiresUpgradeTerminal()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Must inject IGameplayStore
        Assert.Contains("@inject IGameplayStore GameplayStore", home, StringComparison.Ordinal);

        // Must render UpgradeTerminal component
        Assert.Contains("<UpgradeTerminal", home, StringComparison.Ordinal);
        Assert.Contains("ViewModel=\"_upgradeTerminalViewModel\"", home, StringComparison.Ordinal);
        Assert.Contains("OnClose=\"CloseUpgradeTerminal\"", home, StringComparison.Ordinal);

        // Must handle disposal of ViewModel
        Assert.Contains("_upgradeTerminalViewModel?.Dispose()", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_Contract_GlobalKeyDown_SuppressesEnterWhileUpgradeTerminalIsOpen()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("if (_isUpgradeTerminalOpen)", home, StringComparison.Ordinal);
        // Must return before any submit or advance logic
        Assert.Matches(@"if\s*\(_isUpgradeTerminalOpen\)\s*\{\s*return;\s*\}", home);
    }

    [Fact]
    public void Home_Contract_InertAndInputGuards_IsolateKeypadAndSubmissionWhileOpen()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // practice-layout has inert when Upgrade Terminal is open
        Assert.Contains("inert=\"@(_isUpgradeTerminalOpen ? true : null)\"", home, StringComparison.Ordinal);

        // Keypad key handler rejects while Upgrade Terminal is open
        Assert.Contains("_isUpgradeTerminalOpen", home, StringComparison.Ordinal);
        Assert.Matches(@"HandleKeypadKeyAsync[^{]*\{[^}]*_isUpgradeTerminalOpen", home);
    }

    [Fact]
    public void Home_Contract_ManualPause_ProvidesUpgradeAccessOnlyInCyberDefenseMode()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Inside ManualPause, there is an upgrade button guarded by IsCyberDefenseActive
        Assert.Contains("OpenUpgradeTerminalAsync", home, StringComparison.Ordinal);
        Assert.Matches(@"Session\.PracticeGate\s*==\s*PracticeGateState\.ManualPause[\s\S]*?IsCyberDefenseActive[\s\S]*?OpenUpgradeTerminalAsync", home);
    }

    [Fact]
    public void Home_Contract_CalmMode_NeverExposesUpgradeControls()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // HUD is only rendered when IsCyberDefenseActive
        Assert.Matches(@"@if\s*\(IsCyberDefenseActive\s*&&\s*CombatCoordinator\.CurrentViewModel\s*is\s*not\s*null\)\s*\{[\s\S]*?<CyberDefenseHud", home);

        // Pause menu upgrade button is only rendered when IsCyberDefenseActive
        Assert.Matches(@"IsCyberDefenseActive[\s\S]*?OpenUpgradeTerminalAsync", home);
    }

    [Fact]
    public void CyberDefenseHud_Contract_AcceptsAvailableSkillPointsAndOpenCallback()
    {
        var hud = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor"));

        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+int\s+AvailableSkillPoints\s*\{\s*get;\s*set;\s*\}", hud);
        Assert.Matches(@"\[Parameter[^\]]*\]\s*public\s+EventCallback\s+OnOpenUpgradeTerminal\s*\{\s*get;\s*set;\s*\}", hud);
    }

    [Fact]
    public void CyberDefenseHud_Contract_RendersUpgradeIndicatorOnlyWhenSpGreaterThanZero()
    {
        var hud = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Training", "CyberDefenseHud.razor"));

        Assert.Contains("AvailableSkillPoints > 0", hud, StringComparison.Ordinal);
        Assert.Contains("upgrade-indicator", hud, StringComparison.Ordinal);
        Assert.Contains("UpgradeTerminal_IndicatorAriaLabel", hud, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void UpgradeTerminal_Localization_HasIndicatorKeysInAllLocales(string languageCode)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(languageCode);

        string[] requiredKeys =
        [
            "UpgradeTerminal_IndicatorAction",
            "UpgradeTerminal_IndicatorAriaLabel"
        ];

        foreach (var key in requiredKeys)
        {
            var value = localizer[key];
            Assert.False(string.IsNullOrWhiteSpace(value), $"Localized key '{key}' must not be null or empty for language '{languageCode}'.");
            Assert.NotEqual(key, value);
        }
    }
}
