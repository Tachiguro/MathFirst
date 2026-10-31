namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
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
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Issue #117: rendered Chromium regressions for the Upgrade Terminal scroll/focus trap.
/// Production markup comes from the real Razor components (HtmlRenderer); the initial focus call is
/// captured from the component's own OnAfterRenderAsync; geometry comes from production CSS in headless Chromium.
/// This is synthetic UI evidence, not Android WebView or physical-device acceptance.
/// </summary>
public sealed class Issue117UpgradeTerminalRenderedLayoutTests : IDisposable
{
    private const double FullyVisible = 0.99;
    private const double GeometryTolerancePx = 0.5;

    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("phone-384x744", 384, 744),
        ("narrow-320x640", 320, 640),
        ("landscape-744x384", 744, 384)
    ];

    private static readonly string[] Languages = ["en", "de", "ru"];

    // Synthetic player levels: level 1 has no Skill Points; level 6 can afford Attack/Firewall; level 80 unlocks every card.
    private static readonly (string Name, int PlayerLevel)[] Affordability =
    [
        ("none-affordable", 1),
        ("some-affordable", 6),
        ("all-unlocked", 80)
    ];

    private static readonly HashSet<string> MathSurfaceSelectors = new(StringComparer.Ordinal)
    {
        ".training-top-region", ".practice-gate-overlay", ".expression-row", ".answer-input", ".numeric-keypad", ".numeric-keypad-button"
    };

    private static readonly Dictionary<string, string> FocusSelectors = new(StringComparer.Ordinal)
    {
        ["_purchaseButton"] = ".attack-purchase-btn",
        ["_firewallPurchaseButton"] = ".firewall-purchase-btn",
        ["_criticalStrikePurchaseButton"] = ".critical-strike-purchase-btn",
        ["_overdrivePurchaseButton"] = ".overdrive-purchase-btn",
        ["_closeButton"] = ".upgrade-close-btn"
    };

    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempDirectories = [];

    public Issue117UpgradeTerminalRenderedLayoutTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // =========================================================================
    // A. STANDALONE TERMINAL: VIEWPORTS x LOCALES x AFFORDABILITY x THEME x TEXT SIZE
    // =========================================================================

    [Theory(Timeout = 240000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradeTerminal_RenderedInChromium_OpensAtTop_PinsHeaderAndClose_AndScrollsCardsInternally(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        var focusTargets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (affordName, level) in Affordability)
        {
            var directory = CreateTempDirectory();
            using var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue117-terminal.db"));
            await store.InitializeAsync();
            await SetSyntheticPlayerLevelAsync(store, level);
            using var vm = new UpgradeTerminalViewModel(store);
            await vm.InitializeAsync();

            foreach (var language in Languages)
            {
                var localizer = new LocalizationService();
                localizer.ApplyLanguagePreference(language);
                var markup = await RenderUpgradeTerminalMarkupAsync(vm, localizer);
                var focus = await CaptureInitialFocusAsync(vm, localizer);
                focusTargets[$"{affordName}/{language}"] = focus.Selector;

                foreach (var (viewportName, width, height) in Viewports)
                {
                    foreach (var theme in new[] { "light", "dark" })
                    {
                        scenarios.Add(new($"{viewportName}/{language}/{affordName}/{theme}", "terminal", width, height, language, theme, false, markup, focus));
                    }
                    scenarios.Add(new($"{viewportName}/{language}/{affordName}/light/large-text", "terminal", width, height, language, "light", true, markup, focus));
                }
            }
        }

        // The component's own focus policy is unchanged: affordable purchase first, Close otherwise.
        foreach (var language in Languages)
        {
            Assert.Equal(".upgrade-close-btn", focusTargets[$"none-affordable/{language}"]);
            Assert.Equal(".attack-purchase-btn", focusTargets[$"some-affordable/{language}"]);
            Assert.Equal(".attack-purchase-btn", focusTargets[$"all-unlocked/{language}"]);
        }

        var results = await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion, CreateTempDirectory());
        var violations = new List<string>();
        foreach (var result in results)
        {
            var id = result.GetProperty("id").GetString()!;
            var scenario = scenarios.Single(s => s.Id == id);
            Assert.Equal(reducedMotion, result.GetProperty("reducedMotion").GetBoolean());
            AssertViewport(result, scenario);
            CheckFocus(id, result.GetProperty("focus"), violations);
            CheckTerminal(id, result.GetProperty("probe"), scenario.LargeText, violations);
            LogTerminal(id, result);
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered layout violation(s):\n" + string.Join("\n", violations));
    }

    // =========================================================================
    // B. HOME INTEGRATION: OPEN FROM HUD AND PAUSE, BACKGROUND BLOCKING, GEOMETRY, TIMING
    // =========================================================================

    [Theory(Timeout = 240000)]
    [InlineData("hud")]
    [InlineData("pause")]
    public async Task HomeUpgradeTerminal_RenderedInChromium_BlocksBackground_KeepsMathGeometry_AndPreservesTiming(string opening)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();

        foreach (var language in Languages)
        {
            foreach (var (affordName, level) in Affordability.Where(a => a.Name != "all-unlocked"))
            {
                await using var f = await HomeFixture.CreateAsync(this, language, level);
                await f.InvokeHomeAsync("StartOrResumePractice");
                f.Clock.Advance(1300);
                if (opening == "pause")
                {
                    await f.InvokeHomeAsync("PausePractice");
                    Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
                    Assert.Contains("pause-upgrade-btn", await f.GetHtmlAsync(), StringComparison.Ordinal);
                }
                else
                {
                    Assert.Equal(PracticeGateState.Running, f.Session.PracticeGate);
                    Assert.True(f.Session.IsTimingActive);
                }

                var revision = f.Session.FactInstanceRevision;
                var answerBuffer = f.Session.CurrentAnswerInput;
                var before = await f.GetHtmlAsync();

                await f.InvokeHomeAsync("OpenUpgradeTerminalAsync");
                Assert.True(f.IsUpgradeTerminalOpen);
                Assert.False(f.Session.IsTimingActive);
                var open = await f.GetHtmlAsync();
                var focus = await f.CaptureTerminalInitialFocusAsync();

                // Background input is ignored and the open terminal accrues no active answer time.
                await f.TapKeypadAsync("7");
                await f.PressKeyAsync("8");
                Assert.Equal(answerBuffer, f.Session.CurrentAnswerInput);
                f.Clock.Advance(20000);
                Assert.Equal(1300, f.Session.GetCurrentActiveElapsedMs());

                await f.InvokeHomeAsync("CloseUpgradeTerminal");
                Assert.False(f.IsUpgradeTerminalOpen);
                Assert.Equal(opening == "pause" ? PracticeGateState.ManualPause : PracticeGateState.Running, f.Session.PracticeGate);
                Assert.Equal(opening != "pause", f.Session.IsTimingActive);
                Assert.Equal(1300, f.Session.GetCurrentActiveElapsedMs());
                Assert.Equal(revision, f.Session.FactInstanceRevision);
                Assert.Equal(answerBuffer, f.Session.CurrentAnswerInput);
                var after = await f.GetHtmlAsync();

                foreach (var (viewportName, width, height) in Viewports)
                {
                    var prefix = $"{viewportName}/{language}/{affordName}/{opening}";
                    scenarios.Add(new($"{prefix}/before", "home", width, height, language, "light", false, before, null));
                    scenarios.Add(new($"{prefix}/open", "home", width, height, language, "light", false, open, focus));
                    scenarios.Add(new($"{prefix}/after", "home", width, height, language, "light", false, after, null));
                }
            }
        }

        var results = (await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion: false, CreateTempDirectory()))
            .ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var prefix in scenarios.Select(s => s.Id[..s.Id.LastIndexOf('/')]).Distinct())
        {
            var beforeProbe = results[prefix + "/before"].GetProperty("probe");
            var openResult = results[prefix + "/open"];
            var openProbe = openResult.GetProperty("probe");
            var afterProbe = results[prefix + "/after"].GetProperty("probe");

            if (beforeProbe.GetProperty("terminalOpen").GetBoolean() || afterProbe.GetProperty("terminalOpen").GetBoolean())
            {
                violations.Add($"{prefix}: terminal must be absent before opening and after closing");
            }
            if (!openProbe.GetProperty("terminalOpen").GetBoolean())
            {
                violations.Add($"{prefix}: terminal did not render when opened");
                continue;
            }

            CheckFocus(prefix + "/open", openResult.GetProperty("focus"), violations);
            CheckTerminal(prefix + "/open", openProbe.GetProperty("terminal"), allowContentScroll: false, violations);
            LogTerminal(prefix + "/open", openResult);

            var beforeBackground = beforeProbe.GetProperty("background");
            var openBackground = openProbe.GetProperty("background");
            var afterBackground = afterProbe.GetProperty("background");
            Assert.True(beforeBackground.TryGetProperty(opening == "pause" ? ".practice-gate-overlay" : ".expression-row", out _),
                $"{prefix}: expected background surface missing before opening");

            foreach (var element in beforeBackground.EnumerateObject())
            {
                var selector = element.Name;
                var beforeRect = element.Value.GetProperty("rect");

                // Closing restores the previous presentation exactly.
                if (!afterBackground.TryGetProperty(selector, out var afterElement))
                {
                    violations.Add($"{prefix}: {selector} missing after close");
                }
                else if (!SameRect(beforeRect, afterElement.GetProperty("rect")))
                {
                    violations.Add($"{prefix}: {selector} moved after close {Describe(beforeRect)} -> {Describe(afterElement.GetProperty("rect"))}");
                }

                if (!element.Value.GetProperty("hitSelf").GetBoolean() && selector is ".numeric-keypad-button" or ".answer-input" or ".pause-upgrade-btn")
                {
                    violations.Add($"{prefix}: {selector} not hittable before opening");
                }

                var isMathSurface = MathSurfaceSelectors.Contains(selector);
                if (!openBackground.TryGetProperty(selector, out var openElement))
                {
                    // Header-only controls (for example Pause) are intentionally hidden while the modal is open.
                    if (isMathSurface)
                    {
                        violations.Add($"{prefix}: {selector} missing while terminal open");
                    }
                    continue;
                }

                // The arithmetic expression, answer field, keypad and pause surface keep their geometry while the modal is open.
                if (isMathSurface && !SameRect(beforeRect, openElement.GetProperty("rect")))
                {
                    violations.Add($"{prefix}: {selector} moved while open {Describe(beforeRect)} -> {Describe(openElement.GetProperty("rect"))}");
                }

                // Every background control is covered by the modal and inert.
                if (!openElement.GetProperty("hitInsideTerminal").GetBoolean() || openElement.GetProperty("hitSelf").GetBoolean())
                {
                    violations.Add($"{prefix}: background {selector} receives pointer hits while terminal open");
                }
                if (!openElement.GetProperty("inert").GetBoolean())
                {
                    violations.Add($"{prefix}: background {selector} is not inert while terminal open");
                }
            }
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered Home violation(s):\n" + string.Join("\n", violations));
    }

    // =========================================================================
    // CHECKS
    // =========================================================================

    private static void AssertViewport(JsonElement result, ChromiumLayoutHarness.Scenario scenario)
    {
        var viewport = result.GetProperty("viewport");
        Assert.Equal(scenario.Width, viewport.GetProperty("width").GetInt32());
        Assert.Equal(scenario.Height, viewport.GetProperty("height").GetInt32());
    }

    private static void CheckFocus(string id, JsonElement focus, List<string> violations)
    {
        if (focus.ValueKind != JsonValueKind.Object)
        {
            violations.Add($"{id}: no initial focus request captured");
            return;
        }
        if (!focus.GetProperty("found").GetBoolean() || !focus.GetProperty("active").GetBoolean())
        {
            violations.Add($"{id}: initial focus target {focus.GetProperty("selector").GetString()} was not focused");
        }
        var scrolledBefore = focus.GetProperty("scrolledBefore").GetArrayLength();
        var scrolledAfter = focus.GetProperty("scrolledAfter");
        if (scrolledBefore != 0 || scrolledAfter.GetArrayLength() != 0)
        {
            violations.Add($"{id}: opening focus scrolled content ({scrolledAfter.GetRawText()}, preventScroll={focus.GetProperty("preventScroll").GetBoolean()})");
        }
    }

    /// <param name="allowContentScroll">
    /// Only enlarged-text scenarios may fall back to scrolling the whole terminal content when the pinned
    /// header, status and Close cannot fit; ordinary phone text sizes must keep them pinned.
    /// </param>
    private static void CheckTerminal(string id, JsonElement probe, bool allowContentScroll, List<string> violations)
    {
        if (!probe.GetProperty("present").GetBoolean())
        {
            violations.Add($"{id}: terminal markup incomplete");
            return;
        }

        var content = probe.GetProperty("contentOverflow");
        var pinnedFits = content.GetProperty("scrollHeight").GetDouble() <= content.GetProperty("clientHeight").GetDouble() + 1;
        if (!pinnedFits)
        {
            if (!allowContentScroll)
            {
                violations.Add($"{id}: pinned terminal chrome does not fit ({content.GetProperty("scrollHeight").GetDouble()}>{content.GetProperty("clientHeight").GetDouble()})");
            }
            if (content.GetProperty("overflowY").GetString() is not ("auto" or "scroll"))
            {
                violations.Add($"{id}: terminal content overflows but is not user-scrollable (overflow-y {content.GetProperty("overflowY").GetString()})");
            }
        }

        foreach (var phase in new[] { "atOpen", "afterScroll" })
        {
            var pinned = probe.GetProperty(phase);
            var parts = pinnedFits ? new[] { "title", "subtitle", "level", "sp", "close" } : new[] { "title", "subtitle", "level", "sp" };
            foreach (var part in parts)
            {
                var fraction = pinned.GetProperty(part).GetDouble();
                if (fraction < FullyVisible)
                {
                    violations.Add($"{id}: {phase} {part} only {fraction:P0} visible");
                }
            }
            if (pinnedFits && !pinned.GetProperty("closeHit").GetBoolean())
            {
                violations.Add($"{id}: {phase} Close is not the hit target at its centre");
            }
        }

        var closeReachable = probe.GetProperty("closeReachable");
        if (closeReachable.GetProperty("visible").GetDouble() < FullyVisible || !closeReachable.GetProperty("hit").GetBoolean())
        {
            violations.Add($"{id}: Close cannot be reached as a hit target");
        }

        if (probe.GetProperty("contentScrollTop").GetDouble() != 0 || probe.GetProperty("dialogScrollTop").GetDouble() != 0)
        {
            violations.Add($"{id}: terminal opened scrolled (content {probe.GetProperty("contentScrollTop").GetDouble()}, dialog {probe.GetProperty("dialogScrollTop").GetDouble()})");
        }
        if (probe.GetProperty("horizontalOverflow").GetBoolean())
        {
            violations.Add($"{id}: terminal overflows horizontally");
        }

        var cards = probe.GetProperty("cards");
        var overflowY = cards.GetProperty("overflowY").GetString();
        var scrollHeight = cards.GetProperty("scrollHeight").GetDouble();
        var clientHeight = cards.GetProperty("clientHeight").GetDouble();
        if (scrollHeight > clientHeight + 1 && overflowY is not ("auto" or "scroll"))
        {
            violations.Add($"{id}: cards overflow ({scrollHeight}>{clientHeight}) but are not user-scrollable (overflow-y {overflowY})");
        }
        if (clientHeight < 96)
        {
            violations.Add($"{id}: cards viewport too small ({clientHeight}px)");
        }
        if (probe.GetProperty("firstCardHeaderAtStart").GetDouble() < FullyVisible)
        {
            violations.Add($"{id}: first card header not reachable ({probe.GetProperty("firstCardHeaderAtStart").GetDouble():P0})");
        }
        if (probe.GetProperty("lastCardCostRowAtEnd").GetDouble() < FullyVisible)
        {
            violations.Add($"{id}: last card cost row not reachable by scrolling ({probe.GetProperty("lastCardCostRowAtEnd").GetDouble():P0})");
        }
        if (!SameRect(probe.GetProperty("rects").GetProperty("close"), probe.GetProperty("closeRectAfterScroll")))
        {
            violations.Add($"{id}: Close moved while scrolling cards");
        }
        foreach (var button in probe.GetProperty("purchaseButtons").EnumerateArray())
        {
            if (button.GetProperty("visible").GetDouble() < FullyVisible || !button.GetProperty("hit").GetBoolean())
            {
                violations.Add($"{id}: purchase control '{button.GetProperty("cls").GetString()}' not reachable as a hit target");
            }
        }
        var scrolledOutside = probe.GetProperty("scrolledOutsideCards");
        if (scrolledOutside.GetArrayLength() != 0)
        {
            violations.Add($"{id}: scrolling outside the cards container: {scrolledOutside.GetRawText()}");
        }
    }

    private void LogTerminal(string id, JsonElement result)
    {
        var probe = result.GetProperty("probe");
        if (probe.TryGetProperty("terminal", out var terminal))
        {
            probe = terminal;
        }
        if (probe.ValueKind != JsonValueKind.Object || !probe.GetProperty("present").GetBoolean())
        {
            return;
        }
        var cards = probe.GetProperty("cards");
        var focus = result.GetProperty("focus");
        _output.WriteLine(
            $"{id}: content={probe.GetProperty("contentDisplay").GetString()} contentScrollTop={probe.GetProperty("contentScrollTop").GetDouble()} " +
            $"content client/scroll={probe.GetProperty("contentOverflow").GetProperty("clientHeight").GetDouble()}/{probe.GetProperty("contentOverflow").GetProperty("scrollHeight").GetDouble()} " +
            $"cards client/scroll={cards.GetProperty("clientHeight").GetDouble()}/{cards.GetProperty("scrollHeight").GetDouble()} maxScroll={probe.GetProperty("cardsMaxScrollTop").GetDouble()} " +
            $"title={probe.GetProperty("atOpen").GetProperty("title").GetDouble():0.00} close={probe.GetProperty("atOpen").GetProperty("close").GetDouble():0.00} closeHit={probe.GetProperty("atOpen").GetProperty("closeHit").GetBoolean()} " +
            $"lastCard={probe.GetProperty("lastCardCostRowAtEnd").GetDouble():0.00} " +
            (focus.ValueKind == JsonValueKind.Object
                ? $"focus={focus.GetProperty("selector").GetString()} preventScroll={focus.GetProperty("preventScroll").GetBoolean()} focusedVisible={focus.GetProperty("focusedVisible").GetDouble():0.00} scrolled={focus.GetProperty("scrolledAfter").GetArrayLength()}"
                : "focus=none"));
    }

    private static bool SameRect(JsonElement a, JsonElement b) =>
        new[] { "top", "left", "width", "height" }.All(k => Math.Abs(a.GetProperty(k).GetDouble() - b.GetProperty(k).GetDouble()) <= GeometryTolerancePx);

    private static string Describe(JsonElement r) =>
        $"[{r.GetProperty("left").GetDouble():0.#},{r.GetProperty("top").GetDouble():0.#} {r.GetProperty("width").GetDouble():0.#}x{r.GetProperty("height").GetDouble():0.#}]";

    // =========================================================================
    // RENDERING HELPERS
    // =========================================================================

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue117_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }

    private static async Task SetSyntheticPlayerLevelAsync(SqliteGameplayStore store, int playerLevel)
    {
        using var connection = new SqliteConnection($"Data Source={store.StoragePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = @level, earned_sp = @sp, spent_sp = 0, available_sp = @sp WHERE id = 1;";
        command.Parameters.AddWithValue("@xp", CyberDefenseLevelPolicy.GetCumulativeXpThreshold(playerLevel));
        command.Parameters.AddWithValue("@level", playerLevel);
        command.Parameters.AddWithValue("@sp", playerLevel - 1);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string> RenderUpgradeTerminalMarkupAsync(UpgradeTerminalViewModel vm, ILocalizationService localizer)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.UpgradeTerminal>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ViewModel"] = vm,
                ["Localizer"] = localizer
            }));
            return root.ToHtmlString();
        });
    }

    /// <summary>
    /// Runs the real UpgradeTerminal lifecycle (including OnAfterRenderAsync) and records the
    /// focus interop call it issues, mapped back to the focused element's CSS selector.
    /// </summary>
    private static async Task<ChromiumLayoutHarness.FocusRequest> CaptureInitialFocusAsync(UpgradeTerminalViewModel vm, ILocalizationService localizer)
    {
        var js = new RecordingJsRuntime();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(localizer).BuildServiceProvider();
        await using var renderer = new FocusCaptureRenderer(services, services.GetRequiredService<ILoggerFactory>(), js);
        var component = await renderer.RenderAsync<global::MathFirst.Core.Tests.UpgradeTerminal>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["ViewModel"] = vm,
            ["Localizer"] = localizer
        }));

        var call = Assert.Single(js.Calls, c => c.Identifier == "Blazor._internal.domWrapper.focus");
        var focused = Assert.IsType<ElementReference>(call.Args[0]);
        var preventScroll = Assert.IsType<bool>(call.Args[1]);
        var field = typeof(global::MathFirst.Core.Tests.UpgradeTerminal)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(ElementReference))
            .Single(f => ((ElementReference)f.GetValue(component)!).Id == focused.Id);
        Assert.True(FocusSelectors.TryGetValue(field.Name, out var selector), $"Unmapped initial focus target {field.Name}");
        return new ChromiumLayoutHarness.FocusRequest(selector!, preventScroll);
    }

    // BL0006: a minimal test renderer is the supported way to execute OnAfterRenderAsync outside a browser.
#pragma warning disable BL0006
    private sealed class FocusCaptureRenderer : Renderer
    {
        public FocusCaptureRenderer(IServiceProvider services, ILoggerFactory loggerFactory, IJSRuntime jsRuntime)
            : base(services, loggerFactory)
        {
            ElementReferenceContext = new WebElementReferenceContext(jsRuntime);
        }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public Task<T> RenderAsync<T>(ParameterView parameters) where T : IComponent =>
            Dispatcher.InvokeAsync(async () =>
            {
                var component = (T)InstantiateComponent(typeof(T));
                await RenderRootComponentAsync(AssignRootComponentId(component), parameters);
                return component;
            });

        protected override void HandleException(Exception exception) => ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }
#pragma warning restore BL0006

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    // =========================================================================
    // HOME FIXTURE (synthetic stores, fake clock)
    // =========================================================================

    private sealed class HomeFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly global::MathFirst.Core.Tests.Home _home;

        public FakeClock Clock { get; }
        public TrainingSession Session { get; }
        public SqliteGameplayStore Store { get; }
        public LocalizationService Localizer { get; }

        public bool IsUpgradeTerminalOpen => (bool)typeof(global::MathFirst.Core.Tests.Home)
            .GetField("_isUpgradeTerminalOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;

        private HomeFixture(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, global::MathFirst.Core.Tests.Home home,
            FakeClock clock, TrainingSession session, SqliteGameplayStore store, LocalizationService localizer)
        {
            _services = services;
            _renderer = renderer;
            _root = root;
            _home = home;
            Clock = clock;
            Session = session;
            Store = store;
            Localizer = localizer;
        }

        public static async Task<HomeFixture> CreateAsync(Issue117UpgradeTerminalRenderedLayoutTests suite, string language, int playerLevel)
        {
            var directory = suite.CreateTempDirectory();
            var clock = new FakeClock();
            var preferences = new TestPreferenceStore(language);
            var sessionState = new CyberDefenseSessionState(preferences);
            var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue117-home.db"));
            await store.InitializeAsync();
            if (playerLevel == 1)
            {
                // Issue #125: a Level 1 learner has never earned a Skill Point, so Home offers no Upgrade at all. The
                // reachable "nothing affordable" Home state is a discovered learner who has spent every point.
                await SetSyntheticPlayerLevelAsync(store, 2);
                await store.PurchaseAttackUpgradeAsync(0, 0);
            }
            else
            {
                await SetSyntheticPlayerLevelAsync(store, playerLevel);
            }

            var learner = new TestLearnerStore();
            var session = new TrainingSession(learner, clock, preferenceStore: preferences);
            var coordinator = new CyberDefenseCombatCoordinator(store, new CyberDefenseSubmissionConsumer(store, learner));
            var ceremony = new CyberDefensePresentationSequence((_, _) => Task.CompletedTask);
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);

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
                .AddSingleton<IAppResetCoordinator>(new NoResetCoordinator())
                .AddSingleton<MathFirst.Application.Navigation.IAppBackNavigationCoordinator>(new MathFirst.Application.Navigation.AppBackNavigationCoordinator())
                .AddSingleton<IHapticFeedbackService>(new TestHapticFeedbackService())
                .AddSingleton<IJSRuntime>(new RecordingJsRuntime())
                .BuildServiceProvider();

            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            global::MathFirst.Core.Tests.Home capturedHome = null!;
            var root = await renderer.Dispatcher.InvokeAsync(() =>
                renderer.RenderComponentAsync<HomeHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(h => capturedHome = h)
                })));

            return new HomeFixture(services, renderer, root, capturedHome, clock, session, store, localizer);
        }

        public Task InvokeHomeAsync(string methodName) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var result = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, null);
                if (result is Task task) await task;
                StateHasChanged();
            });

        public Task PressKeyAsync(string key) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("HandleGlobalKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)method.Invoke(_home, [new KeyboardEventArgs { Key = key }])!;
            });

        public Task TapKeypadAsync(string key) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod("HandleKeypadKeyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)method.Invoke(_home, [key])!;
            });

        /// <summary>Runs the real terminal focus lifecycle against Home's own terminal view model.</summary>
        public Task<ChromiumLayoutHarness.FocusRequest> CaptureTerminalInitialFocusAsync()
        {
            var vm = (UpgradeTerminalViewModel)typeof(global::MathFirst.Core.Tests.Home)
                .GetField("_upgradeTerminalViewModel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home)!;
            return CaptureInitialFocusAsync(vm, Localizer);
        }

        public Task<string> GetHtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString());

        private void StateHasChanged() =>
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null);

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
            Store.Dispose();
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

    private sealed class TestPreferenceStore(string language) : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        public bool GetCyberDefenseEnabled() => true;
        public void SetCyberDefenseEnabled(bool enabled) { }
        public bool GetTutorialCompleted() => true;
        public void SetTutorialCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => language;
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => [ArithmeticOperation.Addition];
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => false;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    private sealed class TestLearnerStore : ILearnerStore
    {
        private LearnerSnapshot _snapshot = CreateFresh();

        private static LearnerSnapshot CreateFresh()
        {
            var progression = LearnerProgression.CreateFresh();
            progression.StoreRevision = 1;
            return new LearnerSnapshot(progression, new Dictionary<string, ItemLearningState>(), [], 1, LearnerProgression.DefaultSchemaVersion);
        }

        public string StoragePath => "synthetic://issue117-learner";
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

    private sealed class TestHapticFeedbackService : IHapticFeedbackService
    {
        public bool IsSupported => false;
        public void PerformCue(HapticFeedbackCue cue) { }
    }
}
