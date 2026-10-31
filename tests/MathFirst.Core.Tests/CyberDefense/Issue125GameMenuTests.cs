namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Issue #125: the single cyber main menu that is the authoritative ManualPause surface, its pause/resume and input
/// isolation contract, and the progressive Upgrade discovery derived from committed progression. The real Home page
/// runs against a synthetic in-memory learner, a temporary SQLite gameplay store and a fake clock; no learner data.
/// </summary>
public sealed class Issue125GameMenuTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // =========================================================================
    // HEADER: ONE MENU CONTROL
    // =========================================================================

    [Theory]
    [InlineData("en", "Menu", "Open menu and pause")]
    [InlineData("de", "Menü", "Menü öffnen und pausieren")]
    [InlineData("ru", "Меню", "Открыть меню и поставить на паузу")]
    public async Task Header_HasOneMenuButton_ReplacingPauseAndSettings(string language, string label, string accessibleName)
    {
        await using var f = await MenuFixture.CreateAsync(this, new(Language: language));
        await f.StartAsync();

        var header = MenuFixture.Header(await f.HtmlAsync());
        var menu = Regex.Matches(header, @"<button[^>]*class=""button button-pause pause-practice-btn""[^>]*>[\s\S]*?</button>");
        Assert.Single(menu);
        Assert.Contains($"aria-label=\"{accessibleName}\"", menu[0].Value);
        Assert.Contains("aria-haspopup=\"dialog\"", menu[0].Value);
        Assert.Contains($"<span class=\"game-menu-label\">{label}</span>", menu[0].Value);
        // The accessible name contains the visible label (WCAG 2.5.3).
        Assert.Contains(label, accessibleName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings-nav-btn", header);
        Assert.DoesNotContain("href=\"settings\"", header);
        Assert.DoesNotContain("pause-icon", header);
        Assert.Single(Regex.Matches(header, "<button"));
    }

    [Fact]
    public async Task Header_MenuIsTheOnlyPauseEntry_AndIsOfferedOnlyDuringAnswerEntry()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        Assert.Equal(PracticeGateState.InitialReadyGate, f.Session.PracticeGate);
        Assert.DoesNotContain("pause-practice-btn", MenuFixture.Header(await f.HtmlAsync()));

        await f.StartAsync();
        Assert.True(f.CanOpenGameMenu);
        await f.OpenMenuAsync();
        Assert.DoesNotContain("pause-practice-btn", MenuFixture.Header(await f.HtmlAsync()));
        Assert.False(f.CanOpenGameMenu);
    }

    // =========================================================================
    // PAUSE AND RESUME AUTHORITY
    // =========================================================================

    [Fact]
    public async Task OpeningTheMenu_PausesSynchronouslyThroughTheSessionGate_AndFreezesTime()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        f.Clock.Advance(1500);
        var revision = f.Session.FactInstanceRevision;
        var activation = f.Session.PracticeGateActivationRevision;

        // The click handler is synchronous: the gate is closed before any await could run.
        await f.Dispatch(() => f.Invoke("OpenGameMenu"));
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(activation + 1, f.Session.PracticeGateActivationRevision);

        f.Clock.Advance(60000);
        Assert.Equal(1500, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        // Exactly one modal surface: the menu is the pause gate itself, not a second dialog stacked on it.
        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Single(Regex.Matches(html, "practice-gate-overlay"));
        Assert.Contains("game-menu-overlay", html);
        Assert.DoesNotContain("numeric-keypad", html);
        Assert.DoesNotContain("answer-input", html);
        Assert.True(f.Field<bool>("_shouldFocusPrimaryOverlay"));
    }

    [Theory]
    [InlineData("en", "Main menu", "Continue", "Settings", "Progress & statistics", "Tutorial & help")]
    [InlineData("de", "Hauptmenü", "Fortsetzen", "Einstellungen", "Lernfortschritt", "Tutorial und Hilfe")]
    [InlineData("ru", "Главное меню", "Продолжить", "Настройки", "Прогресс и статистика", "Обучение и помощь")]
    public async Task Menu_OffersFunctionalLocalizedActions_WithDialogSemantics_AndNoDeadLinks(
        string language, string kicker, string resume, string settings, string progress, string tutorial)
    {
        await using var f = await MenuFixture.CreateAsync(this, new(Language: language));
        await f.StartAsync();
        await f.OpenMenuAsync();

        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());
        Assert.Matches(@"<section class=""practice-overlay game-menu-overlay practice-gate-overlay"" role=""dialog"" aria-modal=""true"" aria-labelledby=""practice-gate-title""", menu);
        Assert.Contains($"<p class=\"game-menu-kicker\">{kicker}</p>", menu);
        Assert.Contains("<h2 id=\"practice-gate-title\">", menu);
        Assert.Matches($@"<button type=""button"" class=""button button-primary practice-overlay-action""[^>]*>\s*{Regex.Escape(resume)}\s*</button>", menu);
        Assert.Matches(@"<nav class=""game-menu-actions"" aria-label=""[^""]+"">", menu);
        Assert.Matches($@"<a href=""settings"" class=""button button-secondary game-menu-action game-menu-settings""[^>]*>{Regex.Escape(settings)}</a>", menu);
        Assert.Matches($@"<button type=""button"" class=""button button-secondary game-menu-action game-menu-progress-toggle"" aria-expanded=""false"" aria-controls=""game-menu-progress""[^>]*>{Regex.Escape(progress)}</button>", menu);
        Assert.Matches($@"<button type=""button"" class=""button button-secondary game-menu-action game-menu-tutorial""[^>]*>{Regex.Escape(tutorial)}</button>", menu);

        // Every link targets an existing route; no placeholder anchors or disabled dead controls.
        foreach (Match href in Regex.Matches(menu, @"href=""([^""]*)"""))
        {
            Assert.Equal("settings", href.Groups[1].Value);
        }
        Assert.DoesNotContain("disabled", menu);
        Assert.DoesNotContain("href=\"#\"", menu);
    }

    [Fact]
    public async Task Continue_ResumesExactlyOnce_AndKeepsTheSameQuestionAndActiveTime()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        f.Clock.Advance(1200);
        var revision = f.Session.FactInstanceRevision;
        await f.OpenMenuAsync();
        f.Clock.Advance(30000);
        var activation = f.Session.PracticeGateActivationRevision;
        var commits = f.Learner.CommitCalls;

        // Continue click, a repeated click and the Enter fallback produce a single Running transition.
        await f.InvokeAsync("StartOrResumePractice");
        await f.InvokeAsync("StartOrResumePractice");
        await f.PressKeyAsync("Enter");

        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(activation, f.Session.PracticeGateActivationRevision);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(1200, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(commits, f.Learner.CommitCalls);
        Assert.Empty(f.Learner.Attempts);
        f.Clock.Advance(300);
        Assert.Equal(1500, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task SettingsNavigation_KeepsThePause_AndReturningNeverResumesAutomatically()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        f.Clock.Advance(900);
        var revision = f.Session.FactInstanceRevision;
        await f.OpenMenuAsync();

        await f.NavigateToSettingsAndBackAsync();
        f.Clock.Advance(45000);

        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(900, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        var html = await f.RenderedHtmlAsync();
        Assert.Contains("game-menu-overlay", html);
        Assert.DoesNotContain("numeric-keypad", html);

        // Background/foreground on top of the returned menu still needs the explicit Continue.
        f.Session.SetAppForeground(false);
        f.Session.SetAppForeground(true);
        await f.AwaitTerminalRefreshAsync();
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        await f.InvokeAsync("StartOrResumePractice");
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(900, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
    }

    // =========================================================================
    // PROGRESS & STATISTICS
    // =========================================================================

    [Fact]
    public async Task ProgressPanel_ShowsTheRealSessionSummary_StagesAndCommittedLevel_WithoutChangingState()
    {
        var xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(4) + 10;
        await using var f = await MenuFixture.CreateAsync(this, new(CumulativeXp: xp, Stage: CurriculumStage.Stage2_Subtraction,
            Bands: [(ArithmeticOperation.Addition, 10), (ArithmeticOperation.Subtraction, 2)]));
        await f.StartAsync();
        await f.SubmitAsync(correct: true);
        await f.SubmitAsync(correct: true);
        await f.SubmitAsync(correct: false);
        await f.InvokeAsync("AdvanceAsync");
        await f.SubmitAsync(correct: true);
        await f.OpenMenuAsync();

        var collapsed = MenuFixture.Menu(await f.RenderedHtmlAsync());
        Assert.Matches(@"<section id=""game-menu-progress"" class=""game-menu-progress"" role=""region"" aria-label=""[^""]+"" hidden(?:="""")?>", collapsed);

        var gate = f.Session.PracticeGate;
        var revision = f.Session.FactInstanceRevision;
        var attempts = f.Learner.Attempts.Count;
        var progression = await f.Store.GetProgressionAsync();
        await f.InvokeAsync("ToggleMenuProgress");

        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());
        Assert.Contains("aria-expanded=\"true\" aria-controls=\"game-menu-progress\"", menu);
        Assert.Matches(@"<section id=""game-menu-progress"" class=""game-menu-progress"" role=""region"" aria-label=""[^""]+"">", menu);
        var summary = f.Session.GetSessionSummary();
        Assert.Equal(4, summary.CompletedCount);
        Assert.Equal(3, summary.CorrectCount);
        Assert.Equal(new[] { "4", "3", summary.CurrentCorrectStreak.ToString(CultureInfo.InvariantCulture) },
            Regex.Matches(menu, @"<span class=""pause-summary-value"">([^<]*)</span>").Take(3).Select(m => m.Groups[1].Value).ToArray());

        var expectedStages = OperationStageHudProjection.Create(f.Session.Progression, new ArithmeticCurriculum(), f.Session.PracticeMode,
                f.Preferences.GetEnabledOperations())
            .Select(p => $"{p.Operation}:{p.PresentationStage}").ToArray();
        Assert.Equal(["Addition:11", "Subtraction:3"], expectedStages);
        Assert.Equal(["Addition: Stage 11", "Subtraction: Stage 3"],
            Regex.Matches(menu, @"class=""ready-progress-entry"" role=""listitem"" aria-label=""([^""]*)""").Select(m => m.Groups[1].Value).ToArray());

        var committed = CyberDefenseLevelPolicy.CalculateProgress((await f.Store.GetProgressionAsync()).CumulativeXp);
        Assert.Contains($"Player level {committed.Level} · {committed.XpIntoCurrentLevel} / {committed.XpRequiredForNextLevel} XP", menu);

        // Reading progress is a pure projection.
        Assert.Equal(gate, f.Session.PracticeGate);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(attempts, f.Learner.Attempts.Count);
        Assert.Equal(progression, await f.Store.GetProgressionAsync());
        Assert.False(f.Session.IsTimingActive);

        await f.InvokeAsync("ToggleMenuProgress");
        Assert.Contains("aria-expanded=\"false\"", MenuFixture.Menu(await f.RenderedHtmlAsync()));
    }

    [Fact]
    public async Task ProgressPanel_InCalmMode_HasNoCyberLevelOrUpgradeOrTutorial_AndNoFabricatedValues()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(CyberDefense: false, PlayerLevel: 30));
        await f.StartAsync();
        await f.OpenMenuAsync();
        await f.InvokeAsync("ToggleMenuProgress");

        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());
        Assert.Contains("pause-session-summary", menu);
        Assert.Contains("ready-progress-entry", menu);
        Assert.DoesNotContain("Player level", menu);
        Assert.DoesNotContain("game-menu-tutorial", menu);
        Assert.DoesNotContain("pause-upgrade-btn", menu);
        Assert.DoesNotContain("upgrade-nav-btn", await f.RenderedHtmlAsync());

        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.False(f.IsTutorialOpen);
        Assert.False(f.ReplayRequests.IsTutorialReplayRequested);
    }

    // =========================================================================
    // TUTORIAL & HELP
    // =========================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tutorial_FromTheMenu_ReusesTheReplayFlowOverTheInertMenu_AndNeverResumes(bool skip)
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        f.Clock.Advance(700);
        var revision = f.Session.FactInstanceRevision;
        await f.OpenMenuAsync();

        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.True(f.IsTutorialOpen);
        Assert.False(f.ReplayRequests.IsTutorialReplayRequested);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        var html = await f.RenderedHtmlAsync();
        Assert.Matches(@"game-menu-overlay practice-gate-overlay""[^>]*\binert\b", html);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Contains("tutorial-overlay", html);
        Assert.Single(Regex.Matches(html, @"class=""tutorial-overlay"""));

        // A second request while the tutorial is open is ignored rather than queued.
        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.False(f.ReplayRequests.IsTutorialReplayRequested);

        f.Clock.Advance(20000);
        if (skip) await f.SkipTutorialAsync();
        else await f.FinishTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(700, f.Session.GetCurrentActiveElapsedMs());
        Assert.True(f.Field<bool>("_shouldFocusPrimaryOverlay"));
        Assert.DoesNotMatch(@"game-menu-overlay practice-gate-overlay""[^>]*\binert\b", await f.RenderedHtmlAsync());

        await f.InvokeAsync("StartOrResumePractice");
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(700, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.IsTutorialOpen);
    }

    // =========================================================================
    // INPUT ISOLATION AND BACK
    // =========================================================================

    [Fact]
    public async Task OpenMenu_IgnoresKeypadTypingAndDirectSubmit_AndEnterNeverSubmits()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.OpenMenuAsync();

        await f.InvokeAsync("HandleKeypadKeyAsync", "7");
        await f.InvokeAsync("HandleAnswerInput", new ChangeEventArgs { Value = f.Session.CurrentFact!.CorrectResult.ToString(CultureInfo.InvariantCulture) });
        Assert.Equal(string.Empty, f.Session.CurrentAnswerInput);
        f.SetField("_answerInput", f.Session.CurrentFact!.CorrectResult.ToString(CultureInfo.InvariantCulture));
        await f.InvokeAsync("Submit");
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.Empty(f.Learner.Attempts);
        Assert.Equal(0, f.Learner.CommitCalls);
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        // Enter is the menu's primary action (Continue). It resumes once and never submits the paused answer.
        f.SetField("_answerInput", string.Empty);
        await f.PressKeyAsync("Enter");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.Empty(f.Learner.Attempts);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
    }

    [Fact]
    public async Task AndroidBack_OpensTheMenuDuringAnswerEntry_ButNeverResumesOrStacks()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        Assert.Equal(1, f.Back.RegisteredHandlerCount);

        // Ready gate: Back passes through (nothing to pause, nothing to resume).
        Assert.False(f.Back.TryHandleBack());
        Assert.Equal(PracticeGateState.InitialReadyGate, f.Session.PracticeGate);

        await f.StartAsync();
        f.Clock.Advance(400);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        var activation = f.Session.PracticeGateActivationRevision;

        // On the open menu Back is safely consumed and does not resume or reopen anything.
        Assert.True(await f.PressBackAsync());
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.Equal(activation, f.Session.PracticeGateActivationRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(400, f.Session.GetCurrentActiveElapsedMs());

        // Tutorial on top of the menu: Back is safely consumed and neither closes it nor resumes.
        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.True(await f.PressBackAsync());
        Assert.True(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
    }

    [Fact]
    public async Task AndroidBack_IsIgnoredWhileTheUpgradeTerminalOrFeedbackOwnsTheScreen_AndUnregistersOnDispose()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.StartAsync();

        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True(f.IsUpgradeTerminalOpen);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        await f.InvokeAsync("CloseUpgradeTerminal");

        await f.SubmitAsync(correct: false);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, f.Session.InteractionState);
        Assert.False(f.CanOpenGameMenu);
        Assert.True(await f.PressBackAsync());
        await f.InvokeAsync("OpenGameMenu");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, f.Session.InteractionState);

        await f.UnmountAsync();
        Assert.Equal(0, f.Back.RegisteredHandlerCount);
        Assert.False(f.Back.TryHandleBack());
    }

    [Fact]
    public async Task AndroidBack_OnBossRecap_IsConsumed_AndNeverBypassesContinue()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "boss-recap-receipt", "2+2", true, true, 1234, 0, DateTimeOffset.UnixEpoch,
            CyberDefenseStateMachine.ApplyAttempt(
                CyberDefenseRunState.CreateActive(1, 5, 100, new OpponentState(OpponentKind.Boss, 12, 1)),
                true, 1));
        var recap = new CyberDefenseSectorRecap("boss-recap-receipt", 0, 1, 24, 22, 24000, 178, 1);
        var pending = f.Ceremony.RunAsync(receipt, recap, _ => Task.FromResult(true));

        for (var i = 0; i < 50 && (!f.Ceremony.CanContinue || f.Ceremony.Stage != CyberDefensePresentationStage.Recap); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(f.Ceremony.IsBlocking);
        Assert.Equal(CyberDefensePresentationStage.Recap, f.Ceremony.Stage);
        Assert.True(f.Ceremony.CanContinue);

        // Back is consumed while Boss recap is visible; does not bypass Continue
        Assert.True(await f.PressBackAsync());
        Assert.True(f.Ceremony.IsBlocking);
        Assert.False(pending.IsCompleted);
        Assert.Equal(CyberDefensePresentationStage.Recap, f.Ceremony.Stage);

        // Only acknowledging through presentation advances the ceremony
        Assert.True(f.Ceremony.Acknowledge(f.Ceremony.PresentationId));
        Assert.True(await pending);
        Assert.False(f.Ceremony.IsBlocking);
    }

    [Fact]
    public async Task AndroidBack_OnSystemRestart_IsConsumed_AndNeverBypassesContinue()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "restart-receipt", "3+3", false, true, 1234, 0, DateTimeOffset.UnixEpoch,
            CyberDefenseStateMachine.ApplyAttempt(
                CyberDefenseRunState.CreateActive(1, 0, 1, new OpponentState(OpponentKind.Normal, 2, 2)),
                false, 1));
        var pending = f.Ceremony.RunAsync(receipt, null, _ => Task.FromResult(true));

        Assert.True(f.Ceremony.IsBlocking);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, f.Ceremony.Stage);

        // Back is consumed while System Restart / Overload is visible (even before Continue is revealed)
        Assert.True(await f.PressBackAsync());
        Assert.True(f.Ceremony.IsBlocking);
        Assert.False(pending.IsCompleted);

        // Wait until Continue is revealed
        for (var i = 0; i < 50 && !f.Ceremony.CanContinue; i++)
        {
            await Task.Delay(10);
        }

        Assert.True(f.Ceremony.CanContinue);

        // Back is still consumed once Continue is revealed
        Assert.True(await f.PressBackAsync());
        Assert.True(f.Ceremony.IsBlocking);
        Assert.False(pending.IsCompleted);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, f.Ceremony.Stage);

        Assert.True(f.Ceremony.Acknowledge(f.Ceremony.PresentationId));
        Assert.True(await pending);
        Assert.False(f.Ceremony.IsBlocking);
    }

    [Fact]
    public async Task AndroidBack_OnFeedback_DoesNotAdvanceMathQuestion()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        var initialFact = f.Session.CurrentFact;
        var initialRevision = f.Session.FactInstanceRevision;
        Assert.NotNull(initialFact);

        // Submit incorrect answer -> enters IncorrectFeedback
        await f.SubmitAsync(correct: false);
        Assert.Equal(SessionInteractionState.IncorrectFeedback, f.Session.InteractionState);

        // Press Back -> consumed, stays on feedback, does not advance question
        Assert.True(await f.PressBackAsync());
        Assert.Equal(SessionInteractionState.IncorrectFeedback, f.Session.InteractionState);
        Assert.Same(initialFact, f.Session.CurrentFact);
        Assert.Equal(initialRevision, f.Session.FactInstanceRevision);

        // Explicit advance moves to the next question
        await f.InvokeAsync("AdvanceAsync");
        Assert.NotEqual(initialRevision, f.Session.FactInstanceRevision);
    }

    [Fact]
    public async Task AndroidBack_OnRecoveryOrErrorGates_IsConsumedSafely()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        // 1. Persistence failure error gate
        typeof(TrainingSession).GetProperty(nameof(TrainingSession.InteractionState))!
            .SetValue(f.Session, SessionInteractionState.PersistenceFailure);
        Assert.Equal(SessionInteractionState.PersistenceFailure, f.Session.InteractionState);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(SessionInteractionState.PersistenceFailure, f.Session.InteractionState);

        // Reset interaction state to AwaitingAnswer
        typeof(TrainingSession).GetProperty(nameof(TrainingSession.InteractionState))!
            .SetValue(f.Session, SessionInteractionState.AwaitingAnswer);

        // 2. Startup error gate
        f.SetField("_startupFailed", true);
        Assert.True(await f.PressBackAsync());
        f.SetField("_startupFailed", false);

        // 3. Teaching intervention gate
        typeof(TrainingSession).GetProperty(nameof(TrainingSession.InteractionState))!
            .SetValue(f.Session, SessionInteractionState.TeachingIntervention);
        Assert.Equal(SessionInteractionState.TeachingIntervention, f.Session.InteractionState);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(SessionInteractionState.TeachingIntervention, f.Session.InteractionState);

        // 4. Session check-in gate
        typeof(TrainingSession).GetProperty(nameof(TrainingSession.InteractionState))!
            .SetValue(f.Session, SessionInteractionState.SessionCheckIn);
        Assert.Equal(SessionInteractionState.SessionCheckIn, f.Session.InteractionState);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(SessionInteractionState.SessionCheckIn, f.Session.InteractionState);
    }

    [Fact]
    public async Task AndroidBack_RepeatedBack_CannotDoubleTransition()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        // 1st Back opens the menu and pauses
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        var activationRevision = f.Session.PracticeGateActivationRevision;

        // Repeated rapid Back calls do not cause a second transition, resume, or error
        for (var i = 0; i < 5; i++)
        {
            Assert.True(await f.PressBackAsync());
            Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
            Assert.Equal(activationRevision, f.Session.PracticeGateActivationRevision);
            Assert.False(f.Session.IsTimingActive);
        }
    }

    [Fact]
    public async Task AndroidBack_BackgroundResetDisposal_AreSafe()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        // App backgrounding transitions to BackgroundResumeGate
        f.Session.SetAppForeground(false);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);

        // Back on BackgroundResumeGate is safely consumed
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);

        // Returning to foreground still safely consumes Back until explicit Continue
        f.Session.SetAppForeground(true);
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);

        // Unmount / disposal unregisters handler cleanly
        await f.UnmountAsync();
        Assert.Equal(0, f.Back.RegisteredHandlerCount);
        Assert.False(f.Back.TryHandleBack());
    }

    [Fact]
    public async Task AndroidBack_SettingsBackNavigation_RemainsFunctional()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();

        // When Settings is mounted, it registers its LIFO handler
        var navigatedBackToHome = false;
        using (f.Back.RegisterHandler(() =>
        {
            navigatedBackToHome = true;
            return true;
        }))
        {
            // Settings handler runs topmost and returns true
            Assert.True(f.Back.TryHandleBack());
            Assert.True(navigatedBackToHome);
        }

        // After Settings handler is disposed, Home handler is back at topmost position
        Assert.True(await f.PressBackAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
    }

    [Fact]
    public async Task AndroidBack_NativeMainPageFallback_IsNotReached_ForGameOwnedBlockingScreens()
    {
        // 1. Contract check: MainPage.OnBackButtonPressed() consumes whenever coordinator handles
        var mainPageSource = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "MainPage.xaml.cs");
        Assert.Contains("if (_backCoordinator.TryHandleBack())", mainPageSource, StringComparison.Ordinal);
        Assert.Contains("return true;", mainPageSource, StringComparison.Ordinal);
        Assert.Contains("return base.OnBackButtonPressed();", mainPageSource, StringComparison.Ordinal);

        // 2. Active Home surface verification: coordinator returns true for all game-owned blocking states
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.StartAsync();

        // Active answering -> handled (opens menu)
        Assert.True(f.Back.TryHandleBack());
        await f.RendererDispatcherFlushAsync();

        // Open menu -> handled (never reaches base.OnBackButtonPressed)
        Assert.True(f.Back.TryHandleBack());

        // Upgrade terminal -> handled
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True(f.Back.TryHandleBack());
        await f.InvokeAsync("CloseUpgradeTerminal");

        // Tutorial -> handled
        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.True(f.Back.TryHandleBack());
    }

    [Fact]
    public async Task AndroidBack_NeverAwardsDuplicateReceiptsXpOrPurchases()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.StartAsync();

        var initialProgression = await f.Store.GetProgressionAsync();
        var initialAttempts = f.Learner.Attempts.Count;

        // Press Back to open menu
        Assert.True(await f.PressBackAsync());

        // Repeated Back presses while menu is open
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await f.PressBackAsync());
        }

        // Open upgrade terminal and press Back
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True(await f.PressBackAsync());
        await f.InvokeAsync("CloseUpgradeTerminal");

        // Verify no XP awarded, no SP spent, no attempts committed
        var finalProgression = await f.Store.GetProgressionAsync();
        Assert.Equal(initialProgression.CumulativeXp, finalProgression.CumulativeXp);
        Assert.Equal(initialProgression.PlayerLevel, finalProgression.PlayerLevel);
        Assert.Equal(initialProgression.AvailableSkillPoints, finalProgression.AvailableSkillPoints);
        Assert.Equal(initialProgression.SpentSkillPoints, finalProgression.SpentSkillPoints);
        Assert.Equal(initialAttempts, f.Learner.Attempts.Count);
    }

    [Fact]
    public async Task Menu_CannotOpenOverTheTerminalTutorialSubmissionOrAnyOtherGate()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6, TutorialCompleted: false));

        // First start opens the onboarding tutorial; the menu must not stack over it.
        await f.StartAsync();
        Assert.True(f.IsTutorialOpen);
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("OpenGameMenu");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.DoesNotContain("pause-practice-btn", await f.RenderedHtmlAsync());
        await f.SkipTutorialAsync();
        Assert.True(f.CanOpenGameMenu);

        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("OpenGameMenu");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.DoesNotContain("pause-practice-btn", await f.RenderedHtmlAsync());
        await f.InvokeAsync("CloseUpgradeTerminal");

        f.SetField("_isSubmitting", true);
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("OpenGameMenu");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        f.SetField("_isSubmitting", false);

        f.Session.SetAppForeground(false);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        f.Session.SetAppForeground(true);
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("OpenGameMenu");
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
    }

    [Fact]
    public async Task UpgradeTerminal_FromTheMenu_IsTheSingleTopLayer_AndClosesBackToThePausedMenu()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.StartAsync();
        f.Clock.Advance(500);
        await f.OpenMenuAsync();
        Assert.Contains("pause-upgrade-btn", MenuFixture.Menu(await f.RenderedHtmlAsync()));

        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        var html = await f.RenderedHtmlAsync();
        Assert.Matches(@"game-menu-overlay practice-gate-overlay""[^>]*\binert\b", html);
        Assert.Single(Regex.Matches(html, "upgrade-terminal-dialog"));
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("ToggleMenuProgress");
        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.False(f.IsTutorialOpen);
        Assert.Contains("aria-expanded=\"false\"", MenuFixture.Menu(await f.RenderedHtmlAsync()));

        await f.InvokeAsync("CloseUpgradeTerminal");
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(500, f.Session.GetCurrentActiveElapsedMs());
        Assert.True(f.Field<bool>("_shouldFocusPrimaryOverlay"));
    }

    [Fact]
    public async Task ReadyBackgroundAndStartupGates_KeepARealSettingsEntry()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(Language: "de"));
        var ready = MenuFixture.Gate(await f.RenderedHtmlAsync());
        Assert.Matches(@"<a href=""settings"" class=""button button-secondary game-menu-action gate-settings-link""[^>]*>Einstellungen</a>", ready);
        Assert.DoesNotContain("game-menu-actions", ready);

        await f.StartAsync();
        f.Session.SetAppForeground(false);
        f.Session.SetAppForeground(true);
        await f.AwaitTerminalRefreshAsync();
        Assert.Contains("gate-settings-link", MenuFixture.Gate(await f.RenderedHtmlAsync()));

        f.SetField("_startupFailed", true);
        var failed = await f.RenderedHtmlAsync();
        Assert.Contains("startup-error-dialog", failed);
        Assert.Matches(@"<a href=""settings"" class=""button button-secondary game-menu-action gate-settings-link""[^>]*>Einstellungen</a>", failed);
    }

    [Fact]
    public async Task LearnerResetFromSettings_ReplacesTheStaleMenu_AndContinueResumesOnlyTheNewGeneration()
    {
        await using var f = await MenuFixture.CreateAsync(this, new());
        await f.StartAsync();
        await f.SubmitAsync(correct: true);
        await f.OpenMenuAsync();
        await f.InvokeAsync("ToggleMenuProgress");
        var generation = f.Session.LearnerStateGenerationRevision;
        var revision = f.Session.FactInstanceRevision;
        var commits = f.Learner.CommitCalls;

        // Settings resets the learner while Home is not mounted, then navigates back to a new Home instance.
        await f.UnmountAsync();
        await f.DispatchAsync(() => f.Session.ResetLearningProgressAsync(startTiming: false));
        await f.MountAsync();

        Assert.NotEqual(generation, f.Session.LearnerStateGenerationRevision);
        Assert.NotEqual(revision, f.Session.FactInstanceRevision);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.False(f.Field<bool>("_isMenuProgressExpanded"));
        await f.InvokeAsync("ToggleMenuProgress");
        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());
        Assert.Equal("0", Regex.Match(menu, @"<span class=""pause-summary-value"">([^<]*)</span>").Groups[1].Value);

        var newRevision = f.Session.FactInstanceRevision;
        await f.InvokeAsync("StartOrResumePractice");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(newRevision, f.Session.FactInstanceRevision);
        Assert.Equal(commits, f.Learner.CommitCalls);
        Assert.Empty(f.Learner.Attempts);
    }

    // =========================================================================
    // PROGRESSIVE UPGRADE DISCOVERY
    // =========================================================================

    [Fact]
    public async Task FreshLearner_SeesNoUpgradeEntryAnywhere_BeforeTheFirstEarnedOpportunity()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 1));
        Assert.DoesNotContain("upgrade-nav-btn", await f.RenderedHtmlAsync());
        await f.StartAsync();
        await f.SubmitAsync(correct: true);
        await f.SubmitAsync(correct: true);
        Assert.Equal(0, (await f.Store.GetProgressionAsync()).EarnedSkillPoints);

        Assert.False(f.TerminalViewModel.IsUpgradeDiscovered);
        Assert.True(f.TerminalViewModel.HasAuthoritativeStatus);
        await f.OpenMenuAsync();
        await f.InvokeAsync("ToggleMenuProgress");
        var html = await f.RenderedHtmlAsync();
        Assert.DoesNotContain("upgrade-nav-btn", html);
        Assert.DoesNotContain("pause-upgrade-btn", html);
        Assert.DoesNotContain("header-sp-badge", html);
        Assert.DoesNotContain(f.Localizer["UpgradeTerminal_HeaderAction"], html);
        Assert.DoesNotContain(f.Localizer["UpgradeTerminal_Title"], html);
    }

    [Fact]
    public async Task FirstLevelUp_IsAGenuinePurchaseOpportunity_AndRevealsUpgradeInHeaderAndMenu()
    {
        var justBelowLevel2 = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2) - 1;
        await using var f = await MenuFixture.CreateAsync(this, new(CumulativeXp: justBelowLevel2));
        await f.StartAsync();
        Assert.DoesNotContain("upgrade-nav-btn", await f.RenderedHtmlAsync());

        await f.SubmitAsync(correct: true);
        var progression = await f.Store.GetProgressionAsync();
        Assert.Equal(2, progression.PlayerLevel);
        Assert.Equal(1, progression.EarnedSkillPoints);
        Assert.True(f.TerminalViewModel.IsUpgradeDiscovered);
        Assert.True(f.TerminalViewModel.CanPurchase, "The revealed entry must lead to a real purchasable upgrade.");

        var header = MenuFixture.Header(await f.RenderedHtmlAsync());
        Assert.Single(Regex.Matches(header, "upgrade-nav-btn"));
        Assert.Contains("<span class=\"header-sp-badge\"", header);

        // Issue #123: the first opportunity also opens the one-time Upgrade spotlight, which the menu never stacks on.
        // Its explicit Later releases the held answer; the menu then offers Upgrade as well.
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.False(f.CanOpenGameMenu);
        await f.InvokeAsync("DismissUpgradeSpotlight");
        await f.OpenMenuAsync();
        Assert.Contains("pause-upgrade-btn", MenuFixture.Menu(await f.RenderedHtmlAsync()));
        Assert.Equal(progression, await f.Store.GetProgressionAsync());
    }

    [Fact]
    public async Task SpendingEverySkillPoint_KeepsUpgradeDiscovered_AcrossReopenAndAppRestart()
    {
        string directory;
        await using (var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 4)))
        {
            directory = f.Directory;
            await f.StartAsync();
            await f.OpenMenuAsync();
            await f.InvokeAsync("OpenUpgradeTerminalAsync");
            Assert.True((await f.TerminalViewModel.PurchaseUpgradeAsync()).IsSuccess);
            Assert.True((await f.TerminalViewModel.PurchaseUpgradeAsync()).IsSuccess);
            Assert.False((await f.TerminalViewModel.PurchaseUpgradeAsync()).IsSuccess);
            await f.InvokeAsync("CloseUpgradeTerminal");

            var spent = await f.Store.GetProgressionAsync();
            Assert.Equal(0, spent.AvailableSkillPoints);
            Assert.Equal(3, spent.SpentSkillPoints);
            var html = await f.RenderedHtmlAsync();
            Assert.Contains("upgrade-nav-btn", html);
            Assert.DoesNotContain("header-sp-badge", html);
            Assert.Contains("pause-upgrade-btn", MenuFixture.Menu(html));

            await f.InvokeAsync("StartOrResumePractice");
            await f.OpenMenuAsync();
            Assert.Contains("pause-upgrade-btn", MenuFixture.Menu(await f.RenderedHtmlAsync()));
            await f.NavigateToSettingsAndBackAsync();
            Assert.Contains("upgrade-nav-btn", await f.RenderedHtmlAsync());
        }

        // A cold restart: new store, session, coordinator and view over the same durable synthetic file.
        await using var restarted = await MenuFixture.CreateAsync(this, new(PlayerLevel: 4), existingDirectory: directory);
        Assert.Equal(3, (await restarted.Store.GetProgressionAsync()).SpentSkillPoints);
        Assert.True(restarted.TerminalViewModel.IsUpgradeDiscovered);
        Assert.Contains("upgrade-nav-btn", MenuFixture.Header(await restarted.RenderedHtmlAsync()));
        await restarted.StartAsync();
        await restarted.OpenMenuAsync();
        Assert.Contains("pause-upgrade-btn", MenuFixture.Menu(await restarted.RenderedHtmlAsync()));
    }

    [Fact]
    public async Task UnreadableFirstSnapshot_FailsClosed_AndAFailedLaterReadKeepsDiscovery()
    {
        var directory = CreateTempDirectory();
        using var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue125-faults.db"));
        await store.InitializeAsync();
        await MenuFixture.SeedAsync(store, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5));
        var faults = FailingProgressionStore.Wrap(store);

        faults.Fail = true;
        using var vm = new UpgradeTerminalViewModel(faults.Proxy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.InitializeAsync());
        Assert.False(vm.HasAuthoritativeStatus);
        Assert.False(vm.IsUpgradeDiscovered);

        faults.Fail = false;
        await vm.RefreshAsync();
        Assert.True(vm.IsUpgradeDiscovered);

        faults.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RefreshAsync());
        Assert.True(vm.IsUpgradeDiscovered);
        vm.Dispose();
        Assert.False(vm.IsUpgradeDiscovered);
    }

    [Fact]
    public async Task FullGameplayReset_StartsAFreshEpoch_InWhichUpgradeIsHiddenAgain()
    {
        var directory = CreateTempDirectory();
        using var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue125-reset.db"));
        await store.InitializeAsync();
        await MenuFixture.SeedAsync(store, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5));
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        Assert.True(vm.IsUpgradeDiscovered);

        var reset = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(reset.TargetEpoch);
        await store.ClearResetIntentAsync(reset.TargetEpoch);
        await vm.RefreshAsync();

        Assert.Equal(reset.TargetEpoch, vm.ResetEpoch);
        Assert.False(vm.IsUpgradeDiscovered);
    }

    [Fact]
    public void DiscoveryPolicy_IsDerivedFromMonotonicEarnedSkillPoints_AndTheCheapestRealUpgrade()
    {
        Assert.Equal(CyberDefenseAttackUpgradePolicy.GetUpgradeCost(1), UpgradeDiscoveryPolicy.FirstOpportunityCost);
        Assert.Equal(1, UpgradeDiscoveryPolicy.FirstOpportunityCost);

        Assert.False(UpgradeDiscoveryPolicy.IsDiscovered(UpgradeTerminalStatus.Initial()));
        var firstLevel = UpgradeTerminalStatus.FromProgression(Progression(level: 2, earned: 1, spent: 0, attackRank: 0));
        Assert.True(UpgradeDiscoveryPolicy.IsDiscovered(firstLevel));
        Assert.True(firstLevel.CanPurchase);
        // All Skill Points spent: nothing is affordable now, but the capability was genuinely earned.
        var spent = UpgradeTerminalStatus.FromProgression(Progression(level: 3, earned: 2, spent: 2, attackRank: 2));
        Assert.False(spent.CanPurchase);
        Assert.True(UpgradeDiscoveryPolicy.IsDiscovered(spent));
        // A Level 1 learner never has a purchasable upgrade, whatever the rank fields say.
        Assert.False(UpgradeDiscoveryPolicy.IsDiscovered(UpgradeTerminalStatus.FromProgression(Progression(level: 1, earned: 0, spent: 0, attackRank: 0))));
        Assert.Throws<ArgumentNullException>(() => UpgradeDiscoveryPolicy.IsDiscovered(null!));

        static CyberDefenseProgressionRecord Progression(int level, int earned, int spent, int attackRank) => new(
            storeRevision: 3, resetEpoch: 0, cumulativeXp: CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level), playerLevel: level,
            earnedSkillPoints: earned, spentSkillPoints: spent, availableSkillPoints: earned - spent, attackRank: attackRank,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion, updatedAt: DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task MenuInteractions_NeverWriteXpSkillPointsPurchasesOrAttempts()
    {
        await using var f = await MenuFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.StartAsync();
        var progression = await f.Store.GetProgressionAsync();
        var modifiers = await f.Store.GetCombatModifierSnapshotAsync();
        var run = await f.Store.GetRunStateAsync();

        for (var i = 0; i < 3; i++)
        {
            await f.OpenMenuAsync();
            await f.InvokeAsync("ToggleMenuProgress");
            await f.InvokeAsync("OpenUpgradeTerminalAsync");
            await f.InvokeAsync("CloseUpgradeTerminal");
            await f.PressBackAsync();
            await f.NavigateToSettingsAndBackAsync();
            await f.InvokeAsync("StartOrResumePractice");
            await f.PressBackAsync();
            await f.InvokeAsync("StartOrResumePractice");
        }

        Assert.Equal(progression, await f.Store.GetProgressionAsync());
        Assert.Equal(modifiers.CriticalStrikeRank, (await f.Store.GetCombatModifierSnapshotAsync()).CriticalStrikeRank);
        Assert.Equal(modifiers.OverdriveRank, (await f.Store.GetCombatModifierSnapshotAsync()).OverdriveRank);
        Assert.Equal(run, await f.Store.GetRunStateAsync());
        Assert.Empty(f.Learner.Attempts);
        Assert.Equal(0, f.Learner.CommitCalls);
        Assert.Equal(0, f.Preferences.ModeWrites);
    }

    // =========================================================================
    // LOCALIZATION AND STYLE CONTRACTS
    // =========================================================================

    [Theory]
    [InlineData("en", "Menu", "Open menu and pause", "Main menu", "Menu actions", "Continue", "Progress & statistics", "Learning progress", "Arithmetic stages", "Player level 3 · 4 / 20 XP", "Tutorial & help")]
    [InlineData("de", "Menü", "Menü öffnen und pausieren", "Hauptmenü", "Menüaktionen", "Fortsetzen", "Lernfortschritt", "Lernfortschritt", "Rechenstufen", "Spielerlevel 3 · 4 / 20 XP", "Tutorial und Hilfe")]
    [InlineData("ru", "Меню", "Открыть меню и поставить на паузу", "Главное меню", "Действия меню", "Продолжить", "Прогресс и статистика", "Прогресс обучения", "Уровни арифметики", "Уровень игрока 3 · 4 / 20 XP", "Обучение и помощь")]
    public void Localization_MenuCopyIsExactInEnglishGermanAndRussian(string language, string button, string open, string kicker, string actions,
        string resume, string progress, string progressTitle, string stages, string level, string tutorial)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        Assert.Equal(button, localizer["GameMenu_Button"]);
        Assert.Equal(open, localizer["GameMenu_OpenAriaLabel"]);
        Assert.Equal(kicker, localizer["GameMenu_Kicker"]);
        Assert.Equal(actions, localizer["GameMenu_ActionsAriaLabel"]);
        Assert.Equal(resume, localizer["GameMenu_Continue"]);
        Assert.Equal(progress, localizer["GameMenu_Progress"]);
        Assert.Equal(progressTitle, localizer["GameMenu_ProgressTitle"]);
        Assert.Equal(stages, localizer["GameMenu_StagesTitle"]);
        Assert.Equal(level, localizer["GameMenu_PlayerLevel", 3, 4, 20]);
        Assert.Equal(tutorial, localizer["GameMenu_Tutorial"]);
    }

    [Fact]
    public void Styles_MenuActionsAreTouchSized_ReducedMotionSafe_AndThemed()
    {
        var css = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "wwwroot", "app.css");

        var action = Regex.Match(css, @"\.game-menu-action\s*\{(?<rules>[^}]+)\}");
        Assert.True(action.Success);
        Assert.Contains("min-height: 2.75rem;", action.Groups["rules"].Value);
        Assert.Matches(@"\.app-theme-root\[data-theme=""dark""\]\s+\.game-menu-panel\s*\{", css);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*\.game-menu-panel[^{]*\{[^}]*animation: none", css);
        Assert.DoesNotContain(".settings-nav-btn", css);
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue125_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    // =========================================================================
    // FIXTURE
    // =========================================================================

    internal sealed record MenuOptions(
        string Language = "en",
        int PlayerLevel = 1,
        long? CumulativeXp = null,
        bool CyberDefense = true,
        bool TutorialCompleted = true,
        CurriculumStage Stage = CurriculumStage.Stage1_Addition,
        (ArithmeticOperation Operation, int Band)[]? Bands = null,
        // Issue #123: synthetic D10 Addition evidence, so the next correct Addition answer commits Stage 1 -> 2.
        bool AdditionMastered = false,
        // Issue #123: 21 synthetic correct setup attempts, so the next correct answer defeats the sector boss.
        bool PendingBossVictory = false,
        ArithmeticOperation? MasteredOperation = null);

    internal sealed class MenuFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private HtmlRenderer _renderer = null!;
        private HtmlRootComponent _root;
        private global::MathFirst.Core.Tests.Home _home = null!;
        private bool _mounted;

        public string Directory { get; }
        public TrainingSession Session { get; }
        public SqliteGameplayStore Store { get; }
        public LocalizationService Localizer { get; }
        public MenuPreferences Preferences { get; }
        public SyntheticLearner Learner { get; }
        public StepClock Clock { get; }
        public AppBackNavigationCoordinator Back { get; }
        public CyberDefenseSessionState ReplayRequests { get; }
        public CyberDefensePresentationSequence Ceremony { get; }

        public bool IsUpgradeTerminalOpen => Field<bool>("_isUpgradeTerminalOpen");
        public bool IsTutorialOpen => Field<bool>("_isTutorialOpen");
        public bool CanOpenGameMenu => (bool)Property("CanOpenGameMenu")!;
        public UpgradeTerminalViewModel TerminalViewModel => Field<UpgradeTerminalViewModel>("_upgradeTerminalViewModel");

        private MenuFixture(string directory, ServiceProvider services, TrainingSession session, SqliteGameplayStore store, LocalizationService localizer,
            MenuPreferences preferences, SyntheticLearner learner, StepClock clock, AppBackNavigationCoordinator back, CyberDefenseSessionState replay, CyberDefensePresentationSequence ceremony)
        {
            Directory = directory;
            _services = services;
            Session = session;
            Store = store;
            Localizer = localizer;
            Preferences = preferences;
            Learner = learner;
            Clock = clock;
            Back = back;
            ReplayRequests = replay;
            Ceremony = ceremony;
        }

        public static async Task<MenuFixture> CreateAsync(Issue125GameMenuTests suite, MenuOptions options, string? existingDirectory = null)
        {
            var directory = existingDirectory ?? suite.CreateTempDirectory();
            var clock = new StepClock();
            var preferences = new MenuPreferences(options.Language, options.CyberDefense, options.TutorialCompleted);
            var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue125-home.db"));
            await store.InitializeAsync();
            if (existingDirectory is null)
            {
                for (var i = 0; options.PendingBossVictory && i < 21; i++)
                {
                    await store.ApplyAttemptTransactionAsync(new CyberDefensePendingIntentRecord("setup-" + i, "1+1", true, true, 1000, 0, DateTimeOffset.UnixEpoch));
                }
                await SeedAsync(store, options.CumulativeXp ?? CyberDefenseLevelPolicy.GetCumulativeXpThreshold(options.PlayerLevel));
            }

            var progression = LearnerProgression.CreateFresh();
            progression.CurriculumStage = options.Stage;
            foreach (var (operation, band) in options.Bands ?? [])
            {
                progression.OperationProgressions[operation] = new OperationProgression(operation, band, 0);
            }

            var items = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
            var masteredOperation = options.MasteredOperation ?? (options.AdditionMastered ? ArithmeticOperation.Addition : (ArithmeticOperation?)null);
            if (masteredOperation is { } mastered)
            {
                progression.OperationProgressions[mastered] =
                    new OperationProgression(mastered, D10MasteryTestSeed.MasteredBandIndex, 0);
                foreach (var state in MasteredStates(mastered)) items[state.FactId] = state;
            }

            var learner = new SyntheticLearner(progression, items);
            var session = new TrainingSession(learner, clock, preferenceStore: preferences);
            var coordinator = new CyberDefenseCombatCoordinator(store, new CyberDefenseSubmissionConsumer(store, learner));
            var ceremony = new CyberDefensePresentationSequence(async (_, ct) =>
            {
                try { await Task.Delay(10, ct); } catch (OperationCanceledException) { }
            });
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(options.Language);
            var back = new AppBackNavigationCoordinator();
            var replay = new CyberDefenseSessionState(preferences);

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(session)
                .AddSingleton(ceremony)
                .AddSingleton<IGameplayStore>(store)
                .AddSingleton<ICyberDefenseCombatCoordinator>(coordinator)
                .AddSingleton<ILocalizationService>(localizer)
                .AddSingleton<IPreferenceStore>(preferences)
                .AddSingleton<ICyberDefenseModePreferences>(preferences)
                .AddSingleton<ICyberDefenseTutorialPreferences>(preferences)
                .AddSingleton(replay)
                .AddSingleton<ICyberDefenseTutorialReplayRequests>(replay)
                .AddSingleton(new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary())))
                .AddSingleton<IAppResetCoordinator>(new NoResetCoordinator())
                .AddSingleton<IAppBackNavigationCoordinator>(back)
                .AddSingleton<IHapticFeedbackService>(new SilentHaptics())
                .AddSingleton<IJSRuntime>(new NoJsRuntime())
                .BuildServiceProvider();

            var fixture = new MenuFixture(directory, services, session, store, localizer, preferences, learner, clock, back, replay, ceremony);
            await fixture.MountAsync();
            return fixture;
        }

        // Mirrors D10MasteryTestSeed: every D01..D10 mastery fact attempted once and answered correctly.
        private static IEnumerable<ItemLearningState> MasteredStates(ArithmeticOperation operation)
        {
            foreach (var factId in CurriculumUnlockPolicy.GetMasteryFactIds(operation))
            {
                var operands = factId[(factId.IndexOf(':') + 1)..].Split(['+', '-', '*', '/']);
                var state = ItemLearningState.CreateNew(new ArithmeticFact(operation, int.Parse(operands[0], CultureInfo.InvariantCulture), int.Parse(operands[1], CultureInfo.InvariantCulture)));
                Assert.Equal(factId, state.FactId);
                state.TotalAttempts = 1;
                state.CorrectAttempts = 1;
                state.ConsecutiveCorrectStreak = 1;
                yield return state;
            }
        }

        public static async Task SeedAsync(SqliteGameplayStore store, long cumulativeXp)
        {
            var level = CyberDefenseLevelPolicy.GetLevel(cumulativeXp);
            // This short-lived seed connection owns no pool. Store.Dispose clears only the store's own pool.
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = store.StoragePath,
                Pooling = false
            }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = @level, earned_sp = @sp, spent_sp = 0, available_sp = @sp WHERE id = 1;";
            command.Parameters.AddWithValue("@xp", cumulativeXp);
            command.Parameters.AddWithValue("@level", level);
            command.Parameters.AddWithValue("@sp", level - 1);
            await command.ExecuteNonQueryAsync();
        }

        public async Task MountAsync()
        {
            _renderer = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
            _root = await _renderer.Dispatcher.InvokeAsync(() =>
                _renderer.RenderComponentAsync<MenuHomeHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(h => _home = h)
                })));
            _mounted = true;
            await AwaitTerminalRefreshAsync();
        }

        public async Task UnmountAsync()
        {
            if (!_mounted) return;
            _mounted = false;
            var refresh = Field<Task?>("_terminalRefreshTask");
            await _renderer.DisposeAsync();
            if (refresh is not null) await refresh.WaitAsync(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Leaves Home for the Settings route and comes back. Settings.razor only suspends the practice surface on
        /// entry (<c>Session.PauseItemTiming()</c>); Home is disposed and a new instance re-enters the same session.
        /// </summary>
        public async Task NavigateToSettingsAndBackAsync()
        {
            await UnmountAsync();
            Session.PauseItemTiming();
            Clock.Advance(5000);
            await MountAsync();
        }

        public Task StartAsync() => InvokeAsync("StartOrResumePractice");

        public Task OpenMenuAsync() => InvokeAsync("OpenGameMenu");

        public async Task<bool> PressBackAsync()
        {
            var handled = Back.TryHandleBack();
            await _renderer.Dispatcher.InvokeAsync(() => { });
            return handled;
        }

        public Task RendererDispatcherFlushAsync() => _renderer.Dispatcher.InvokeAsync(() => { });

        public Task PressKeyAsync(string key) => InvokeAsync("HandleGlobalKeyDown", new KeyboardEventArgs { Key = key });

        public Task FinishTutorialAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tutorial = Field<object>("_tutorialComponent");
            for (var i = 0; i < 3; i++) await (Task)tutorial.GetType().GetMethod("NextAsync")!.Invoke(tutorial, null)!;
            await (Task)tutorial.GetType().GetMethod("FinishAsync")!.Invoke(tutorial, null)!;
        });

        public Task SkipTutorialAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var tutorial = Field<object>("_tutorialComponent");
            await (Task)tutorial.GetType().GetMethod("SkipAsync")!.Invoke(tutorial, null)!;
        });

        public async Task SubmitAsync(bool correct)
        {
            await _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var answer = Session.CurrentFact!.CorrectResult + (correct ? 0 : 1);
                SetField("_answerInput", answer.ToString(CultureInfo.InvariantCulture));
                await (Task)Method("Submit").Invoke(_home, null)!;
            });
            await AwaitTerminalRefreshAsync();
        }

        public async Task AwaitTerminalRefreshAsync()
        {
            await _renderer.Dispatcher.InvokeAsync(() => { });
            if (Field<Task?>("_terminalRefreshTask") is { } refresh) await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            await _renderer.Dispatcher.InvokeAsync(() => { });
        }

        public T Field<T>(string name) => (T)typeof(global::MathFirst.Core.Tests.Home)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;

        public object? Property(string name) => typeof(global::MathFirst.Core.Tests.Home)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home);

        public void SetField(string name, object value) => typeof(global::MathFirst.Core.Tests.Home)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_home, value);

        private static MethodInfo Method(string name) => typeof(global::MathFirst.Core.Tests.Home)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Home", name);

        /// <summary>Invokes a Home handler on the renderer dispatcher; the caller must already be on it.</summary>
        public void Invoke(string name, params object?[] args)
        {
            try
            {
                var result = Method(name).Invoke(_home, args.Length > 0 ? args : null);
                if (result is Task { IsCompleted: false })
                    throw new InvalidOperationException($"{name} must complete synchronously.");
            }
            catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }

        public Task InvokeAsync(string name, params object?[] args) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                object? result;
                try { result = Method(name).Invoke(_home, args.Length > 0 ? args : null); }
                catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
                if (result is Task task) await task;
                StateHasChanged();
            });

        public Task Dispatch(Action action) => _renderer.Dispatcher.InvokeAsync(action);

        public Task DispatchAsync(Func<Task> action) => _renderer.Dispatcher.InvokeAsync(action);

        private void StateHasChanged() =>
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null);

        public Task<string> HtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_root.ToHtmlString()));

        public async Task<string> RenderedHtmlAsync()
        {
            await _renderer.Dispatcher.InvokeAsync(StateHasChanged);
            return await HtmlAsync();
        }

        public static string Header(string html) => Regex.Match(html, @"<header class=""mathfirst-header""[\s\S]*?</header>").Value;

        public static string Gate(string html) => Regex.Match(html, @"<section class=""practice-overlay (?:game-menu-overlay )?practice-gate-overlay""[\s\S]*?</section>\s*</main>").Value;

        public static string Menu(string html)
        {
            var menu = Gate(html);
            Assert.Contains("game-menu-overlay", menu);
            return menu;
        }

        public async ValueTask DisposeAsync()
        {
            await UnmountAsync();
            await _services.DisposeAsync();
            Store.Dispose();
        }
    }

    internal sealed class MenuHomeHost : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.Home> Capture { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.Home>(0);
            builder.AddComponentReferenceCapture(1, instance => Capture((global::MathFirst.Core.Tests.Home)instance));
            builder.CloseComponent();
        }
    }

    internal sealed class MenuPreferences(string language, bool cyberDefense, bool tutorialCompleted)
        : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        private bool _tutorialCompleted = tutorialCompleted;
        public int ModeWrites { get; private set; }
        public bool GetCyberDefenseEnabled() => cyberDefense;
        public void SetCyberDefenseEnabled(bool enabled) => ModeWrites++;
        public bool GetTutorialCompleted() => _tutorialCompleted;
        public void SetTutorialCompleted(bool completed) => _tutorialCompleted = completed;
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => language;
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => operation == ArithmeticOperation.Addition;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => [ArithmeticOperation.Addition];
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => false;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    internal sealed class SyntheticLearner(LearnerProgression initial, IReadOnlyDictionary<string, ItemLearningState>? items = null) : ILearnerStore
    {
        private readonly LearnerProgression _initial = initial;
        private LearnerSnapshot _snapshot = new(initial, new Dictionary<string, ItemLearningState>(items ?? new Dictionary<string, ItemLearningState>(), StringComparer.Ordinal), [], 1, LearnerProgression.DefaultSchemaVersion);

        public int CommitCalls { get; private set; }
        public IReadOnlyList<AttemptRecord> Attempts => _snapshot.RecentAttempts;
        public string StoragePath => "synthetic://issue125-learner";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(PracticeSelectionEvidenceRequest request, CancellationToken ct = default) =>
            Task.FromResult(PracticeSelectionEvidence.FromSnapshot(_snapshot, request));
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long position, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, position, ids));
        // The authoritative mastery read (ADR-0013): only rows the synthetic store actually holds.
        public Task<IReadOnlyDictionary<string, ItemLearningState>> LoadItemStatesAsync(IReadOnlyCollection<string> factIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ItemLearningState>>(factIds
                .Where(_snapshot.ItemStates.ContainsKey)
                .ToDictionary(id => id, id => _snapshot.ItemStates[id], StringComparer.Ordinal));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet change, CancellationToken ct = default)
        {
            CommitCalls++;
            var items = _snapshot.ItemStates.ToDictionary(p => p.Key, p => p.Value);
            items[change.UpdatedItemState.FactId] = change.UpdatedItemState;
            _snapshot = new LearnerSnapshot(change.UpdatedProgression, items, _snapshot.RecentAttempts.Append(change.Attempt).ToArray(), change.ExpectedRevision + 1, LearnerProgression.DefaultSchemaVersion);
            _snapshot.Progression.StoreRevision = _snapshot.Revision;
            return Task.FromResult(PersistenceResult.Success(_snapshot.Revision));
        }
        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string id, CancellationToken ct = default)
        {
            var attempt = _snapshot.RecentAttempts.FirstOrDefault(a => a.SubmissionId == id);
            return Task.FromResult(attempt is null ? null : new CommittedLearnerAttemptEvidence(id, attempt.FactId, attempt.IsCorrect, attempt.ResponseLatencyMs, attempt.PracticePosition, attempt.Timestamp));
        }
        public Task ResetLearningProgressAsync(CancellationToken ct = default)
        {
            var fresh = LearnerProgression.CreateFresh();
            fresh.CurriculumStage = _initial.CurriculumStage;
            fresh.StoreRevision = _snapshot.Revision + 1;
            _snapshot = new LearnerSnapshot(fresh, new Dictionary<string, ItemLearningState>(), [], _snapshot.Revision + 1, LearnerProgression.DefaultSchemaVersion);
            return Task.CompletedTask;
        }
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class StepClock : IClock
    {
        private long _now;
        public long GetTimestamp() => _now;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(_now - startTimestamp);
        public void Advance(long milliseconds) => _now += milliseconds;
    }

    /// <summary>Forwards to the real synthetic SQLite store and fails the progression read on demand.</summary>
    public class FailingProgressionStore : DispatchProxy
    {
        private IGameplayStore _inner = null!;
        public volatile bool Fail;
        public IGameplayStore Proxy => (IGameplayStore)(object)this;

        public static FailingProgressionStore Wrap(IGameplayStore inner)
        {
            var proxy = (FailingProgressionStore)(object)Create<IGameplayStore, FailingProgressionStore>();
            proxy._inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Fail && targetMethod!.Name == nameof(IGameplayStore.GetProgressionAsync))
            {
                return Task.FromException<CyberDefenseProgressionRecord>(new InvalidOperationException("Synthetic unreadable progression snapshot"));
            }
            try { return targetMethod!.Invoke(_inner, args); }
            catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
        }
    }

    private sealed class NoResetCoordinator : IAppResetCoordinator
    {
        public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SilentHaptics : IHapticFeedbackService
    {
        public bool IsSupported => false;
        public void PerformCue(HapticFeedbackCue cue) { }
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
