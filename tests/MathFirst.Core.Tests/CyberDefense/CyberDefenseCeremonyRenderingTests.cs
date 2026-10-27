namespace MathFirst.Core.Tests.CyberDefense;

using System.Net;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class CyberDefenseCeremonyRenderingTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task LocalizedVictoryRecapAndReboot_HaveAccessibleAnnouncements_AndNoInventedMetrics(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var recap = new CyberDefenseSectorRecap("boss", 0, 1, 24, 22, 24000, 178, 1);
        async Task<string> Render(CyberDefensePresentationStage stage, CyberDefenseSectorRecap? evidence = null)
            => await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseCeremony>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Stage"] = stage, ["Recap"] = evidence, ["CompletedSector"] = 1 }))).ToHtmlString()));

        var victory = await Render(CyberDefensePresentationStage.Victory);
        Assert.Contains("role=\"status\"", victory);
        Assert.Contains("aria-live=\"assertive\"", victory);
        Assert.Contains("aria-atomic=\"true\"", victory);
        Assert.Contains("tabindex=\"-1\"", victory);
        AssertLocalized(victory, localizer, "CyberDefense_Victory");
        var summary = await Render(CyberDefensePresentationStage.Recap, recap);
        AssertLocalized(summary, localizer, "CyberDefense_RecapTitle", 1);
        AssertLocalized(summary, localizer, "CyberDefense_RecapAccuracy", recap.AccuracyPercentage);
        AssertLocalized(summary, localizer, "CyberDefense_RecapActiveAnswering", 24m);
        AssertLocalized(summary, localizer, "CyberDefense_RecapXp", 178L);
        AssertLocalized(summary, localizer, "CyberDefense_RecapLevels", 1);
        var missing = await Render(CyberDefensePresentationStage.Recap);
        AssertLocalized(missing, localizer, "CyberDefense_RecapUnavailable");
        Assert.DoesNotContain("recap-metrics", missing);
        var reboot = await Render(CyberDefensePresentationStage.SystemOverload);
        AssertLocalized(reboot, localizer, "CyberDefense_SystemOverload");
        AssertLocalized(reboot, localizer, "CyberDefense_ProgressRetained");
        Assert.Equal(string.Empty, await Render(CyberDefensePresentationStage.None));
    }

    private static void AssertLocalized(string html, LocalizationService localizer, string key, params object[] arguments)
    {
        var text = localizer[key, arguments];
        Assert.NotEqual(key, text);
        Assert.Contains(text, html);
    }
}
