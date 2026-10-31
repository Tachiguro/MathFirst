namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using Microsoft.AspNetCore.Components;
using MenuFixture = Issue125GameMenuTests.MenuFixture;

/// <summary>
/// Issue #123: the one-time Upgrade spotlight and operation-unlock announcements that follow a newly committed answer.
/// The submission completes once the answer is committed; the presentation gate, not the submission task, holds the
/// committed answer on CorrectFeedback until its own explicit acknowledgement advances the question exactly once.
/// The real Home page runs against a synthetic in-memory learner, a temporary SQLite gameplay store and a fake clock.
/// Every test is time-bounded so a presentation that waits on a submission can never hang the run again.
/// </summary>
public sealed class Issue123PresentationGateTests : IDisposable
{
    private const int Bound = 30000;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(10);

    private readonly Issue125GameMenuTests _suite = new();

    public void Dispose() => _suite.Dispose();

    private static long JustBelowLevel2 => CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2) - 1;

    [Theory(Timeout = Bound)]
    [InlineData("cancel")]
    [InlineData("disposed")]
    [InlineData("failure")]
    public async Task UpgradeRefreshFailure_PreservesIndependentCommittedUnlock_UntilExplicitContinue(string failure)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, AdditionMastered: true));
        await f.StartAsync();
        var faults = await InstallRefreshFaultAsync(f);
        faults.Read = () => Task.FromException<CyberDefenseProgressionRecord>(RefreshException(failure));
        var revision = f.Session.FactInstanceRevision;
        f.Clock.Advance(1700);

        await f.SubmitAsync(true).WaitAsync(Step);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, f.Session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Contains("Subtraction unlocked", await f.RenderedHtmlAsync());
        await f.Dispatch(() => f.Session.SetAppForeground(false));
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        await f.Dispatch(() => f.Session.SetAppForeground(true));
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        await f.InvokeAsync("StartOrResumePractice");
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.Equal(1700, f.Learner.Attempts[0].ResponseLatencyMs);
    }

    [Theory(Timeout = Bound)]
    [InlineData("cancel", true)]
    [InlineData("disposed", false)]
    [InlineData("failure", true)]
    [InlineData("failure", false)]
    public async Task FailedGuidedTerminalOpening_RestoresExplicitRecovery_AndAdvancesExactlyOnce(string failure, bool retry)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        await f.SubmitAsync(true).WaitAsync(Step);
        var revision = f.Session.FactInstanceRevision;
        var latency = f.Session.LastEvaluation!.LatencyMs;
        var faults = await InstallRefreshFaultAsync(f);
        faults.Read = () => Task.FromException<CyberDefenseProgressionRecord>(RefreshException(failure));

        await f.InvokeAsync("OpenUpgradeTerminalFromSpotlightAsync");

        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.NotNull(f.Field<object?>("_committedPresentationGate"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(SessionInteractionState.CorrectFeedback, f.Session.InteractionState);
        Assert.False(f.Session.IsTimingActive);
        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Contains("aria-label=\"Open Upgrades\"", html);
        Assert.Contains("aria-label=\"Later\"", html);
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*\binert\b", html);
        f.Clock.Advance(60000);
        await f.InvokeAsync("HandleKeypadKeyAsync", "7");
        await f.InvokeAsync("Submit");
        await f.InvokeAsync("AdvanceAsync");
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(latency, f.Session.LastEvaluation.LatencyMs);

        if (retry)
        {
            faults.Read = null;
            await f.InvokeAsync("OpenUpgradeTerminalFromSpotlightAsync");
            Assert.True(f.IsUpgradeTerminalOpen);
            Assert.True(f.Field<bool>("_isUpgradeOnboardingGuided"));
            Assert.Equal(revision, f.Session.FactInstanceRevision);
            await f.InvokeAsync("CloseUpgradeTerminal");
            await f.InvokeAsync("CloseUpgradeTerminal");
        }
        else
        {
            await f.InvokeAsync("DismissUpgradeSpotlight");
            await f.InvokeAsync("DismissUpgradeSpotlight");
        }
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.Null(f.Field<object?>("_committedPresentationGate"));
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
    }

    private static Exception RefreshException(string kind) => kind switch
    {
        "cancel" => new OperationCanceledException("Synthetic unrelated Upgrade cancellation"),
        "disposed" => new ObjectDisposedException("Synthetic Upgrade read"),
        _ => new InvalidOperationException("Synthetic Upgrade refresh failure")
    };

    [Theory(Timeout = Bound)]
    [InlineData(false, "reset")]
    [InlineData(false, "dispose")]
    [InlineData(true, "reset")]
    [InlineData(true, "dispose")]
    [InlineData(true, "background")]
    public async Task DelayedRefreshFailure_RespectsInvalidationAndForeground(bool openingTerminal, string invalidation)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, AdditionMastered: !openingTerminal));
        await f.StartAsync();
        if (openingTerminal) await f.SubmitAsync(true).WaitAsync(Step);
        var faults = await InstallRefreshFaultAsync(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<CyberDefenseProgressionRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        faults.Read = () => { entered.TrySetResult(); return read.Task; };
        var pending = openingTerminal ? f.InvokeAsync("OpenUpgradeTerminalFromSpotlightAsync") : f.SubmitAsync(true);
        await entered.Task.WaitAsync(Step);
        var revision = f.Session.FactInstanceRevision;
        if (invalidation == "reset") await f.DispatchAsync(() => f.Session.ResetLearningProgressAsync(startTiming: false));
        else if (invalidation == "dispose") await f.UnmountAsync();
        else await f.Dispatch(() => f.Session.SetAppForeground(false));
        var invalidatedRevision = f.Session.FactInstanceRevision;
        read.SetException(new OperationCanceledException("Synthetic delayed refresh cancellation"));
        await pending.WaitAsync(Step);

        Assert.Equal(invalidatedRevision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        if (invalidation == "background")
        {
            Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
            await f.InvokeAsync("DismissUpgradeSpotlight");
            Assert.Equal(revision, f.Session.FactInstanceRevision);
            faults.Read = null;
            await f.Dispatch(() => f.Session.SetAppForeground(true));
            await f.InvokeAsync("DismissUpgradeSpotlight");
            Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
            Assert.False(f.Session.IsTimingActive);
            Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        }
        else
        {
            Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
            Assert.Null(f.Field<object?>("_committedPresentationGate"));
        }
    }

    [Theory(Timeout = Bound)]
    [InlineData(CurriculumStage.Stage2_Subtraction, ArithmeticOperation.Subtraction, CurriculumStage.Stage3_Multiplication, ArithmeticOperation.Multiplication, "Multiplication unlocked")]
    [InlineData(CurriculumStage.Stage3_Multiplication, ArithmeticOperation.Multiplication, CurriculumStage.Stage4_Division, ArithmeticOperation.Division, "Division unlocked")]
    public async Task LaterCommittedStageTransition_RequiresContinueOnce_AndStartsNextFactTiming(
        CurriculumStage initial, ArithmeticOperation prerequisite, CurriculumStage expected, ArithmeticOperation unlocked, string title)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 6, Stage: initial, MasteredOperation: prerequisite));
        await f.StartAsync();
        Assert.Equal(initial, f.Session.Progression.CurriculumStage);
        var revision = f.Session.FactInstanceRevision;
        f.Clock.Advance(1200);
        await f.SubmitAsync(true).WaitAsync(Step);

        Assert.Equal(expected, f.Session.Progression.CurriculumStage);
        Assert.Equal(expected, (await f.Learner.LoadSnapshotAsync()).Progression.CurriculumStage);
        Assert.Equal(unlocked, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(0, f.Session.Progression.OperationProgressions[unlocked].BandIndex);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        var html = await f.RenderedHtmlAsync();
        Assert.Contains(title, html);
        Assert.Contains("operation-unlock-continue-btn", html);
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        await f.PressKeyAsync("Enter");
        await f.InvokeAsync("AdvanceAsync");
        await f.InvokeAsync("Submit");
        f.Clock.Advance(60000);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(1200, f.Session.LastEvaluation!.LatencyMs);
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, f.Session.InteractionState);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.DoesNotContain("operation-unlock-dialog", await f.RenderedHtmlAsync());
        f.Clock.Advance(250);
        Assert.Equal(250, f.Session.GetCurrentActiveElapsedMs());
        await f.SubmitAsync(true).WaitAsync(Step);
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(2, f.Learner.CommitCalls);
        await f.NavigateToSettingsAndBackAsync();
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(revision + 2, f.Session.FactInstanceRevision);
    }

    [Fact(Timeout = Bound)]
    public async Task CancelledDiscoveryRead_AfterANewerResetEpoch_NeverPresentsTheOldUnlock()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, AdditionMastered: true));
        await f.StartAsync();
        var faults = await InstallRefreshFaultAsync(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<CyberDefenseProgressionRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        faults.Read = () => { entered.TrySetResult(); return read.Task; };
        var submission = f.SubmitAsync(true);
        await entered.Task.WaitAsync(Step);
        var oldEpoch = f.TerminalViewModel.ResetEpoch;
        var reset = await f.Store.BeginOrGetResetIntentAsync();
        await f.Store.ResetGameplayStateAsync(reset.TargetEpoch);
        await f.Store.ClearResetIntentAsync(reset.TargetEpoch);
        faults.Read = null;
        await f.TerminalViewModel.RefreshAsync();
        Assert.True(f.TerminalViewModel.ResetEpoch > oldEpoch);
        read.SetException(new OperationCanceledException("Older discovery read cancelled after reset"));
        await submission.WaitAsync(Step);

        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Null(f.Field<object?>("_committedPresentationGate"));
    }

    [Theory]
    [InlineData("en", "Congratulations!", "Subtraction", "Multiplication", "Division")]
    [InlineData("de", "Glückwunsch!", "Subtraktionsaufgaben", "Multiplikationsaufgaben", "Divisionsaufgaben")]
    [InlineData("ru", "Поздравляем!", "вычитание", "умножение", "деление")]
    public void UnlockDescriptions_CongratulateAvailability_WithoutClaimingMastery(string language, string congratulations, string subtraction, string multiplication, string division)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        foreach (var (operation, name) in new[] { ("Subtraction", subtraction), ("Multiplication", multiplication), ("Division", division) })
        {
            var description = localizer["OperationUnlock_Desc_" + operation];
            Assert.StartsWith(congratulations, description);
            Assert.Contains(name, description);
            Assert.DoesNotContain("mastery", description);
            Assert.DoesNotContain("gemeistert", description);
            Assert.DoesNotContain("освоили", description);
        }
    }

    private static async Task<RefreshFaultStore> InstallRefreshFaultAsync(MenuFixture f)
    {
        await f.AwaitTerminalRefreshAsync();
        var faults = RefreshFaultStore.Wrap(f.Store);
        var vm = new UpgradeTerminalViewModel((IGameplayStore)(object)faults);
        await vm.InitializeAsync();
        f.TerminalViewModel.Dispose();
        f.SetField("_upgradeTerminalViewModel", vm);
        return faults;
    }

    // Only the Upgrade view model uses this adapter. Commits, combat and the learner remain real fixture services.
    public class RefreshFaultStore : DispatchProxy
    {
        private IGameplayStore _inner = null!;
        public Func<Task<CyberDefenseProgressionRecord>>? Read { get; set; }
        public static RefreshFaultStore Wrap(IGameplayStore inner)
        {
            var proxy = (RefreshFaultStore)(object)Create<IGameplayStore, RefreshFaultStore>();
            proxy._inner = inner;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IGameplayStore.GetProgressionAsync) && Read is { } read) return read();
            try { return targetMethod.Invoke(_inner, args); }
            catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
        }
    }

    // =========================================================================
    // THE FORMERLY HANGING LEVEL 2 / FIRST SKILL POINT SUBMISSION
    // =========================================================================

    [Fact(Timeout = Bound)]
    public async Task FirstSkillPoint_SubmissionCompletes_AndOpensTheSpotlightOverTheHeldCommittedAnswer()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        f.Clock.Advance(1700);

        await f.SubmitAsync(correct: true).WaitAsync(Step);

        Assert.False(f.Field<bool>("_isSubmitting"));
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(SessionInteractionState.CorrectFeedback, f.Session.InteractionState);
        Assert.True(f.Session.IsCurrentSubmissionCommitted);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(1700, f.Session.LastEvaluation!.LatencyMs);

        var progression = await f.Store.GetProgressionAsync();
        Assert.Equal(2, progression.PlayerLevel);
        Assert.Equal(1, progression.EarnedSkillPoints);
        Assert.Equal(1, progression.AvailableSkillPoints);
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);

        var html = await f.RenderedHtmlAsync();
        Assert.Contains("data-spotlight-active=\"upgrade-header\"", html);
        var header = MenuFixture.Header(html);
        Assert.DoesNotMatch(@"<header class=""mathfirst-header""[^>]*\binert\b", header);
        var upgrade = Regex.Match(header, @"<button[^>]*class=""button button-secondary upgrade-nav-btn""[^>]*>").Value;
        Assert.Contains("data-spotlight-id=\"upgrade-header\"", upgrade);
        Assert.DoesNotContain("disabled", upgrade);
        Assert.DoesNotContain("pause-practice-btn", header);
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*\binert\b", html);

        // Exactly one modal surface: the spotlight. No held-answer dialog, recap or menu is stacked under it.
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Matches(@"<section class=""upgrade-spotlight-overlay""[^>]*role=""dialog""[^>]*aria-modal=""true""[^>]*aria-labelledby=""upgrade-spotlight-title""", html);
        Assert.DoesNotContain("feedback-held", html);
        Assert.Matches(@"upgrade-spotlight-skip-btn""[^>]*>\s*Later\s*</button>", html);
        Assert.Matches(@"upgrade-spotlight-open-btn""[^>]*>\s*Open Upgrades\s*</button>", html);
    }

    [Fact(Timeout = Bound)]
    public async Task PendingSpotlight_KeepsMathInputInert_AndTimingPaused_AndCommitsNothing()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);
        var latency = f.Session.LastEvaluation!.LatencyMs;
        var committed = await f.Store.GetProgressionAsync();

        f.Clock.Advance(60000);
        await f.InvokeAsync("HandleKeypadKeyAsync", "7");
        await f.InvokeAsync("HandleAnswerInput", new ChangeEventArgs { Value = "5" });
        f.SetField("_answerInput", "5");
        await f.InvokeAsync("Submit");
        await f.PressKeyAsync("Enter");
        await f.InvokeAsync("ResumeCommittedAnswerAsync");
        await f.InvokeAsync("AdvanceAsync");
        await f.InvokeAsync("OpenGameMenu");
        await f.InvokeAsync("StartOrResumePractice");

        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(SessionInteractionState.CorrectFeedback, f.Session.InteractionState);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(latency, f.Session.LastEvaluation!.LatencyMs);
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.Equal(committed, await f.Store.GetProgressionAsync());

        // Later releases the gate: one advance, a fresh question whose timing starts at zero.
        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, f.Session.InteractionState);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(string.Empty, f.Field<string>("_answerInput"));
        Assert.True(f.Field<bool>("_shouldFocusInput"));
        f.Clock.Advance(250);
        Assert.Equal(250, f.Session.GetCurrentActiveElapsedMs());
        Assert.Equal(committed, await f.Store.GetProgressionAsync());
        Assert.Single(f.Learner.Attempts);
    }

    [Fact(Timeout = Bound)]
    public async Task RepeatedAcknowledgement_AdvancesExactlyOnce_WithoutAnotherCommitOrReward()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);
        var committed = await f.Store.GetProgressionAsync();

        await f.InvokeAsync("DismissUpgradeSpotlight");
        await f.InvokeAsync("DismissUpgradeSpotlight");
        await f.InvokeAsync("OpenUpgradeTerminalFromSpotlightAsync");
        await f.InvokeAsync("AcknowledgeOperationUnlock");

        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.Null(f.Field<object?>("_committedPresentationGate"));
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.Equal(committed, await f.Store.GetProgressionAsync());
    }

    // =========================================================================
    // THE REAL UPGRADE CONTROL AND THE GUIDED TERMINAL
    // =========================================================================

    [Theory(Timeout = Bound)]
    [InlineData("en", "Improve Attack now for 1 Skill Point(s), or save your Skill Points for later.", "Got it")]
    [InlineData("de", "Verbessere jetzt deinen Angriff für 1 Skill-Punkt(e) oder spare deine Skill-Punkte für später.", "Verstanden")]
    [InlineData("ru", "Улучшите атаку сейчас за 1 очк. навыков или сохраните очки навыков на потом.", "Понятно")]
    public async Task SpotlightedHeaderControl_OpensTheRealGuidedTerminal_WhichOwnsTheGateUntilClose(string language, string advice, string gotIt)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language, CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);

        // The header button's own handler: the spotlight is acknowledged by opening the real terminal.
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True(f.IsUpgradeTerminalOpen);
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.True(f.Field<bool>("_isUpgradeOnboardingGuided"));
        Assert.NotNull(f.Field<object?>("_committedPresentationGate"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);

        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "upgrade-terminal-dialog\""));
        Assert.DoesNotContain("upgrade-spotlight-overlay", html);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Matches(@"<section class=""upgrade-guided-advice-card"" role=""region"" aria-label=""[^""]+"">", html);
        Assert.Contains($"<p class=\"guided-advice-text\">{advice}</p>", html);
        Assert.Contains(gotIt, html);

        f.Clock.Advance(5000);
        await f.InvokeAsync("CloseUpgradeTerminal");
        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.Null(f.Field<object?>("_committedPresentationGate"));
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.True(f.Session.IsTimingActive);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());
        Assert.DoesNotContain("upgrade-guided-advice-card", await f.RenderedHtmlAsync());

        // Nothing was bought or manufactured on the user's behalf.
        var progression = await f.Store.GetProgressionAsync();
        Assert.Equal(1, progression.AvailableSkillPoints);
        Assert.Equal(0, progression.SpentSkillPoints);
    }

    [Fact(Timeout = Bound)]
    public async Task SpotlightOpenAction_PurchasesOnlyThroughTheTerminal_AndTheNextQuestionOwnsTheNewRank()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);

        await f.InvokeAsync("OpenUpgradeTerminalFromSpotlightAsync");
        Assert.True(f.IsUpgradeTerminalOpen);
        Assert.True((await f.TerminalViewModel.PurchaseUpgradeAsync()).IsSuccess);
        Assert.False((await f.TerminalViewModel.PurchaseUpgradeAsync()).IsSuccess);
        Assert.Contains("Attack improved!", await f.RenderedHtmlAsync());
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        await f.InvokeAsync("CloseUpgradeTerminal");
        var progression = await f.Store.GetProgressionAsync();
        Assert.Equal(1, progression.SpentSkillPoints);
        Assert.Equal(0, progression.AvailableSkillPoints);
        Assert.Equal(1, progression.AttackRank);
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.True(f.Session.IsTimingActive);
        Assert.Single(f.Learner.Attempts);
    }

    [Fact(Timeout = Bound)]
    public async Task AndroidBack_IsTheSpotlightsLater_AndIsConsumed()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);

        Assert.True(await f.PressBackAsync());
        await WaitUntilAsync(f, () => f.Session.FactInstanceRevision == revision + 1);

        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(SessionInteractionState.AwaitingAnswer, f.Session.InteractionState);
        Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
        Assert.True(f.Session.IsTimingActive);
        Assert.Single(f.Learner.Attempts);
    }

    // =========================================================================
    // NEVER FOR ALREADY PERSISTED OR UNEARNED STATE
    // =========================================================================

    [Theory(Timeout = Bound)]
    [InlineData(6, true)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    public async Task NoSpotlight_ForAnAlreadyDiscoveredUpgrade_WithoutANewOpportunity_OrInCalmMode(int level, bool cyberDefense)
    {
        var xp = level == 1 && !cyberDefense ? JustBelowLevel2 : CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level);
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: level, CumulativeXp: xp, CyberDefense: cyberDefense));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;

        await f.SubmitAsync(correct: true).WaitAsync(Step);

        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Null(f.Field<object?>("_committedPresentationGate"));
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.DoesNotContain("upgrade-spotlight-overlay", await f.RenderedHtmlAsync());
    }

    [Fact(Timeout = Bound)]
    public async Task Spotlight_IsShownOnce_NotOnRerender_ReentryOrLaterAnswers()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2));
        await f.StartAsync();
        await f.SubmitAsync(correct: true).WaitAsync(Step);
        await f.InvokeAsync("DismissUpgradeSpotlight");

        await f.RenderedHtmlAsync();
        await f.NavigateToSettingsAndBackAsync();
        await f.InvokeAsync("StartOrResumePractice");
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        for (var i = 0; i < 3; i++)
        {
            await f.SubmitAsync(correct: true).WaitAsync(Step);
            Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
            Assert.Null(f.Field<object?>("_committedPresentationGate"));
        }
        Assert.Equal(4, f.Learner.Attempts.Count);
    }

    // =========================================================================
    // OPERATION UNLOCK ANNOUNCEMENTS
    // =========================================================================

    [Theory(Timeout = Bound)]
    [InlineData("en", "Subtraction unlocked", "Continue")]
    [InlineData("de", "Subtraktion freigeschaltet", "Weiter")]
    [InlineData("ru", "Вычитание разблокировано", "Продолжить")]
    public async Task NewlyCommittedStageTransition_AnnouncesTheOperationOnce_AndOnlyItsContinueAdvances(string language, string title, string resume)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language, PlayerLevel: 6, AdditionMastered: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        Assert.Equal(ArithmeticOperation.Addition, f.Session.CurrentFact!.Operation);

        await f.SubmitAsync(correct: true).WaitAsync(Step);

        Assert.Equal(CurriculumStage.Stage2_Subtraction, f.Session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);

        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Matches(@"<section class=""practice-overlay operation-unlock-dialog"" role=""dialog"" aria-modal=""true"" aria-labelledby=""operation-unlock-title"" aria-describedby=""operation-unlock-desc""", html);
        Assert.Contains($"<h2 id=\"operation-unlock-title\" class=\"operation-unlock-title\">{title}</h2>", html);
        Assert.Matches($@"<button type=""button"" class=""button button-primary practice-overlay-action operation-unlock-continue-btn"" aria-label=""{Regex.Escape(title)}\. {Regex.Escape(resume)}""[^>]*>\s*{Regex.Escape(resume)}\s*</button>", html);
        Assert.Matches(@"<header class=""mathfirst-header""[^>]*\binert\b", html);
        Assert.Matches(@"<main class=""training-card[^""]*""[^>]*\binert\b", html);

        // Back, Enter on the page, the header Upgrade handler and the menu never dismiss or bypass it.
        Assert.True(await f.PressBackAsync());
        await f.PressKeyAsync("Enter");
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        await f.InvokeAsync("OpenGameMenu");
        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.True(f.Session.IsTimingActive);
        Assert.Single(f.Learner.Attempts);
        Assert.DoesNotContain("operation-unlock-dialog", await f.RenderedHtmlAsync());

        // The persisted stage is never announced again: not on later answers, rerender or re-entry.
        await f.SubmitAsync(correct: true).WaitAsync(Step);
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        await f.NavigateToSettingsAndBackAsync();
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.DoesNotContain("operation-unlock-dialog", await f.RenderedHtmlAsync());
    }

    [Theory(Timeout = Bound)]
    [InlineData(CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage4_Division)]
    public async Task AlreadyPersistedStage_IsNeverAnnounced(CurriculumStage stage)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 6, Stage: stage, Bands: [(ArithmeticOperation.Addition, 10)]));
        Assert.DoesNotContain("operation-unlock-dialog", await f.RenderedHtmlAsync());
        await f.StartAsync();
        for (var i = 0; i < 3; i++)
        {
            await f.SubmitAsync(correct: true).WaitAsync(Step);
            Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        }
        Assert.Equal(stage, f.Session.Progression.CurriculumStage);
    }

    [Fact(Timeout = Bound)]
    public async Task UnlockAndFirstSkillPoint_InOneCommit_PresentOneAtATime_UnlockFirst()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, AdditionMastered: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);

        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Single(Regex.Matches(await f.RenderedHtmlAsync(), "role=\"dialog\""));

        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.False(f.Session.IsTimingActive);
        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Contains("upgrade-spotlight-overlay", html);

        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.True(f.Session.IsTimingActive);
        Assert.Single(f.Learner.Attempts);
    }

    // =========================================================================
    // BACKGROUND, RE-ENTRY AND RECAP COORDINATION
    // =========================================================================

    [Fact(Timeout = Bound)]
    public async Task Background_KeepsTheAnnouncementOpen_AndItsContinueNeverResumesTimingByItself()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 6, AdditionMastered: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);

        await f.Dispatch(() => f.Session.SetAppForeground(false));
        await f.AwaitTerminalRefreshAsync();
        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));

        await f.Dispatch(() => f.Session.SetAppForeground(true));
        await f.AwaitTerminalRefreshAsync();
        Assert.Equal(ArithmeticOperation.Subtraction, f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(SessionInteractionState.CorrectFeedback, f.Session.InteractionState);

        await f.InvokeAsync("AcknowledgeOperationUnlock");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        f.Clock.Advance(40000);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        await f.InvokeAsync("StartOrResumePractice");
        Assert.True(f.Session.IsTimingActive);
        Assert.Single(f.Learner.Attempts);
    }

    [Fact(Timeout = Bound)]
    public async Task LeavingHomeWithAnOpenGate_NeverAcknowledgesIt_AndReentryAdvancesExactlyOnceWithoutRepeating()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 6, AdditionMastered: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;
        await f.SubmitAsync(correct: true).WaitAsync(Step);
        Assert.NotNull(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));

        await f.UnmountAsync();
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(SessionInteractionState.CorrectFeedback, f.Session.InteractionState);

        await f.MountAsync();
        Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.DoesNotContain("operation-unlock-dialog", await f.RenderedHtmlAsync());
    }

    [Fact(Timeout = Bound)]
    public async Task BossVictoryThatEarnsTheFirstSkillPoint_ShowsTheRecapFirst_ThenTheSpotlight_NeverStacked()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, PendingBossVictory: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;

        var submission = f.SubmitAsync(correct: true);
        var continues = await AcknowledgeCeremonyUntilAsync(f, () => submission.IsCompleted);
        await submission.WaitAsync(Step);

        Assert.True(continues > 0, "The boss victory must present its recap before any Issue #123 presentation.");
        Assert.False(f.Ceremony.IsBlocking);
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        var html = await f.RenderedHtmlAsync();
        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.DoesNotContain("combat-ceremony-overlay", html);

        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Single(f.Learner.Attempts);
    }

    [Fact(Timeout = Bound)]
    public async Task UnacknowledgedRecap_KeepsTheSpotlightForItsRecovery_AndPresentsItOnce()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(CumulativeXp: JustBelowLevel2, PendingBossVictory: true));
        await f.StartAsync();
        var revision = f.Session.FactInstanceRevision;

        var submission = f.SubmitAsync(correct: true);
        await WaitUntilAsync(f, () => f.Ceremony.IsBlocking);
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        await f.Dispatch(() => f.Session.SetAppForeground(false));
        await submission.WaitAsync(Step);

        // Backgrounding cancels the recap: the answer is held and the spotlight waits behind it, not shown, not lost.
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.NotNull(f.Field<object?>("_heldCommittedPresentations"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        var xp = (await f.Store.GetProgressionAsync()).CumulativeXp;

        await f.Dispatch(() => f.Session.SetAppForeground(true));
        var continues = await AcknowledgeCeremonyUntilAsync(f, () => f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.True(continues > 0);
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));
        Assert.Null(f.Field<object?>("_heldCommittedPresentations"));
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.Equal(revision + 1, f.Session.FactInstanceRevision);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, f.Session.PracticeGate);
        Assert.False(f.Session.IsTimingActive);
        Assert.Equal(xp, (await f.Store.GetProgressionAsync()).CumulativeXp);
        Assert.Single(f.Learner.Attempts);
        Assert.Equal(1, f.Learner.CommitCalls);
    }

    // =========================================================================
    // LOCALIZATION AND STYLE CONTRACTS
    // =========================================================================

    [Theory]
    [InlineData("en", "Subtraction unlocked", "Multiplication unlocked", "Division unlocked", "Upgrades Unlocked", "Open Upgrades", "Later")]
    [InlineData("de", "Subtraktion freigeschaltet", "Multiplikation freigeschaltet", "Division freigeschaltet", "Upgrades freigeschaltet", "Upgrades öffnen", "Später")]
    [InlineData("ru", "Вычитание разблокировано", "Умножение разблокировано", "Деление разблокировано", "Улучшения разблокированы", "Открыть улучшения", "Позже")]
    public void Localization_PresentationCopyIsExactInEnglishGermanAndRussian(
        string language, string subtraction, string multiplication, string division, string badge, string open, string later)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        Assert.Equal(subtraction, localizer["OperationUnlock_Title_Subtraction"]);
        Assert.Equal(multiplication, localizer["OperationUnlock_Title_Multiplication"]);
        Assert.Equal(division, localizer["OperationUnlock_Title_Division"]);
        Assert.Equal(badge, localizer["UpgradeSpotlight_Badge"]);
        Assert.Equal(open, localizer["UpgradeSpotlight_OpenTerminal"]);
        Assert.Equal(later, localizer["UpgradeSpotlight_Skip"]);
        foreach (var key in new[]
        {
            "OperationUnlock_Badge", "OperationUnlock_Desc_Subtraction", "OperationUnlock_Desc_Multiplication", "OperationUnlock_Desc_Division",
            "UpgradeSpotlight_Title", "UpgradeSpotlight_Description", "UpgradeTerminal_GuidedAdviceAriaLabel",
            "UpgradeTerminal_GuidedAdviceAttackPurchased", "UpgradeTerminal_GuidedAdviceSaveSp", "UpgradeTerminal_GuidedAdviceGotIt"
        })
        {
            Assert.False(string.IsNullOrWhiteSpace(localizer[key]));
            Assert.NotEqual(key, localizer[key]);
        }
    }

    [Fact]
    public void Styles_PresentationActionsAreTouchSized_AndReducedMotionSafe()
    {
        var spotlight = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "UpgradeSpotlight.razor.css");
        var unlock = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "OperationUnlockAnnouncement.razor.css");
        Assert.Matches(@"\.upgrade-spotlight-btn\s*\{[^}]*min-height: 44px;", spotlight);
        Assert.Matches(@"\.operation-unlock-continue-btn\s*\{[^}]*min-height: 48px;", unlock);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*\.upgrade-spotlight-card", spotlight);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)\s*\{[^}]*\.operation-unlock-content", unlock);
        var app = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "wwwroot", "app.css");
        Assert.Matches(@"\[data-spotlight-active=""upgrade-header""\] \[data-spotlight-id=""upgrade-header""\]\s*\{[^}]*pointer-events: auto;", app);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static async Task WaitUntilAsync(MenuFixture f, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Step;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached within the bound.");
            await f.RendererDispatcherFlushAsync();
            await Task.Delay(10);
        }
    }

    // Acknowledges each revealed recap stage through Home's own Continue handler until the condition holds.
    private static async Task<int> AcknowledgeCeremonyUntilAsync(MenuFixture f, Func<bool> done)
    {
        var continues = 0;
        var deadline = DateTime.UtcNow + Step;
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, "Recap/presentation sequence did not complete within the bound.");
            if (f.Ceremony.IsBlocking && f.Ceremony.CanContinue)
            {
                Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
                Assert.Null(f.Field<ArithmeticOperation?>("_activeOperationUnlock"));
                await f.InvokeAsync("HandleCeremonyContinue", f.Ceremony.PresentationId);
                continues++;
            }
            await Task.Delay(10);
        }
        return continues;
    }
}
