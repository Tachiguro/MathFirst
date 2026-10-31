namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.Json;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using Xunit.Abstractions;

/// <summary>
/// Issue #125: rendered Chromium regression for the cyber main menu. Markup comes from the real Home page with the menu
/// opened through its own handler; geometry from the production CSS in headless Chromium. Every menu action must be a
/// reachable, uncovered touch target of at least 44 px on narrow portrait and short landscape phones, in EN/DE/RU,
/// light/dark and enlarged text, with no horizontal overflow and no hidden scroll trap.
/// Synthetic UI evidence only; it is not Android WebView, TalkBack or physical-device acceptance.
/// </summary>
public sealed class Issue125GameMenuRenderedLayoutTests : IDisposable
{
    private const double MinimumTarget = 44;

    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("short-320x480", 320, 480),
        ("narrow-320x640", 320, 640),
        ("phone-384x744", 384, 744),
        ("landscape-744x384", 744, 384)
    ];

    private static readonly string[] Languages = ["en", "de", "ru"];

    private readonly ITestOutputHelper _output;
    private readonly Issue125GameMenuTests _suite = new();
    private readonly List<string> _tempDirectories = [];

    public Issue125GameMenuRenderedLayoutTests(ITestOutputHelper output) => _output = output;

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
    public async Task GameMenu_RenderedInChromium_KeepsEveryActionAReachableTouchTarget(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        foreach (var language in Languages)
        {
            var variants = new (string Name, Issue125GameMenuTests.MenuOptions Options, bool Expand, int Actions)[]
            {
                // A fresh learner: Continue, Settings, Progress, Tutorial; no Upgrade entry.
                ("fresh", new(Language: language, PlayerLevel: 1), false, 4),
                // Upgrade discovered: the fifth entry is present.
                ("discovered", new(Language: language, PlayerLevel: 6), false, 5),
                // Expanded progress with four operations and a three-digit level: the tallest menu.
                ("expanded", new(Language: language, CumulativeXp: CyberDefenseLevelPolicy.GetCumulativeXpThreshold(120) + 98765,
                    Stage: CurriculumStage.Stage4_Division,
                    Bands: [(ArithmeticOperation.Addition, 11), (ArithmeticOperation.Subtraction, 11), (ArithmeticOperation.Multiplication, 11), (ArithmeticOperation.Division, 10)]), true, 5),
                // Calm mode: no Tutorial and no Upgrade.
                ("calm", new(Language: language, PlayerLevel: 30, CyberDefense: false), true, 3)
            };

            foreach (var (name, options, expand, actions) in variants)
            {
                await using var fixture = await Issue125GameMenuTests.MenuFixture.CreateAsync(_suite, options);
                await fixture.StartAsync();
                await fixture.OpenMenuAsync();
                if (expand) await fixture.InvokeAsync("ToggleMenuProgress");
                var html = await fixture.RenderedHtmlAsync();
                Assert.Contains("game-menu-overlay", html);

                foreach (var (viewportName, width, height) in Viewports)
                {
                    foreach (var (theme, largeText) in new[] { ("light", false), ("dark", false), ("light", true) })
                    {
                        var id = $"{viewportName}/{language}/{name}/{theme}{(largeText ? "-large" : string.Empty)}/actions={actions}/expand={expand}";
                        scenarios.Add(new(id, "menu", width, height, language, theme, largeText, html, null));
                    }
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
            CheckScenario(scenario.Id, result.GetProperty("probe"), reducedMotion, violations);
            if (scenario.Id.StartsWith("short-320x480/ru/expanded/light-large", StringComparison.Ordinal) ||
                scenario.Id.StartsWith("landscape-744x384/de/discovered/dark", StringComparison.Ordinal))
            {
                Log(scenario.Id, result.GetProperty("probe"));
            }
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered menu layout violation(s):\n" + string.Join("\n", violations.Take(80)));
    }

    private static void CheckScenario(string id, JsonElement probe, bool reducedMotion, List<string> violations)
    {
        if (!probe.GetProperty("present").GetBoolean()) { violations.Add($"{id}: menu not present"); return; }
        var expectedActions = int.Parse(id[(id.IndexOf("actions=", StringComparison.Ordinal) + 8)..].Split('/')[0]);
        var expanded = id.EndsWith("expand=True", StringComparison.Ordinal);

        if (probe.GetProperty("actionCount").GetInt32() != expectedActions) violations.Add($"{id}: {probe.GetProperty("actionCount").GetInt32()} actions, expected {expectedActions}");
        if (probe.GetProperty("practiceSurfacePresent").GetBoolean()) violations.Add($"{id}: keypad or answer field rendered under the menu");
        if (probe.GetProperty("headerMenuPresent").GetBoolean()) violations.Add($"{id}: header Menu button still offered while the menu is open");
        if (probe.GetProperty("progressExpanded").GetBoolean() != expanded) violations.Add($"{id}: progress expansion state differs");
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal page overflow (scrollWidth {probe.GetProperty("scrollWidth").GetInt32()})");
        if (probe.GetProperty("headingAtOpen").GetDouble() < 0.99) violations.Add($"{id}: menu heading not fully visible when opened ({probe.GetProperty("headingAtOpen").GetDouble():0.00})");
        var animation = probe.GetProperty("animationName").GetString();
        if (reducedMotion && animation != "none") violations.Add($"{id}: entrance animation '{animation}' runs under reduced motion");
        if (!reducedMotion && animation != "game-menu-enter") violations.Add($"{id}: expected the subtle entrance animation, got '{animation}'");

        var viewportWidth = int.Parse(id.Split('/')[0].Split('-')[^1].Split('x')[0]);
        var panel = probe.GetProperty("panel");
        if (panel.GetProperty("left").GetDouble() < -0.5 || panel.GetProperty("right").GetDouble() > viewportWidth + 0.5)
            violations.Add($"{id}: menu panel outside the page width");

        foreach (var action in probe.GetProperty("actions").EnumerateArray())
        {
            CheckReachable(id, action, requireTouchSize: true, violations);
        }
        if (expanded)
        {
            var tail = probe.GetProperty("progressTail");
            if (tail.ValueKind != JsonValueKind.Object) violations.Add($"{id}: expanded progress has no content");
            else CheckReachable(id, tail, requireTouchSize: false, violations);
        }
    }

    private static void CheckReachable(string id, JsonElement target, bool requireTouchSize, List<string> violations)
    {
        var label = $"'{target.GetProperty("text").GetString()}'";
        if (target.GetProperty("visible").GetDouble() < 0.99) violations.Add($"{id}: {label} not fully visible after scrolling to it ({target.GetProperty("visible").GetDouble():0.00})");
        if (target.GetProperty("clip").GetDouble() < 0.99) violations.Add($"{id}: {label} clipped by an ancestor");
        if (requireTouchSize && !target.GetProperty("hit").GetBoolean()) violations.Add($"{id}: {label} covered by another element");
        if (target.GetProperty("textOverflow").GetBoolean()) violations.Add($"{id}: {label} text overflows its box");
        if (target.GetProperty("traps").GetArrayLength() > 0) violations.Add($"{id}: {label} reachable only by scrolling a non-scrollable container: {target.GetProperty("traps")}");
        if (requireTouchSize && (target.GetProperty("height").GetDouble() < MinimumTarget || target.GetProperty("width").GetDouble() < MinimumTarget))
            violations.Add($"{id}: {label} touch target too small ({target.GetProperty("width").GetDouble():0.0}x{target.GetProperty("height").GetDouble():0.0})");
    }

    private void Log(string id, JsonElement probe)
    {
        _output.WriteLine($"{id}: heading={probe.GetProperty("headingAtOpen").GetDouble():0.00} actions=" + string.Join(" | ",
            probe.GetProperty("actions").EnumerateArray().Select(a =>
                $"{a.GetProperty("text").GetString()} {a.GetProperty("width").GetDouble():0}x{a.GetProperty("height").GetDouble():0}")));
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue125Layout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
