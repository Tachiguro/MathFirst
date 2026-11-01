namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using MathFirst.Application;
using Microsoft.AspNetCore.Components;
using MenuFixture = Issue125GameMenuTests.MenuFixture;

/// <summary>
/// Issue #136: a tutorial replayed from the cyber main menu (the ManualPause gate) or from Settings while the menu is
/// open must show the paused gameplay field behind it as inert spotlight context, instead of the menu alone. The real
/// Home page runs against the Issue #125 fixture: synthetic in-memory learner, temporary SQLite store and step clock.
/// Pause, timing, resume and input isolation are asserted unchanged.
/// </summary>
public sealed class Issue136TutorialReplayTests : IDisposable
{
    private static readonly string[] SpotlightTargets = ["enemy-hud", "expression-keypad", "system-integrity"];

    private readonly Issue125GameMenuTests _suite = new();

    public void Dispose() => _suite.Dispose();

    [Theory]
    [InlineData("en", false)]
    [InlineData("de", true)]
    [InlineData("ru", false)]
    public async Task TutorialFromTheMenu_ShowsThePausedGameplayFieldAsInertSpotlightContext(string language, bool skip)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language));
        await f.StartAsync();
        f.Clock.Advance(700);
        var revision = f.Session.FactInstanceRevision;
        var fact = f.Session.CurrentFact!;
        await f.OpenMenuAsync();
        var activation = f.Session.PracticeGateActivationRevision;

        await f.InvokeAsync("OpenTutorialFromMenu");
        Assert.True(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);

        var html = await f.RenderedHtmlAsync();
        AssertGameplayContextBehindTutorial(html, fact.LeftOperand, fact.RightOperand);

        // Every spotlight step points at a target that is actually rendered behind the tutorial.
        var tutorial = f.Field<object>("_tutorialComponent");
        for (var step = 0; step < 3; step++)
        {
            await f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod("NextAsync")!.Invoke(tutorial, null)!);
            var stepHtml = await f.RenderedHtmlAsync();
            var active = Regex.Match(stepHtml, @"data-spotlight-active=""([^""]+)""").Groups[1].Value;
            Assert.Equal(SpotlightTargets[step], active);
            Assert.Contains($"data-spotlight-id=\"{active}\"", MainBlock(stepHtml));
            AssertGameplayContextBehindTutorial(stepHtml, fact.LeftOperand, fact.RightOperand);
        }

        // The visible field stays inert: keypad, typing, a direct Submit, Enter and Back reach nothing.
        await f.InvokeAsync("HandleKeypadKeyAsync", "7");
        await f.InvokeAsync("HandleAnswerInput", new ChangeEventArgs { Value = fact.CorrectResult.ToString(CultureInfo.InvariantCulture) });
        Assert.Equal(string.Empty, f.Session.CurrentAnswerInput);
        f.SetField("_answerInput", fact.CorrectResult.ToString(CultureInfo.InvariantCulture));
        await f.InvokeAsync("Submit");
        await f.PressKeyAsync("Enter");
        Assert.True(await f.PressBackAsync());
        f.SetField("_answerInput", string.Empty);
        f.Clock.Advance(20000);
        Assert.True(f.IsTutorialOpen);
        Assert.Empty(f.Learner.Attempts);
        Assert.Equal(0, f.Learner.CommitCalls);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.Equal(activation, f.Session.PracticeGateActivationRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(700, f.Session.GetCurrentActiveElapsedMs());

        if (skip)
        {
            await f.SkipTutorialAsync();
        }
        else
        {
            await f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod("NextAsync")!.Invoke(tutorial, null)!);
            await f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod("NextAsync")!.Invoke(tutorial, null)!);
            await FinishFromLastStepAsync(f);
        }

        // The menu returns, still paused, with focus on its Continue; only that Continue resumes, once.
        Assert.False(f.IsTutorialOpen);
        var after = await f.RenderedHtmlAsync();
        Assert.Contains("game-menu-overlay", after);
        Assert.DoesNotMatch(@"game-menu-overlay practice-gate-overlay""[^>]*\binert\b", after);
        Assert.DoesNotContain("tutorial-overlay", after);
        Assert.DoesNotContain("numeric-keypad", after);
        Assert.True(f.Field<bool>("_shouldFocusPrimaryOverlay"));
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(700, f.Session.GetCurrentActiveElapsedMs());

        await f.InvokeAsync("StartOrResumePractice");
        await f.InvokeAsync("StartOrResumePractice");
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(700, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Empty(f.Learner.Attempts);
    }

    [Fact]
    public async Task TutorialFromTheMenu_BackStepsKeepTheirTargets_AndSkipFromAnyStepReturnsToTheMenu()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new());
        await f.StartAsync();
        await f.OpenMenuAsync();
        await f.InvokeAsync("OpenTutorialFromMenu");
        var tutorial = f.Field<object>("_tutorialComponent");

        Task Step(string method) => f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod(method)!.Invoke(tutorial, null)!);
        await Step("NextAsync");
        await Step("NextAsync");
        await Step("BackAsync");
        var html = await f.RenderedHtmlAsync();
        Assert.Equal("enemy-hud", Regex.Match(html, @"data-spotlight-active=""([^""]+)""").Groups[1].Value);
        Assert.Contains("data-spotlight-id=\"enemy-hud\"", MainBlock(html));

        // Finish is refused before the last step; the tutorial stays open over the paused field.
        await Step("FinishAsync");
        Assert.True(f.IsTutorialOpen);

        await f.SkipTutorialAsync();
        Assert.False(f.IsTutorialOpen);
        Assert.Contains("game-menu-overlay", await f.RenderedHtmlAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
    }

    [Fact]
    public async Task TutorialReplayFromSettings_WhileTheMenuIsOpen_AlsoShowsTheGameplayField()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new());
        await f.StartAsync();
        f.Clock.Advance(400);
        var fact = f.Session.CurrentFact!;
        await f.OpenMenuAsync();

        // Settings' "Replay tutorial" while Home is unmounted; Home re-enters the same paused session.
        await f.UnmountAsync();
        f.Session.PauseItemTiming();
        f.ReplayRequests.RequestTutorialReplay();
        f.Clock.Advance(5000);
        await f.MountAsync();

        Assert.True(f.IsTutorialOpen);
        Assert.False(f.ReplayRequests.IsTutorialReplayRequested);
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        AssertGameplayContextBehindTutorial(await f.RenderedHtmlAsync(), fact.LeftOperand, fact.RightOperand);

        await f.SkipTutorialAsync();
        Assert.Contains("game-menu-overlay", await f.RenderedHtmlAsync());
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(400, f.Session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task FirstStartTutorial_WhileRunning_KeepsShowingTheGameplayField()
    {
        // Guard: the onboarding tutorial (Running gate) already showed the field and must keep doing so.
        await using var f = await MenuFixture.CreateAsync(_suite, new(TutorialCompleted: false));
        await f.StartAsync();
        Assert.True(f.IsTutorialOpen);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        var fact = f.Session.CurrentFact!;
        AssertGameplayContextBehindTutorial(await f.RenderedHtmlAsync(), fact.LeftOperand, fact.RightOperand);
        await f.SkipTutorialAsync();
        Assert.True(f.Session.IsTimingActive);
    }

    private static async Task FinishFromLastStepAsync(MenuFixture f)
    {
        var tutorial = f.Field<object>("_tutorialComponent");
        await f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod("FinishAsync", BindingFlags.Public | BindingFlags.Instance)!.Invoke(tutorial, null)!);
    }

    private static void AssertGameplayContextBehindTutorial(string html, int left, int right)
    {
        Assert.Single(Regex.Matches(html, @"class=""tutorial-overlay"""));
        // The tutorial is the only dialog: the menu does not stack under it.
        Assert.Single(Regex.Matches(html, @"role=""dialog"""));
        Assert.DoesNotContain("game-menu-overlay", html);

        var main = MainBlock(html);
        Assert.Matches(@"^<main class=""training-card[^""]*""[^>]*\binert\b", main);
        Assert.Matches(@"^<main [^>]*aria-hidden=""true""", main);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        foreach (var target in SpotlightTargets)
        {
            Assert.Contains($"data-spotlight-id=\"{target}\"", main);
        }
        Assert.Contains("class=\"cyber-defense-hud", main);
        Assert.Contains("class=\"player-shield-panel\"", main);
        Assert.Contains("class=\"numeric-keypad\"", main);
        Assert.Contains("answer-input", main);
        Assert.Contains($"<span class=\"operand\">{left}</span>", main);
        Assert.Contains($"<span class=\"operand\">{right}</span>", main);
    }

    private static string MainBlock(string html)
    {
        var start = html.IndexOf("<main class=\"training-card", StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing the training card.");
        var end = html.IndexOf("</main>", start, StringComparison.Ordinal);
        return html[start..(end + "</main>".Length)];
    }
}
