namespace MathFirst.Core.Tests.CyberDefense;

using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudRender = Issue134HudPresentationTests.HudRender;

/// <summary>
/// Issue #138: a defeated sector boss is presented as ONE persistent result card. "Boss defeated!", a short decorative
/// victory flourish, "Sector X complete", the authoritative recap metrics and an enabled Continue are all part of the
/// card from its first presentation. No standalone victory stage, no recap without Continue, no delayed reveal and no
/// timer that dismisses or replaces the card. System Restart keeps its own reveal timing. Presentation only.
/// Also: the player HP row carries a visible "HP" label between the heart and the HP meter.
/// </summary>
public sealed class Issue138BossResultCardTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CyberDefenseReceiptRecord Receipt(bool victory = true, long epoch = 0, string id = "completion")
    {
        var run = victory
            ? CyberDefenseRunState.CreateActive(1, 5, 100, new OpponentState(OpponentKind.Boss, 12, 1))
            : CyberDefenseRunState.CreateActive(1, 0, 1, new OpponentState(OpponentKind.Normal, 2, 2));
        return CyberDefenseReceiptRecord.CreateApplied(id, "1+1", victory, true, 1234, epoch, DateTimeOffset.UnixEpoch,
            CyberDefenseStateMachine.ApplyAttempt(run, victory, 1));
    }

    private static CyberDefenseSectorRecap Recap(string id = "completion", long epoch = 0) => new(id, epoch, 1, 24, 22, 24000, 178, 1);

    private static Task<bool> Current(CancellationToken _) => Task.FromResult(true);

    // =========================================================================
    // PRESENTATION SEQUENCE
    // =========================================================================

    [Fact]
    public async Task BossCompletion_FirstPresentedStage_IsTheCombinedRecapWithContinueAlreadyAvailable()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var observed = new List<(CyberDefensePresentationStage Stage, bool CanContinue, bool HasRecap)>();
        sequence.Changed += () => observed.Add((sequence.Stage, sequence.CanContinue, sequence.Recap is not null));

        var pending = sequence.RunAsync(Receipt(), Recap(), Current);

        // The very first change already shows the whole card: no standalone victory, no recap without Continue.
        var first = Assert.Single(observed);
        Assert.Equal((CyberDefensePresentationStage.Recap, true, true), first);
        Assert.Equal(1, sequence.CompletedSector);
        Assert.True(sequence.IsBlocking);
        Assert.False(pending.IsCompleted);

        // The only timer is the validity re-observation; nothing is waited for before Continue is available.
        var tick = await clock.NextAsync();
        Assert.Equal(CyberDefensePresentationSequence.RevalidationInterval, tick.Duration);

        Assert.True(sequence.Acknowledge(sequence.PresentationId));
        Assert.True(await pending.WaitAsync(Wait));
        Assert.Equal([CyberDefensePresentationStage.Recap, CyberDefensePresentationStage.None],
            observed.Select(o => o.Stage).Distinct().ToArray());
    }

    [Fact]
    public async Task ImmediateContinue_OnTheFirstPresentation_IsAcceptedExactlyOnce()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var receipt = Receipt();
        var pending = sequence.RunAsync(receipt, Recap(), Current);
        var id = sequence.PresentationId;

        var accepted = 0;
        Parallel.For(0, 64, _ => { if (sequence.Acknowledge(id)) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
        Assert.True(await pending.WaitAsync(Wait));
        Assert.True(sequence.HasAcknowledged(receipt));
        Assert.False(sequence.Acknowledge(id));
        Assert.False(sequence.IsBlocking);

        // An acknowledged receipt is never presented or acknowledged again.
        Assert.False(await sequence.RunAsync(receipt, Recap(), Current));
        Assert.False(sequence.IsBlocking);
    }

    [Fact]
    public async Task BossResultCard_IsNeverDismissedOrReplacedByTime()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var stages = new List<CyberDefensePresentationStage>();
        sequence.Changed += () => stages.Add(sequence.Stage);
        var pending = sequence.RunAsync(Receipt(), Recap(), Current);
        var id = sequence.PresentationId;

        for (var simulated = TimeSpan.Zero; simulated < TimeSpan.FromSeconds(60);)
        {
            var tick = await clock.NextAsync();
            Assert.Equal(CyberDefensePresentationSequence.RevalidationInterval, tick.Duration);
            tick.Elapse();
            simulated += tick.Duration;
            Assert.Equal(CyberDefensePresentationStage.Recap, sequence.Stage);
            Assert.True(sequence.CanContinue);
        }
        await clock.NextAsync();
        Assert.False(pending.IsCompleted);
        Assert.Equal(id, sequence.PresentationId);
        Assert.NotNull(sequence.Recap);
        Assert.All(stages, stage => Assert.Equal(CyberDefensePresentationStage.Recap, stage));

        Assert.True(sequence.Acknowledge(id));
        Assert.True(await pending.WaitAsync(Wait));
    }

    [Fact]
    public async Task RecoveredBossResult_PresentsTheSameCombinedCardUnderANewIdentity()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var receipt = Receipt();
        var first = sequence.RunAsync(receipt, Recap(), Current);
        var staleId = sequence.PresentationId;
        sequence.Cancel();
        Assert.False(await first.WaitAsync(Wait));
        Assert.False(sequence.HasAcknowledged(receipt));

        var observed = new List<(CyberDefensePresentationStage Stage, bool CanContinue)>();
        sequence.Changed += () => observed.Add((sequence.Stage, sequence.CanContinue));
        var replay = sequence.RunAsync(receipt, Recap(), Current);
        Assert.Equal((CyberDefensePresentationStage.Recap, true), Assert.Single(observed));
        Assert.NotEqual(staleId, sequence.PresentationId);
        Assert.False(sequence.Acknowledge(staleId));
        Assert.True(sequence.Acknowledge(sequence.PresentationId));
        Assert.True(await replay.WaitAsync(Wait));
    }

    [Fact]
    public async Task SystemRestart_KeepsItsSeparateRevealDelay()
    {
        var clock = new FakeClock();
        var sequence = new CyberDefensePresentationSequence(clock.Delay);
        var pending = sequence.RunAsync(Receipt(victory: false), null, Current);
        Assert.Equal(CyberDefensePresentationStage.SystemOverload, sequence.Stage);
        Assert.False(sequence.CanContinue);
        Assert.False(sequence.Acknowledge(sequence.PresentationId));
        var reveal = await clock.NextAsync();
        Assert.Equal(CyberDefensePresentationSequence.ContinueRevealDelay, reveal.Duration);
        Assert.Equal(TimeSpan.FromSeconds(3), reveal.Duration);
        reveal.Elapse();
        while (!sequence.CanContinue) await Task.Delay(5);
        Assert.True(sequence.Acknowledge(sequence.PresentationId));
        Assert.True(await pending.WaitAsync(Wait));
    }

    // =========================================================================
    // RENDERED CARD
    // =========================================================================

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task BossResultCard_ShowsVictoryHeadingFlourishResultHeadingMetricsAndEnabledContinueTogether(string language)
    {
        var localizer = Localizer(language);
        var recap = Recap();
        var html = await RenderCeremonyAsync(localizer, CyberDefensePresentationStage.Recap, recap, canContinue: true);

        Assert.Single(Regex.Matches(html, "role=\"dialog\""));
        Assert.Single(Regex.Matches(html, "class=\"combat-ceremony-card[^\"]*\""));
        var victory = localizer["CyberDefense_Victory"];
        var result = localizer["CyberDefense_RecapTitle", 1];
        Assert.Single(Regex.Matches(html, Regex.Escape(victory)));
        Assert.Single(Regex.Matches(html, Regex.Escape(result)));
        Assert.True(html.IndexOf(victory, StringComparison.Ordinal) < html.IndexOf(result, StringComparison.Ordinal));

        // A decorative, non-interactive flourish on the same card.
        Assert.Matches("class=\"combat-ceremony-card ceremony-victory\"", html);
        Assert.Matches("<div class=\"ceremony-victory-flourish\" aria-hidden=\"true\"></div>", html);

        // The dialog is named by both headings; the victory heading receives the initial focus.
        var labelledBy = Regex.Match(html, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value.Split(' ');
        Assert.Equal(["combat-ceremony-title", "combat-ceremony-result"], labelledBy);
        Assert.Matches($"<h2 id=\"combat-ceremony-title\"[^>]*tabindex=\"-1\"[^>]*>{Regex.Escape(victory)}</h2>", html);
        Assert.Matches($"<h3 id=\"combat-ceremony-result\"[^>]*>{Regex.Escape(result)}</h3>", html);

        foreach (var (key, argument) in new (string, object)[]
        {
            ("CyberDefense_RecapAccuracy", recap.AccuracyPercentage), ("CyberDefense_RecapMistakes", 2),
            ("CyberDefense_RecapActiveAnswering", 24m), ("CyberDefense_RecapXp", 178L), ("CyberDefense_RecapLevels", 1)
        })
        {
            Assert.Contains(localizer[key, argument], html);
        }

        var button = Assert.Single(Regex.Matches(html, "<button[^>]*>[\\s\\S]*?</button>")).Value;
        Assert.Contains("ceremony-continue", button);
        Assert.Contains(localizer["Common_Continue"], button);
        Assert.DoesNotContain("disabled", button);
        Assert.DoesNotContain("aria-disabled", button);
    }

    [Fact]
    public async Task UnavailableRecap_StillShowsTheVictoryHeadingAndContinueOnTheSameCard()
    {
        var localizer = Localizer("de");
        var html = await RenderCeremonyAsync(localizer, CyberDefensePresentationStage.Recap, null, canContinue: true);
        Assert.Contains("Boss besiegt!", html);
        Assert.Contains("Sektor 1 abgeschlossen", html);
        Assert.Contains(localizer["CyberDefense_RecapUnavailable"], html);
        Assert.DoesNotContain("recap-metrics", html);
        Assert.Single(Regex.Matches(html, "ceremony-continue"));
    }

    [Fact]
    public async Task SystemRestart_HasNoVictoryHeadingOrFlourish()
    {
        var localizer = Localizer("en");
        var html = await RenderCeremonyAsync(localizer, CyberDefensePresentationStage.SystemOverload, null, canContinue: true);
        Assert.DoesNotContain(localizer["CyberDefense_Victory"], html);
        Assert.DoesNotContain("ceremony-victory", html);
        Assert.Contains("aria-labelledby=\"combat-ceremony-title\"", html);
        Assert.Matches($"<h2 id=\"combat-ceremony-title\"[^>]*>{Regex.Escape(localizer["CyberDefense_SystemOverload"])}</h2>", html);
    }

    [Fact]
    public void VictoryAnimation_IsDecorativeShortAndDisabledForReducedMotion()
    {
        var css = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "Components", "Training", "CyberDefenseCeremony.razor.css");
        var reduced = Regex.Match(css, @"@media \(prefers-reduced-motion: reduce\) \{(?<body>[\s\S]*?)\n\}").Groups["body"].Value;
        Assert.Matches(@"\.ceremony-victory\s*\{[^}]*animation:\s*none", reduced);
        Assert.Matches(@"\.ceremony-victory-flourish\s*\{[^}]*display:\s*none", reduced);
        Assert.Matches(@"\.ceremony-victory-flourish\s*\{[^}]*pointer-events:\s*none", css);

        // Never moves, scales or hides the card's content: no transform or opacity on the card's own animation.
        var cardKeyframes = Regex.Match(css, @"@keyframes ceremony-victory \{(?<body>[\s\S]*?)\n\}").Groups["body"].Value;
        Assert.NotEmpty(cardKeyframes);
        Assert.DoesNotContain("transform", cardKeyframes);
        Assert.DoesNotContain("opacity", cardKeyframes);

        // Brief, finite and not repeated: no infinite loop that could flash.
        foreach (Match animation in Regex.Matches(css, @"animation:\s*(?<value>[^;]+);"))
        {
            Assert.DoesNotContain("infinite", animation.Groups["value"].Value);
        }
    }

    // =========================================================================
    // HP LABEL
    // =========================================================================

    [Theory]
    [InlineData("en", "HP: 73 / 100")]
    [InlineData("de", "KP: 73 / 100")]
    [InlineData("ru", "ОЗ: 73 / 100")]
    public async Task PlayerHpRow_ShowsHeartThenVisibleHpLabelThenMeterThenValues(string language, string accessibleName)
    {
        await using var hud = await HudRender.CreateAsync(language);
        await hud.SetAsync(CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, 0, 73,
            new OpponentState(OpponentKind.Normal, 2, 2), 1, 0, 12)));

        var html = await hud.HtmlAsync();
        var hpRow = Issue134HudPresentationTests.Block(html, "player-hp-cluster");
        Assert.Matches(
            @"^<div class=""player-hp-cluster"">\s*<svg class=""player-status-icon player-hp-icon""[^>]*aria-hidden=""true""[\s\S]*?</svg>\s*" +
            @"<span class=""player-hp-label"" aria-hidden=""true"">HP</span>\s*<div class=""meter-bar player-hp-bar""", hpRow);
        Assert.Contains("<span class=\"player-hp-value\">73 / 100</span>", hpRow);
        Assert.True(hpRow.IndexOf("player-hp-bar", StringComparison.Ordinal) < hpRow.IndexOf("player-hp-value", StringComparison.Ordinal));

        // The localized accessible names and the numeric values are unchanged.
        Assert.Matches($@"class=""meter-bar player-hp-bar""[^>]*aria-label=""{Regex.Escape(accessibleName)}""[^>]*aria-valuenow=""73""[^>]*aria-valuemax=""100""", hpRow);
        Assert.Matches($"<section class=\"player-shield-panel\"[^>]*aria-label=\"{Regex.Escape(accessibleName)}\"", html);
        Assert.Single(Regex.Matches(html, ">73 / 100<"));
        Assert.Single(Regex.Matches(html, "player-hp-label"));
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static LocalizationService Localizer(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        return localizer;
    }

    private static async Task<string> RenderCeremonyAsync(LocalizationService localizer, CyberDefensePresentationStage stage,
        CyberDefenseSectorRecap? recap, bool canContinue)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseCeremony>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stage"] = stage, ["Recap"] = recap, ["CompletedSector"] = 1,
                ["CanContinue"] = canContinue, ["PresentationId"] = 7L
            }))).ToHtmlString()));
    }

    private sealed record PendingDelay(TimeSpan Duration, TaskCompletionSource Gate)
    {
        public void Elapse() => Gate.TrySetResult();
    }

    private sealed class FakeClock
    {
        private readonly Channel<PendingDelay> _requests = Channel.CreateUnbounded<PendingDelay>();

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Writer.TryWrite(new PendingDelay(duration, gate));
            ct.Register(() => gate.TrySetCanceled(ct));
            return gate.Task.WaitAsync(ct);
        }

        /// <summary>Next delay that is still outstanding; requests cancelled by the sequence are skipped.</summary>
        public async Task<PendingDelay> NextAsync()
        {
            while (true)
            {
                var next = await _requests.Reader.ReadAsync().AsTask().WaitAsync(Wait);
                if (!next.Gate.Task.IsCompleted) return next;
            }
        }
    }
}
