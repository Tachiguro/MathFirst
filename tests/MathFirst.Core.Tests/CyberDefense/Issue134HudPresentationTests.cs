namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.CyberDefense;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MenuFixture = Issue125GameMenuTests.MenuFixture;

/// <summary>
/// Issue #134: combat HUD readability and the earned Critical Strike radar. Component tests render the real
/// <c>CyberDefenseHud</c>; Home tests use the real page with a step clock, an in-memory learner and a temporary
/// SQLite gameplay store. Presentation only: no damage, HP, timing, rank or purchase rule is exercised differently.
/// </summary>
public sealed class Issue134HudPresentationTests : IDisposable
{
    private readonly Issue125GameMenuTests _suite = new();

    public void Dispose() => _suite.Dispose();

    // =========================================================================
    // OPPONENT DISPLAY
    // =========================================================================

    [Theory]
    [InlineData("en", OpponentKind.Normal, 2, 2, "HP: 2 / 2")]
    [InlineData("de", OpponentKind.Normal, 2, 1, "KP: 1 / 2")]
    [InlineData("ru", OpponentKind.Normal, 2, 1, "ОЗ: 1 / 2")]
    [InlineData("en", OpponentKind.Normal, 120, 37, "HP: 37 / 120")]
    [InlineData("de", OpponentKind.Boss, 12, 12, "KP: 12 / 12")]
    [InlineData("ru", OpponentKind.Boss, 12, 7, "ОЗ: 7 / 12")]
    [InlineData("en", OpponentKind.Boss, 999, 1, "HP: 1 / 999")]
    public async Task OpponentHp_IsShownAsNumericCurrentOverMaximum_WithAnAccurateBarAndAccessibleValues(
        string language, OpponentKind kind, int maxHp, int currentHp, string expected)
    {
        await using var hud = await HudRender.CreateAsync(language);
        var model = Model(kind, maxHp, currentHp);
        await hud.SetAsync(model);

        var html = await hud.HtmlAsync();
        var value = Regex.Match(html, @"<span class=""enemy-hp-value[^""]*"">([^<]*)</span>");
        Assert.True(value.Success, "The opponent HP value must be rendered.");
        Assert.Equal(expected, value.Groups[1].Value);
        Assert.DoesNotContain("% HP", html);

        var meter = Regex.Match(html, "<div class=\"meter-bar enemy-hp-bar[^>]*>").Value;
        Assert.Contains($"aria-valuenow=\"{currentHp}\"", meter);
        Assert.Contains("aria-valuemin=\"0\"", meter);
        Assert.Contains($"aria-valuemax=\"{maxHp}\"", meter);
        Assert.Contains($"aria-valuetext=\"{expected}\"", meter);
        var expectedPercent = (int)Math.Round((double)currentHp / maxHp * 100);
        Assert.Equal(expectedPercent, model.OpponentHpPercentage);
        Assert.Matches($@"class=""meter-fill hp-fill {(kind == OpponentKind.Boss ? "boss-hp-fill" : string.Empty)}"" style=""width: {expectedPercent}%""", html);
    }

    [Theory]
    [InlineData("en", OpponentKind.Normal)]
    [InlineData("de", OpponentKind.Normal)]
    [InlineData("ru", OpponentKind.Normal)]
    [InlineData("en", OpponentKind.Boss)]
    [InlineData("de", OpponentKind.Boss)]
    [InlineData("ru", OpponentKind.Boss)]
    public async Task OpponentName_IsGroupedWithSectorAndWaveAtTheTop_AndTheDescriptionAndHpStayInTheStage(string language, OpponentKind kind)
    {
        await using var hud = await HudRender.CreateAsync(language);
        var model = Model(kind, kind == OpponentKind.Boss ? 12 : 2, 1);
        await hud.SetAsync(model);

        var html = await hud.HtmlAsync();
        var name = hud.Localizer[model.OpponentNameKey];
        var description = hud.Localizer[model.OpponentDescKey];
        var topBar = Block(html, "scene-top-bar");
        var stage = Block(html, "scene-stage");

        Assert.Contains("scene-sector-tag", topBar);
        Assert.Contains("scene-wave-tag", topBar);
        Assert.Matches($@"<h2 class=""scene-enemy-name[^""]*"">{Regex.Escape(name)}</h2>", topBar);
        Assert.DoesNotContain("scene-enemy-name", stage);
        Assert.Contains($">{description}</p>", stage);
        Assert.Contains("enemy-hp-value", stage);
        // The stage ends with the numeric HP row; the description is above it.
        Assert.True(stage.IndexOf("scene-enemy-desc", StringComparison.Ordinal) < stage.IndexOf("enemy-hp-cluster", StringComparison.Ordinal));

        // Bosses keep their distinct label in the top grouping, ordinary opponents their wave counter.
        if (kind == OpponentKind.Boss)
        {
            Assert.Contains("sector-boss-badge", topBar);
            Assert.Contains(hud.Localizer["CyberDefense_SectorBossLabel"], topBar);
            Assert.Contains("scene-boss", html);
        }
        else
        {
            Assert.DoesNotContain("sector-boss-badge", html);
            Assert.Contains(hud.Localizer["CyberDefense_WaveDisplay", model.WaveNumber, model.TotalOpponentCount], topBar);
        }

        // One heading names the opponent; the image keeps the same accessible name and the region label.
        Assert.Single(Regex.Matches(html, "<h2 "));
        Assert.Contains($"alt=\"{name}\"", html);
        Assert.Contains($"aria-label=\"{name}\"", html);
    }

    // =========================================================================
    // PLAYER DISPLAY
    // =========================================================================

    [Theory]
    [InlineData("en", "HP: 73 / 100", 0, 0)]
    [InlineData("de", "KP: 73 / 100", 0, 0)]
    [InlineData("ru", "ОЗ: 73 / 100", 0, 0)]
    [InlineData("en", "HP: 73 / 100", 2, 1)]
    public async Task PlayerHp_IsVisibleExactlyOnce_AndKeepsItsAccessibleStatusAndShields(string language, string expected, int capacity, int active)
    {
        await using var hud = await HudRender.CreateAsync(language);
        await hud.SetAsync(CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, 0, 73,
            new OpponentState(OpponentKind.Normal, 2, 2), capacity, active, 0)));

        var html = await hud.HtmlAsync();
        var panel = Block(html, "player-shield-panel", "section");
        // Issue #136: the HP row shows a heart and the real values once; the localized text is the accessible name.
        Assert.Single(Regex.Matches(panel, ">73 / 100<"));
        Assert.Contains("<span class=\"player-hp-value\">73 / 100</span>", panel);
        Assert.Matches($"<section class=\"player-shield-panel\"[^>]*aria-label=\"{Regex.Escape(expected)}\"", html);
        Assert.Matches($"class=\"meter-bar player-hp-bar\"[^>]*aria-label=\"{Regex.Escape(expected)}\"[^>]*aria-valuenow=\"73\"[^>]*aria-valuemax=\"100\"", html);
        Assert.Equal(capacity > 0, panel.Contains("shield-badges-row", StringComparison.Ordinal));
        Assert.Equal(capacity > 0 ? 1 : 0, Regex.Matches(panel, "class=\"shield-badge ").Count);
        // Level/XP stays untouched (Issue #122).
        Assert.Contains("player-xp-cluster", panel);
    }

    [Theory]
    [InlineData("en", 2, 1, 12, "Shield recharge: 12 / 30")]
    [InlineData("de", 3, 0, 29, "Schildaufladung: 29 / 30")]
    [InlineData("ru", 1, 0, 0, "Перезарядка щита: 0 / 30")]
    public async Task ShieldRecharge_IsShownFromAuthoritativeRunStateOnlyWhileAShieldIsDown(string language, int capacity, int active, int units, string expected)
    {
        await using var hud = await HudRender.CreateAsync(language);
        var run = CyberDefenseRunState.CreateActive(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2), capacity, active, units);
        var model = CyberDefenseHudViewModel.FromRunState(run);
        Assert.Equal(units, model.ShieldRechargeUnits);
        await hud.SetAsync(model);

        var meter = Regex.Match(await hud.HtmlAsync(), "<div class=\"meter-bar shield-recharge-bar\"[^>]*>");
        Assert.True(meter.Success);
        Assert.Contains("role=\"progressbar\"", meter.Value);
        Assert.Contains($"aria-valuenow=\"{units}\"", meter.Value);
        Assert.Contains($"aria-valuemax=\"{CyberDefenseFirewallPolicy.RechargeThresholdUnits}\"", meter.Value);
        Assert.Contains($"aria-valuetext=\"{expected}\"", meter.Value);
        var percent = ((double)units / CyberDefenseFirewallPolicy.RechargeThresholdUnits * 100).ToString("0.##", CultureInfo.InvariantCulture);
        Assert.Contains($"class=\"meter-fill shield-recharge-fill\" style=\"width: {percent}%\"", await hud.HtmlAsync());

        // Full shields and no shield capacity show no recharge progress; the recharge rule itself is not exercised here.
        await hud.SetAsync(CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2), capacity, capacity, 0)));
        Assert.DoesNotContain("shield-recharge-bar", await hud.HtmlAsync());
        await hud.SetAsync(CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2), 0, 0, 0)));
        Assert.DoesNotContain("shield-recharge-bar", await hud.HtmlAsync());
    }

    // =========================================================================
    // CRITICAL STRIKE RADAR
    // =========================================================================

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task CriticalRadar_IsShownOnlyForAnAuthoritativePurchasedRank(int? rank, bool visible)
    {
        await using var hud = await HudRender.CreateAsync();
        await hud.SetAsync(Model(OpponentKind.Normal, 2, 2), rank: rank, elapsed: 0, windowMs: 3000);

        var html = await hud.HtmlAsync();
        Assert.Equal(visible, html.Contains("tactical-radar-widget", StringComparison.Ordinal));
        Assert.Equal(visible, html.Contains("class=\"radar-timing-arc", StringComparison.Ordinal));
        // Combat status never depends on the radar.
        Assert.Contains("player-hp-bar", html);
        Assert.Contains("enemy-hp-bar", html);
        Assert.Contains("tactical-telemetry-list", html);
    }

    [Fact]
    public async Task CriticalRadar_IsExhaustedAtTheEndOfTheUnchangedCriticalWindow()
    {
        await using var hud = await HudRender.CreateAsync();
        await hud.SetAsync(Model(OpponentKind.Normal, 2, 2), rank: 1, elapsed: 3000, windowMs: 3000);
        var html = await hud.HtmlAsync();
        Assert.Contains("tactical-radar-widget", html);
        Assert.DoesNotContain("class=\"radar-timing-arc", html);

        await hud.SetAsync(Model(OpponentKind.Normal, 2, 2), rank: 1, elapsed: 0, windowMs: 3000);
        Assert.Contains("--crit-window-duration: 3000ms", Arc(await hud.HtmlAsync()));
    }

    [Fact]
    public async Task CriticalRadar_RerendersWithinOneRunningSegment_KeepTheAnimationBindingUnchanged()
    {
        await using var hud = await HudRender.CreateAsync();
        var model = Model(OpponentKind.Normal, 2, 2);
        await hud.SetAsync(model, rank: 1, elapsed: 0, windowMs: 6000, taskKey: "add:7+8_100");
        var initial = Arc(await hud.HtmlAsync());
        Assert.Contains("--crit-window-duration: 6000ms", initial);
        Assert.Contains("--crit-window-delay: 0ms", initial);

        // Typing the first and second digit of a multi-digit answer rerenders the HUD with the advanced clock.
        foreach (var elapsed in new long[] { 1500, 2200, 2900 })
        {
            await hud.SetAsync(model, rank: 1, elapsed: elapsed, windowMs: 6000, taskKey: "add:7+8_100");
            Assert.Equal(initial, Arc(await hud.HtmlAsync()));
        }
    }

    [Fact]
    public async Task CriticalRadar_PauseFreezesAtTheAuthoritativeElapsedTime_AndResumeAndNewTasksRestartFromIt()
    {
        await using var hud = await HudRender.CreateAsync();
        var model = Model(OpponentKind.Normal, 2, 2);
        await hud.SetAsync(model, rank: 1, elapsed: 0, windowMs: 6000, taskKey: "t1");
        var running = Arc(await hud.HtmlAsync());
        Assert.DoesNotContain("paused", running);

        await hud.SetAsync(model, rank: 1, elapsed: 2500, windowMs: 6000, taskKey: "t1", timingActive: false);
        var paused = Arc(await hud.HtmlAsync());
        Assert.Contains("--crit-window-delay: -2500ms", paused);
        Assert.Contains("animation-play-state: paused", paused);
        Assert.NotEqual(Segment(running), Segment(paused));

        // Rerendering while paused (wall time passes, active time does not) changes nothing.
        await hud.SetAsync(model, rank: 1, elapsed: 2500, windowMs: 6000, taskKey: "t1", timingActive: false);
        Assert.Equal(paused, Arc(await hud.HtmlAsync()));

        await hud.SetAsync(model, rank: 1, elapsed: 2500, windowMs: 6000, taskKey: "t1");
        var resumed = Arc(await hud.HtmlAsync());
        Assert.Contains("--crit-window-delay: -2500ms", resumed);
        Assert.DoesNotContain("paused", resumed);
        Assert.NotEqual(Segment(paused), Segment(resumed));
        await hud.SetAsync(model, rank: 1, elapsed: 3100, windowMs: 6000, taskKey: "t1");
        Assert.Equal(resumed, Arc(await hud.HtmlAsync()));

        await hud.SetAsync(model, rank: 1, elapsed: 0, windowMs: 4000, taskKey: "t2");
        var next = Arc(await hud.HtmlAsync());
        Assert.Contains("--crit-window-delay: 0ms", next);
        Assert.Contains("--crit-window-duration: 4000ms", next);
        Assert.NotEqual(Segment(resumed), Segment(next));
    }

    [Fact]
    public void RadarTimingPolicy_AndCriticalStrikePolicy_AreUnchanged()
    {
        var state = CyberDefenseRadarTimingPolicy.CalculateTimingState(6000, 1500);
        Assert.Equal(6000, state.TotalDurationMs);
        Assert.Equal(-1500, state.NegativeDelayMs);
        Assert.Equal(4500, state.RemainingMs);
        Assert.Equal(6000, CyberDefenseRadarTimingPolicy.CalculateCriticalHitThresholdMs(3000, 15));
        Assert.Equal(0, CyberDefenseCriticalStrikePolicy.GetBonusPercent(0));
        Assert.False(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(0, 5));
        Assert.True(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(1, 5));
        Assert.Equal(12, CyberDefenseCriticalStrikePolicy.UnlockLevel);
    }

    // =========================================================================
    // HOME INTEGRATION
    // =========================================================================

    [Fact(Timeout = 60000)]
    public async Task Home_RankZero_ShowsNoRadar_EvenWhenAffordable_AndAPurchaseRevealsItWithoutChangingTheQuestion()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 12));
        await f.StartAsync();
        var html = await f.RenderedHtmlAsync();
        Assert.True(f.TerminalViewModel.CanPurchaseCriticalStrike);
        Assert.Equal(0, f.TerminalViewModel.CurrentCriticalStrikeRank);
        Assert.DoesNotContain("tactical-radar-widget", html);

        var revision = f.Session.FactInstanceRevision;
        var expression = Expression(html);
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True((await f.TerminalViewModel.PurchaseCriticalStrikeUpgradeAsync()).IsSuccess);
        await f.InvokeAsync("CloseUpgradeTerminal");

        html = await f.RenderedHtmlAsync();
        Assert.Contains("tactical-radar-widget", html);
        Assert.Contains("class=\"radar-timing-arc", html);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(expression, Expression(html));
        Assert.Equal(0, f.Learner.CommitCalls);
    }

    [Fact(Timeout = 60000)]
    public async Task Home_UnknownGameplayStatus_ShowsNoRadar()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 12));
        await f.StartAsync();
        Assert.Equal(0, f.Property("HudCriticalStrikeRank"));
        using var viewModel = f.TerminalViewModel;
        f.SetField("_upgradeTerminalViewModel", null!);
        Assert.Null(f.Property("HudCriticalStrikeRank"));
        Assert.DoesNotContain("tactical-radar-widget", await f.RenderedHtmlAsync());
    }

    [Fact(Timeout = 60000)]
    public async Task Home_DigitEntry_LeavesTheAuthoritativeClockTaskAndRadarBindingUnchanged()
    {
        // BandIndex 59 is the N = 60 ring: every fact contains 60, so the answer has at least two digits.
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 12, Bands: [(ArithmeticOperation.Addition, 59)]));
        Assert.True((await f.TerminalViewModel.PurchaseCriticalStrikeUpgradeAsync()).IsSuccess);
        await f.StartAsync();

        var answer = f.Session.CurrentFact!.CorrectResult.ToString(CultureInfo.InvariantCulture);
        Assert.True(answer.Length >= 2, $"Synthetic fixture must present a multi-digit answer, got {answer}.");
        var revision = f.Session.FactInstanceRevision;
        var ready = f.Session.ItemReadyTimestamp;
        var window = f.Session.CurrentFactCriticalHitThresholdMs;
        var initial = await f.RenderedHtmlAsync();
        var arc = Arc(initial);
        Assert.Contains($"--crit-window-duration: {window}ms", arc);
        Assert.Equal(0, f.Session.GetCurrentActiveElapsedMs());

        var samples = new List<long> { 0 };
        f.Clock.Advance(1300);
        await f.InvokeAsync("HandleKeypadKeyAsync", answer[..1]);
        samples.Add(f.Session.GetCurrentActiveElapsedMs());
        var afterFirst = await f.HtmlAsync();

        f.Clock.Advance(450);
        await f.Dispatch(() => { });
        var afterRerender = await f.RenderedHtmlAsync();
        samples.Add(f.Session.GetCurrentActiveElapsedMs());

        Assert.Equal([0, 1300, 1750], samples);
        Assert.Equal(answer[..1], f.Session.CurrentAnswerInput);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, f.Session.InteractionState);
        Assert.Equal(revision, f.Session.FactInstanceRevision);
        Assert.Equal(ready, f.Session.ItemReadyTimestamp);
        Assert.Equal(window, f.Session.CurrentFactCriticalHitThresholdMs);
        Assert.Equal(0, f.Learner.CommitCalls);
        Assert.Equal(arc, Arc(afterFirst));
        Assert.Equal(arc, Arc(afterRerender));
        Assert.Equal(Expression(initial), Expression(afterRerender));

        // Completing the answer still measures the full active time once.
        f.Clock.Advance(250);
        await f.InvokeAsync("HandleKeypadKeyAsync", answer[1..2]);
        if (answer.Length > 2) await f.InvokeAsync("HandleKeypadKeyAsync", answer[2..]);
        await f.AwaitTerminalRefreshAsync();
        Assert.Equal(1, f.Learner.CommitCalls);
        Assert.Equal(2000, f.Learner.Attempts[^1].ResponseLatencyMs);
    }

    [Fact(Timeout = 60000)]
    public async Task Home_UpgradeTerminalPause_FreezesTheRadarAtTheAuthoritativeElapsedTime_AndCloseResumesFromIt()
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 12));
        Assert.True((await f.TerminalViewModel.PurchaseCriticalStrikeUpgradeAsync()).IsSuccess);
        await f.StartAsync();
        var running = Arc(await f.RenderedHtmlAsync());

        f.Clock.Advance(900);
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.False(f.Session.IsTimingActive);
        var paused = Arc(await f.RenderedHtmlAsync());
        Assert.Contains("--crit-window-delay: -900ms", paused);
        Assert.Contains("animation-play-state: paused", paused);
        Assert.NotEqual(Segment(running), Segment(paused));

        f.Clock.Advance(5000);
        Assert.Equal(paused, Arc(await f.RenderedHtmlAsync()));
        Assert.Equal(900, f.Session.GetCurrentActiveElapsedMs());

        await f.InvokeAsync("CloseUpgradeTerminal");
        Assert.True(f.Session.IsTimingActive);
        var resumed = Arc(await f.RenderedHtmlAsync());
        Assert.Contains("--crit-window-delay: -900ms", resumed);
        Assert.DoesNotContain("paused", resumed);
        Assert.NotEqual(Segment(paused), Segment(resumed));
        Assert.Equal(900, f.Session.GetCurrentActiveElapsedMs());
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    // The production projection of a Sector 1 opponent; the HP pair is synthetic so tiny and large ranges render
    // through the same presentation-only view model (the HUD never validates combat rules).
    private static CyberDefenseHudViewModel Model(OpponentKind kind, int maxHp, int currentHp)
    {
        var index = kind == OpponentKind.Boss ? CyberDefenseScalingPolicy.GetBossIndex(1) : 0;
        var policyMax = CyberDefenseScalingPolicy.GetOpponentMaxHp(1, index);
        var t = CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.CreateActive(1, index, 100,
            new OpponentState(kind, policyMax, policyMax), 0, 0, 0));
        return new CyberDefenseHudViewModel(t.SectorNumber, t.OpponentIndex, t.NormalOpponentCount, t.TotalOpponentCount, t.OpponentKind,
            t.IsBoss, t.IsSectorBoss, currentHp, maxHp, (int)Math.Round((double)currentHp / maxHp * 100), t.PlayerCurrentHp, t.PlayerMaxHp,
            t.PlayerHpPercentage, t.OpponentNameKey, t.OpponentDescKey, t.OpponentAssetPath, t.OpponentScaleClass, CyberDefenseFeedbackKind.None,
            0, 0, 0, false, false, 0, null);
    }

    private static string Arc(string html)
    {
        var match = Regex.Match(html, @"<circle[^>]*class=""radar-timing-arc""[^>]*>");
        Assert.True(match.Success, "The radar timing arc must be rendered.");
        return match.Value;
    }

    private static string Segment(string arc) => Regex.Match(arc, @"data-radar-segment=""([^""]*)""").Groups[1].Value;

    private static string Expression(string html) => Regex.Match(html, @"<div class=""expression-row[\s\S]*?</span>\s*</span>").Value;

    // The element that opens with the given class, up to its own matching close tag.
    internal static string Block(string html, string cssClass, string tag = "div")
    {
        var open = Regex.Match(html, $@"<{tag} class=""{Regex.Escape(cssClass)}[ ""]");
        Assert.True(open.Success, $"Missing <{tag} class=\"{cssClass}\">.");
        var depth = 0;
        foreach (Match token in Regex.Matches(html[open.Index..], $@"<{tag}[\s>]|</{tag}>"))
        {
            depth += token.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0) return html.Substring(open.Index, token.Index + token.Length);
        }
        throw new InvalidOperationException($"Unclosed <{tag} class=\"{cssClass}\">.");
    }

    internal sealed class Issue134HudHost : ComponentBase
    {
        [Parameter] public Action<Issue134HudHost> Capture { get; set; } = null!;
        private Dictionary<string, object?> _parameters = new() { ["ViewModel"] = CyberDefenseHudViewModel.Initial() };

        protected override void OnInitialized() => Capture(this);

        public void Set(Dictionary<string, object?> parameters)
        {
            _parameters = parameters;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.CyberDefenseHud>(0);
            builder.AddAttribute(1, "ViewModel", _parameters.GetValueOrDefault("ViewModel"));
            if (_parameters.TryGetValue("CriticalWindowMs", out var window)) builder.AddAttribute(2, "CriticalWindowMs", window);
            if (_parameters.TryGetValue("ActiveElapsedMs", out var elapsed)) builder.AddAttribute(3, "ActiveElapsedMs", elapsed);
            if (_parameters.TryGetValue("TaskKey", out var task)) builder.AddAttribute(4, "TaskKey", task);
            if (_parameters.TryGetValue("IsTimingActive", out var active)) builder.AddAttribute(5, "IsTimingActive", active);
            if (_parameters.TryGetValue("CriticalStrikeRank", out var rank)) builder.AddAttribute(6, "CriticalStrikeRank", rank);
            if (_parameters.TryGetValue("PlayerProgress", out var progress)) builder.AddAttribute(7, "PlayerProgress", progress);
            builder.CloseComponent();
        }
    }

    internal sealed class HudRender(ServiceProvider services, HtmlRenderer renderer, LocalizationService localizer) : IAsyncDisposable
    {
        private Issue134HudHost _host = null!;
        private HtmlRootComponent _root;

        public LocalizationService Localizer { get; } = localizer;

        public static async Task<HudRender> CreateAsync(string language = "en")
        {
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);
            var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var render = new HudRender(services, renderer, localizer);
            render._root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Issue134HudHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Capture"] = (Action<Issue134HudHost>)(host => render._host = host) })));
            return render;
        }

        public Task SetAsync(CyberDefenseHudViewModel model, int? rank = null, long elapsed = 0, long windowMs = 2500,
            string taskKey = "task", bool timingActive = true, PlayerLevelProgress? progress = null) =>
            renderer.Dispatcher.InvokeAsync(() => _host.Set(new Dictionary<string, object?>
            {
                ["ViewModel"] = model,
                ["CriticalWindowMs"] = windowMs,
                ["ActiveElapsedMs"] = elapsed,
                ["TaskKey"] = taskKey,
                ["IsTimingActive"] = timingActive,
                ["CriticalStrikeRank"] = rank,
                ["PlayerProgress"] = progress ?? CyberDefenseLevelPolicy.CalculateProgress(0)
            }));

        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_root.ToHtmlString()));

        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
