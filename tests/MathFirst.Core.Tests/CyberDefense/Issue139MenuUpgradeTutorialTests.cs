namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using MenuFixture = Issue125GameMenuTests.MenuFixture;
using CyberDefenseTutorialStep = global::MathFirst.Core.Tests.CyberDefenseTutorial.CyberDefenseTutorialStep;

/// <summary>
/// Issue #139: menu-only Upgrade access, one-time first upgrade onboarding instructions (Menu -> Upgrade Terminal),
/// and five-step Tutorial & Help for Cyber Defense.
/// </summary>
public sealed class Issue139MenuUpgradeTutorialTests : IDisposable
{
    private readonly Issue125GameMenuTests _suite = new();

    public void Dispose() => _suite.Dispose();

    // =========================================================================
    // 1. NO DUPLICATE UPGRADE HEADER BUTTON
    // =========================================================================

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task Header_HasNoUpgradeNavButton_EvenAfterUpgradeIsDiscovered(string language)
    {
        // PlayerLevel 2 starts with 1 earned SP, making upgrade discovered
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language, PlayerLevel: 2));
        await f.StartAsync();

        Assert.True(f.TerminalViewModel.IsUpgradeDiscovered, "Upgrade must be discovered.");
        var html = await f.RenderedHtmlAsync();
        var header = MenuFixture.Header(html);

        // Header must NOT contain the separate top-right upgrade nav button or badge
        Assert.DoesNotContain("upgrade-nav-btn", header);
        Assert.DoesNotContain("header-sp-badge", header);
        Assert.DoesNotContain("data-spotlight-id=\"upgrade-header\"", header);

        // Exactly one header action: the Menu control
        var menu = Regex.Matches(header, @"<button[^>]*class=""button button-pause pause-practice-btn""[^>]*>[\s\S]*?</button>");
        Assert.Single(menu);
        Assert.Single(Regex.Matches(header, "<button"));
    }

    // =========================================================================
    // 2. MENU UPGRADE TERMINAL POSITION AND AVAILABILITY
    // =========================================================================

    [Fact]
    public async Task Menu_UpgradeTerminal_HiddenBeforeEarnedDiscovery()
    {
        // PlayerLevel 1 has 0 SP, upgrade not discovered
        await using var f = await MenuFixture.CreateAsync(_suite, new(PlayerLevel: 1));
        await f.StartAsync();
        Assert.False(f.TerminalViewModel.IsUpgradeDiscovered);

        await f.OpenMenuAsync();
        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());

        // Tutorial is offered, but Upgrade Terminal is NOT shown before discovery
        Assert.Contains("game-menu-tutorial", menu);
        Assert.DoesNotContain("pause-upgrade-btn", menu);
    }

    [Theory]
    [InlineData("en", "Upgrade Terminal", "Tutorial & help")]
    [InlineData("de", "Upgrade-Terminal", "Tutorial und Hilfe")]
    [InlineData("ru", "Терминал улучшений", "Обучение и помощь")]
    public async Task Menu_UpgradeTerminal_IsPlacedImmediatelyBelowTutorialAndHelp_WhenDiscovered(string language, string upgradeTitle, string tutorialTitle)
    {
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language, PlayerLevel: 2));
        await f.StartAsync();
        Assert.True(f.TerminalViewModel.IsUpgradeDiscovered);

        await f.OpenMenuAsync();
        var menu = MenuFixture.Menu(await f.RenderedHtmlAsync());

        Assert.Contains("game-menu-tutorial", menu);
        Assert.Contains("pause-upgrade-btn", menu);
        Assert.Contains(upgradeTitle, menu);
        Assert.Contains(tutorialTitle, menu);

        // Verify order: Tutorial & Help button appears before Upgrade Terminal button in the DOM
        var tutorialIndex = menu.IndexOf("game-menu-tutorial", StringComparison.Ordinal);
        var upgradeIndex = menu.IndexOf("pause-upgrade-btn", StringComparison.Ordinal);
        Assert.True(tutorialIndex >= 0, "Tutorial button must be present in menu.");
        Assert.True(upgradeIndex > tutorialIndex, "Upgrade Terminal must appear immediately after Tutorial & Help in menu.");

        // Opening Upgrade Terminal from the menu works
        await f.InvokeAsync("OpenUpgradeTerminalAsync");
        Assert.True(f.Field<bool>("_isUpgradeTerminalOpen"));
        var terminalHtml = await f.RenderedHtmlAsync();
        Assert.Contains("upgrade-terminal-dialog", terminalHtml);

        // Closing returns to menu
        await f.InvokeAsync("CloseUpgradeTerminal");
        Assert.False(f.Field<bool>("_isUpgradeTerminalOpen"));
    }

    // =========================================================================
    // 3. FIRST UPGRADE ONBOARDING: TRUTHFUL INSTRUCTIONS (MENU -> UPGRADE TERMINAL)
    // =========================================================================

    [Theory]
    [InlineData("en", "Menu", "Upgrade Terminal")]
    [InlineData("de", "Menü", "Upgrade-Terminal")]
    [InlineData("ru", "Меню", "Терминал улучшений")]
    public async Task UpgradeSpotlight_GuidesToMenuUpgradeTerminal_WithoutObsoleteHeaderSpotlight(string language, string menuLabel, string upgradeLabel)
    {
        var justBelowLevel2 = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2) - 1;
        await using var f = await MenuFixture.CreateAsync(_suite, new(Language: language, CumulativeXp: justBelowLevel2));
        await f.StartAsync();

        // Answering correctly triggers level up to 2 and earns first SP
        await f.SubmitAsync(correct: true);
        var progression = await f.Store.GetProgressionAsync();
        Assert.Equal(2, progression.PlayerLevel);
        Assert.Equal(1, progression.EarnedSkillPoints);
        Assert.True(f.TerminalViewModel.IsUpgradeDiscovered);

        // Upgrade spotlight opens as one-time presentation
        Assert.True(f.Field<bool>("_isUpgradeSpotlightOpen"));

        var html = await f.RenderedHtmlAsync();

        // Must NOT spotlight header
        Assert.DoesNotContain("data-spotlight-active=\"upgrade-header\"", html);
        Assert.DoesNotContain("data-spotlight-id=\"upgrade-header\"", html);

        // Must display description instructing Menu -> Upgrade Terminal
        var desc = f.Localizer["UpgradeSpotlight_Description"];
        Assert.Contains(menuLabel, desc);
        Assert.Contains(upgradeLabel, desc);
        Assert.Contains(desc, html);

        // Later button safely dismisses spotlight without opening terminal
        await f.InvokeAsync("DismissUpgradeSpotlight");
        Assert.False(f.Field<bool>("_isUpgradeSpotlightOpen"));
    }

    // =========================================================================
    // 4. FIVE-STEP TUTORIAL & HELP (MANUAL REPLAY)
    // =========================================================================

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task Tutorial_ManualReplay_HasFiveNumberedGuidanceSteps_FollowStoryCard(string language)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        await using var tutorial = await RenderedTutorialHost.CreateAsync(language, isManualReplay: true);
        var html = await tutorial.HtmlAsync();

        // Step 0: Story card
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);
        Assert.Contains("data-step=\"StoryCard\"", html);

        // Step 1: Enemy information (Spotlight 1 of 5)
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.Enemy, tutorial.Component.CurrentStep);
        var step1 = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_StepProgress", 1, 5], step1);
        Assert.Contains(localizer["CyberDefense_Tutorial_Enemy_Title"], step1);

        // Step 2: Solving & Keypad (Spotlight 2 of 5)
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);
        var step2 = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_StepProgress", 2, 5], step2);
        Assert.Contains(localizer["CyberDefense_Tutorial_ExpressionKeypad_Title"], step2);

        // Step 3: Player Integrity (Spotlight 3 of 5)
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);
        var step3 = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_StepProgress", 3, 5], step3);
        Assert.Contains(localizer["CyberDefense_Tutorial_SystemIntegrity_Title"], step3);

        // Step 4: Upgrade Terminal (Spotlight 4 of 5)
        await tutorial.NextAsync();
        Assert.Equal((CyberDefenseTutorialStep)4, tutorial.Component.CurrentStep);
        var step4 = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_StepProgress", 4, 5], step4);
        Assert.Contains(localizer["CyberDefense_Tutorial_UpgradeTerminal_Title"], step4);
        var upgradeBody = localizer["CyberDefense_Tutorial_UpgradeTerminal_Body"];
        Assert.False(string.IsNullOrWhiteSpace(upgradeBody));
        Assert.Contains(upgradeBody, step4);

        // Step 5: Abilities (Spotlight 5 of 5)
        await tutorial.NextAsync();
        Assert.Equal((CyberDefenseTutorialStep)5, tutorial.Component.CurrentStep);
        var step5 = await tutorial.HtmlAsync();
        Assert.Contains(localizer["CyberDefense_Tutorial_StepProgress", 5, 5], step5);
        Assert.Contains(localizer["CyberDefense_Tutorial_Abilities_Title"], step5);
        var abilitiesBody = localizer["CyberDefense_Tutorial_Abilities_Body"];
        Assert.False(string.IsNullOrWhiteSpace(abilitiesBody));
        Assert.Contains(abilitiesBody, step5);

        // Step 5 has Finish button instead of Next
        Assert.Contains(localizer["CyberDefense_Tutorial_Finish"], step5);
        Assert.DoesNotContain(localizer["CyberDefense_Tutorial_Next"], step5);

        // Finish completes
        await tutorial.FinishAsync();
        Assert.Equal(1, tutorial.FinishedCount);
    }

    [Fact]
    public async Task Tutorial_FirstRun_RetainsConciseThreeSteps()
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference("en");

        await using var tutorial = await RenderedTutorialHost.CreateAsync("en", isManualReplay: false);

        // Step 0: Story card
        Assert.Equal(CyberDefenseTutorialStep.StoryCard, tutorial.Component.CurrentStep);

        // Step 1: Enemy (1 of 3)
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.Enemy, tutorial.Component.CurrentStep);
        Assert.Contains("Spotlight 1 of 3", await tutorial.HtmlAsync());

        // Step 2: ExpressionKeypad (2 of 3)
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.ExpressionKeypad, tutorial.Component.CurrentStep);
        Assert.Contains("Spotlight 2 of 3", await tutorial.HtmlAsync());

        // Step 3: SystemIntegrity (3 of 3) -> Finish
        await tutorial.NextAsync();
        Assert.Equal(CyberDefenseTutorialStep.SystemIntegrity, tutorial.Component.CurrentStep);
        var step3 = await tutorial.HtmlAsync();
        Assert.Contains("Spotlight 3 of 3", step3);
        Assert.Contains(localizer["CyberDefense_Tutorial_Finish"], step3);

        await tutorial.FinishAsync();
        Assert.Equal(1, tutorial.FinishedCount);
    }

    [Theory]
    [InlineData("en", "Attack", "Firewall", "Critical Strike", "Overdrive")]
    [InlineData("de", "Angriffskraft", "Firewall", "Kritischer Treffer", "Overdrive")]
    [InlineData("ru", "атаки", "файрвол", "Критический удар", "Овердрайв")]
    public void Abilities_TruthfulMechanics_ExplainCurrentAbilitiesWithoutInventingFeatures(string language, string attackTerm, string firewallTerm, string criticalTerm, string overdriveTerm)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);

        var body = localizer["CyberDefense_Tutorial_Abilities_Body"];
        Assert.Contains(attackTerm, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(firewallTerm, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(criticalTerm, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(overdriveTerm, body, StringComparison.OrdinalIgnoreCase);

        // Mentions required unlock levels
        Assert.Contains("5", body);
        Assert.Contains("12", body);
        Assert.Contains("20", body);
    }

    // =========================================================================
    // 5. TEST HOST HARNESS FOR TUTORIAL COMPONENT
    // =========================================================================

    private sealed class TutorialWrapperHost : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.CyberDefenseTutorial> Capture { get; set; } = null!;
        [Parameter] public Func<Task>? OnFinished { get; set; }
        [Parameter] public Func<Task>? OnSkipped { get; set; }
        [Parameter] public ILocalizationService? Localizer { get; set; }
        [Parameter] public bool IsManualReplay { get; set; }

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
            builder.AddAttribute(4, "IsManualReplay", IsManualReplay);
            builder.AddComponentReferenceCapture(5, instance => Capture((global::MathFirst.Core.Tests.CyberDefenseTutorial)instance));
            builder.CloseComponent();
        }
    }

    private sealed class RenderedTutorialHost : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly global::MathFirst.Core.Tests.CyberDefenseTutorial _component;
        public int FinishedCount { get; set; }
        public int SkippedCount { get; set; }
        public global::MathFirst.Core.Tests.CyberDefenseTutorial Component => _component;

        private RenderedTutorialHost(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, global::MathFirst.Core.Tests.CyberDefenseTutorial component)
        {
            _services = services;
            _renderer = renderer;
            _root = root;
            _component = component;
        }

        public static async Task<RenderedTutorialHost> CreateAsync(string language, bool isManualReplay)
        {
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILocalizationService>(localizer)
                .BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

            global::MathFirst.Core.Tests.CyberDefenseTutorial capturedComponent = null!;
            RenderedTutorialHost result = null!;

            var root = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                return await renderer.RenderComponentAsync<TutorialWrapperHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Localizer"] = localizer,
                    ["IsManualReplay"] = isManualReplay,
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.CyberDefenseTutorial>)(c => capturedComponent = c),
                    ["OnFinished"] = (Func<Task>)(() => { if (result != null) result.FinishedCount++; return Task.CompletedTask; }),
                    ["OnSkipped"] = (Func<Task>)(() => { if (result != null) result.SkippedCount++; return Task.CompletedTask; })
                }));
            });

            result = new RenderedTutorialHost(services, renderer, root, capturedComponent);
            return result;
        }

        public async Task<string> HtmlAsync()
        {
            return await _renderer.Dispatcher.InvokeAsync(() =>
            {
                using var writer = new StringWriter();
                _root.WriteHtmlTo(writer);
                return WebUtility.HtmlDecode(writer.ToString());
            });
        }

        public Task NextAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            await _component.NextAsync();
        });

        public Task BackAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            await _component.BackAsync();
        });

        public Task SkipAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            await _component.SkipAsync();
        });

        public Task FinishAsync() => _renderer.Dispatcher.InvokeAsync(async () =>
        {
            await _component.FinishAsync();
        });

        public ValueTask DisposeAsync() => _services.DisposeAsync();
    }
}
