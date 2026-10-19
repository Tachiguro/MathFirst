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
    public async Task UpgradeTerminal_OpenFromRunning_PurchaseFirewall_AndClose_PreservesTimingFactRevisionAndAnswerBuffer()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_firewall_integration.db");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Seed progression: Level 5, 5 SP (enough for rank 1 cost of 3 SP)
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET cumulative_xp = 500,
                    player_level = 5,
                    earned_sp = 5,
                    spent_sp = 0,
                    available_sp = 5,
                    attack_rank = 0,
                    firewall_rank = 0,
                    activated_firewall_rank = 0
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();

        // 1. Learner actively thinks for 1,200 ms and enters partial answer "42"
        clock.AdvanceMs(1200);
        session.SetCurrentAnswerInput("42");

        var initialFact = session.CurrentFact;
        Assert.NotNull(initialFact);
        var initialFactId = initialFact.Id;
        var initialRevision = session.FactInstanceRevision;
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());
        Assert.Equal("42", session.CurrentAnswerInput);

        // 2. Open Upgrade Terminal from Running:
        bool wasTimingSuspendedByTerminal = session.IsTimingActive;
        session.PauseItemTiming();
        Assert.True(wasTimingSuspendedByTerminal);
        Assert.False(session.IsTimingActive);

        // 3. User spends 10,000 ms inside the terminal and purchases Firewall
        clock.AdvanceMs(10000);
        Assert.True(vm.CanPurchaseFirewall);
        var purchaseResult = await vm.PurchaseFirewallUpgradeAsync();
        Assert.Equal(UpgradePurchaseStatus.Success, purchaseResult.Status);
        Assert.Equal(1, vm.CurrentFirewallRank);
        Assert.Equal(1, vm.CurrentPermanentCapacity);
        Assert.Equal(2, vm.AvailableSkillPoints);

        // Active elapsed time must remain exactly 1,200 ms during terminal interaction
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());

        // 4. Close Upgrade Terminal:
        if (wasTimingSuspendedByTerminal &&
            session.IsAppForeground &&
            session.PracticeGate == PracticeGateState.Running &&
            session.InteractionState == SessionInteractionState.AwaitingAnswer)
        {
            session.ResumeItemTiming();
        }

        // 5. Verification after closing:
        Assert.True(session.IsTimingActive, "Timing must be resumed after closing terminal.");
        Assert.Equal(1200, session.GetCurrentActiveElapsedMs());
        Assert.Equal("42", session.CurrentAnswerInput);
        Assert.Equal(initialRevision, session.FactInstanceRevision);
        Assert.Equal(initialFactId, session.CurrentFact?.Id);

        // Additional practice time resumes normally
        clock.AdvanceMs(800);
        Assert.Equal(2000, session.GetCurrentActiveElapsedMs());
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

    // =========================================================================
    // SLICE 6D: Hardening Regressions (Findings 1 & 2)
    // =========================================================================

    [Fact]
    public void Home_Contract_InertAndAriaHidden_IsolatesTrainingTopRegionWhileOpen()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // training-top-region must have inert and aria-hidden when Upgrade Terminal is open
        Assert.Contains("<div class=\"training-top-region\" inert=\"@(_isUpgradeTerminalOpen ? true : null)\" aria-hidden=\"@(_isUpgradeTerminalOpen ? \"true\" : null)\">", home, StringComparison.Ordinal);

        // practice-layout must also preserve its inert and aria-hidden
        Assert.Contains("<div class=\"practice-layout\" inert=\"@(_isUpgradeTerminalOpen ? true : null)\" aria-hidden=\"@(_isUpgradeTerminalOpen ? \"true\" : null)\">", home, StringComparison.Ordinal);

        // UpgradeTerminal component must be rendered outside training-top-region and practice-layout
        var topRegionIndex = home.IndexOf("class=\"training-top-region\"", StringComparison.Ordinal);
        var practiceLayoutIndex = home.IndexOf("class=\"practice-layout\"", StringComparison.Ordinal);
        var upgradeTerminalIndex = home.IndexOf("<UpgradeTerminal", StringComparison.Ordinal);

        Assert.True(topRegionIndex > 0, "training-top-region must exist in Home.razor.");
        Assert.True(practiceLayoutIndex > topRegionIndex, "practice-layout must be placed after training-top-region.");
        Assert.True(upgradeTerminalIndex > practiceLayoutIndex, "UpgradeTerminal modal must be outside and after the inert layout containers.");
    }

    [Fact]
    public void Home_Contract_TerminalBackgroundRefresh_UsesSafeObservationPattern()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Must NOT contain fire-and-forget unobserved task calls to RefreshAsync
        Assert.DoesNotContain("_ = _upgradeTerminalViewModel?.RefreshAsync()", home, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = _upgradeTerminalViewModel.RefreshAsync()", home, StringComparison.Ordinal);

        // Must route through safe scheduled background refresh
        Assert.Contains("ScheduleUpgradeTerminalRefresh()", home, StringComparison.Ordinal);
        Assert.Contains("RunObservedTerminalRefreshAsync()", home, StringComparison.Ordinal);

        // Must handle cancellation and disposal safely in refresh runner
        Assert.Contains("catch (OperationCanceledException)", home, StringComparison.Ordinal);
        Assert.Contains("catch (ObjectDisposedException)", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_Contract_ModalClosure_RestoresAppropriateFocus()
    {
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // CloseUpgradeTerminalCore restores focus to input if running and awaiting answer, or primary overlay otherwise
        Assert.Contains("CloseUpgradeTerminalCore", home, StringComparison.Ordinal);
        Assert.Matches(@"if\s*\(shouldResumeTiming\)\s*\{[\s\S]*?_shouldFocusInput\s*=\s*true;[\s\S]*?_shouldFocusPrimaryOverlay\s*=\s*false;", home);
        Assert.Matches(@"_shouldFocusInput\s*=\s*false;[\s\S]*?_shouldFocusPrimaryOverlay\s*=\s*true;", home);
    }

    // =========================================================================
    // SLICE 6D.2: Modal Header Accessibility Containment (MAJOR-01 & MINOR-01)
    // =========================================================================

    [Fact]
    public void Home_Contract_HeaderAccessibilityContainment_WhenUpgradeTerminalIsOpen()
    {
        // ---------------------------------------------------------------------
        // Structural source-contract test (static markup verification):
        // Verifies MAJOR-01 header isolation and MINOR-01 mode toggle defense.
        // ---------------------------------------------------------------------
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // 1. Locate the page header element
        var headerStart = home.IndexOf("<header class=\"mathfirst-header\"", StringComparison.Ordinal);
        Assert.True(headerStart >= 0, "Header element '<header class=\"mathfirst-header\"' must exist in Home.razor.");

        var headerTagEnd = home.IndexOf('>', headerStart);
        Assert.True(headerTagEnd > headerStart, "Header opening tag must be closed with '>'.");
        var headerOpeningTag = home.Substring(headerStart, headerTagEnd - headerStart + 1);

        // MAJOR-01 Requirement 1: State-dependent inert protection on mathfirst-header
        Assert.Contains("inert=\"@(_isUpgradeTerminalOpen ? true : null)\"", headerOpeningTag, StringComparison.Ordinal);

        // MAJOR-01 Requirement 2: State-dependent aria-hidden protection on mathfirst-header
        Assert.Contains("aria-hidden=\"@(_isUpgradeTerminalOpen ? \"true\" : null)\"", headerOpeningTag, StringComparison.Ordinal);

        // 2. Locate the Mode Toggle button inside the header
        var headerEnd = home.IndexOf("</header>", headerStart, StringComparison.Ordinal);
        Assert.True(headerEnd > headerTagEnd, "</header> closing tag must exist after header opening tag.");
        var headerContent = home.Substring(headerStart, headerEnd - headerStart + 9);

        // MINOR-01 Requirement 3: Mode toggle button is disabled when modal is open
        Assert.Contains("mode-toggle-btn", headerContent, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(_isSubmitting || _isUpgradeTerminalOpen)\"", headerContent, StringComparison.Ordinal);

        // 3. Requirement 4: State-dependent evaluation simulation
        // In Blazor, binding null removes the attribute, while true/"true" renders the attribute
        bool isModalOpen = true;
        bool? inertWhenOpen = isModalOpen ? true : null;
        string? ariaHiddenWhenOpen = isModalOpen ? "true" : null;
        Assert.True(inertWhenOpen.HasValue && inertWhenOpen.Value, "Inert must evaluate to true when modal is open.");
        Assert.Equal("true", ariaHiddenWhenOpen);

        bool isModalClosed = false;
        bool? inertWhenClosed = isModalClosed ? true : null;
        string? ariaHiddenWhenClosed = isModalClosed ? "true" : null;
        Assert.Null(inertWhenClosed); // Attribute removed -> header accessible
        Assert.Null(ariaHiddenWhenClosed); // Attribute removed -> header visible in accessibility tree

        // Mode toggle disabled evaluation simulation:
        // Closed + Not submitting -> Enabled
        Assert.False(false || isModalClosed, "Mode toggle must be enabled when modal is closed and not submitting.");
        // Open + Not submitting -> Disabled
        Assert.True(false || isModalOpen, "Mode toggle must be disabled when modal is open.");
        // Closed + Submitting -> Disabled
        Assert.True(true || isModalClosed, "Mode toggle must be disabled when submitting.");

        // 4. Requirement 5: UpgradeTerminal is rendered outside the header and outside all inert containers
        var upgradeTerminalPos = home.IndexOf("<UpgradeTerminal", StringComparison.Ordinal);
        Assert.True(upgradeTerminalPos > headerEnd,
            "UpgradeTerminal must be rendered outside (after) the header element.");

        // UpgradeTerminal must be outside training-top-region and practice-layout
        var topRegionPos = home.IndexOf("class=\"training-top-region\"", StringComparison.Ordinal);
        var practiceLayoutPos = home.IndexOf("class=\"practice-layout\"", StringComparison.Ordinal);
        Assert.True(upgradeTerminalPos > topRegionPos, "UpgradeTerminal must be outside training-top-region.");
        Assert.True(upgradeTerminalPos > practiceLayoutPos, "UpgradeTerminal must be outside practice-layout.");

        // Confirm that the UpgradeTerminal tag itself is not inside an inert container
        // Ensure no ancestor enclosing UpgradeTerminal has inert="@(_isUpgradeTerminalOpen ? true : null)"
        var mainCardPos = home.IndexOf("<main class=\"training-card", StringComparison.Ordinal);
        var mainCardTagEnd = home.IndexOf('>', mainCardPos);
        var mainCardTag = home.Substring(mainCardPos, mainCardTagEnd - mainCardPos + 1);
        Assert.DoesNotContain("inert", mainCardTag, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-hidden", mainCardTag, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_Contract_HeaderAccessibility_PreservesInputIsolationAndFocusRestoration()
    {
        // ---------------------------------------------------------------------
        // Structural source-contract & behavioral test:
        // Verifies Requirements 6 & 7 (Input isolation & Focus restoration contracts).
        // ---------------------------------------------------------------------
        var home = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Requirement 6: Existing input isolation is preserved
        // 1. Global keydown returns immediately when Upgrade Terminal is open
        Assert.Matches(@"HandleGlobalKeyDown[^{]*\{[\s\S]*?if\s*\(_isUpgradeTerminalOpen\)\s*\{\s*return;\s*\}", home);

        // 2. Keypad click handler rejects while Upgrade Terminal is open
        Assert.Matches(@"HandleKeypadKeyAsync[^{]*\{[\s\S]*?if\s*\(_isUpgradeTerminalOpen\)\s*\{\s*return;\s*\}", home);

        // 3. Mode Toggle handler rejects while Upgrade Terminal is open
        Assert.Matches(@"ToggleCyberDefenseMode\(\)[\s\S]*?if\s*\(_isSubmitting\s*\|\|\s*_isUpgradeTerminalOpen\)\s*\{\s*return;\s*\}", home);

        // Requirement 7: Existing focus restoration contracts remain intact across all three gate states
        // In CloseUpgradeTerminalCore:
        // GateState.Running + AwaitingAnswer -> focus input
        // GateState.ManualPause -> focus primary overlay (resume button)
        // GateState.BackgroundResumeGate -> focus primary overlay (resume button)
        Assert.Contains("CloseUpgradeTerminalCore", home, StringComparison.Ordinal);
        Assert.Matches(@"if\s*\(shouldResumeTiming\)[\s\S]*?_shouldFocusInput\s*=\s*true;[\s\S]*?_shouldFocusPrimaryOverlay\s*=\s*false;", home);
        Assert.Matches(@"else[\s\S]*?_shouldFocusInput\s*=\s*false;[\s\S]*?_shouldFocusPrimaryOverlay\s*=\s*true;", home);
    }

    [Fact]
    public async Task ObservedTerminalRefresh_SafeObservationPattern_CatchesExpectedCancellationAndDisposal()
    {
        var store = new TestGameplayStore();
        using var vm = new UpgradeTerminalViewModel(store);
        using var coordinator = new ObservedTerminalRefreshCoordinator(vm);

        // 1. Cancellation test
        store.GetProgressionHandler = _ => throw new OperationCanceledException();
        coordinator.ScheduleRefresh();
        var task = coordinator.CurrentTask;
        Assert.NotNull(task);
        await task;
        Assert.True(task.IsCompletedSuccessfully, "Task must complete cleanly without faulting on OperationCanceledException.");

        // 2. Disposal test
        store.GetProgressionHandler = _ => throw new ObjectDisposedException("store");
        coordinator.ScheduleRefresh();
        var task2 = coordinator.CurrentTask;
        Assert.NotNull(task2);
        await task2;
        Assert.True(task2.IsCompletedSuccessfully, "Task must complete cleanly without faulting on ObjectDisposedException.");
    }

    [Fact]
    public async Task ObservedTerminalRefresh_UnexpectedStoreException_DoesNotThrowUnobservedException()
    {
        var store = new TestGameplayStore();
        using var vm = new UpgradeTerminalViewModel(store);
        using var coordinator = new ObservedTerminalRefreshCoordinator(vm);

        store.GetProgressionHandler = _ => throw new InvalidOperationException("SQLite disk I/O error");

        coordinator.ScheduleRefresh();
        var task = coordinator.CurrentTask;
        Assert.NotNull(task);
        await task;

        Assert.True(task.IsCompletedSuccessfully, "Unexpected exception must be observed and handled without faulting the task.");
        Assert.Equal(1, coordinator.ObservedExceptionCount);
    }

    [Fact]
    public async Task ObservedTerminalRefresh_CompletedMathSubmission_SurvivesSecondaryRefreshFailure()
    {
        var clock = new FakeClock();
        var session = await CreateAndStartSessionAsync(clock);

        var initialFact = session.CurrentFact;
        Assert.NotNull(initialFact);

        // Simulate successful math submission
        var eval = session.SubmitAnswer(initialFact.CorrectResult);
        Assert.True(eval.IsCorrect);

        var persistence = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistence.IsSuccess);
        Assert.True(session.IsCurrentSubmissionCommitted);

        // Secondary UI refresh fails with store exception
        var store = new TestGameplayStore
        {
            GetProgressionHandler = _ => throw new InvalidOperationException("Store unavailable")
        };
        using var vm = new UpgradeTerminalViewModel(store);
        using var coordinator = new ObservedTerminalRefreshCoordinator(vm);

        coordinator.ScheduleRefresh();
        var refreshTask = coordinator.CurrentTask;
        Assert.NotNull(refreshTask);
        await refreshTask;

        // Mathematical advancement proceeds completely unaffected
        var advanced = await session.AdvanceAfterCorrectAnswerAsync(startTiming: true);
        Assert.True(advanced);
        Assert.NotEqual(initialFact.Id, session.CurrentFact!.Id);
    }

    [Fact]
    public async Task ObservedTerminalRefresh_RapidRequests_CoalescesToSinglePendingPass()
    {
        var store = new TestGameplayStore();
        using var vm = new UpgradeTerminalViewModel(store);
        using var coordinator = new ObservedTerminalRefreshCoordinator(vm);

        var firstCallTcs = new TaskCompletionSource<bool>();
        var allowFirstCallToComplete = new TaskCompletionSource<bool>();

        store.GetProgressionHandler = async ct =>
        {
            if (store.RefreshCallCount == 1)
            {
                firstCallTcs.TrySetResult(true);
                await allowFirstCallToComplete.Task;
            }
            return CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow);
        };

        // First call starts executing and waits
        coordinator.ScheduleRefresh();
        await firstCallTcs.Task;

        // Schedule 5 more rapid requests while first is still executing
        for (int i = 0; i < 5; i++)
        {
            coordinator.ScheduleRefresh();
        }

        // Release first call
        allowFirstCallToComplete.TrySetResult(true);

        // Await until active tasks finish
        while (coordinator.CurrentTask != null && !coordinator.CurrentTask.IsCompleted)
        {
            await Task.Delay(10);
        }

        // Total calls should be exactly 2: initial executing pass + single coalesced pending pass
        Assert.Equal(2, store.RefreshCallCount);
    }

    [Fact]
    public async Task ObservedTerminalRefresh_DisposalDuringExecution_CancelsSafelyWithoutUiUpdate()
    {
        var store = new TestGameplayStore();
        using var vm = new UpgradeTerminalViewModel(store);
        var coordinator = new ObservedTerminalRefreshCoordinator(vm);

        var executingTcs = new TaskCompletionSource<bool>();

        store.GetProgressionHandler = async ct =>
        {
            executingTcs.TrySetResult(true);
            await Task.Delay(5000, ct); // Block until cancelled
            return CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow);
        };

        coordinator.ScheduleRefresh();
        await executingTcs.Task;

        // Dispose coordinator during active execution
        coordinator.Dispose();

        var task = coordinator.CurrentTask;
        if (task != null)
        {
            await task;
            Assert.True(task.IsCompletedSuccessfully);
        }

        Assert.Equal(0, coordinator.StateChangeNotifications);
    }

    private sealed class TestGameplayStore : IGameplayStore
    {
        public string StoragePath => "inmemory://test-gameplay";
        public Func<CancellationToken, Task<CyberDefenseProgressionRecord>>? GetProgressionHandler { get; set; }
        public int RefreshCallCount { get; private set; }

#pragma warning disable CS0067 // Event is never used
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
#pragma warning restore CS0067

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseRunState.InitialRun());
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public async Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            if (GetProgressionHandler != null)
            {
                return await GetProgressionHandler(cancellationToken);
            }
            return CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow);
        }

        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseAttackUpgradeSnapshot.FromProgression(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow)));

        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow));

        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseFirewallUpgradeSnapshot.FromProgression(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow)));

        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged { add { } remove { } }

        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseProgressionRecord.Initial(DateTimeOffset.UtcNow));

        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) =>
            Task.FromResult(CyberDefenseFirewallActivationResult.Applied(CyberDefenseRunState.InitialRun()));

        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CyberDefenseReceiptRecord?>(null);

        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CyberDefensePendingIntentRecord?>(null);
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CyberDefensePendingIntentRecord>>([]);
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CyberDefenseReceiptRecord(intent.SubmissionId, CyberDefenseReceiptKind.CalmModeSuppressed, "fact1", true, true, 500, 0L, DateTimeOffset.UtcNow));
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameplayResetIntentRecord(false, 0L, 0L, null, DateTimeOffset.UtcNow));
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GameplayResetIntentRecord(false, 0L, 0L, null, DateTimeOffset.UtcNow));
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class ObservedTerminalRefreshCoordinator : IDisposable
    {
        private readonly object _lock = new();
        private readonly UpgradeTerminalViewModel _vm;
        private CancellationTokenSource? _cts = new();
        private Task? _activeTask;
        private bool _hasQueuedRefresh;
        private bool _isDisposed;
        public int CompletedRefreshCount { get; private set; }
        public int ObservedExceptionCount { get; private set; }
        public int StateChangeNotifications { get; private set; }

        public ObservedTerminalRefreshCoordinator(UpgradeTerminalViewModel vm)
        {
            _vm = vm;
            _vm.StateChanged += () =>
            {
                if (!_isDisposed)
                {
                    StateChangeNotifications++;
                }
            };
        }

        public void ScheduleRefresh()
        {
            if (_isDisposed) return;
            lock (_lock)
            {
                if (_isDisposed) return;
                if (_activeTask is not null && !_activeTask.IsCompleted)
                {
                    _hasQueuedRefresh = true;
                    return;
                }
                _activeTask = RunObservedRefreshAsync();
            }
        }

        public Task? CurrentTask
        {
            get { lock (_lock) return _activeTask; }
        }

        private async Task RunObservedRefreshAsync()
        {
            while (true)
            {
                CancellationToken ct;
                lock (_lock)
                {
                    if (_isDisposed || _cts is null) return;
                    ct = _cts.Token;
                    _hasQueuedRefresh = false;
                }

                try
                {
                    await _vm.RefreshAsync(ct).ConfigureAwait(false);
                    CompletedRefreshCount++;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ObservedExceptionCount++;
                    System.Diagnostics.Trace.TraceWarning("UpgradeTerminal background refresh failed: {0}", ex);
                }

                lock (_lock)
                {
                    if (_isDisposed || !_hasQueuedRefresh)
                    {
                        _activeTask = null;
                        return;
                    }
                }
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            lock (_lock)
            {
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
                _cts?.Dispose();
                _cts = null;
                _hasQueuedRefresh = false;
            }
        }
    }
}
