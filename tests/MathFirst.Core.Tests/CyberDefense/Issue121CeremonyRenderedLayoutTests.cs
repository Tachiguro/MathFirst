namespace MathFirst.Core.Tests.CyberDefense;

using System.Net;
using System.Text.Json;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Issue #121: rendered Chromium regressions for the boss recap / System Restart ceremony.
/// Markup comes from the real Razor component, geometry from the production scoped CSS in headless Chromium.
/// Synthetic UI evidence only; it is not Android WebView or physical-device acceptance.
/// </summary>
public sealed class Issue121CeremonyRenderedLayoutTests : IDisposable
{
    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("narrow-320x640", 320, 640),
        ("phone-384x744", 384, 744),
        ("short-320x480", 320, 480),
        ("landscape-744x384", 744, 384)
    ];

    private static readonly string[] Languages = ["en", "de", "ru"];

    // Deliberately wide synthetic numbers: they must never clip or overlap the button.
    private static readonly CyberDefenseSectorRecap LongRecap = new("boss", 0, 12, 1234, 1002, 987654321, 123456789012, 79);

    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Theory(Timeout = 240000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CeremonyContinue_RenderedInChromium_StaysReachableWithoutOverlapOrClipping(bool reducedMotion)
    {
        var scenarios = new List<ChromiumLayoutHarness.Scenario>();
        foreach (var language in Languages)
        {
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);
            var variants = new (string Name, string Markup)[]
            {
                ("recap", await RenderAsync(localizer, CyberDefensePresentationStage.Recap, LongRecap, canContinue: true)),
                ("recap-unavailable", await RenderAsync(localizer, CyberDefensePresentationStage.Recap, null, canContinue: true)),
                ("restart", await RenderAsync(localizer, CyberDefensePresentationStage.SystemOverload, null, canContinue: true)),
                ("victory", await RenderAsync(localizer, CyberDefensePresentationStage.Victory, null, canContinue: false))
            };
            foreach (var (name, markup) in variants)
            {
                foreach (var (viewportName, width, height) in Viewports)
                {
                    foreach (var theme in new[] { "light", "dark" })
                        scenarios.Add(new($"{viewportName}/{language}/{name}/{theme}", "ceremony", width, height, language, theme, false, markup, null));
                    scenarios.Add(new($"{viewportName}/{language}/{name}/light/large-text", "ceremony", width, height, language, "light", true, markup, null));
                }
            }
        }

        var results = await ChromiumLayoutHarness.RunAsync(scenarios, reducedMotion, CreateTempDirectory());
        var violations = new List<string>();
        foreach (var result in results)
        {
            var id = result.GetProperty("id").GetString()!;
            var scenario = scenarios.Single(s => s.Id == id);
            Assert.Equal(reducedMotion, result.GetProperty("reducedMotion").GetBoolean());
            var viewport = result.GetProperty("viewport");
            Assert.Equal(scenario.Width, viewport.GetProperty("width").GetInt32());
            Assert.Equal(scenario.Height, viewport.GetProperty("height").GetInt32());
            Check(id, result.GetProperty("probe"), expectButton: !id.Contains("/victory/"), reducedMotion, violations);
        }

        Assert.True(violations.Count == 0, $"{violations.Count} rendered ceremony layout violation(s):\n" + string.Join("\n", violations));
    }

    private static void Check(string id, JsonElement probe, bool expectButton, bool reducedMotion, List<string> violations)
    {
        if (!probe.GetProperty("present").GetBoolean()) { violations.Add($"{id}: ceremony not rendered"); return; }
        if (probe.GetProperty("dialogRole").GetString() != "dialog") violations.Add($"{id}: overlay is not a dialog");
        if (probe.GetProperty("horizontalOverflow").GetBoolean()) violations.Add($"{id}: horizontal overflow");
        if (probe.GetProperty("headingAtTop").GetDouble() < 0.99) violations.Add($"{id}: heading clipped at top ({probe.GetProperty("headingAtTop").GetDouble():0.00})");
        var overlay = probe.GetProperty("overlay");
        if (overlay.GetProperty("overflowY").GetString() != "auto") violations.Add($"{id}: overlay cannot scroll");

        if (!expectButton)
        {
            if (probe.GetProperty("hasButton").GetBoolean()) violations.Add($"{id}: Continue visible too early");
            var animation = probe.GetProperty("animationName").GetString();
            if (reducedMotion && animation != "none") violations.Add($"{id}: animation '{animation}' not disabled for reduced motion");
            if (!reducedMotion && animation != "ceremony-victory") violations.Add($"{id}: victory animation missing ('{animation}')");
            return;
        }

        if (!probe.GetProperty("hasButton").GetBoolean()) { violations.Add($"{id}: Continue missing"); return; }
        var button = probe.GetProperty("rects").GetProperty("button");
        if (button.GetProperty("height").GetDouble() < 43.5) violations.Add($"{id}: touch target too short ({button.GetProperty("height").GetDouble():0.0}px)");
        if (button.GetProperty("width").GetDouble() < 43.5) violations.Add($"{id}: touch target too narrow");
        if (probe.GetProperty("buttonAfterScroll").GetDouble() < 0.99) violations.Add($"{id}: Continue clipped after scrolling ({probe.GetProperty("buttonAfterScroll").GetDouble():0.00})");
        if (!probe.GetProperty("buttonHit").GetBoolean()) violations.Add($"{id}: Continue covered by another element");
        if (!probe.GetProperty("buttonInsideCard").GetBoolean()) violations.Add($"{id}: Continue overflows its card");
    }

    private static async Task<string> RenderAsync(LocalizationService localizer, CyberDefensePresentationStage stage,
        CyberDefenseSectorRecap? recap, bool canContinue)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseCeremony>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stage"] = stage, ["Recap"] = recap, ["CompletedSector"] = 12,
                ["CanContinue"] = canContinue, ["PresentationId"] = 3L
            }))).ToHtmlString());
    }

    private string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue121_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return directory;
    }
}
