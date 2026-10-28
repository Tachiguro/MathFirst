namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MathFirst.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using CyberDefenseTutorialStep = global::MathFirst.Core.Tests.CyberDefenseTutorial.CyberDefenseTutorialStep;

public sealed class SpotlightTutorialComponentTests
{
    private static readonly string[] RequiredTutorialKeys =
    [
        "CyberDefense_Tutorial_Story_Badge",
        "CyberDefense_Tutorial_Story_Title",
        "CyberDefense_Tutorial_StepProgress",
        "CyberDefense_Tutorial_Enemy_Title",
        "CyberDefense_Tutorial_Enemy_Body",
        "CyberDefense_Tutorial_ExpressionKeypad_Title",
        "CyberDefense_Tutorial_ExpressionKeypad_Body",
        "CyberDefense_Tutorial_SystemIntegrity_Title",
        "CyberDefense_Tutorial_SystemIntegrity_Body",
        "CyberDefense_Tutorial_Next",
        "CyberDefense_Tutorial_Back",
        "CyberDefense_Tutorial_Skip",
        "CyberDefense_Tutorial_Finish",
        "CyberDefense_Story_Intro"
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task Tutorial_InitialMount_RendersStoryCardFirst_WithReusedStoryText(string language)
    {
        await using var tutorial = await RenderedTutorial.CreateAsync(language);
        var html = await tutorial.HtmlAsync();
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        // Story card rendered first
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);
        Assert.Contains("data-step=\"StoryCard\"", html);
        Assert.Contains("data-target=\"story-intro\"", html);

        // Reuses the approved CyberDefense_Story_Intro text without duplication
        var expectedIntro = localizer["CyberDefense_Story_Intro"];
        Assert.False(string.IsNullOrWhiteSpace(expectedIntro));
        Assert.Contains(expectedIntro, html);

        // Mission briefing badge and title
        Assert.Contains(localizer["CyberDefense_Tutorial_Story_Badge"], html);
        Assert.Contains(localizer["CyberDefense_Tutorial_Story_Title"], html);

        // Navigation state on Story Card: Next and Skip present, Back is NOT rendered
        Assert.Contains(localizer["CyberDefense_Tutorial_Next"], html);
        Assert.Contains(localizer["CyberDefense_Tutorial_Skip"], html);
        Assert.DoesNotContain(localizer["CyberDefense_Tutorial_Back"], html);
        Assert.DoesNotContain(localizer["CyberDefense_Tutorial_Finish"], html);

        // Semantic dialog labeling
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("aria-labelledby=\"tutorial-step-title\"", html);
        Assert.Contains("aria-describedby=\"tutorial-step-desc\"", html);
        Assert.Contains($"lang=\"{language}\"", html);
    }

    [Fact]
    public async Task Tutorial_ExactlyThreeSpotlightStations_FollowStoryCard()
    {
        await using var tutorial = await RenderedTutorial.CreateAsync("en");

        // Station 0: Story Card (Introductory, not a spotlight station)
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);

        // Station 1: Enemy spotlight
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.Enemy, tutorial.Component.CurrentStep);
        var enemyHtml = await tutorial.HtmlAsync();
        Assert.Contains("data-target=\"enemy-hud\"", enemyHtml);
        Assert.Contains("Spotlight 1 of 3", enemyHtml);

        // Station 2: Expression and Keypad spotlight
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);
        var keypadHtml = await tutorial.HtmlAsync();
        Assert.Contains("data-target=\"expression-keypad\"", keypadHtml);
        Assert.Contains("Spotlight 2 of 3", keypadHtml);

        // Station 3: System Integrity spotlight
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);
        var integrityHtml = await tutorial.HtmlAsync();
        Assert.Contains("data-target=\"system-integrity\"", integrityHtml);
        Assert.Contains("Spotlight 3 of 3", integrityHtml);

        // Final station has Finish button instead of Next
        Assert.Contains("Finish", integrityHtml);
        Assert.DoesNotContain(">Next<", integrityHtml);

        // Calling Next again does not advance past SystemIntegrity
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);
    }

    [Fact]
    public async Task Tutorial_Navigation_BackMovesToPreviousStep_AndNextMovesForward()
    {
        await using var tutorial = await RenderedTutorial.CreateAsync("en");

        // Advance to step 3
        await tutorial.NextAsync(); // Enemy
        await tutorial.NextAsync(); // ExpressionKeypad
        await tutorial.NextAsync(); // SystemIntegrity
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);

        // Back to step 2
        await tutorial.BackAsync();
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);

        // Back to step 1
        await tutorial.BackAsync();
        Assert.Equal(CyberDefenseTutorialStep.Enemy, tutorial.Component.CurrentStep);

        // Back to step 0 (Story Card)
        await tutorial.BackAsync();
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);

        // Back on step 0 cannot go further back
        await tutorial.BackAsync();
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);
    }

    [Fact]
    public async Task Tutorial_Skip_IsAvailableAtAllStages_AndEmitsOnSkippedAtMostOnce()
    {
        // Skip from StoryCard
        await using (var t0 = await RenderedTutorial.CreateAsync("en"))
        {
            var html = await t0.HtmlAsync();
            Assert.Contains("Skip Tutorial", html);
            await t0.SkipAsync();
            Assert.Equal(1, t0.SkippedCount);
            Assert.Equal(0, t0.FinishedCount);
            // Subsequent skip does not re-emit
            await t0.SkipAsync();
            Assert.Equal(1, t0.SkippedCount);
            // Finish does not emit after skip
            await t0.FinishAsync();
            Assert.Equal(0, t0.FinishedCount);
        }

        // Skip from Enemy (Spotlight 1)
        await using (var t1 = await RenderedTutorial.CreateAsync("en"))
        {
            await t1.NextAsync();
            var html = await t1.HtmlAsync();
            Assert.Contains("Skip Tutorial", html);
            await t1.SkipAsync();
            Assert.Equal(1, t1.SkippedCount);
            Assert.Equal(0, t1.FinishedCount);
        }

        // Skip from ExpressionKeypad (Spotlight 2)
        await using (var t2 = await RenderedTutorial.CreateAsync("en"))
        {
            await t2.NextAsync();
            await t2.NextAsync();
            var html = await t2.HtmlAsync();
            Assert.Contains("Skip Tutorial", html);
            await t2.SkipAsync();
            Assert.Equal(1, t2.SkippedCount);
            Assert.Equal(0, t2.FinishedCount);
        }

        // Skip from SystemIntegrity (Spotlight 3)
        await using (var t3 = await RenderedTutorial.CreateAsync("en"))
        {
            await t3.NextAsync();
            await t3.NextAsync();
            await t3.NextAsync();
            var html = await t3.HtmlAsync();
            Assert.Contains("Skip Tutorial", html);
            await t3.SkipAsync();
            Assert.Equal(1, t3.SkippedCount);
            Assert.Equal(0, t3.FinishedCount);
        }
    }

    [Fact]
    public async Task Tutorial_Finish_OnlyAvailableAtFinalSpotlight_AndEmitsOnFinishedAtMostOnce()
    {
        await using var tutorial = await RenderedTutorial.CreateAsync("en");

        // Calling finish before final spotlight does not emit
        await tutorial.FinishAsync();
        Assert.Equal(0, tutorial.FinishedCount);

        await tutorial.NextAsync(); // Enemy
        await tutorial.FinishAsync();
        Assert.Equal(0, tutorial.FinishedCount);

        await tutorial.NextAsync(); // ExpressionKeypad
        await tutorial.FinishAsync();
        Assert.Equal(0, tutorial.FinishedCount);

        // On final spotlight (SystemIntegrity)
        await tutorial.NextAsync(); // SystemIntegrity
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);

        await tutorial.FinishAsync();
        Assert.Equal(1, tutorial.FinishedCount);
        Assert.Equal(0, tutorial.SkippedCount);

        // Subsequent finish does not re-emit
        await tutorial.FinishAsync();
        Assert.Equal(1, tutorial.FinishedCount);

        // Skip does not emit after finish
        await tutorial.SkipAsync();
        Assert.Equal(0, tutorial.SkippedCount);
    }

    [Fact]
    public async Task Tutorial_Rerender_DoesNotResetCurrentStep_AndResetRestoresInitialState()
    {
        await using var tutorial = await RenderedTutorial.CreateAsync("en");

        await tutorial.NextAsync(); // Enemy
        await tutorial.NextAsync(); // ExpressionKeypad
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);

        // Rerender does not reset the step
        await tutorial.RerenderAsync();
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);

        // Explicit Reset restores StoryCard
        await tutorial.ResetAsync();
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Tutorial_LocalizationParity_AllKeysPresentAcrossAllSupportedLanguages(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        foreach (var key in RequiredTutorialKeys)
        {
            var value = key == "CyberDefense_Tutorial_StepProgress"
                ? localizer[key, 1, 3]
                : localizer[key];

            Assert.False(string.IsNullOrWhiteSpace(value), $"Key '{key}' in locale '{language}' must not be null or whitespace.");
            Assert.NotEqual(key, value); // Must not return fallback unlocalized key name
        }
    }

    [Theory]
    [InlineData("en", "100 HP")]
    [InlineData("de", "100 KP")]
    [InlineData("ru", "100 ОЗ")]
    public async Task Tutorial_ContentFidelity_AdheresToProductRequirements(string language, string expectedHpText)
    {
        await using var tutorial = await RenderedTutorial.CreateAsync(language);
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        // 1. Enemy explanation identifies opponent and displayed combat status
        await tutorial.NextAsync();
        var enemyHtml = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_Enemy_Title"], enemyHtml);
        Assert.Contains(localizer["CyberDefense_Tutorial_Enemy_Body"], enemyHtml);

        // 2. Expression and Keypad explanation describes answering arithmetic problems to attack
        await tutorial.NextAsync();
        var keypadHtml = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_ExpressionKeypad_Title"], keypadHtml);
        Assert.Contains(localizer["CyberDefense_Tutorial_ExpressionKeypad_Body"], keypadHtml);

        // 3. System Integrity explanation identifies 100 HP and incorrect answer consequences
        await tutorial.NextAsync();
        var integrityHtml = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_SystemIntegrity_Title"], integrityHtml);
        Assert.Contains(localizer["CyberDefense_Tutorial_SystemIntegrity_Body"], integrityHtml);
        Assert.Contains(expectedHpText, integrityHtml);

        // Respectful copy: no fabricated achievements or unintegrated Google Play references
        Assert.DoesNotContain("Google Play", integrityHtml);
        Assert.DoesNotContain("Leaderboard", integrityHtml);
        Assert.DoesNotContain("Achievement Unlocked", integrityHtml);
    }

    [Theory]
    [InlineData(
        "en",
        "System integrity starts at 100 HP",
        "Firewall shield",
        "available",
        "absorb",
        "trigger enemy attacks",
        "before HP is lost",
        "shield and system integrity",
        "allow hostile strikes through")]
    [InlineData(
        "de",
        "Systemintegrität startet mit 100 KP",
        "Firewall-Schilde",
        "vorhandene",
        "fangen",
        "greifen Gegner an",
        "bevor KP verloren gehen",
        "Schild und deine Systemintegrität",
        "lassen gegnerische Treffer durch")]
    [InlineData(
        "ru",
        "Прочность системы начинается со 100 ОЗ",
        "щиты Файрвола",
        "доступные",
        "поглощают",
        "противник атакует",
        "прежде чем теряются ОЗ",
        "защитный щит и прочность",
        "пропускают удары")]
    public async Task Tutorial_SystemIntegrity_DistinguishesPlayerHpFromFirewallShields(
        string language,
        string expectedIntegrityHpClause,
        string expectedShieldTerminology,
        string expectedConditionalAvailability,
        string expectedAbsorbAction,
        string expectedAttackTrigger,
        string expectedShieldProtectionClause,
        string forbiddenShieldHpConflation,
        string forbiddenUnshieldedDepletionClause)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        var body = localizer["CyberDefense_Tutorial_SystemIntegrity_Body"];

        // 1. Localization parity: returns real translated content, not fallback or key name
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.NotEqual("CyberDefense_Tutorial_SystemIntegrity_Body", body);
        if (language != "en")
        {
            Assert.DoesNotContain("System integrity", body, StringComparison.OrdinalIgnoreCase);
        }

        // 2. 100 HP/KP/ОЗ refers specifically to PLAYER integrity, not shields
        Assert.Contains(expectedIntegrityHpClause, body);
        Assert.DoesNotContain(forbiddenShieldHpConflation, body, StringComparison.OrdinalIgnoreCase);

        // 3. Shield capacity is separate from HP and availability is conditional
        Assert.Contains(expectedShieldTerminology, body);
        Assert.Contains(expectedConditionalAvailability, body);

        // 4. Incorrect answers trigger enemy attacks
        Assert.Contains(expectedAttackTrigger, body);

        // 5. Available active shields absorb hits before HP is depleted (not all attacks bypass shields)
        Assert.Contains(expectedAbsorbAction, body);
        Assert.Contains(expectedShieldProtectionClause, body);
        Assert.DoesNotContain(forbiddenUnshieldedDepletionClause, body, StringComparison.OrdinalIgnoreCase);

        // 6. Verify rendered UI spotlight station carries this exact validated copy
        await using var tutorial = await RenderedTutorial.CreateAsync(language);
        await tutorial.NextAsync(); // Enemy
        await tutorial.NextAsync(); // ExpressionKeypad
        await tutorial.NextAsync(); // SystemIntegrity
        var html = await tutorial.HtmlAsync();
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);
        Assert.Contains(localizer["CyberDefense_Tutorial_SystemIntegrity_Title"], html);
        Assert.Contains(body, html);
    }

    [Fact]
    public async Task Tutorial_Accessibility_SemanticsAndAttributes()
    {
        await using var tutorial = await RenderedTutorial.CreateAsync("en");
        var html = await tutorial.HtmlAsync();

        // Accessible dialog semantics
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("aria-labelledby=\"tutorial-step-title\"", html);
        Assert.Contains("aria-describedby=\"tutorial-step-desc\"", html);
        Assert.Contains("tabindex=\"-1\"", html); // Heading focus target
        Assert.Contains("type=\"button\"", html); // All controls are explicit button types
    }

    private sealed class TutorialHost : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.CyberDefenseTutorial> Capture { get; set; } = null!;
        [Parameter] public Action<TutorialHost> CaptureHost { get; set; } = null!;
        [Parameter] public Func<Task>? OnFinished { get; set; }
        [Parameter] public Func<Task>? OnSkipped { get; set; }
        [Parameter] public ILocalizationService? Localizer { get; set; }

        protected override void OnInitialized() => CaptureHost(this);

        public void TriggerRerender() => StateHasChanged();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.CyberDefenseTutorial>(0);
            if (Localizer is not null)
            {
                builder.AddAttribute(1, "Localizer", Localizer);
            }
            if (OnFinished is not null)
            {
                builder.AddAttribute(2, "OnFinished", EventCallback.Factory.Create(this, OnFinished));
            }
            if (OnSkipped is not null)
            {
                builder.AddAttribute(3, "OnSkipped", EventCallback.Factory.Create(this, OnSkipped));
            }
            builder.AddComponentReferenceCapture(4, instance => Capture((global::MathFirst.Core.Tests.CyberDefenseTutorial)instance));
            builder.CloseComponent();
        }
    }

    private sealed class RenderedTutorial : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly TutorialHost _host;
        private readonly global::MathFirst.Core.Tests.CyberDefenseTutorial _component;
        public int FinishedCount { get; set; }
        public int SkippedCount { get; set; }
        public global::MathFirst.Core.Tests.CyberDefenseTutorial Component => _component;

        private RenderedTutorial(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, TutorialHost host, global::MathFirst.Core.Tests.CyberDefenseTutorial component)
        {
            _services = services;
            _renderer = renderer;
            _root = root;
            _host = host;
            _component = component;
        }

        public static async Task<RenderedTutorial> CreateAsync(string language = "en")
        {
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILocalizationService>(localizer)
                .BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

            global::MathFirst.Core.Tests.CyberDefenseTutorial capturedComponent = null!;
            TutorialHost capturedHost = null!;
            RenderedTutorial result = null!;

            var root = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                return await renderer.RenderComponentAsync<TutorialHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Localizer"] = localizer,
                    ["CaptureHost"] = (Action<TutorialHost>)(h => capturedHost = h),
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.CyberDefenseTutorial>)(c => capturedComponent = c),
                    ["OnFinished"] = (Func<Task>)(() => { if (result != null) result.FinishedCount++; return Task.CompletedTask; }),
                    ["OnSkipped"] = (Func<Task>)(() => { if (result != null) result.SkippedCount++; return Task.CompletedTask; })
                }));
            });

            result = new RenderedTutorial(services, renderer, root, capturedHost, capturedComponent);
            return result;
        }

        public Task NextAsync() => _renderer.Dispatcher.InvokeAsync(() => _component.NextAsync());
        public Task BackAsync() => _renderer.Dispatcher.InvokeAsync(() => _component.BackAsync());
        public Task SkipAsync() => _renderer.Dispatcher.InvokeAsync(() => _component.SkipAsync());
        public Task FinishAsync() => _renderer.Dispatcher.InvokeAsync(() => _component.FinishAsync());
        public Task ResetAsync() => _renderer.Dispatcher.InvokeAsync(() => _component.Reset());
        public Task RerenderAsync() => _renderer.Dispatcher.InvokeAsync(() => _host.TriggerRerender());
        public Task<string> HtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_root.ToHtmlString()));

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }
}
