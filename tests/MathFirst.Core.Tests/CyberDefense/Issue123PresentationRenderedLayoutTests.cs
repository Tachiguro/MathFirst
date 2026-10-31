namespace MathFirst.Core.Tests.CyberDefense;

using System.Text.Json;
using MathFirst.Domain.CyberDefense;
using Xunit.Abstractions;
using MenuFixture = Issue125GameMenuTests.MenuFixture;

/// <summary>
/// Issue #123: rendered Chromium regression for the operation-unlock announcement, the Upgrade spotlight and the guided
/// Upgrade Terminal. Markup comes from the real Home page after a real committed submission; geometry from the
/// production CSS in headless Chromium. On narrow portrait and short landscape phones, in EN/DE/RU, light/dark and
/// enlarged text, each acknowledgement must be a reachable, uncovered touch target of at least 44 px, the spotlighted
/// header Upgrade control must stay visible and tappable, the practice surface underneath must not be hit, and nothing
/// may overflow horizontally. Synthetic UI evidence only; it is not Android WebView, TalkBack or device acceptance.
/// </summary>
public sealed class Issue123PresentationRenderedLayoutTests : IDisposable
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

    public Issue123PresentationRenderedLayoutTests(ITestOutputHelper output) => _output = output;

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
    public async Task Presentations_RenderedInChromium_KeepEveryActionAndTheSpotlightTargetReachable(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        var justBelowLevel2 = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2) - 1;
        foreach (var language in Languages)
        {
            var markup = new List<(string Name, string Probe, string Html)>();

            await using (var f = await MenuFixture.CreateAsync(_suite, new(Language: language, PlayerLevel: 6, AdditionMastered: true)))
            {
                await f.StartAsync();
                await f.SubmitAsync(correct: true).WaitAsync(TimeSpan.FromSeconds(10));
                var html = await f.RenderedHtmlAsync();
                Assert.Contains("operation-unlock-dialog", html);
                markup.Add(("unlock", "presentation", html));
            }

            await using (var f = await MenuFixture.CreateAsync(_suite, new(Language: language, CumulativeXp: justBelowLevel2)))
            {
                await f.StartAsync();
                await f.SubmitAsync(correct: true).WaitAsync(TimeSpan.FromSeconds(10));
                var html = await f.RenderedHtmlAsync();
                Assert.Contains("upgrade-spotlight-overlay", html);
                markup.Add(("spotlight", "presentation", html));

                await f.InvokeAsync("OpenUpgradeTerminalAsync");
                html = await f.RenderedHtmlAsync();
                Assert.Contains("upgrade-guided-advice-card", html);
                markup.Add(("guided-terminal", "terminal", html));
            }

            foreach (var (name, probe, html) in markup)
            {
                foreach (var (viewportName, width, height) in Viewports)
                {
                    foreach (var (theme, largeText) in new[] { ("light", false), ("dark", false), ("light", true) })
                    {
                        var id = $"{viewportName}/{language}/{name}/{theme}{(largeText ? "-large" : string.Empty)}";
                        scenarios.Add(new(id, probe, width, height, language, theme, largeText, html, null));
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
            var probe = result.GetProperty("probe");
            var before = violations.Count;
            if (scenario.Probe == "terminal") CheckGuidedTerminal(scenario.Id, probe, violations);
            else CheckPresentation(scenario.Id, probe, violations);
            if (violations.Count > before ||
                (scenario.Id.StartsWith("short-320x480/ru/", StringComparison.Ordinal) && scenario.Id.EndsWith("light-large", StringComparison.Ordinal)))
            {
                _output.WriteLine($"{scenario.Id}: {probe}");
            }
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered presentation layout violation(s):\n" + string.Join("\n", violations.Take(80)));
    }

    private static void CheckPresentation(string id, JsonElement probe, List<string> violations)
    {
        if (!probe.GetProperty("present").GetBoolean()) { violations.Add($"{id}: presentation not present"); return; }
        var spotlight = id.Contains("/spotlight/", StringComparison.Ordinal);
        var viewportWidth = int.Parse(id.Split('/')[0].Split('-')[^1].Split('x')[0]);

        if (probe.GetProperty("kind").GetString() != (spotlight ? "spotlight" : "unlock")) violations.Add($"{id}: wrong presentation kind");
        if (probe.GetProperty("dialogCount").GetInt32() != 1) violations.Add($"{id}: {probe.GetProperty("dialogCount").GetInt32()} dialogs rendered, expected exactly one");
        if (!probe.GetProperty("mainInert").GetBoolean()) violations.Add($"{id}: practice surface not inert under the presentation");
        if (probe.GetProperty("headerInert").GetBoolean() == spotlight) violations.Add($"{id}: header inert state wrong for {(spotlight ? "spotlight" : "unlock")}");
        if (probe.GetProperty("practiceSurfaceHit").GetBoolean()) violations.Add($"{id}: keypad, answer field or menu reachable under the presentation");
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal overflow (scrollWidth {probe.GetProperty("scrollWidth").GetInt32()})");
        if (probe.GetProperty("headingAtOpen").GetDouble() < 0.99) violations.Add($"{id}: heading not fully visible when opened ({probe.GetProperty("headingAtOpen").GetDouble():0.00})");
        var card = probe.GetProperty("card");
        if (card.GetProperty("left").GetDouble() < -0.5 || card.GetProperty("right").GetDouble() > viewportWidth + 0.5) violations.Add($"{id}: card outside the page width");

        var actions = probe.GetProperty("actions").EnumerateArray().ToArray();
        if (actions.Length != (spotlight ? 2 : 1)) violations.Add($"{id}: {actions.Length} actions");
        foreach (var action in actions)
        {
            var label = $"'{action.GetProperty("text").GetString()}'";
            if (action.GetProperty("visible").GetDouble() < 0.99) violations.Add($"{id}: {label} not fully visible after scrolling to it");
            if (action.GetProperty("clip").GetDouble() < 0.99) violations.Add($"{id}: {label} clipped by an ancestor");
            if (!action.GetProperty("hit").GetBoolean()) violations.Add($"{id}: {label} covered by another element");
            if (action.GetProperty("textOverflow").GetBoolean()) violations.Add($"{id}: {label} text overflows its box");
            if (action.GetProperty("traps").GetArrayLength() > 0) violations.Add($"{id}: {label} reachable only by scrolling a non-scrollable container");
            if (action.GetProperty("height").GetDouble() < MinimumTarget || action.GetProperty("width").GetDouble() < MinimumTarget)
                violations.Add($"{id}: {label} touch target too small ({action.GetProperty("width").GetDouble():0.0}x{action.GetProperty("height").GetDouble():0.0})");
        }

        var target = probe.GetProperty("spotlightTarget");
        if (!spotlight)
        {
            if (target.ValueKind != JsonValueKind.Null) violations.Add($"{id}: spotlight target active during an unlock announcement");
            return;
        }
        if (target.ValueKind != JsonValueKind.Object) { violations.Add($"{id}: spotlighted header Upgrade control missing"); return; }
        if (target.GetProperty("visible").GetDouble() < 0.99) violations.Add($"{id}: spotlighted Upgrade control not fully visible");
        if (!target.GetProperty("hit").GetBoolean()) violations.Add($"{id}: spotlighted Upgrade control covered (not tappable)");
        if (target.GetProperty("disabled").GetBoolean()) violations.Add($"{id}: spotlighted Upgrade control disabled");
        if (target.GetProperty("overlapsCard").GetBoolean()) violations.Add($"{id}: spotlight card overlaps the spotlighted control");
    }

    // The Issue #117 terminal contract holds with the advice shown: at ordinary text sizes the header, status and Close
    // stay pinned and visible when the terminal opens; only enlarged text may fall back to scrolling the whole content.
    private static void CheckGuidedTerminal(string id, JsonElement probe, List<string> violations)
    {
        if (!probe.GetProperty("present").GetBoolean()) { violations.Add($"{id}: terminal not present"); return; }
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: terminal horizontal overflow");
        var content = probe.GetProperty("contentOverflow");
        var pinnedFits = content.GetProperty("scrollHeight").GetDouble() <= content.GetProperty("clientHeight").GetDouble() + 1;
        if (!pinnedFits && !id.EndsWith("-large", StringComparison.Ordinal))
            violations.Add($"{id}: pinned terminal chrome does not fit with the guided advice ({content.GetProperty("scrollHeight").GetDouble()}>{content.GetProperty("clientHeight").GetDouble()})");
        if (pinnedFits)
        {
            var atOpen = probe.GetProperty("atOpen");
            if (atOpen.GetProperty("close").GetDouble() < 0.99 || !atOpen.GetProperty("closeHit").GetBoolean()) violations.Add($"{id}: Close not pinned and tappable when the terminal opens");
        }
        if (probe.GetProperty("guidedAdviceAtOpen").ValueKind != JsonValueKind.Number || probe.GetProperty("guidedAdviceAtOpen").GetDouble() <= 0)
            violations.Add($"{id}: guided advice not visible when the terminal opens");
        var close = probe.GetProperty("closeReachable");
        if (close.GetProperty("visible").GetDouble() < 0.99 || !close.GetProperty("hit").GetBoolean()) violations.Add($"{id}: Close not reachable");
        var gotIt = probe.GetProperty("guidedAdviceAction");
        if (gotIt.GetProperty("visible").GetDouble() < 0.99 || !gotIt.GetProperty("hit").GetBoolean()) violations.Add($"{id}: advice 'Got it' not reachable");
        if (gotIt.GetProperty("height").GetDouble() < MinimumTarget) violations.Add($"{id}: advice 'Got it' touch target too small ({gotIt.GetProperty("height").GetDouble():0.0})");
        foreach (var button in probe.GetProperty("purchaseButtons").EnumerateArray())
        {
            if (button.GetProperty("visible").GetDouble() < 0.99 || !button.GetProperty("hit").GetBoolean())
                violations.Add($"{id}: purchase button '{button.GetProperty("cls").GetString()}' not reachable under the guided advice");
        }
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue123Layout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
