namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

/// <summary>
/// Authoritative behavioral integration and lifecycle test suite for Slice 3B:
/// Cyber Defense tutorial activation, Settings replay, spotlight targeting and timing-safe lifecycle integration.
/// </summary>
public sealed class SpotlightTutorialLifecycleTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch { }
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MathFirstTutorialLifecycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    // =========================================================================
    // 1. ELIGIBILITY
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Eligibility_FirstCyberDefenseStart_MountsTutorial_AndSuspendsTiming()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);

        // Before Start: At InitialReadyGate, no tutorial overlay yet
        Assert.Equal(PracticeGateState.InitialReadyGate, f.Session.PracticeGate);
        Assert.False(f.IsTutorialOpen);
        Assert.DoesNotContain("tutorial-overlay", await f.GetHtmlAsync());

        // Initial Start action
        await f.StartOrResumePracticeAsync();

        // Tutorial must be mounted, practice gate Running, and timing strictly suspended
        Assert.True(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        var html = await f.GetHtmlAsync();
        Assert.Contains("tutorial-overlay", html);
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("data-step=\"StoryCard\"", html);
    }

    [Fact(Timeout = 10000)]
    public async Task Eligibility_CompletedLearner_Start_SuppressesTutorialAutoOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true);

        await f.StartOrResumePracticeAsync();

        // Practice starts normally, no tutorial
        Assert.False(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.DoesNotContain("tutorial-overlay", await f.GetHtmlAsync());
    }

    [Fact(Timeout = 10000)]
    public async Task Eligibility_CalmMode_Start_SuppressesTutorialAutoOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: false, tutorialCompleted: false);

        await f.StartOrResumePracticeAsync();

        // Calm Mode suppresses tutorial
        Assert.False(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.DoesNotContain("tutorial-overlay", await f.GetHtmlAsync());
    }

    [Fact(Timeout = 10000)]
    public async Task Eligibility_StartupFailure_SuppressesTutorial()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false, failStartup: true);

        Assert.True(f.StartupFailed);
        Assert.False(f.IsTutorialOpen);
        Assert.DoesNotContain("tutorial-overlay", await f.GetHtmlAsync());
    }

    [Fact(Timeout = 10000)]
    public async Task Eligibility_BlockingOverlay_SuppressesTutorial()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);

        // Ceremony blocking
        typeof(CyberDefensePresentationSequence)
            .GetMethod("SetStage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Ceremony, new object[] { CyberDefensePresentationStage.Victory });
        Assert.True(f.Ceremony.IsBlocking);

        // While ceremony is blocking, start is blocked
        await f.StartOrResumePracticeAsync();
        Assert.False(f.IsTutorialOpen);
    }

    // =========================================================================
    // 2. PRESENTATION & SPOTLIGHT TARGET STATIONS
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Presentation_StoryCardFirst_ThenExactlyThreeSpotlightStations()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        // Station 0: Story Card
        Assert.True(f.IsTutorialOpen);
        Assert.Equal("story-intro", f.CurrentTutorialTarget);
        var html0 = await f.GetHtmlAsync();
        Assert.Contains("data-step=\"StoryCard\"", html0);
        Assert.Contains("data-target=\"story-intro\"", html0);

        // Station 1: Enemy HUD
        await f.NextTutorialStepAsync();
        Assert.Equal("enemy-hud", f.CurrentTutorialTarget);
        var html1 = await f.GetHtmlAsync();
        Assert.Contains("data-step=\"Enemy\"", html1);
        Assert.Contains("data-target=\"enemy-hud\"", html1);
        Assert.Contains("data-spotlight-active=\"enemy-hud\"", html1);

        // Station 2: Expression & Keypad
        await f.NextTutorialStepAsync();
        Assert.Equal("expression-keypad", f.CurrentTutorialTarget);
        var html2 = await f.GetHtmlAsync();
        Assert.Contains("data-step=\"ExpressionKeypad\"", html2);
        Assert.Contains("data-target=\"expression-keypad\"", html2);
        Assert.Contains("data-spotlight-active=\"expression-keypad\"", html2);

        // Station 3: System Integrity
        await f.NextTutorialStepAsync();
        Assert.Equal("system-integrity", f.CurrentTutorialTarget);
        var html3 = await f.GetHtmlAsync();
        Assert.Contains("data-step=\"SystemIntegrity\"", html3);
        Assert.Contains("data-target=\"system-integrity\"", html3);
        Assert.Contains("data-spotlight-active=\"system-integrity\"", html3);
        Assert.Contains("Finish", html3);

        // Back navigation
        await f.BackTutorialStepAsync();
        Assert.Equal("expression-keypad", f.CurrentTutorialTarget);
    }

    [Theory(Timeout = 10000)]
    [InlineData(0)] // Skip from StoryCard
    [InlineData(1)] // Skip from Enemy
    [InlineData(2)] // Skip from ExpressionKeypad
    [InlineData(3)] // Skip from SystemIntegrity
    public async Task Presentation_SkipAtAnyStage_DismissesTutorial_AndPersistsCompletion(int stepIndex)
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        for (int i = 0; i < stepIndex; i++)
        {
            await f.NextTutorialStepAsync();
        }

        Assert.True(f.IsTutorialOpen);
        await f.SkipTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Preferences.GetTutorialCompleted());
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
    }

    [Fact(Timeout = 10000)]
    public async Task Presentation_FinishOnFinalStage_DismissesTutorial_AndPersistsCompletion()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        await f.NextTutorialStepAsync(); // Enemy
        await f.NextTutorialStepAsync(); // ExpressionKeypad
        await f.NextTutorialStepAsync(); // SystemIntegrity

        Assert.True(f.IsTutorialOpen);
        await f.FinishTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Preferences.GetTutorialCompleted());
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
    }

    [Fact(Timeout = 10000)]
    public async Task Presentation_NormalRerender_PreservesCurrentStepAndDoesNotReset()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();
        await f.NextTutorialStepAsync(); // Step 1: Enemy

        Assert.Equal("enemy-hud", f.CurrentTutorialTarget);

        await f.RerenderAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.Equal("enemy-hud", f.CurrentTutorialTarget);
    }

    // =========================================================================
    // 3. PERSISTENCE & RESET BOUNDARIES
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Persistence_BackgroundOrNavigation_DoesNotPersistCompletion()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.False(f.Preferences.GetTutorialCompleted());

        // Background transition
        await f.DispatchAsync(() => f.Session.SetAppForeground(false));
        Assert.False(f.Preferences.GetTutorialCompleted());

        // Foreground return
        await f.DispatchAsync(() => f.Session.SetAppForeground(true));
        Assert.False(f.Preferences.GetTutorialCompleted());
    }

    [Fact(Timeout = 10000)]
    public async Task Persistence_UiReset_PreservesTutorialCompleted()
    {
        var prefs = new TestPreferenceStore();
        prefs.SetTutorialCompleted(true);

        prefs.ResetPracticePreferences();

        Assert.True(prefs.GetTutorialCompleted());
    }

    [Fact(Timeout = 10000)]
    public async Task Persistence_FullLocalReset_ClearsTutorialCompleted()
    {
        var prefs = new TestPreferenceStore();
        prefs.SetTutorialCompleted(true);

        prefs.ResetAllPreferences();

        Assert.False(prefs.GetTutorialCompleted());
    }

    // =========================================================================
    // 4. SETTINGS REPLAY
    // =========================================================================

    [Fact]
    public void Replay_CyberDefenseSessionState_Contract_RequestPeekConsume()
    {
        var prefs = new TestPreferenceStore();
        var sessionState = new CyberDefenseSessionState(prefs);

        Assert.False(sessionState.IsTutorialReplayRequested);
        Assert.False(sessionState.ConsumeTutorialReplayRequest());

        sessionState.RequestTutorialReplay();
        Assert.True(sessionState.IsTutorialReplayRequested);

        // Consumed once
        Assert.True(sessionState.ConsumeTutorialReplayRequest());
        Assert.False(sessionState.IsTutorialReplayRequested);
        Assert.False(sessionState.ConsumeTutorialReplayRequest());

        // Full reset clears pending replay
        sessionState.RequestTutorialReplay();
        Assert.True(sessionState.IsTutorialReplayRequested);
        sessionState.ClearEncounter();
        Assert.False(sessionState.IsTutorialReplayRequested);
    }

    [Fact(Timeout = 10000)]
    public async Task Replay_SettingsRequest_ReachesHome_MountsTutorialOnce_AndRetainsCompletion()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true);

        // Initially completed learner starts practice without tutorial
        await f.StartOrResumePracticeAsync();
        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Session.IsTimingActive);

        // Accrue some time
        f.Clock.Advance(2300);
        Assert.Equal(2300, f.Session.GetCurrentActiveElapsedMs());

        // Learner goes to Settings, requests replay via typed interface
        f.ReplayRequests.RequestTutorialReplay();
        Assert.True(f.CyberDefenseSessionState.IsTutorialReplayRequested);

        // Learner returns to Home: Home re-renders / re-initializes
        await f.MountOrSyncViewAsync();

        // Tutorial mounts for replay, timing is suspended, accrued time is preserved
        Assert.True(f.IsTutorialOpen);
        Assert.False(f.CyberDefenseSessionState.IsTutorialReplayRequested); // Request consumed
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(2300, f.Session.GetCurrentActiveElapsedMs());

        // Clock advances while in replay
        f.Clock.Advance(5000);
        Assert.Equal(2300, f.Session.GetCurrentActiveElapsedMs());

        // Dismiss replay
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.FinishTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Preferences.GetTutorialCompleted()); // Retained true!
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(2300, f.Session.GetCurrentActiveElapsedMs());

        // Further clock advance counts
        f.Clock.Advance(1000);
        Assert.Equal(3300, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact(Timeout = 10000)]
    public async Task Replay_RepeatedNavigation_DoesNotReplayAgain()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true);
        await f.StartOrResumePracticeAsync();

        f.CyberDefenseSessionState.RequestTutorialReplay();
        await f.MountOrSyncViewAsync();
        Assert.True(f.IsTutorialOpen);

        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);

        // Next navigation/mount does NOT replay
        await f.MountOrSyncViewAsync();
        Assert.False(f.IsTutorialOpen);
    }

    // =========================================================================
    // 5. TIMING SAFETY & TWO RESUMPTION CASES
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Timing_Case1_DeferredFirstStart_ZeroAccumulatedTime_WhileTutorialOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);

        await f.StartOrResumePracticeAsync();
        Assert.True(f.IsTutorialOpen);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // Advance synthetic clock during tutorial
        f.Clock.Advance(30000);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.False(f.Session.IsTimingActive);

        // Safe dismissal activates timing from 0
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.FinishTutorialAsync();
        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        f.Clock.Advance(1500);
        Assert.Equal(1500, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact(Timeout = 10000)]
    public async Task Timing_Case2_InterruptedActiveQuestion_PreservesAccruedTime()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true);
        await f.StartOrResumePracticeAsync();

        f.Clock.Advance(4200);
        Assert.Equal(4200, f.Session.GetCurrentActiveElapsedMs());

        // Trigger replay via typed interface while question is active
        f.ReplayRequests.RequestTutorialReplay();
        await f.MountOrSyncViewAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(4200, f.Session.GetCurrentActiveElapsedMs());

        // Advance synthetic clock
        f.Clock.Advance(18000);
        Assert.Equal(4200, f.Session.GetCurrentActiveElapsedMs());

        // Dismiss
        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(4200, f.Session.GetCurrentActiveElapsedMs());

        f.Clock.Advance(800);
        Assert.Equal(5000, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact(Timeout = 10000)]
    public async Task Timing_BackgroundTransition_DoesNotResumeTiming()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.False(f.Session.IsTimingActive);

        // Background the app while tutorial is open
        await f.DispatchAsync(() => f.Session.SetAppForeground(false));
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        // Return to foreground
        await f.DispatchAsync(() => f.Session.SetAppForeground(true));
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // Dismissing tutorial while in BackgroundResumeGate does NOT resume timing
        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact(Timeout = 10000)]
    public async Task Timing_StaleLearnerContext_FencesTimingResumption()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);

        // Simulate independent reset or generation advance while tutorial was open
        await f.DispatchAsync(() => f.Session.ResetLearningProgressAsync(startTiming: false));

        // Dismiss tutorial
        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);

        // Timing must NOT resume because generation changed
        Assert.False(f.Session.IsTimingActive);
    }

    // =========================================================================
    // 6. INPUT & MODAL ISOLATION
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Input_EnterKey_DoesNotSubmitOrAdvance_WhileTutorialOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);
        var revision = f.Session.FactInstanceRevision;

        // Press Enter
        await f.PressKeyAsync("Enter");

        // No advance, fact revision unchanged
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.True(f.IsTutorialOpen);
    }

    [Fact(Timeout = 10000)]
    public async Task Input_KeypadTaps_Ignored_WhileTutorialOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);

        // Tap keypad key "5"
        await f.TapKeypadAsync("5");

        Assert.Equal(string.Empty, f.Session.CurrentAnswerInput);
    }

    [Fact(Timeout = 10000)]
    public async Task Input_UpgradeTerminal_CannotOpen_WhileTutorialOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();

        Assert.True(f.IsTutorialOpen);

        await f.TryOpenUpgradeTerminalAsync();

        Assert.False(f.IsUpgradeTerminalOpen);
    }

    // =========================================================================
    // 7. LOCALIZATION & ACCESSIBILITY
    // =========================================================================

    [Theory]
    [InlineData("en", "Replay Tutorial")]
    [InlineData("de", "Anleitung wiederholen")]
    [InlineData("ru", "Повторить обучение")]
    public void Localization_ReplayLabel_ExactParity(string lang, string expected)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(lang);

        Assert.Equal(expected, localizer["Settings_CyberDefenseReplayTutorial"]);
    }

    [Fact]
    public void Accessibility_CssSpotlightTargeting_ContainsReducedMotionAndGlowRules()
    {
        var cssPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css");
        var css = File.ReadAllText(cssPath);

        Assert.Contains("data-spotlight-active", css);
        Assert.Contains("data-spotlight-id", css);
        Assert.Contains("prefers-reduced-motion", css);
    }

    [Fact(Timeout = 10000)]
    public async Task Reentry_UnfinishedTutorial_PreservesIncompleteState_AndDoesNotBypassOnboarding()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);

        // Instance 1: start practice -> tutorial mounts
        await f.StartOrResumePracticeAsync();
        Assert.True(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // Dispose Instance 1 before Skip or Finish
        await f.DisposeCurrentHomeAsync();
        Assert.False(f.Preferences.GetTutorialCompleted());

        // Render genuine second Home instance sharing the same application-scoped Session, preferences, and state
        await f.RenderSecondHomeInstanceAsync();

        // Required invariants on re-entry:
        // - Completion remains false
        // - Tutorial appears again when eligible
        // - Practice surface is timing-inactive (no thinking time leaks)
        Assert.False(f.Preferences.GetTutorialCompleted());
        Assert.True(f.IsTutorialOpen, "Unfinished tutorial must not be silently bypassed on re-entry.");
        Assert.False(f.Session.IsTimingActive, "No active thinking time may accumulate during re-entry.");
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // Normal completion on the second instance permits safe practice continuation
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        await f.FinishTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.True(f.Preferences.GetTutorialCompleted());
        Assert.True(f.Session.IsTimingActive);
    }

    [Fact(Timeout = 10000)]
    public async Task Timing_BackgroundResumeGate_FollowedBySkip_RestoresTimingOnExplicitResume()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);

        // 1. Start new Cyber Defense tutorial
        await f.StartOrResumePracticeAsync();
        Assert.True(f.IsTutorialOpen);

        // 2. Verify timing inactive and elapsed 0
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // 3. Advance fake clock while tutorial is open
        f.Clock.Advance(10000);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // 4. Background application
        await f.DispatchAsync(() => f.Session.SetAppForeground(false));
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        // 5. Return to foreground with BackgroundResumeGate
        await f.DispatchAsync(() => f.Session.SetAppForeground(true));
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        // 6. Explicitly Skip tutorial
        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);

        // 7. Verify timing remains inactive while at BackgroundResumeGate
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        // 8. Explicitly resume practice
        await f.StartOrResumePracticeAsync();

        // 9. Verify timing becomes active
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive, "Explicit resume after tutorial dismissal must restore timing activity.");

        // 10. Advance fake clock
        f.Clock.Advance(2500);

        // 11. Verify elapsed increases by the correct amount
        Assert.Equal(2500, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact(Timeout = 10000)]
    public async Task ModalIsolation_HeaderAndMainInert_WhenTutorialIsOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();
        Assert.True(f.IsTutorialOpen);

        var html = await f.GetHtmlAsync();

        // Header must be inert and hidden from assistive navigation while tutorial is open
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*aria-hidden=""true""", html);

        // Main region must be inert and aria-hidden
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*\binert\b", html);
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*aria-hidden=""true""", html);

        // Tutorial dialog must be interactive (not enclosed in inert element)
        Assert.Contains("class=\"tutorial-overlay\"", html);

        // Dismiss tutorial
        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);

        var dismissedHtml = await f.GetHtmlAsync();
        var headerStart = dismissedHtml.IndexOf("<header class=\"mathfirst-header\"", StringComparison.Ordinal);
        var headerEnd = dismissedHtml.IndexOf('>', headerStart);
        var headerTag = dismissedHtml.Substring(headerStart, headerEnd - headerStart + 1);
        Assert.DoesNotContain("inert", headerTag);
        Assert.DoesNotContain("aria-hidden=\"true\"", headerTag);
    }

    [Fact(Timeout = 10000)]
    public async Task InputGuard_DirectSubmit_BlockedWhileTutorialIsOpen()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: false);
        await f.StartOrResumePracticeAsync();
        Assert.True(f.IsTutorialOpen);

        f.Session.SetCurrentAnswerInput("42");

        // Invoking direct submit must be rejected
        await f.DirectSubmitAsync();

        Assert.Equal(0, f.Session.SessionTotalCount);
        Assert.Null(f.Session.LastEvaluation);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    // =========================================================================
    // TEST FIXTURE HARNESS
    // =========================================================================

    // =========================================================================
    // ISSUE #112: PAUSE-STATE REPLAY, UPGRADE MODAL CONTAINMENT AND INPUT BLOCKING
    // =========================================================================

    [Fact(Timeout = 10000)]
    public async Task Issue112_ReplayDuringManualPause_OpensOverTheInertPausedGameField_AndKeepsTimingSuspendedUntilResume()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true);
        await f.StartOrResumePracticeAsync();
        f.Clock.Advance(1700);
        var revision = f.Session.FactInstanceRevision;

        await f.InvokeHomeAsync("PausePractice");
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        f.ReplayRequests.RequestTutorialReplay();
        await f.MountOrSyncViewAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.False(f.Session.IsTimingActive);
        var html = await f.GetHtmlAsync();
        // Issue #136: the paused game field, not the pause gate, is the inert spotlight context behind the tutorial.
        Assert.DoesNotContain("practice-gate-overlay", html);
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*\binert\b", html);
        Assert.Contains("data-spotlight-id=\"enemy-hud\"", html);
        Assert.Contains("class=\"numeric-keypad\"", html);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Contains("tutorial-btn-skip", html);
        Assert.Contains("tutorial-btn-next", html);
        Assert.DoesNotContain("tutorial-btn-back", html);

        await f.NextTutorialStepAsync();
        html = await f.GetHtmlAsync();
        Assert.Contains("tutorial-btn-skip", html);
        Assert.Contains("tutorial-btn-back", html);
        Assert.Contains("tutorial-btn-next", html);

        await f.NextTutorialStepAsync();
        await f.NextTutorialStepAsync();
        html = await f.GetHtmlAsync();
        Assert.Contains("tutorial-btn-skip", html);
        Assert.Contains("tutorial-btn-back", html);
        Assert.Contains("tutorial-btn-finish", html);
        Assert.DoesNotContain("tutorial-btn-next", html);

        f.Clock.Advance(9000);
        await f.FinishTutorialAsync();

        Assert.False(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(1700, f.Session.GetCurrentActiveElapsedMs());
        var after = await f.GetHtmlAsync();
        Assert.Contains("practice-gate-overlay", after);
        Assert.DoesNotMatch(@"practice-gate-overlay""[^>]*\binert\b", after);

        await f.StartOrResumePracticeAsync();
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(1700, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
    }

    [Fact(Timeout = 10000)]
    public async Task Issue112_UpgradeTerminal_IsSingleTopLayerOutsideTrainingCard_BlocksBackgroundInput_AndSuspendsTiming()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true, upgradeDiscovered: true);
        await f.StartOrResumePracticeAsync();
        f.Clock.Advance(1200);
        var revision = f.Session.FactInstanceRevision;
        var before = await f.GetHtmlAsync();
        var expressionBefore = Regex.Match(before, @"<div class=""expression-row[\s\S]*?</div>").Value;
        Assert.NotEmpty(expressionBefore);
        Assert.Matches(@"upgrade-nav-btn[^>]*>[\s\S]*?<span class=""upgrade-nav-text"">" + Regex.Escape(f.Localizer["UpgradeTerminal_HeaderAction"]) + "</span>", before);

        await f.TryOpenUpgradeTerminalAsync();
        await f.RerenderAsync();

        Assert.True(f.IsUpgradeTerminalOpen);
        Assert.False(f.Session.IsTimingActive);
        var html = await f.GetHtmlAsync();
        Assert.Single(Regex.Matches(html, @"upgrade-terminal-dialog"));
        Assert.True(html.IndexOf("upgrade-terminal-dialog", StringComparison.Ordinal) > html.IndexOf("</main>", StringComparison.Ordinal),
            "The Upgrade Terminal must render outside the training-card grid.");
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Matches(@"class=""training-top-region""[^>]*\binert\b", html);
        Assert.Matches(@"class=""practice-layout""[^>]*\binert\b", html);
        Assert.DoesNotContain("tutorial-overlay", html);

        var input = f.Session.CurrentAnswerInput;
        await f.TapKeypadAsync("7");
        await f.PressKeyAsync("8");
        Assert.Equal(input, f.Session.CurrentAnswerInput);

        f.Clock.Advance(15000);
        Assert.Equal(1200, f.Session.GetCurrentActiveElapsedMs());

        await f.InvokeHomeAsync("CloseUpgradeTerminal");

        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(1200, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        var after = await f.GetHtmlAsync();
        Assert.DoesNotContain("upgrade-terminal-dialog", after);
        Assert.DoesNotMatch(@"class=""practice-layout""[^>]*\binert\b", after);
        Assert.Equal(expressionBefore, Regex.Match(after, @"<div class=""expression-row[\s\S]*?</div>").Value);
    }

    [Fact(Timeout = 10000)]
    public async Task Issue112_UpgradeTerminalFromPauseOverlay_ClosesBackToResponsivePause_WithoutResumingTiming()
    {
        await using var f = await HomeFixture.CreateAsync(this, cyberDefense: true, tutorialCompleted: true, upgradeDiscovered: true);
        await f.StartOrResumePracticeAsync();
        f.Clock.Advance(800);
        await f.InvokeHomeAsync("PausePractice");
        Assert.Contains("pause-upgrade-btn", await f.GetHtmlAsync());

        await f.TryOpenUpgradeTerminalAsync();
        await f.RerenderAsync();
        var html = await f.GetHtmlAsync();
        Assert.True(f.IsUpgradeTerminalOpen);
        Assert.Matches(@"practice-gate-overlay""[^>]*\binert\b", html);
        Assert.True(html.IndexOf("upgrade-terminal-dialog", StringComparison.Ordinal) > html.IndexOf("</main>", StringComparison.Ordinal));

        await f.InvokeHomeAsync("CloseUpgradeTerminal");

        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(800, f.Session.GetCurrentActiveElapsedMs());
        Assert.DoesNotMatch(@"practice-gate-overlay""[^>]*\binert\b", await f.GetHtmlAsync());
    }

    private sealed class HomeFixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private HtmlRootComponent _root;
        private global::MathFirst.Core.Tests.Home _home;

        public FakeClock Clock { get; }
        public TestPreferenceStore Preferences { get; }
        public CyberDefenseSessionState CyberDefenseSessionState { get; }
        public ICyberDefenseTutorialReplayRequests ReplayRequests => _services.GetRequiredService<ICyberDefenseTutorialReplayRequests>();
        public TrainingSession Session { get; }
        public CyberDefenseCombatCoordinator Coordinator { get; }
        public CyberDefensePresentationSequence Ceremony { get; }
        public SqliteGameplayStore Store { get; }
        public LocalizationService Localizer { get; }
        public bool StartupFailed => (bool)typeof(global::MathFirst.Core.Tests.Home)
            .GetField("_startupFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;
        public bool IsTutorialOpen => (bool)(typeof(global::MathFirst.Core.Tests.Home)
            .GetField("_isTutorialOpen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home) ?? false);
        public bool IsUpgradeTerminalOpen => (bool)typeof(global::MathFirst.Core.Tests.Home)
            .GetField("_isUpgradeTerminalOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;
        public string? CurrentTutorialTarget
        {
            get
            {
                var stepVal = typeof(global::MathFirst.Core.Tests.Home)
                    .GetField("_tutorialCurrentStep", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);
                if (stepVal is null) return null;
                return (int)stepVal switch
                {
                    0 => "story-intro",
                    1 => "enemy-hud",
                    2 => "expression-keypad",
                    3 => "system-integrity",
                    _ => "unknown"
                };
            }
        }

        private HomeFixture(
            string directory,
            ServiceProvider services,
            HtmlRenderer renderer,
            HtmlRootComponent root,
            global::MathFirst.Core.Tests.Home home,
            FakeClock clock,
            TestPreferenceStore preferences,
            CyberDefenseSessionState sessionState,
            TrainingSession session,
            CyberDefenseCombatCoordinator coordinator,
            CyberDefensePresentationSequence ceremony,
            SqliteGameplayStore store,
            LocalizationService localizer)
        {
            _directory = directory;
            _services = services;
            _renderer = renderer;
            _root = root;
            _home = home;
            Clock = clock;
            Preferences = preferences;
            CyberDefenseSessionState = sessionState;
            Session = session;
            Coordinator = coordinator;
            Ceremony = ceremony;
            Store = store;
            Localizer = localizer;
        }

        public static async Task<HomeFixture> CreateAsync(
            SpotlightTutorialLifecycleTests suite,
            bool cyberDefense = true,
            bool tutorialCompleted = false,
            bool failStartup = false,
            bool upgradeDiscovered = false)
        {
            var dir = suite.CreateTempDirectory();
            var clock = new FakeClock();
            var preferences = new TestPreferenceStore();
            preferences.SetCyberDefenseEnabled(cyberDefense);
            preferences.SetTutorialCompleted(tutorialCompleted);

            var sessionState = new CyberDefenseSessionState(preferences);
            var store = new SqliteGameplayStore(Path.Combine(dir, "gameplay.db"));
            await store.InitializeAsync();
            if (upgradeDiscovered)
            {
                // Issue #125: Upgrade is offered only after the first earned Skill Point (synthetic Level 2).
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.StoragePath}");
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = 2, earned_sp = 1, spent_sp = 0, available_sp = 1 WHERE id = 1;";
                command.Parameters.AddWithValue("@xp", MathFirst.Domain.CyberDefense.CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2));
                await command.ExecuteNonQueryAsync();
            }

            var learner = new TestLearnerStore();
            var session = new TrainingSession(learner, clock, preferenceStore: preferences);
            var coordinator = new CyberDefenseCombatCoordinator(store, new CyberDefenseSubmissionConsumer(store, learner));
            var ceremony = new CyberDefensePresentationSequence((_, ct) => Task.CompletedTask);
            var localizer = new LocalizationService();

            IAppResetCoordinator resetCoord = failStartup ? new FailingResetCoordinator() : new NoResetCoordinator();

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
                .AddSingleton(sessionState)
                .AddSingleton<ICyberDefenseTutorialReplayRequests>(sp => sp.GetRequiredService<CyberDefenseSessionState>())
                .AddSingleton(new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary())))
                .AddSingleton<IAppResetCoordinator>(resetCoord)
                .AddSingleton<MathFirst.Application.Navigation.IAppBackNavigationCoordinator>(new MathFirst.Application.Navigation.AppBackNavigationCoordinator())
                .AddSingleton<IHapticFeedbackService>(new TestHapticFeedbackService())
                .AddSingleton<IJSRuntime>(new TestJsRuntime())
                .BuildServiceProvider();

            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

            global::MathFirst.Core.Tests.Home capturedHome = null!;
            var root = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                return await renderer.RenderComponentAsync<HomeHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(h => capturedHome = h)
                }));
            });

            return new HomeFixture(dir, services, renderer, root, capturedHome, clock, preferences, sessionState, session, coordinator, ceremony, store, localizer);
        }

        public Task StartOrResumePracticeAsync() =>
            _renderer.Dispatcher.InvokeAsync(() =>
            {
                typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("StartOrResumePractice", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task NextTutorialStepAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var comp = (global::MathFirst.Core.Tests.CyberDefenseTutorial?)typeof(global::MathFirst.Core.Tests.Home)
                    .GetField("_tutorialComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);
                if (comp is not null) await comp.NextAsync();
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task BackTutorialStepAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var comp = (global::MathFirst.Core.Tests.CyberDefenseTutorial?)typeof(global::MathFirst.Core.Tests.Home)
                    .GetField("_tutorialComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);
                if (comp is not null) await comp.BackAsync();
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task SkipTutorialAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var comp = (global::MathFirst.Core.Tests.CyberDefenseTutorial?)typeof(global::MathFirst.Core.Tests.Home)
                    .GetField("_tutorialComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);
                if (comp is not null) await comp.SkipAsync();
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task FinishTutorialAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var comp = (global::MathFirst.Core.Tests.CyberDefenseTutorial?)typeof(global::MathFirst.Core.Tests.Home)
                    .GetField("_tutorialComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);
                if (comp is not null) await comp.FinishAsync();
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task PressKeyAsync(string key) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("HandleGlobalKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)method.Invoke(_home, new object[] { new KeyboardEventArgs { Key = key } })!;
                await task;
            });

        public Task TapKeypadAsync(string key) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("HandleKeypadKeyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)method.Invoke(_home, new object[] { key })!;
                await task;
            });

        public Task TryOpenUpgradeTerminalAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("OpenUpgradeTerminalAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)method.Invoke(_home, null)!;
                await task;
            });

        public Task MountOrSyncViewAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var initMethod = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("OnInitializedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)initMethod.Invoke(_home, null)!;
                await task;
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task RerenderAsync() =>
            _renderer.Dispatcher.InvokeAsync(() =>
            {
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public string? ValidationError => (string?)typeof(global::MathFirst.Core.Tests.Home)
            .GetField("_validationError", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_home);

        public async Task<global::MathFirst.Core.Tests.Home> RenderSecondHomeInstanceAsync()
        {
            global::MathFirst.Core.Tests.Home secondHome = null!;
            _root = await _renderer.Dispatcher.InvokeAsync(async () =>
            {
                return await _renderer.RenderComponentAsync<HomeHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(h => secondHome = h)
                }));
            });
            _home = secondHome;
            return secondHome;
        }

        public Task DisposeCurrentHomeAsync() =>
            _renderer.Dispatcher.InvokeAsync(() =>
            {
                _home.Dispose();
            });

        public Task DirectSubmitAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)method.Invoke(_home, null)!;
                await task;
            });

        public Task InvokeHomeAsync(string methodName) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var result = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
                if (result is Task task) await task;
                typeof(ComponentBase)
                    .GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
            });

        public Task DispatchAsync(Action action) => _renderer.Dispatcher.InvokeAsync(action);
        public Task DispatchAsync(Func<Task> action) => _renderer.Dispatcher.InvokeAsync(action);

        public Task<string> GetHtmlAsync() =>
            _renderer.Dispatcher.InvokeAsync(() => System.Net.WebUtility.HtmlDecode(_root.ToHtmlString()));

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class HomeHost : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.Home> Capture { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.Home>(0);
            builder.AddComponentReferenceCapture(1, instance => Capture((global::MathFirst.Core.Tests.Home)instance));
            builder.CloseComponent();
        }
    }

    private sealed class TestPreferenceStore : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        private bool _cyberDefense = true;
        private bool _tutorialCompleted;

        public bool GetCyberDefenseEnabled() => _cyberDefense;
        public void SetCyberDefenseEnabled(bool enabled) => _cyberDefense = enabled;

        public bool GetTutorialCompleted() => _tutorialCompleted;
        public void SetTutorialCompleted(bool completed) => _tutorialCompleted = completed;

        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => "en";
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => [ArithmeticOperation.Addition];
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => false;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() => _cyberDefense = true;
        public void ResetAllPreferences()
        {
            _cyberDefense = true;
            _tutorialCompleted = false;
        }
    }

    private sealed class TestLearnerStore : ILearnerStore
    {
        private LearnerSnapshot _snapshot = CreateFresh();

        private static LearnerSnapshot CreateFresh()
        {
            var p = LearnerProgression.CreateFresh();
            p.StoreRevision = 1;
            return new LearnerSnapshot(p, new Dictionary<string, ItemLearningState>(), [], 1, LearnerProgression.DefaultSchemaVersion);
        }

        public string StoragePath => "synthetic://test-learner";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(PracticeSelectionEvidenceRequest request, CancellationToken ct = default) =>
            Task.FromResult(PracticeSelectionEvidence.FromSnapshot(_snapshot, request));
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long position, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet change, CancellationToken ct = default)
        {
            _snapshot = new LearnerSnapshot(change.UpdatedProgression, _snapshot.ItemStates, _snapshot.RecentAttempts.Append(change.Attempt).ToArray(), _snapshot.Revision + 1, LearnerProgression.DefaultSchemaVersion);
            return Task.FromResult(PersistenceResult.Success(_snapshot.Revision));
        }
        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string id, CancellationToken ct = default) => Task.FromResult<CommittedLearnerAttemptEvidence?>(null);
        public Task ResetLearningProgressAsync(CancellationToken ct = default)
        {
            _snapshot = CreateFresh();
            return Task.CompletedTask;
        }
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeClock : IClock
    {
        private long _timestamp;
        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(_timestamp - startTimestamp);
        public void Advance(long ms) => _timestamp += ms;
    }

    private sealed class NoResetCoordinator : IAppResetCoordinator
    {
        public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FailingResetCoordinator : IAppResetCoordinator
    {
        public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => throw new InvalidOperationException("Simulated startup failure");
        public Task ExecuteFullResetAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class TestHapticFeedbackService : IHapticFeedbackService
    {
        public bool IsSupported => false;
        public void PerformCue(HapticFeedbackCue cue) { }
    }

    private sealed class TestJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
