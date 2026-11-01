namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.Json;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit.Abstractions;
using HomeFixture = Issue122HomeHudTests.HomeFixture;
using HomeOptions = Issue122HomeHudTests.HomeOptions;
using MenuFixture = Issue125GameMenuTests.MenuFixture;

/// <summary>
/// Issue #136: rendered Chromium regressions for the opponent description, the compact player status (Level/XP on top,
/// shields in the middle, HP bottom-left) and the tutorial replayed over the paused game. Markup comes from the real Home
/// page and the real HUD and tutorial components; geometry from the production CSS in headless Chromium.
/// Synthetic UI evidence only; it is not Android WebView, Android font scaling, TalkBack or physical-device acceptance.
/// </summary>
public sealed class Issue136RenderedLayoutTests : IDisposable
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("narrow-320x640", 320, 640),
        ("android-360x780", 360, 780),
        ("phone-384x744", 384, 744),
        ("short-320x480", 320, 480),
        ("landscape-744x384", 744, 384)
    ];

    private static readonly string[] Languages = ["en", "de", "ru"];

    private static readonly (string Theme, bool LargeText)[] Themes = [("light", false), ("dark", false), ("light", true)];

    // Expression row top and first keypad key top on main@4529cd9 and main@e20c7f6 (Issue #134 harness). Key: viewport, 130% text.
    private static readonly Dictionary<(string Viewport, bool LargeText), (double ExpressionTop, double KeyTop)> BaseMathGeometry = new()
    {
        [("narrow-320x640", false)] = (343, 437.39),
        [("narrow-320x640", true)] = (393, 513.55),
        [("android-360x780", false)] = (382.9, 487.13),
        [("android-360x780", true)] = (425.9, 547.28),
        [("phone-384x744", false)] = (372.5, 470.05),
        [("phone-384x744", true)] = (416.3, 537.09),
        [("short-320x480", false)] = (328.2, 421.17),
        [("short-320x480", true)] = (386.7, 507.23),
        [("landscape-744x384", false)] = (264.3, 286.58),
        [("landscape-744x384", true)] = (322.8, 335.09)
    };

    private static readonly string[] ProtectedSelectors = [".expression-row", ".answer-input", ".numeric-keypad", ".operation-progress-hud", ".mathfirst-header", ".training-top-region"];

    // Every shipped opponent description (eight ordinary opponents and the Nexus Overlord boss).
    private static readonly string[] DescriptionKeys =
    [
        "CyberDefense_Opponent_GlitchDrone_Desc", "CyberDefense_Opponent_DataLeech_Desc", "CyberDefense_Opponent_FirewallBreaker_Desc",
        "CyberDefense_Opponent_VirusCore_Desc", "CyberDefense_Opponent_SignalPhantom_Desc", "CyberDefense_Opponent_QuantumBug_Desc",
        "CyberDefense_Opponent_TrojanWasp_Desc", "CyberDefense_Opponent_CrystalMalware_Desc", "CyberDefense_Opponent_NexusOverlord_Desc"
    ];

    private const string StressDescription =
        "Synthetic stress text: a deliberately long localized opponent description that is far longer than any shipped copy, " +
        "so that even the tallest opponent stage cannot show all of it and an intentional whole-line fallback must apply.";

    private readonly ITestOutputHelper _output;
    private readonly Issue122HomeHudTests _homeSuite = new();
    private readonly Issue125GameMenuTests _menuSuite = new();
    private readonly List<string> _tempDirectories = [];

    public Issue136RenderedLayoutTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _homeSuite.Dispose();
        _menuSuite.Dispose();
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // =========================================================================
    // OPPONENT DESCRIPTION
    // =========================================================================

    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpponentDescription_RenderedInChromium_UsesTheFreeStageHeightAboveTheEnemyHp(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        foreach (var language in Languages)
        {
            var variants = new (string Name, HomeOptions Options)[]
            {
                ("normal", new(Language: language, PlayerLevel: 1)),
                ("boss", new(Language: language, PlayerLevel: 12,
                    SeedRunState: store => SeedRunAsync(store, 1, CyberDefenseScalingPolicy.GetBossIndex(1), CyberDefenseScalingPolicy.GetBossMaxHp(1)))),
                ("sector-boss", new(Language: language, PlayerLevel: 12,
                    SeedRunState: store => SeedRunAsync(store, 4, CyberDefenseScalingPolicy.GetBossIndex(4), CyberDefenseScalingPolicy.GetBossMaxHp(4))))
            };
            foreach (var (name, options) in variants)
            {
                await using var fixture = await HomeFixture.CreateAsync(_homeSuite, options);
                await fixture.InvokeHomeAsync("StartOrResumePractice");
                var html = await fixture.GetHtmlAsync();
                var shown = Regex.Match(html, @"<p class=""scene-enemy-desc"">([^<]*)</p>");
                Assert.True(shown.Success, "The opponent description must be rendered.");
                var longest = DescriptionKeys.Select(key => fixture.Localizer[key]).MaxBy(text => text.Length)!;
                var replaced = html.Replace(shown.Value, $"<p class=\"scene-enemy-desc\">{longest}</p>", StringComparison.Ordinal);
                var stressed = html.Replace(shown.Value, $"<p class=\"scene-enemy-desc\">{StressDescription}</p>", StringComparison.Ordinal);
                Assert.Contains(longest, replaced);
                Assert.Contains(StressDescription, stressed);

                AddScenarios(scenarios, "combat", language, name, html, Viewports);
                AddScenarios(scenarios, "combat", language, name + "-longest", replaced, Viewports);
                AddScenarios(scenarios, "combat", language, name + "-stress", stressed, Viewports);
            }
        }

        var results = await RunAsync(scenarios, reducedMotion);
        var violations = new List<string>();
        foreach (var scenario in scenarios)
        {
            CheckDescription(scenario.Id, results[scenario.Id], reducedMotion, violations);
        }
        CheckMathGeometry(scenarios, results, violations);
        Assert.True(violations.Count == 0, $"{violations.Count} rendered opponent description violation(s):\n" + string.Join("\n", violations.Take(400)));
    }

    private static void CheckDescription(string id, JsonElement probe, bool reducedMotion, List<string> violations)
    {
        var rects = probe.GetProperty("rects");
        bool Has(string selector) => rects.GetProperty(selector).ValueKind == JsonValueKind.Object;
        double R(string selector, string edge) => rects.GetProperty(selector).GetProperty(edge).GetDouble();
        double Clip(string selector) => probe.GetProperty("clip").TryGetProperty(selector, out var v) ? v.GetDouble() : 0;
        foreach (var selector in new[] { ".scene-stage", ".scene-intel", ".scene-enemy-desc", ".enemy-hp-cluster", ".enemy-hp-value", ".scene-artwork-wrapper", ".scene-opponent-image" })
        {
            if (!Has(selector)) { violations.Add($"{id}: {selector} missing"); return; }
        }
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow");

        var lines = probe.GetProperty("lines").GetProperty(".scene-enemy-desc");
        var lineHeight = lines.GetProperty("lineHeight").GetDouble();
        var shown = lines.GetProperty("clientHeight").GetDouble();
        var full = lines.GetProperty("scrollHeight").GetDouble();
        var visibleLines = shown / lineHeight;
        var gap = probe.GetProperty("rootFontPx").GetDouble() * 0.25;
        var top = R(".scene-enemy-desc", "top");
        var hpTop = R(".enemy-hp-cluster", "top");
        var truncated = full > shown + 1;

        // Whole lines only, at least one, fully inside the stage and above the numeric HP row.
        if (visibleLines < 0.95) violations.Add($"{id}: description shows {visibleLines:0.00} lines");
        if (Math.Abs(visibleLines - Math.Round(visibleLines)) * lineHeight > 1.5) violations.Add($"{id}: description cuts a line ({visibleLines:0.00} lines visible)");
        if (Clip(".scene-enemy-desc") < 0.99) violations.Add($"{id}: description clipped by the stage ({Clip(".scene-enemy-desc"):0.00})");
        if (R(".scene-enemy-desc", "bottom") > hpTop + 0.5) violations.Add($"{id}: description reaches into the HP row");
        if (R(".scene-enemy-desc", "bottom") > R(".scene-stage", "bottom") + 0.5 || top < R(".scene-stage", "top") - 0.5) violations.Add($"{id}: description outside the stage");

        // The full text is shown whenever it fits above the HP row; an ellipsis only when not even one more line fits.
        var room = hpTop - gap - top;
        if (truncated && full <= room + 0.5)
            violations.Add($"{id}: description cut to {visibleLines:0} line(s) although all {full / lineHeight:0} fit ({room:0} px free above the HP row)");
        if (truncated && shown + lineHeight <= room + 0.5)
            violations.Add($"{id}: description ends with an ellipsis after {visibleLines:0} line(s) although another line fits ({room - shown:0} px free)");

        // HP and artwork stay intact: HP at the bottom of the stage, unclipped, and the art not under the text.
        if (Clip(".enemy-hp-value") < 0.99) violations.Add($"{id}: opponent HP value clipped");
        if (R(".enemy-hp-cluster", "bottom") < R(".scene-stage", "bottom") - 16) violations.Add($"{id}: HP row is not at the bottom of the opponent section");
        if (R(".scene-enemy-desc", "right") > R(".scene-artwork-wrapper", "left") + 0.5) violations.Add($"{id}: description extends into the artwork area");
        if (Clip(".scene-opponent-image") < (reducedMotion ? 0.98 : 0.9)) violations.Add($"{id}: opponent artwork cut by the stage");
        foreach (var overlap in probe.GetProperty("overlaps").EnumerateArray())
        {
            var pair = overlap.GetProperty("pair").GetString()!;
            var tolerance = !reducedMotion && pair.StartsWith(".scene-opponent-image", StringComparison.Ordinal) ? 6 : 0.5;
            if (overlap.GetProperty("depth").GetDouble() > tolerance) violations.Add($"{id}: overlap {pair}");
        }
    }

    // =========================================================================
    // PLAYER STATUS
    // =========================================================================

    private sealed record StatusExpectation(int Capacity, int Active, int RechargeUnits, string XpText, bool Radar);

    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayerStatus_RenderedInChromium_StacksXpShieldsAndHpWithCompactAlignedMeters(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        var expectations = new Dictionary<string, StatusExpectation>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            var variants = new (string Name, HomeOptions Options, int Capacity, int Active, int Units)[]
            {
                ("no-shield", new(Language: language, PlayerLevel: 1), 0, 0, 0),
                ("shield-full", new(Language: language, PlayerLevel: 20, FirewallPurchases: 1,
                    SeedRunState: store => SeedShieldAsync(store, 1, 1, 0)), 1, 1, 0),
                ("shield-depleted", new(Language: language, PlayerLevel: 20, FirewallPurchases: 1,
                    SeedRunState: store => SeedShieldAsync(store, 1, 0, 12)), 1, 0, 12),
                ("shields-recharging", new(Language: language, PlayerLevel: 60, FirewallPurchases: 3, CriticalStrikePurchases: 2,
                    SeedRunState: store => SeedShieldAsync(store, 3, 2, 29)), 3, 2, 29)
            };
            foreach (var (name, options, capacity, active, units) in variants)
            {
                await using var fixture = await HomeFixture.CreateAsync(_homeSuite, options);
                await fixture.InvokeHomeAsync("StartOrResumePractice");
                var html = await fixture.GetHtmlAsync();
                var xpText = Regex.Match(html, @"<span class=""player-xp-value"">([^<]*)</span>").Groups[1].Value;
                Assert.False(string.IsNullOrEmpty(xpText));
                var radar = options.CriticalStrikePurchases > 0;
                Add(name, html, new(capacity, active, units, xpText, radar));

                if (name == "shields-recharging")
                {
                    // Beside the radar and a recharge row: Level 999 with its real XP requirement (four digits), and a
                    // synthetic five-digit XP value beyond the shipped curve.
                    var level999 = CyberDefenseLevelPolicy.GetDeltaXp(999);
                    foreach (var (variant, level, into, required) in new[] { ("large-level", 999, level999 - 1, level999), ("large-xp", 99, 12345, 15000) })
                    {
                        var xp = $"{into} / {required}";
                        var levelText = fixture.Localizer["CyberDefense_PlayerLevelDisplay", level];
                        var large = Regex.Replace(html, @"(<span class=""player-xp-value"">)[^<]*(</span>)", m => m.Groups[1].Value + xp + m.Groups[2].Value);
                        large = Regex.Replace(large, @"(<span class=""player-level-label"">)[^<]*(</span>)", m => m.Groups[1].Value + levelText + m.Groups[2].Value);
                        Assert.Contains(xp, large);
                        Add(variant, large, new(capacity, active, units, xp, radar));
                    }
                }

                void Add(string variant, string markup, StatusExpectation expectation)
                {
                    foreach (var (viewportName, width, height) in Viewports)
                    {
                        foreach (var (theme, largeText) in Themes)
                        {
                            var id = $"{viewportName}/{language}/{theme}{(largeText ? "-large" : string.Empty)}/{variant}";
                            scenarios.Add(new(id, "status", width, height, language, theme, largeText, markup, null));
                            expectations[id] = expectation;
                        }
                    }
                }
            }
        }

        var results = await RunAsync(scenarios, reducedMotion);
        var violations = new List<string>();
        foreach (var scenario in scenarios)
        {
            CheckStatus(scenario.Id, results[scenario.Id], expectations[scenario.Id], violations);
        }
        CheckMathGeometry(scenarios, results, violations);
        foreach (var id in scenarios.Select(s => s.Id).Where(id => (id.EndsWith("/large-xp", StringComparison.Ordinal) || id.EndsWith("/large-level", StringComparison.Ordinal)) && id.Contains("narrow-320x640", StringComparison.Ordinal)))
        {
            LogStatus(id, results[id]);
        }
        Assert.True(violations.Count == 0, $"{violations.Count} rendered player status violation(s):\n" + string.Join("\n", violations.Take(400)));
    }

    private static readonly string[] StatusParts =
    [
        ".xp-progress-icon", ".player-level-label", ".player-xp-bar", ".player-xp-value", ".shield-badge", ".shield-count-text",
        ".shield-recharge-bar", ".shield-recharge-value", ".player-hp-icon", ".player-hp-bar", ".player-hp-value"
    ];

    private static void CheckStatus(string id, JsonElement probe, StatusExpectation expected, List<string> violations)
    {
        var rects = probe.GetProperty("rects");
        bool Has(string selector) => rects.GetProperty(selector).ValueKind == JsonValueKind.Object;
        double R(string selector, string edge) => rects.GetProperty(selector).GetProperty(edge).GetDouble();
        double Clip(string selector) => probe.GetProperty("clip").TryGetProperty(selector, out var v) ? v.GetDouble() : 0;
        bool Overflows(string selector) => probe.GetProperty("textOverflow").TryGetProperty(selector, out var v) && v.GetBoolean();
        string Text(string selector) => probe.GetProperty("text").TryGetProperty(selector, out var v) ? v.GetString() ?? string.Empty : string.Empty;
        bool Inside(string inner, string outer) => Has(inner) && Has(outer) &&
            R(inner, "left") >= R(outer, "left") - 0.5 && R(inner, "right") <= R(outer, "right") + 0.5 &&
            R(inner, "top") >= R(outer, "top") - 0.5 && R(inner, "bottom") <= R(outer, "bottom") + 0.5;

        var required = new[] { ".player-shield-panel", ".shield-status-column", ".player-xp-cluster", ".player-status-heading", ".player-hp-cluster",
            ".xp-progress-icon", ".player-level-label", ".player-xp-bar", ".player-xp-value", ".player-hp-icon", ".player-hp-bar", ".player-hp-value" };
        var missing = required.Where(selector => !Has(selector)).ToArray();
        foreach (var selector in missing) violations.Add($"{id}: {selector} missing");
        if (missing.Length > 0) return;
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow");

        // Hierarchy: Level/XP on top, the shield row in the middle, HP at the bottom left.
        if (!(R(".player-xp-cluster", "bottom") <= R(".player-status-heading", "top") + 0.5 && R(".player-status-heading", "bottom") <= R(".player-hp-cluster", "top") + 0.5))
            violations.Add($"{id}: rows are not stacked XP → shields → HP (tops {R(".player-xp-cluster", "top"):0.#}, {R(".player-status-heading", "top"):0.#}, {R(".player-hp-cluster", "top"):0.#})");
        var columnLeft = R(".shield-status-column", "left");
        if (Math.Abs(R(".player-hp-icon", "left") - columnLeft) > 1) violations.Add($"{id}: HP heart is not at the left edge of the status column");
        if (R(".player-hp-icon", "right") > R(".player-hp-bar", "left") + 0.5) violations.Add($"{id}: HP heart is not left of the HP meter");
        if (R(".xp-progress-icon", "right") > R(".player-level-label", "left") + 0.5) violations.Add($"{id}: XP progress icon is not before the level");

        // Shields: icon and active / capacity whenever capacity exists; recharge only while a shield is down.
        if (Has(".shield-badges-row") != expected.Capacity > 0) violations.Add($"{id}: shield status {(expected.Capacity > 0 ? "missing for an earned capacity" : "shown without capacity")}");
        if (expected.Capacity > 0)
        {
            if (!Has(".shield-badge") || !Has(".shield-count-text")) { violations.Add($"{id}: shield icon or count missing"); return; }
            if (Text(".shield-count-text") != $"{expected.Active} / {expected.Capacity}") violations.Add($"{id}: shield count '{Text(".shield-count-text")}', expected '{expected.Active} / {expected.Capacity}'");
            if (Math.Abs(R(".shield-badge", "left") - columnLeft) > 1) violations.Add($"{id}: shield icon is not at the left edge of the status column");
            if (R(".shield-badge", "right") > R(".shield-count-text", "left") + 0.5) violations.Add($"{id}: shield count is not beside its icon");
        }
        var recharging = expected.Capacity > 0 && expected.Active < expected.Capacity;
        if (Has(".shield-recharge-bar") != recharging) violations.Add($"{id}: recharge meter {(recharging ? "missing while a shield is down" : "shown without a downed shield")}");
        if (Has(".shield-recharge-value") != recharging) violations.Add($"{id}: recharge units {(recharging ? "missing" : "shown")}");
        if (recharging && Has(".shield-recharge-bar") && Has(".shield-recharge-value"))
        {
            if (!Text(".shield-recharge-value").EndsWith($"{expected.RechargeUnits} / {CyberDefenseFirewallPolicy.RechargeThresholdUnits}", StringComparison.Ordinal))
                violations.Add($"{id}: recharge units '{Text(".shield-recharge-value")}', expected {expected.RechargeUnits} / {CyberDefenseFirewallPolicy.RechargeThresholdUnits}");
            if (R(".shield-count-text", "right") > R(".shield-recharge-bar", "left") + 0.5 || R(".shield-recharge-bar", "right") > R(".shield-recharge-value", "left") + 0.5)
                violations.Add($"{id}: shield count, recharge meter and recharge units are not in reading order");
        }

        // Compact aligned meters: same start and width, at most ~75% of the status column and ~half where it fits.
        var columnWidth = R(".shield-status-column", "width");
        var bars = new[] { ".player-xp-bar", ".player-hp-bar", ".shield-recharge-bar" }.Where(Has).ToArray();
        foreach (var bar in bars)
        {
            var width = R(bar, "width");
            if (width > 0.75 * columnWidth + 0.5) violations.Add($"{id}: {bar} takes {width / columnWidth:P0} of the status column");
            if (width < 24) violations.Add($"{id}: {bar} collapsed to {width:0} px");
            if (Math.Abs(R(bar, "left") - R(".player-xp-bar", "left")) > 1) violations.Add($"{id}: {bar} does not start with the XP meter");
            if (Math.Abs(width - R(".player-xp-bar", "width")) > 1) violations.Add($"{id}: {bar} width {width:0} differs from the XP meter {R(".player-xp-bar", "width"):0}");
            if (Math.Abs(R(bar, "height") - R(".player-xp-bar", "height")) > 0.5) violations.Add($"{id}: {bar} height differs from the XP meter");
        }
        var isStress = id.EndsWith("/large-xp", StringComparison.Ordinal) || id.EndsWith("/large-level", StringComparison.Ordinal);
        // At enlarged text on narrow phones the numbers take precedence; the meters then only keep a usable minimum.
        if (!isStress && !id.Contains("-large/", StringComparison.Ordinal) && R(".player-xp-bar", "width") < 0.4 * columnWidth - 0.5) violations.Add($"{id}: meters only {R(".player-xp-bar", "width") / columnWidth:P0} of the status column");

        // Every value readable: present, unclipped, not overflowing, inside the panel, never overlapping anything else.
        if (Text(".player-xp-value") != expected.XpText) violations.Add($"{id}: XP shows '{Text(".player-xp-value")}', expected '{expected.XpText}'");
        var present = StatusParts.Where(Has).ToArray();
        foreach (var part in present)
        {
            if (Clip(part) < 0.99) violations.Add($"{id}: {part} clipped ({Clip(part):0.00})");
            if (Overflows(part)) violations.Add($"{id}: {part} text overflows its box");
            if (!Inside(part, ".player-shield-panel")) violations.Add($"{id}: {part} outside the player panel");
        }
        var withRadar = Has(".tactical-radar-widget") ? present.Append(".tactical-radar-widget").ToArray() : present;
        if (Has(".tactical-radar-widget") != expected.Radar) violations.Add($"{id}: radar presence {Has(".tactical-radar-widget")}, expected {expected.Radar}");
        for (var i = 0; i < withRadar.Length; i++)
        {
            for (var j = i + 1; j < withRadar.Length; j++)
            {
                var a = withRadar[i];
                var b = withRadar[j];
                if (R(a, "left") < R(b, "right") - 0.5 && R(b, "left") < R(a, "right") - 0.5 && R(a, "top") < R(b, "bottom") - 0.5 && R(b, "top") < R(a, "bottom") - 0.5)
                    violations.Add($"{id}: {a} overlaps {b}");
            }
        }
    }

    private void LogStatus(string id, JsonElement probe)
    {
        var rects = probe.GetProperty("rects");
        string F(string selector)
        {
            var r = rects.GetProperty(selector);
            return r.ValueKind == JsonValueKind.Object
                ? $"{selector}=({r.GetProperty("left").GetDouble():0.#},{r.GetProperty("top").GetDouble():0.#} {r.GetProperty("width").GetDouble():0.#}x{r.GetProperty("height").GetDouble():0.#})"
                : selector + "=none";
        }
        _output.WriteLine($"{id}: " + string.Join(' ', new[] { ".player-shield-panel", ".shield-status-column", ".player-xp-cluster", ".player-xp-bar", ".player-xp-value",
            ".player-status-heading", ".shield-recharge-bar", ".player-hp-cluster", ".player-hp-bar", ".tactical-radar-widget" }.Select(F)));
    }

    // =========================================================================
    // TUTORIAL OVER THE PAUSED GAME
    // =========================================================================

    [Fact(Timeout = 600000)]
    public async Task TutorialFromTheMenu_RenderedInChromium_ShowsTheInertGameplayFieldBehindEveryStep()
    {
        string[] targets = ["story-intro", "enemy-hud", "expression-keypad", "system-integrity"];
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        var expectedTarget = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            await using var f = await MenuFixture.CreateAsync(_menuSuite, new(Language: language));
            await f.StartAsync();
            await f.OpenMenuAsync();
            await f.InvokeAsync("OpenTutorialFromMenu");
            Assert.True(f.IsTutorialOpen);
            Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
            var tutorial = f.Field<object>("_tutorialComponent");
            for (var step = 0; step < targets.Length; step++)
            {
                if (step > 0) await f.DispatchAsync(() => (Task)tutorial.GetType().GetMethod("NextAsync")!.Invoke(tutorial, null)!);
                var html = await f.RenderedHtmlAsync();
                foreach (var (viewportName, width, height) in Viewports.Take(3))
                {
                    foreach (var (theme, largeText) in new[] { ("dark", false), ("light", true) })
                    {
                        var id = $"{viewportName}/{language}/{theme}{(largeText ? "-large" : string.Empty)}/{targets[step]}";
                        scenarios.Add(new(id, "tutorial", width, height, language, theme, largeText, html, null));
                        expectedTarget[id] = targets[step];
                    }
                }
            }
        }

        var results = await RunAsync(scenarios, reducedMotion: false);
        var violations = new List<string>();
        foreach (var scenario in scenarios)
        {
            var id = scenario.Id;
            var probe = results[id];
            if (!probe.GetProperty("present").GetBoolean()) { violations.Add($"{id}: tutorial missing"); continue; }
            if (probe.GetProperty("dialogCount").GetInt32() != 1) violations.Add($"{id}: {probe.GetProperty("dialogCount").GetInt32()} dialogs, expected only the tutorial");
            if (probe.GetProperty("menuPresent").GetBoolean()) violations.Add($"{id}: the main menu is stacked under the tutorial instead of the game field");
            if (probe.GetProperty("activeTarget").GetString() != expectedTarget[id]) violations.Add($"{id}: spotlight target {probe.GetProperty("activeTarget")}");
            if (expectedTarget[id] != "story-intro" && probe.GetProperty("target").ValueKind != JsonValueKind.Object)
                violations.Add($"{id}: spotlight target '{expectedTarget[id]}' is not rendered");
            if (probe.GetProperty("headingVisible").GetDouble() < 0.99) violations.Add($"{id}: tutorial heading not visible");
            if (probe.GetProperty("practiceSurfaceHit").GetBoolean()) violations.Add($"{id}: the practice surface can be hit under the tutorial");
            if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow");
            foreach (var part in probe.GetProperty("field").EnumerateObject())
            {
                if (part.Value.ValueKind != JsonValueKind.Object) { violations.Add($"{id}: gameplay context {part.Name} not rendered behind the tutorial"); continue; }
                if (!part.Value.GetProperty("displayed").GetBoolean()) violations.Add($"{id}: gameplay context {part.Name} hidden");
                if (!part.Value.GetProperty("inert").GetBoolean()) violations.Add($"{id}: gameplay context {part.Name} is not inert");
            }
            var hud = probe.GetProperty("field").GetProperty(".enemy-battle-scene");
            if (hud.ValueKind == JsonValueKind.Object && hud.GetProperty("visible").GetDouble() < 0.99) violations.Add($"{id}: the opponent scene is not on screen behind the tutorial");
        }
        Assert.True(violations.Count == 0, $"{violations.Count} rendered tutorial violation(s):\n" + string.Join("\n", violations.Take(400)));
    }

    // =========================================================================
    // SHARED
    // =========================================================================

    private static void AddScenarios(List<ChromiumLayoutHarness.Scenario> scenarios, string probe, string language, string variant, string html,
        IEnumerable<(string Name, int Width, int Height)> viewports)
    {
        foreach (var (viewportName, width, height) in viewports)
        {
            foreach (var (theme, largeText) in Themes)
            {
                scenarios.Add(new($"{viewportName}/{language}/{theme}{(largeText ? "-large" : string.Empty)}/{variant}", probe, width, height, language, theme, largeText, html, null));
            }
        }
    }

    // Within one viewport, language, theme and text size the math workspace is identical for every variant, and it is
    // where it was before Issue #134 and on main before Issue #136.
    private static void CheckMathGeometry(List<ChromiumLayoutHarness.Scenario> scenarios, Dictionary<string, JsonElement> results, List<string> violations)
    {
        foreach (var group in scenarios.GroupBy(s => s.Id[..s.Id.LastIndexOf('/')]))
        {
            var reference = results[group.First().Id];
            foreach (var other in group.Skip(1))
            {
                var probe = results[other.Id];
                foreach (var selector in ProtectedSelectors)
                {
                    var a = reference.GetProperty("rects").GetProperty(selector);
                    var b = probe.GetProperty("rects").GetProperty(selector);
                    if (a.ValueKind != JsonValueKind.Object || b.ValueKind != JsonValueKind.Object) { violations.Add($"{other.Id}: {selector} missing"); continue; }
                    foreach (var edge in new[] { "top", "left", "width", "height" })
                    {
                        if (Math.Abs(a.GetProperty(edge).GetDouble() - b.GetProperty(edge).GetDouble()) > 0.5) violations.Add($"{other.Id}: {selector} {edge} moved");
                    }
                }
                var keys = reference.GetProperty("keypad").EnumerateArray().ToArray();
                var otherKeys = probe.GetProperty("keypad").EnumerateArray().ToArray();
                if (keys.Length != otherKeys.Length || keys.Length < 11) { violations.Add($"{other.Id}: keypad key count {otherKeys.Length}"); continue; }
                for (var i = 0; i < keys.Length; i++)
                {
                    foreach (var edge in new[] { "top", "left", "width", "height" })
                    {
                        if (Math.Abs(keys[i].GetProperty(edge).GetDouble() - otherKeys[i].GetProperty(edge).GetDouble()) > 0.5) violations.Add($"{other.Id}: keypad key {i} {edge} moved");
                    }
                }
            }

            var viewport = group.Key[..group.Key.IndexOf('/')];
            var (expressionTop, keyTop) = BaseMathGeometry[(viewport, group.Key.EndsWith("-large", StringComparison.Ordinal))];
            var measuredExpression = reference.GetProperty("rects").GetProperty(".expression-row").GetProperty("top").GetDouble();
            var measuredKey = reference.GetProperty("keypad")[0].GetProperty("top").GetDouble();
            if (Math.Abs(measuredExpression - expressionTop) > 1) violations.Add($"{group.Key}: expression row top {measuredExpression:0.##} moved from {expressionTop:0.##}");
            if (Math.Abs(measuredKey - keyTop) > 1) violations.Add($"{group.Key}: keypad top {measuredKey:0.##} moved from {keyTop:0.##}");
        }
    }

    private async Task<Dictionary<string, JsonElement>> RunAsync(List<ChromiumLayoutHarness.Scenario> scenarios, bool reducedMotion)
    {
        var results = await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion, CreateTempDirectory());
        foreach (var result in results) Assert.Equal(reducedMotion, result.GetProperty("reducedMotion").GetBoolean());
        return results.ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("probe"), StringComparer.Ordinal);
    }

    private static async Task SeedRunAsync(SqliteGameplayStore store, int sector, int opponentIndex, int opponentHp)
    {
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE gameplay_run_state SET sector = @sector, opponent_index = @index, opponent_current_hp = @hp WHERE id = 1;";
            command.Parameters.AddWithValue("@sector", sector);
            command.Parameters.AddWithValue("@index", opponentIndex);
            command.Parameters.AddWithValue("@hp", opponentHp);
            await command.ExecuteNonQueryAsync();
        });
    }

    private static async Task SeedShieldAsync(SqliteGameplayStore store, int capacity, int active, int rechargeUnits)
    {
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            // The purchased rank is already activated for this run, so starting the run does not refill the shields.
            command.CommandText = "UPDATE gameplay_progression SET activated_firewall_rank = @capacity WHERE id = 1; " +
                                  "UPDATE gameplay_run_state SET shield_capacity = @capacity, active_shields = @active, shield_recharge_units = @units WHERE id = 1;";
            command.Parameters.AddWithValue("@capacity", capacity);
            command.Parameters.AddWithValue("@active", active);
            command.Parameters.AddWithValue("@units", rechargeUnits);
            await command.ExecuteNonQueryAsync();
        });
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue136Layout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
