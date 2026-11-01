namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MathFirst.Application.Gameplay;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit.Abstractions;

/// <summary>
/// Issue #134: rendered Chromium regressions for the combat HUD. Markup comes from the real Home page and the real HUD
/// component; geometry and animation timing from the production CSS in headless Chromium.
/// Synthetic UI evidence only; it is not Android WebView, TalkBack or physical-device acceptance.
/// </summary>
public sealed class Issue134RenderedLayoutTests : IDisposable
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

    // The protected math workspace: nothing in the HUD may move these within a question.
    private static readonly string[] ProtectedSelectors = [".expression-row", ".answer-input", ".numeric-keypad", ".operation-progress-hud", ".mathfirst-header"];

    // Expression row top and first keypad key top before Issue #134 (main@4529cd9, this harness, identical in EN/DE/RU,
    // light and dark). Key: viewport, 130% root text.
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

    private const string StressName ="Квантово-фазовый взломщик фаервола Omega";
    private const string StressDescription =
        "Synthetic stress text: a deliberately long localized opponent description that is far longer than any shipped copy and must stay bounded to whole lines.";

    private readonly ITestOutputHelper _output;
    private readonly Issue122HomeHudTests _suite = new();
    private readonly List<string> _tempDirectories = [];

    public Issue134RenderedLayoutTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _suite.Dispose();
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CombatHud_RenderedInChromium_IsReadableUnclippedAndNeverMovesTheMathWorkspace(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        var expectations = new Dictionary<string, (string HpText, bool Radar)>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            var variants = new (string Name, Issue122HomeHudTests.HomeOptions Options)[]
            {
                // A fresh learner: Sector 1, first opponent at 2 / 2 HP, no Critical Strike rank.
                ("normal-rank0", new(Language: language, PlayerLevel: 1)),
                // A purchased rank 1 and a damaged opponent.
                ("normal-rank1", new(Language: language, PlayerLevel: 12, CriticalStrikePurchases: 1,
                    SeedRunState: store => SeedRunAsync(store, 1, 0, 1))),
                // The Sector 1 boss at full HP, Critical Strike affordable but not bought.
                ("boss", new(Language: language, PlayerLevel: 12,
                    SeedRunState: store => SeedRunAsync(store, 1, CyberDefenseScalingPolicy.GetBossIndex(1), CyberDefenseScalingPolicy.GetBossMaxHp(1)))),
                // Late run: Sector 7, Firewall shields, rank 2 and the longest shipped opponent copy.
                ("late", new(Language: language, PlayerLevel: 50, FirewallPurchases: 1, CriticalStrikePurchases: 2,
                    SeedRunState: store => SeedRunAsync(store, 7, 4, CyberDefenseScalingPolicy.GetNormalMaxHp(7)))),
                // A downed Firewall shield with authoritative recharge progress beside the shield slot.
                ("shield-down", new(Language: language, PlayerLevel: 20, FirewallPurchases: 1,
                    SeedRunState: store => SeedShieldAsync(store, active: 0, rechargeUnits: 12))),
                // Sector 4 boss: the sector-boss artwork tier with a multi-digit HP value.
                ("sector-boss", new(Language: language, PlayerLevel: 12,
                    SeedRunState: store => SeedRunAsync(store, 4, CyberDefenseScalingPolicy.GetBossIndex(4), CyberDefenseScalingPolicy.GetBossMaxHp(4))))
            };

            foreach (var (name, options) in variants)
            {
                await using var fixture = await Issue122HomeHudTests.HomeFixture.CreateAsync(_suite, options);
                await fixture.InvokeHomeAsync("StartOrResumePractice");
                var html = await fixture.GetHtmlAsync();
                var hpText = Regex.Match(html, @"<div class=""meter-bar enemy-hp-bar[^>]*aria-valuetext=""([^""]*)""").Groups[1].Value;
                Assert.False(string.IsNullOrEmpty(hpText));
                var radar = options.CriticalStrikePurchases > 0;
                AddScenarios(scenarios, expectations, language, name, html, hpText, radar);

                if (name == "normal-rank0")
                {
                    // Synthetic long localized strings, beyond any shipped copy.
                    var nameKey = fixture.Localizer["CyberDefense_Opponent_GlitchDrone"];
                    var descKey = fixture.Localizer["CyberDefense_Opponent_GlitchDrone_Desc"];
                    var stressed = html.Replace(">" + nameKey + "<", ">" + StressName + "<", StringComparison.Ordinal)
                        .Replace(">" + descKey + "<", ">" + StressDescription + "<", StringComparison.Ordinal);
                    Assert.Contains(StressName, stressed);
                    Assert.Contains(StressDescription, stressed);
                    AddScenarios(scenarios, expectations, language, "stress", stressed, hpText, radar);
                }
            }
        }

        var results = (await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion, CreateTempDirectory()))
            .ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var scenario in scenarios)
        {
            var result = results[scenario.Id];
            Assert.Equal(reducedMotion, result.GetProperty("reducedMotion").GetBoolean());
            CheckScenario(scenario, result.GetProperty("probe"), expectations[scenario.Id], reducedMotion, violations);
        }

        // Within one viewport, language, theme and text size the math workspace is identical for every opponent,
        // boss, rank, shield and string-length variant: nothing a question can change in the HUD moves it.
        foreach (var group in scenarios.GroupBy(s => GroupKey(s.Id)))
        {
            var reference = group.First();
            var referenceProbe = results[reference.Id].GetProperty("probe");
            foreach (var other in group.Skip(1))
            {
                CompareMathGeometry(reference.Id, referenceProbe, other.Id, results[other.Id].GetProperty("probe"), violations);
            }
            if (group.Key.Contains("/en/", StringComparison.Ordinal) || group.Key.EndsWith("-large", StringComparison.Ordinal))
            {
                Log(reference.Id, referenceProbe);
            }

            // And it is where it was before Issue #134 (measured with this harness at main@4529cd9).
            var viewport = group.Key[..group.Key.IndexOf('/')];
            var (expressionTop, keyTop) = BaseMathGeometry[(viewport, group.Key.EndsWith("-large", StringComparison.Ordinal))];
            var measuredExpression = referenceProbe.GetProperty("rects").GetProperty(".expression-row").GetProperty("top").GetDouble();
            var measuredKey = referenceProbe.GetProperty("keypad")[0].GetProperty("top").GetDouble();
            if (Math.Abs(measuredExpression - expressionTop) > 1) violations.Add($"{group.Key}: expression row top {measuredExpression:0.##} moved from the pre-#134 {expressionTop:0.##}");
            if (Math.Abs(measuredKey - keyTop) > 1) violations.Add($"{group.Key}: keypad top {measuredKey:0.##} moved from the pre-#134 {keyTop:0.##}");
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered combat HUD violation(s):\n" + string.Join("\n", violations.Take(400)));
    }

    [Fact(Timeout = 300000)]
    public async Task RadarArc_RenderedInChromium_FollowsActiveTimeThroughDigitEntryPauseAndResume()
    {
        const long window = 6000;
        await using var hud = await Issue134HudPresentationTests.HudRender.CreateAsync();
        var model = CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.InitialRun());

        async Task<string> RenderAsync(long elapsed, bool active, string task = "add:60+7_4200")
        {
            await hud.SetAsync(model, rank: 1, elapsed: elapsed, windowMs: window, taskKey: task, timingActive: active);
            return await hud.HtmlAsync();
        }

        // (wall time since the previous render, active elapsed, timing active): first and second digit of a
        // multi-digit answer, a pause with time passing, a rerender while paused, resume and another digit.
        (long Advance, long Elapsed, bool Active, string Label)[] steps =
        [
            (1500, 1500, true, "first digit"),
            (700, 2200, true, "second digit"),
            (300, 2500, false, "pause"),
            (5000, 2500, false, "rerender while paused"),
            (0, 2500, true, "resume"),
            (1000, 3500, true, "digit after resume")
        ];

        var body = new StringBuilder(await RenderAsync(0, true));
        foreach (var step in steps)
        {
            var markup = await RenderAsync(step.Elapsed, step.Active);
            body.Append(CultureInfo.InvariantCulture, $"<template class=\"mf-radar-step\" data-advance-ms=\"{step.Advance}\">{markup}</template>");
        }

        var scenario = new ChromiumLayoutHarness.Scenario("radar/digit-pause-resume", "radar", 384, 744, "en", "dark", false, body.ToString(), null);
        var probe = (await ChromiumLayoutHarness.RunAsync([scenario], reducedMotion: false, CreateTempDirectory()))[0].GetProperty("probe");

        var initial = probe.GetProperty("initial");
        Assert.True(initial.GetProperty("present").GetBoolean());
        Assert.Equal(0d, initial.GetProperty("progress").GetDouble(), 2);

        var observed = probe.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(steps.Length, observed.Length);
        var violations = new List<string>();
        for (var i = 0; i < steps.Length; i++)
        {
            var expected = (double)steps[i].Elapsed / window;
            var actual = observed[i];
            var progress = actual.GetProperty("progress").ValueKind == JsonValueKind.Number ? actual.GetProperty("progress").GetDouble() : double.NaN;
            _output.WriteLine($"{steps[i].Label}: action={actual.GetProperty("action").GetString()} playState={actual.GetProperty("playState")} progress={progress:0.000} expected={expected:0.000}");
            if (!(Math.Abs(progress - expected) <= 0.01))
                violations.Add($"{steps[i].Label}: rendered arc progress {progress:0.000}, authoritative active time {expected:0.000} ({steps[i].Elapsed} of {window} ms)");
            var expectedState = steps[i].Active ? "running" : "paused";
            if (actual.GetProperty("playState").GetString() != expectedState)
                violations.Add($"{steps[i].Label}: arc animation is {actual.GetProperty("playState")}, expected {expectedState}");
        }
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact(Timeout = 300000)]
    public async Task RadarArc_UnderReducedMotion_IsStaticAndStillHiddenAtRankZero()
    {
        await using var hud = await Issue134HudPresentationTests.HudRender.CreateAsync();
        var model = CyberDefenseHudViewModel.FromRunState(CyberDefenseRunState.InitialRun());
        await hud.SetAsync(model, rank: 1, elapsed: 0, windowMs: 6000);
        var earned = await hud.HtmlAsync();
        await hud.SetAsync(model, rank: 0, elapsed: 0, windowMs: 6000);
        var unearned = await hud.HtmlAsync();

        var results = await ChromiumLayoutHarness.RunAsync(
        [
            new("radar/reduced-earned", "radar", 384, 744, "en", "dark", false, earned, null),
            new("radar/reduced-unearned", "radar", 384, 744, "en", "dark", false, unearned, null)
        ], reducedMotion: true, CreateTempDirectory());

        var earnedArc = results[0].GetProperty("probe").GetProperty("initial");
        Assert.True(earnedArc.GetProperty("present").GetBoolean());
        Assert.Equal(0, earnedArc.GetProperty("animations").GetInt32());
        Assert.False(results[1].GetProperty("probe").GetProperty("initial").GetProperty("present").GetBoolean());
    }

    private static void AddScenarios(List<ChromiumLayoutHarness.Scenario> scenarios, Dictionary<string, (string, bool)> expectations,
        string language, string variant, string html, string hpText, bool radar)
    {
        foreach (var (viewportName, width, height) in Viewports)
        {
            foreach (var (theme, largeText) in new[] { ("light", false), ("dark", false), ("light", true) })
            {
                var id = $"{viewportName}/{language}/{theme}{(largeText ? "-large" : string.Empty)}/{variant}";
                scenarios.Add(new(id, "combat", width, height, language, theme, largeText, html, null));
                expectations[id] = (hpText, radar);
            }
        }
    }

    private static string GroupKey(string id) => id[..id.LastIndexOf('/')];

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

    private static async Task SeedShieldAsync(SqliteGameplayStore store, int active, int rechargeUnits)
    {
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            // The purchased rank is already activated for this run, so starting the run does not refill the shield.
            command.CommandText = "UPDATE gameplay_progression SET activated_firewall_rank = 1 WHERE id = 1; " +
                                  "UPDATE gameplay_run_state SET shield_capacity = 1, active_shields = @active, shield_recharge_units = @units WHERE id = 1;";
            command.Parameters.AddWithValue("@active", active);
            command.Parameters.AddWithValue("@units", rechargeUnits);
            await command.ExecuteNonQueryAsync();
        });
    }

    private static void CheckScenario(ChromiumLayoutHarness.Scenario scenario, JsonElement probe, (string HpText, bool Radar) expected,
        bool reducedMotion, List<string> violations)
    {
        var id = scenario.Id;
        var rects = probe.GetProperty("rects");
        bool Has(string selector) => rects.GetProperty(selector).ValueKind == JsonValueKind.Object;
        double R(string selector, string edge) => rects.GetProperty(selector).GetProperty(edge).GetDouble();
        double Clip(string selector) => probe.GetProperty("clip").TryGetProperty(selector, out var v) ? v.GetDouble() : 0;
        bool Overflows(string selector) => probe.GetProperty("textOverflow").TryGetProperty(selector, out var v) && v.GetBoolean();
        string Text(string selector) => probe.GetProperty("text").TryGetProperty(selector, out var v) ? v.GetString() ?? string.Empty : string.Empty;
        bool Inside(string inner, string outer) => Has(inner) && Has(outer) &&
            R(inner, "left") >= R(outer, "left") - 0.5 && R(inner, "right") <= R(outer, "right") + 0.5 &&
            R(inner, "top") >= R(outer, "top") - 0.5 && R(inner, "bottom") <= R(outer, "bottom") + 0.5;

        var missing = new[] { ".enemy-battle-scene", ".scene-top-bar", ".scene-enemy-name", ".scene-sector-tag", ".scene-wave-tag",
                ".scene-stage", ".scene-enemy-desc", ".enemy-hp-cluster", ".enemy-hp-bar", ".enemy-hp-value", ".scene-artwork-wrapper",
                ".scene-opponent-image", ".player-shield-panel", ".player-hp-value", ".expression-row", ".answer-input", ".numeric-keypad" }
            .Where(selector => !Has(selector)).ToArray();
        foreach (var selector in missing) violations.Add($"{id}: {selector} missing");
        if (missing.Length > 0) return;

        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow (scrollWidth {probe.GetProperty("scrollWidth").GetDouble():0})");

        // Opponent name: grouped with Sector and wave at the top, wrapped (never cut) to at most two lines.
        if (!Inside(".scene-enemy-name", ".scene-top-bar")) violations.Add($"{id}: opponent name is not in the top Sector/wave grouping");
        foreach (var selector in new[] { ".scene-enemy-name", ".scene-sector-tag", ".scene-wave-tag" })
        {
            if (Clip(selector) < 0.99) violations.Add($"{id}: {selector} clipped ({Clip(selector):0.00})");
            if (Overflows(selector)) violations.Add($"{id}: {selector} text overflows its box");
            if (!Inside(selector, ".scene-top-bar")) violations.Add($"{id}: {selector} outside the top bar");
        }
        // The top bar belongs to the dark battle screen in both themes: its text keeps WCAG AA contrast (4.5:1).
        foreach (var part in new[] { "name", "sector", "wave" })
        {
            var ratio = probe.GetProperty("contrasts").GetProperty(part);
            if (ratio.ValueKind != JsonValueKind.Number) violations.Add($"{id}: top bar {part} contrast not measurable (translucent background)");
            else if (ratio.GetDouble() < 4.5) violations.Add($"{id}: top bar {part} contrast {ratio.GetDouble():0.0}:1 below 4.5:1");
        }

        var nameLines = probe.GetProperty("lines").GetProperty(".scene-enemy-name");
        var nameLineCount = nameLines.GetProperty("clientHeight").GetDouble() / nameLines.GetProperty("lineHeight").GetDouble();
        if (nameLineCount > 2.05) violations.Add($"{id}: opponent name shows {nameLineCount:0.0} lines");
        // Shipped names are never truncated; only the synthetic stress name may reach the two-line bound.
        var isStress = id.EndsWith("/stress", StringComparison.Ordinal);
        if (!isStress && nameLines.GetProperty("scrollHeight").GetDouble() > nameLines.GetProperty("clientHeight").GetDouble() + 1) violations.Add($"{id}: opponent name is truncated");
        if (!isStress && nameLineCount > 1.05) violations.Add($"{id}: shipped opponent name wraps to {nameLineCount:0.0} lines");

        // Description: whole visible lines, fully inside the stage. Issue #136 lets it use every whole line that fits above
        // the HP row instead of two (Issue136RenderedLayoutTests asserts it is not cut while it fits).
        var desc = probe.GetProperty("lines").GetProperty(".scene-enemy-desc");
        var descHeight = desc.GetProperty("clientHeight").GetDouble();
        var lineHeight = desc.GetProperty("lineHeight").GetDouble();
        var visibleLines = descHeight / lineHeight;
        if (visibleLines < 0.95) violations.Add($"{id}: description shows {visibleLines:0.00} lines");
        if (Math.Abs(visibleLines - Math.Round(visibleLines)) * lineHeight > 1.5) violations.Add($"{id}: description cuts a line ({visibleLines:0.00} lines visible)");
        if (Clip(".scene-enemy-desc") < 0.99) violations.Add($"{id}: description clipped by the stage ({Clip(".scene-enemy-desc"):0.00})");
        if (!Inside(".scene-enemy-desc", ".scene-stage")) violations.Add($"{id}: description outside the stage");

        // Numeric current / maximum HP at the bottom of the opponent section, below the description.
        if (Text(".enemy-hp-value") != expected.HpText) violations.Add($"{id}: opponent HP shows '{Text(".enemy-hp-value")}', expected '{expected.HpText}'");
        foreach (var selector in new[] { ".enemy-hp-value", ".enemy-hp-bar" })
        {
            if (Clip(selector) < 0.99) violations.Add($"{id}: {selector} clipped ({Clip(selector):0.00})");
            if (!Inside(selector, ".scene-stage")) violations.Add($"{id}: {selector} outside the stage");
        }
        if (Overflows(".enemy-hp-value")) violations.Add($"{id}: opponent HP text overflows");
        if (R(".enemy-hp-bar", "width") < 40) violations.Add($"{id}: opponent HP bar collapsed ({R(".enemy-hp-bar", "width"):0}px)");
        if (R(".enemy-hp-cluster", "top") < R(".scene-enemy-desc", "bottom") - 0.5) violations.Add($"{id}: HP row is not below the description");
        if (R(".enemy-hp-cluster", "bottom") < R(".scene-stage", "bottom") - 16) violations.Add($"{id}: HP row is not at the bottom of the opponent section");

        // Artwork: a clean area at least as large as the former box, not cut by the stage, not under any text.
        var formerBox = Math.Clamp(0.16 * scenario.Height, 90, 140);
        if (R(".scene-artwork-wrapper", "height") < Math.Min(formerBox, R(".scene-stage", "height")) - 0.5)
            violations.Add($"{id}: artwork area {R(".scene-artwork-wrapper", "height"):0}px is smaller than the former {formerBox:0}px box");
        var imageClip = Clip(".scene-opponent-image");
        if (imageClip < (reducedMotion ? 0.98 : 0.9)) violations.Add($"{id}: opponent artwork cut by the stage ({imageClip:0.00} visible)");
        foreach (var overlap in probe.GetProperty("overlaps").EnumerateArray())
        {
            var pair = overlap.GetProperty("pair").GetString()!;
            var depth = overlap.GetProperty("depth").GetDouble();
            var tolerance = !reducedMotion && pair.StartsWith(".scene-opponent-image", StringComparison.Ordinal) ? 6 : 0.5;
            if (depth > tolerance) violations.Add($"{id}: overlap {pair} ({depth:0.0}px)");
        }

        // Player panel: one visible HP value, never cut; radar only for a purchased rank.
        if (Clip(".player-hp-value") < 0.99) violations.Add($"{id}: player HP value clipped");
        if (Overflows(".player-hp-value")) violations.Add($"{id}: player HP text overflows");
        if (Has(".tactical-radar-widget") != expected.Radar) violations.Add($"{id}: radar {(expected.Radar ? "missing for a purchased rank" : "advertised without a purchased rank")}");
        if (!Inside(".player-hp-value", ".player-shield-panel")) violations.Add($"{id}: player HP value outside its panel");
        var shieldDown = id.EndsWith("/shield-down", StringComparison.Ordinal);
        if (Has(".shield-recharge-bar") != shieldDown) violations.Add($"{id}: shield recharge progress {(shieldDown ? "missing while a shield is down" : "shown without a downed shield")}");
        if (shieldDown && Has(".shield-recharge-bar") && (Clip(".shield-recharge-bar") < 0.99 || R(".shield-recharge-bar", "width") < 20 || !Inside(".shield-recharge-bar", ".player-shield-panel")))
            violations.Add($"{id}: shield recharge progress clipped or collapsed ({R(".shield-recharge-bar", "width"):0}px)");
    }

    private static void CompareMathGeometry(string referenceId, JsonElement reference, string id, JsonElement probe, List<string> violations)
    {
        foreach (var selector in ProtectedSelectors.Concat([".training-top-region"]))
        {
            var a = reference.GetProperty("rects").GetProperty(selector);
            var b = probe.GetProperty("rects").GetProperty(selector);
            if (a.ValueKind != JsonValueKind.Object || b.ValueKind != JsonValueKind.Object) { violations.Add($"{id}: {selector} missing in comparison"); continue; }
            foreach (var edge in new[] { "top", "left", "width", "height" })
            {
                var delta = Math.Abs(a.GetProperty(edge).GetDouble() - b.GetProperty(edge).GetDouble());
                if (delta > 0.5) violations.Add($"{id}: {selector} {edge} differs by {delta:0.0}px from {referenceId}");
            }
        }
        var keys = reference.GetProperty("keypad").EnumerateArray().ToArray();
        var other = probe.GetProperty("keypad").EnumerateArray().ToArray();
        if (keys.Length != other.Length || keys.Length < 11) { violations.Add($"{id}: keypad key count {other.Length} vs {keys.Length}"); return; }
        for (var i = 0; i < keys.Length; i++)
        {
            foreach (var edge in new[] { "top", "left", "width", "height" })
            {
                if (Math.Abs(keys[i].GetProperty(edge).GetDouble() - other[i].GetProperty(edge).GetDouble()) > 0.5)
                    violations.Add($"{id}: keypad key {i} {edge} differs from {referenceId}");
            }
        }
    }

    private void Log(string id, JsonElement probe)
    {
        var rects = probe.GetProperty("rects");
        string F(string selector)
        {
            var r = rects.GetProperty(selector);
            return r.ValueKind == JsonValueKind.Object
                ? $"{selector}=({r.GetProperty("left").GetDouble():0.#},{r.GetProperty("top").GetDouble():0.#} {r.GetProperty("width").GetDouble():0.#}x{r.GetProperty("height").GetDouble():0.#})"
                : selector + "=none";
        }
        var firstKey = probe.GetProperty("keypad").EnumerateArray().FirstOrDefault();
        _output.WriteLine($"{id}: " + string.Join(' ', new[] { ".enemy-battle-scene", ".scene-top-bar", ".scene-stage", ".scene-artwork-wrapper",
            ".scene-opponent-image", ".scene-enemy-desc", ".player-shield-panel", ".training-top-region", ".expression-row", ".answer-input", ".numeric-keypad" }.Select(F))
            + (firstKey.ValueKind == JsonValueKind.Object ? $" key0.top={firstKey.GetProperty("top").GetDouble():0.##}" : string.Empty));
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue134Layout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
