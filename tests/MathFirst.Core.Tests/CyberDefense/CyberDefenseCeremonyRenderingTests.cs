namespace MathFirst.Core.Tests.CyberDefense;

using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class CyberDefenseCeremonyRenderingTests
{
    private static readonly CyberDefenseSectorRecap Valid = new("boss", 0, 1, 24, 22, 24000, 178, 1);

    private static async Task<string> RenderAsync(LocalizationService localizer, CyberDefensePresentationStage stage,
        CyberDefenseSectorRecap? recap = null, bool canContinue = false, long presentationId = 7)
    {
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseCeremony>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Stage"] = stage, ["Recap"] = recap, ["CompletedSector"] = 1,
                ["CanContinue"] = canContinue, ["PresentationId"] = presentationId
            }))).ToHtmlString()));
    }

    private static LocalizationService Localizer(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        return localizer;
    }

    private static void AssertLocalized(string html, LocalizationService localizer, string key, params object[] arguments)
    {
        var text = localizer[key, arguments];
        Assert.NotEqual(key, text);
        Assert.Contains(text, html);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task LocalizedVictoryRecapAndRestart_ShowOnlyRealMetrics(string language)
    {
        var localizer = Localizer(language);

        var victory = await RenderAsync(localizer, CyberDefensePresentationStage.Victory);
        AssertLocalized(victory, localizer, "CyberDefense_Victory");
        Assert.DoesNotContain("recap-metrics", victory);

        var summary = await RenderAsync(localizer, CyberDefensePresentationStage.Recap, Valid);
        AssertLocalized(summary, localizer, "CyberDefense_RecapTitle", 1);
        AssertLocalized(summary, localizer, "CyberDefense_RecapAccuracy", Valid.AccuracyPercentage);
        AssertLocalized(summary, localizer, "CyberDefense_RecapMistakes", 2);
        AssertLocalized(summary, localizer, "CyberDefense_RecapActiveAnswering", 24m);
        AssertLocalized(summary, localizer, "CyberDefense_RecapXp", 178L);
        AssertLocalized(summary, localizer, "CyberDefense_RecapLevels", 1);

        var missing = await RenderAsync(localizer, CyberDefensePresentationStage.Recap);
        AssertLocalized(missing, localizer, "CyberDefense_RecapUnavailable");
        Assert.DoesNotContain("recap-metrics", missing);
        Assert.DoesNotContain(localizer["CyberDefense_RecapMistakes", 0], missing);

        var restart = await RenderAsync(localizer, CyberDefensePresentationStage.SystemOverload);
        AssertLocalized(restart, localizer, "CyberDefense_SystemOverload");
        AssertLocalized(restart, localizer, "CyberDefense_ProgressRetained");

        Assert.Equal(string.Empty, await RenderAsync(localizer, CyberDefensePresentationStage.None));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task ContinueButton_IsHiddenUntilRevealed_ThenLocalizedOnEveryStage(string language)
    {
        var localizer = Localizer(language);
        var label = localizer["Common_Continue"];
        Assert.NotEqual("Common_Continue", label);
        foreach (var (stage, recap) in new[]
        {
            (CyberDefensePresentationStage.Victory, (CyberDefenseSectorRecap?)null),
            (CyberDefensePresentationStage.Recap, Valid),
            (CyberDefensePresentationStage.Recap, null),
            (CyberDefensePresentationStage.SystemOverload, null)
        })
        {
            var hidden = await RenderAsync(localizer, stage, recap, canContinue: false);
            Assert.DoesNotContain("<button", hidden);
            Assert.DoesNotContain(label, hidden);
        }

        foreach (var (stage, recap) in new[]
        {
            (CyberDefensePresentationStage.Recap, Valid),
            (CyberDefensePresentationStage.Recap, null),
            (CyberDefensePresentationStage.SystemOverload, null)
        })
        {
            var shown = await RenderAsync(localizer, stage, recap, canContinue: true);
            var button = Regex.Matches(shown, "<button[^>]*>[\\s\\S]*?</button>");
            var only = Assert.Single(button);
            Assert.Contains("type=\"button\"", only.Value);
            Assert.Contains("ceremony-continue", only.Value);
            Assert.Contains(label, only.Value);
        }
    }

    [Fact]
    public async Task Ceremony_IsAnAccessibleModalDialog_LabelledByItsHeading_AndAnnouncesTheRevealedAction()
    {
        var localizer = Localizer("en");
        var html = await RenderAsync(localizer, CyberDefensePresentationStage.Recap, Valid, canContinue: true);
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        var labelledBy = Regex.Match(html, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(labelledBy);
        Assert.Matches($"<h2[^>]*id=\"{labelledBy}\"", html);
        Assert.Contains("tabindex=\"-1\"", html);
        Assert.Matches("class=\"ceremony-actions\"[^>]*aria-live=\"polite\"", html);
        Assert.Contains("lang=\"en\"", html);
    }

    [Fact]
    public async Task InconsistentRecapCounts_AreTreatedAsUnavailable_NotInventedOrCrashing()
    {
        var localizer = Localizer("en");
        foreach (var invalid in new[]
        {
            new CyberDefenseSectorRecap("boss", 0, 1, 0, 0, 1000, 10, 0),
            new CyberDefenseSectorRecap("boss", 0, 1, 5, 6, 1000, 10, 0),
            new CyberDefenseSectorRecap("boss", 0, 1, 5, -1, 1000, 10, 0),
            new CyberDefenseSectorRecap("boss", 0, 1, 5, 5, -1, 10, 0)
        })
        {
            var html = await RenderAsync(localizer, CyberDefensePresentationStage.Recap, invalid, canContinue: true);
            AssertLocalized(html, localizer, "CyberDefense_RecapUnavailable");
            Assert.DoesNotContain("recap-metrics", html);
            Assert.Contains("ceremony-continue", html);
        }
    }

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(24, 22, 2)]
    [InlineData(7, 0, 7)]
    public void RecapMistakes_AreDerivedFromRealAnswerCounts(int answers, int correct, int mistakes)
    {
        var recap = new CyberDefenseSectorRecap("s", 0, 1, answers, correct, 1000, 1, 0);
        Assert.True(recap.HasValidCounts);
        Assert.Equal(mistakes, recap.MistakeCount);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 6, 0)]
    [InlineData(5, -1, 0)]
    [InlineData(5, 5, -1)]
    public void RecapWithImpossibleCounts_FabricatesNoMistakeCount(int answers, int correct, long time)
    {
        var recap = new CyberDefenseSectorRecap("s", 0, 1, answers, correct, time, 1, 0);
        Assert.False(recap.HasValidCounts);
        Assert.Null(recap.MistakeCount);
    }

    [Fact]
    public async Task ContinueActivation_InvokesTheCallbackOnceWithTheRenderedPresentationIdentity()
    {
        var localizer = Localizer("en");
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var received = new List<long>();
        global::MathFirst.Core.Tests.CyberDefenseCeremony? component = null;
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Capture"] = (Action<global::MathFirst.Core.Tests.CyberDefenseCeremony>)(c => component = c),
            ["Callback"] = EventCallback.Factory.Create<long>(this, id => received.Add(id))
        })));
        var activate = typeof(global::MathFirst.Core.Tests.CyberDefenseCeremony)
            .GetMethod("ContinueAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < 5; i++)
            await renderer.Dispatcher.InvokeAsync(() => (Task)activate.Invoke(component, null)!);
        Assert.Equal(new long[] { 42 }, received);
    }

    private sealed class Host : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.CyberDefenseCeremony> Capture { get; set; } = null!;
        [Parameter] public EventCallback<long> Callback { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.CyberDefenseCeremony>(0);
            builder.AddAttribute(1, "Stage", CyberDefensePresentationStage.Recap);
            builder.AddAttribute(2, "CanContinue", true);
            builder.AddAttribute(3, "PresentationId", 42L);
            builder.AddAttribute(4, "OnContinue", Callback);
            builder.AddComponentReferenceCapture(5, c => Capture((global::MathFirst.Core.Tests.CyberDefenseCeremony)c));
            builder.CloseComponent();
        }
    }
}
