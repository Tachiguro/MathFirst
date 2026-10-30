namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.Json;
using System.Text.RegularExpressions;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using Xunit.Abstractions;

/// <summary>
/// Issue #122: rendered Chromium regressions for the simplified header, the Level/XP row and the Stage strip.
/// Markup comes from the real Home page; geometry from the production CSS in headless Chromium.
/// Each scenario is compared with the same page minus the new XP row, which is the pre-#122 answer geometry,
/// so the XP row can never move the question, answer field, keypad, header or Stage strip.
/// Synthetic UI evidence only; it is not Android WebView or physical-device acceptance.
/// </summary>
public sealed class Issue122RenderedLayoutTests : IDisposable
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("narrow-320x640", 320, 640),
        ("phone-384x744", 384, 744),
        ("short-320x480", 320, 480),
        ("landscape-744x384", 744, 384)
    ];

    private static readonly string[] Languages = ["en", "de", "ru"];

    private static readonly string[] GeometrySelectors =
    [
        ".mathfirst-header", ".upgrade-nav-btn", ".pause-practice-btn", ".settings-nav-btn", ".practice-layout",
        ".expression-row", ".answer-input", ".numeric-keypad", ".operation-progress-hud", ".player-shield-panel", ".player-hp-bar"
    ];

    // The player panel hosts the new row and vertically centres its content, so these two move internally by design.
    private static readonly string[] HostSelectors = [".player-shield-panel", ".player-hp-bar", ".practice-layout"];

    private readonly ITestOutputHelper _output;
    private readonly Issue122HomeHudTests _suite = new();
    private readonly List<string> _tempDirectories = [];

    public Issue122RenderedLayoutTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        _suite.Dispose();
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Theory(Timeout = 480000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveGameplayHud_RenderedInChromium_KeepsAnswerGeometryAndHasNoOverflowOverlapOrClipping(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        foreach (var language in Languages)
        {
            var variants = new (string Name, Issue122HomeHudTests.HomeOptions Options)[]
            {
                // Level 1, no XP, a single operation, no Skill Points: the smallest content.
                ("starter", new(Language: language, PlayerLevel: 1)),
                // A purchased Firewall adds the shield badge row to the player panel, its tallest content.
                ("shields", new(Language: language, PlayerLevel: 20, FirewallPurchases: 1)),
                // Wide synthetic values: three-digit level, large XP remainder, four operations, two-digit Skill Points.
                ("wide", new(Language: language, CumulativeXp: CyberDefenseLevelPolicy.GetCumulativeXpThreshold(120) + 98765,
                    Stage: CurriculumStage.Stage4_Division,
                    Bands: [(ArithmeticOperation.Addition, 11), (ArithmeticOperation.Subtraction, 11), (ArithmeticOperation.Multiplication, 11), (ArithmeticOperation.Division, 10)]))
            };

            foreach (var (name, options) in variants)
            {
                await using var fixture = await Issue122HomeHudTests.HomeFixture.CreateAsync(_suite, options);
                await fixture.InvokeHomeAsync("StartOrResumePractice");
                var withXp = await fixture.GetHtmlAsync();
                Assert.Contains("player-xp-cluster", withXp);
                var baseline = RemoveXpRow(withXp);

                foreach (var (viewportName, width, height) in Viewports)
                {
                    foreach (var (theme, largeText) in new[] { ("light", false), ("dark", false), ("light", true) })
                    {
                        var id = $"{viewportName}/{language}/{name}/{theme}{(largeText ? "-large" : string.Empty)}";
                        scenarios.Add(new(id + "/xp", "hud", width, height, language, theme, largeText, withXp, null));
                        scenarios.Add(new(id + "/base", "hud", width, height, language, theme, largeText, baseline, null));
                    }
                }
            }
        }

        var results = (await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion, CreateTempDirectory()))
            .ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var scenario in scenarios.Where(s => s.Id.EndsWith("/xp", StringComparison.Ordinal)))
        {
            var prefix = scenario.Id[..^3];
            var result = results[scenario.Id];
            Assert.Equal(reducedMotion, result.GetProperty("reducedMotion").GetBoolean());
            var probe = result.GetProperty("probe");
            var baseProbe = results[prefix + "/base"].GetProperty("probe");
            CheckScenario(prefix, probe, baseProbe, violations);
            if (prefix.Contains("/shields/", StringComparison.Ordinal))
                Assert.Contains("shield-badges-row", scenario.BodyMarkup);
            if (prefix.Contains("/en/wide/light", StringComparison.Ordinal) || prefix.Contains("/ru/wide/light-large", StringComparison.Ordinal) || prefix.Contains("/en/starter/light", StringComparison.Ordinal))
            {
                Log(prefix, probe);
            }
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered HUD layout violation(s):\n" + string.Join("\n", violations));
    }

    private static void CheckScenario(string id, JsonElement probe, JsonElement baseline, List<string> violations)
    {
        var rects = probe.GetProperty("rects");
        var clip = probe.GetProperty("clip");
        var overflowText = probe.GetProperty("textOverflow");

        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow (scrollWidth {probe.GetProperty("scrollWidth").GetInt32()})");
        foreach (var overlap in probe.GetProperty("overlaps").EnumerateArray()) violations.Add($"{id}: overlap {overlap.GetString()}");

        foreach (var selector in new[] { ".player-hp-bar", ".player-xp-bar", ".player-level-label", ".player-xp-value", ".player-xp-cluster" })
        {
            if (rects.GetProperty(selector).ValueKind != JsonValueKind.Object) { violations.Add($"{id}: {selector} missing"); continue; }
            if (clip.GetProperty(selector).GetDouble() < 0.99) violations.Add($"{id}: {selector} clipped ({clip.GetProperty(selector).GetDouble():0.00})");
        }
        foreach (var selector in new[] { ".player-level-label", ".player-xp-value", ".player-hp-value" })
        {
            if (overflowText.GetProperty(selector).GetBoolean()) violations.Add($"{id}: {selector} text overflows its box");
        }

        // The XP row sits below the HP row and spans no wider than the status column.
        var hpBar = rects.GetProperty(".player-hp-bar");
        var xpBar = rects.GetProperty(".player-xp-bar");
        if (xpBar.GetProperty("top").GetDouble() < hpBar.GetProperty("bottom").GetDouble() - 0.5) violations.Add($"{id}: XP bar is not below the HP bar");
        if (xpBar.GetProperty("width").GetDouble() < 24) violations.Add($"{id}: XP bar collapsed ({xpBar.GetProperty("width").GetDouble():0.0}px)");

        // Header actions: reachable, unclipped, uncovered and a usable touch target; the removed toggle leaves no gap or overlap.
        foreach (var selector in new[] { ".upgrade-nav-btn", ".pause-practice-btn", ".settings-nav-btn" })
        {
            var rect = rects.GetProperty(selector);
            if (rect.ValueKind != JsonValueKind.Object) { violations.Add($"{id}: {selector} missing"); continue; }
            if (clip.GetProperty(selector).GetDouble() < 0.99) violations.Add($"{id}: {selector} clipped");
            if (!probe.GetProperty("hit").GetProperty(selector).GetBoolean()) violations.Add($"{id}: {selector} covered by another element");
            // Upgrade and Pause are the primary actions (28 px); Settings is an icon link kept at the 24 px WCAG 2.5.8 floor.
            var minimum = selector == ".settings-nav-btn" ? 24 : 28;
            if (rect.GetProperty("height").GetDouble() < minimum || rect.GetProperty("width").GetDouble() < minimum)
                violations.Add($"{id}: {selector} hit target too small ({rect.GetProperty("width").GetDouble():0.0}x{rect.GetProperty("height").GetDouble():0.0})");
        }

        // The XP row must not move anything the learner interacts with, nor the Stage strip.
        foreach (var selector in GeometrySelectors)
        {
            var with = baseline.GetProperty("rects").GetProperty(selector);
            var now = rects.GetProperty(selector);
            if (with.ValueKind != JsonValueKind.Object || now.ValueKind != JsonValueKind.Object) { violations.Add($"{id}: {selector} missing in comparison"); continue; }
            if (HostSelectors.Contains(selector)) continue;
            foreach (var edge in new[] { "top", "left", "width", "height" })
            {
                var delta = Math.Abs(now.GetProperty(edge).GetDouble() - with.GetProperty(edge).GetDouble());
                if (delta > Tolerance(id)) violations.Add($"{id}: {selector} {edge} moved by {delta:0.0}px when the XP row is added");
            }
        }
        var keypad = probe.GetProperty("keypad").EnumerateArray().ToArray();
        var baseKeypad = baseline.GetProperty("keypad").EnumerateArray().ToArray();
        if (keypad.Length != 12 && keypad.Length != 11 || keypad.Length != baseKeypad.Length) violations.Add($"{id}: unexpected keypad key count {keypad.Length}");
        else for (var i = 0; i < keypad.Length; i++)
        {
            foreach (var edge in new[] { "top", "left", "width", "height" })
            {
                if (Math.Abs(keypad[i].GetProperty(edge).GetDouble() - baseKeypad[i].GetProperty(edge).GetDouble()) > Tolerance(id))
                    violations.Add($"{id}: keypad key {i} {edge} moved when the XP row is added");
            }
        }

        // Every Stage entry is fully inside the page width and not cut by an ancestor, even with four wide operations.
        var viewportWidth = probe.GetProperty("viewportWidth").GetDouble();
        foreach (var operation in probe.GetProperty("operations").EnumerateArray())
        {
            var rect = operation.GetProperty("rect");
            if (rect.GetProperty("right").GetDouble() > viewportWidth + 0.5 || rect.GetProperty("left").GetDouble() < -0.5)
                violations.Add($"{id}: Stage entry outside the page width ({rect.GetProperty("left").GetDouble():0}..{rect.GetProperty("right").GetDouble():0} of {viewportWidth:0})");
            if (operation.GetProperty("overflow").GetBoolean()) violations.Add($"{id}: Stage entry text overflows");
        }
        var panel = rects.GetProperty(".player-shield-panel");
        var xpCluster = rects.GetProperty(".player-xp-cluster");
        if (xpCluster.GetProperty("bottom").GetDouble() > panel.GetProperty("bottom").GetDouble() + 0.5) violations.Add($"{id}: XP row spills out of the player panel");
    }

    // At the default text scale the XP row fits the radar-bound panel height and moves nothing. The radar graphic is a fixed
    // 46 px, so at 130% text scale the larger rows can outgrow it: that bounded growth is accepted, shifting is not.
    private static double Tolerance(string id) => id.EndsWith("-large", StringComparison.Ordinal) ? 12 : 0.5;

    private void Log(string id, JsonElement probe)
    {
        var rects = probe.GetProperty("rects");
        string R(string selector)
        {
            var r = rects.GetProperty(selector);
            return r.ValueKind == JsonValueKind.Object
                ? $"{selector}=({r.GetProperty("left").GetDouble():0.#},{r.GetProperty("top").GetDouble():0.#} {r.GetProperty("width").GetDouble():0.#}x{r.GetProperty("height").GetDouble():0.#})"
                : selector + "=none";
        }
        _output.WriteLine($"{id}: " + string.Join(' ', GeometrySelectors.Concat([".player-xp-cluster"]).Select(R)));
        _output.WriteLine($"{id} OPS: " + string.Join(" | ", probe.GetProperty("operations").EnumerateArray().Select(o =>
            $"({o.GetProperty("rect").GetProperty("left").GetDouble():0.#},{o.GetProperty("rect").GetProperty("top").GetDouble():0.#} {o.GetProperty("rect").GetProperty("width").GetDouble():0.#}x{o.GetProperty("rect").GetProperty("height").GetDouble():0.#} clip={o.GetProperty("clip").GetDouble():0.00})")));
    }

    // The XP row is one self-contained block that ends with the value span; the surrounding markup is untouched.
    private static string RemoveXpRow(string html)
    {
        var stripped = Regex.Replace(html, @"<div class=""player-xp-cluster"">[\s\S]*?class=""player-xp-value"">[^<]*</span>\s*</div>", string.Empty);
        Assert.DoesNotContain("player-xp-cluster", stripped);
        return stripped;
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue122Layout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
