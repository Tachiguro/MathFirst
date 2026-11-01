namespace MathFirst.Core.Tests.CyberDefense;

using System.Net;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class CyberDefenseHudRenderingTests
{
    [Theory]
    [InlineData("en", "HP: 73 / 100")]
    [InlineData("de", "KP: 73 / 100")]
    [InlineData("ru", "ОЗ: 73 / 100")]
    public async Task Hud_VisiblePlayerHpUsesLocalizedAuthoritativeValues(string language, string expected)
    {
        await using var hud = await RenderedHud.CreateAsync(language);
        await hud.ShowAsync(CyberDefenseRunState.CreateActive(1, 0, 73,
            new OpponentState(OpponentKind.Normal, 2, 1), 3, 2, 0));

        var html = await hud.HtmlAsync();
        // Issue #136: a heart names the row and the real values are shown; the localized text is the meter's name.
        Assert.Contains("<span class=\"player-hp-value\">73 / 100</span>", html);
        Assert.Contains("player-hp-icon", html);
        Assert.Matches($"class=\"meter-bar player-hp-bar\"[^>]*aria-label=\"{expected}\"[^>]*aria-valuenow=\"73\"[^>]*aria-valuemax=\"100\"", html);
        Assert.Contains("style=\"width: 73%\"", html);
    }

    [Theory]
    [InlineData("en", OpponentKind.Normal, "HP: 1 / 2")]
    [InlineData("de", OpponentKind.Normal, "KP: 1 / 2")]
    [InlineData("ru", OpponentKind.Normal, "ОЗ: 1 / 2")]
    [InlineData("en", OpponentKind.Boss, "HP: 1 / 12")]
    [InlineData("de", OpponentKind.Boss, "KP: 1 / 12")]
    [InlineData("ru", OpponentKind.Boss, "ОЗ: 1 / 12")]
    public async Task Hud_EnemyProgressbarExposesLocalizedCurrentAndMaximumHp(
        string language, OpponentKind kind, string expected)
    {
        await using var hud = await RenderedHud.CreateAsync(language);
        await hud.ShowAsync(CyberDefenseRunState.CreateActive(1, kind == OpponentKind.Boss ? 5 : 0, 100,
            new OpponentState(kind, kind == OpponentKind.Boss ? 12 : 2, 1), 2, 1, 0));

        var html = await hud.HtmlAsync();
        var meter = Regex.Match(html, "<div class=\"meter-bar enemy-hp-bar[^>]*>").Value;
        Assert.Contains($"aria-valuetext=\"{expected}\"", meter);
        Assert.Contains("aria-valuenow=\"1\"", meter);
        Assert.Contains("aria-valuemin=\"0\"", meter);
        Assert.Contains(kind == OpponentKind.Boss ? "aria-valuemax=\"12\"" : "aria-valuemax=\"2\"", meter);
        Assert.Contains("role=\"progressbar\"", meter);
        Assert.Equal(kind == OpponentKind.Boss, html.Contains("sector-boss-badge"));
        Assert.Contains("scene-sector-tag", html);
        Assert.Contains("scene-wave-tag", html);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task Hud_RadarTracksParametersWithoutRemovingCombatStatus_AndRendersNoSectorUpgradeEntry(string language)
    {
        await using var hud = await RenderedHud.CreateAsync(language);
        await hud.ShowControlsAsync(3000);
        var html = await hud.HtmlAsync();
        // The single Upgrade entry lives in the page header (Issue #122); the sector HUD renders no button at all.
        Assert.DoesNotContain("upgrade-indicator", html);
        Assert.DoesNotContain("<button", html);
        Assert.DoesNotContain("class=\"radar-timing-arc\"", html);
        Assert.Contains("tactical-radar-widget", html);

        await hud.ShowControlsAsync(0);
        html = await hud.HtmlAsync();
        Assert.DoesNotContain("upgrade-indicator", html);
        Assert.Contains("class=\"radar-timing-arc\"", html);
        Assert.Contains("scene-sector-tag", html);
        Assert.Contains("player-hp-bar", html);
        Assert.Contains("enemy-hp-bar", html);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 3)]
    public async Task Hud_RendersOnlyAuthoritativeShieldSlots(int capacity, int active)
    {
        await using var hud = await RenderedHud.CreateAsync();
        await hud.ShowAsync(CreateRun(capacity, active));

        AssertShields(await hud.HtmlAsync(), capacity, active);
    }

    [Theory]
    [InlineData("en", "Shield: 1 / 2")]
    [InlineData("de", "Schild: 1 / 2")]
    [InlineData("ru", "Щит: 1 / 2")]
    public async Task Hud_ShieldMeterHasLocalizedAccessibleState(string language, string expectedState)
    {
        await using var hud = await RenderedHud.CreateAsync(language);
        await hud.ShowAsync(CreateRun(2, 1));

        var html = await hud.HtmlAsync();
        Assert.Contains($"aria-valuetext=\"{expectedState}\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Matches("class=\"shield-badges-row\"[^>]*aria-label=\"[^\"]+\"", html);
    }

    [Fact]
    public async Task Hud_SameComponentReflectsShieldLossRechargeAndRefresh()
    {
        await using var hud = await RenderedHud.CreateAsync();
        var run = CreateRun(2, 2);
        await hud.ShowAsync(run);
        AssertShields(await hud.HtmlAsync(), 2, 2);

        run = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1).NextState;
        await hud.ShowAsync(run);
        AssertShields(await hud.HtmlAsync(), 2, 1);

        // A persisted near-threshold state is refreshed, then the real combat transition restores a shield.
        run = CreateRun(2, 1, recharge: 28);
        await hud.ShowAsync(run);
        AssertShields(await hud.HtmlAsync(), 2, 1);
        run = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1).NextState;
        await hud.ShowAsync(run);
        AssertShields(await hud.HtmlAsync(), 2, 2, opponentHp: 1);

        await hud.ShowAsync(CyberDefenseRunState.InitialRun());
        AssertShields(await hud.HtmlAsync(), 0, 0);
    }

    [Fact]
    public async Task Hud_EmergencyShieldRemainsVisibleWithoutPermanentFirewallEntitlement()
    {
        await using var hud = await RenderedHud.CreateAsync();
        // No rank/upgrade data is supplied to the HUD: temporary run-state capacity is authoritative.
        var emergencyRun = CreateRun(1, 1);
        await hud.ShowAsync(emergencyRun);
        AssertShields(await hud.HtmlAsync(), 1, 1);

        emergencyRun = CyberDefenseStateMachine.ApplyAttempt(emergencyRun, false, 1).NextState;
        await hud.ShowAsync(emergencyRun);
        AssertShields(await hud.HtmlAsync(), 1, 0);
    }

    [Fact]
    public void Hud_ShieldOnlyChangesInvalidatePresentationEquality()
    {
        var full = CyberDefenseHudViewModel.FromRunState(CreateRun(2, 2));
        var depleted = CyberDefenseHudViewModel.FromRunState(CreateRun(2, 1));
        var locked = CyberDefenseHudViewModel.FromRunState(CreateRun(0, 0));
        var available = CyberDefenseHudViewModel.FromRunState(CreateRun(1, 0));

        Assert.NotEqual(full, depleted);
        Assert.NotEqual(locked, available);
        var refreshed = CyberDefenseHudViewModel.FromRunState(CreateRun(2, 2));
        Assert.Equal(full, refreshed);
        Assert.Equal(full.GetHashCode(), refreshed.GetHashCode());
    }

    [Fact]
    public async Task Hud_CoordinatorRefreshRendersPersistedRankZeroEmergencyShield()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstHudTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new SqliteGameplayStore(Path.Combine(directory, "gameplay.db"));
            await store.InitializeAsync();
            var coordinator = new CyberDefenseCombatCoordinator(store);
            try
            {
                await coordinator.InitializeAsync();
                await using var hud = await RenderedHud.CreateAsync();
                await hud.ShowModelAsync(coordinator.CurrentViewModel);
                AssertShields(await hud.HtmlAsync(), 0, 0);

                await store.ExecuteUnderGateForTestAsync(async connection =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE gameplay_progression SET consecutive_early_defeats = 2 WHERE id = 1; " +
                                          "UPDATE gameplay_run_state SET player_current_hp = 1 WHERE id = 1;";
                    await command.ExecuteNonQueryAsync();
                });
                await store.ApplyAttemptTransactionAsync(new CyberDefensePendingIntentRecord(
                    "hud-emergency-reboot", "add:1+1", false, true, 250, 0,
                    new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), effectiveAttackDamage: 1));
                Assert.Equal(0, (await store.GetProgressionAsync()).FirewallRank);

                await coordinator.RefreshStateAsync();
                await hud.ShowModelAsync(coordinator.CurrentViewModel);
                AssertShields(await hud.HtmlAsync(), 1, 1);

                await store.ApplyAttemptTransactionAsync(new CyberDefensePendingIntentRecord(
                    "hud-emergency-loss", "add:1+1", false, true, 250, 0,
                    new DateTimeOffset(2026, 10, 21, 0, 0, 1, TimeSpan.Zero), effectiveAttackDamage: 1));
                await coordinator.RefreshStateAsync();
                await hud.ShowModelAsync(coordinator.CurrentViewModel);
                AssertShields(await hud.HtmlAsync(), 1, 0);

                // Clearing Sector 1 removes temporary capacity rather than leaving a fictitious slot.
                await store.ExecuteUnderGateForTestAsync(async connection =>
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "UPDATE gameplay_run_state SET opponent_index = 5, opponent_current_hp = 1 WHERE id = 1;";
                    await command.ExecuteNonQueryAsync();
                });
                await store.ApplyAttemptTransactionAsync(new CyberDefensePendingIntentRecord(
                    "hud-emergency-sector-clear", "add:1+1", true, true, 250, 0,
                    new DateTimeOffset(2026, 10, 21, 0, 0, 2, TimeSpan.Zero), effectiveAttackDamage: 1));
                await coordinator.RefreshStateAsync();
                Assert.Equal(2, coordinator.CurrentViewModel.SectorNumber);
                await hud.ShowModelAsync(coordinator.CurrentViewModel);
                AssertShields(await hud.HtmlAsync(), 0, 0);
            }
            finally
            {
                coordinator.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CyberDefenseRunState CreateRun(int capacity, int active, int recharge = 0) =>
        CyberDefenseRunState.CreateActive(1, 0, 100, new OpponentState(OpponentKind.Normal, 2, 2), capacity, active, recharge);

    private static void AssertShields(string html, int capacity, int active, int opponentHp = 2)
    {
        // Issue #136: one shield icon (intact while any shield is active) beside the authoritative active / capacity.
        Assert.Equal(capacity > 0 ? 1 : 0, Regex.Matches(html, "class=\"shield-badge ").Count);
        Assert.Equal(capacity > 0 && active > 0 ? 1 : 0, Regex.Matches(html, "class=\"shield-badge shield-intact\"").Count);
        Assert.Equal(capacity > 0 && active == 0 ? 1 : 0, Regex.Matches(html, "class=\"shield-badge shield-broken\"").Count);
        var meter = Regex.Match(html, "<div class=\"shield-badges-row\"[^>]*>");
        Assert.Equal(capacity > 0, meter.Success);
        if (capacity > 0)
        {
            Assert.Contains("role=\"meter\"", meter.Value);
            Assert.Contains("aria-valuemin=\"0\"", meter.Value);
            Assert.Contains($"aria-valuemax=\"{capacity}\"", meter.Value);
            Assert.Contains($"aria-valuenow=\"{active}\"", meter.Value);
            Assert.Contains($"<span class=\"shield-count-text\" aria-hidden=\"true\">{active} / {capacity}</span>", html);
        }

        var playerMeter = Regex.Match(html, "<div class=\"meter-bar player-hp-bar\"[^>]*>").Value;
        Assert.Contains("role=\"progressbar\"", playerMeter);
        Assert.Contains("aria-valuenow=\"100\"", playerMeter);
        Assert.Contains("aria-valuemax=\"100\"", playerMeter);
        var enemyMeter = Regex.Match(html, "<div class=\"meter-bar enemy-hp-bar[^>]*>").Value;
        Assert.Contains("role=\"progressbar\"", enemyMeter);
        Assert.Contains($"aria-valuenow=\"{opponentHp}\"", enemyMeter);
        Assert.Contains("tactical-radar-widget", html);
        Assert.Contains("scene-sector-tag", html);
        Assert.DoesNotContain("upgrade-indicator", html); // Issue #122: the header owns the single Upgrade entry.
    }

    public sealed class HudHost : ComponentBase
    {
        [Parameter] public Action<HudHost> Capture { get; set; } = null!;
        private CyberDefenseHudViewModel _model = CyberDefenseHudViewModel.Initial();
        private long _elapsed;
        protected override void OnInitialized() => Capture(this);
        public void Show(CyberDefenseRunState run)
            => ShowModel(CyberDefenseHudViewModel.FromRunState(run));
        public void ShowModel(CyberDefenseHudViewModel model)
        {
            _model = model;
            StateHasChanged();
        }

        public void ShowControls(long elapsed)
        {
            _elapsed = elapsed;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.CyberDefenseHud>(0);
            builder.AddAttribute(1, "ViewModel", _model);
            builder.AddAttribute(3, "ActiveElapsedMs", _elapsed);
            // Issue #134: the radar is shown only for a purchased Critical Strike rank; these tests cover an earned radar.
            builder.AddAttribute(4, "CriticalStrikeRank", (int?)1);
            builder.CloseComponent();
        }
    }

    private sealed class RenderedHud(ServiceProvider services, HtmlRenderer renderer) : IAsyncDisposable
    {
        private HudHost _host = null!;
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent _root;

        public static async Task<RenderedHud> CreateAsync(string language = "en")
        {
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(language);
            var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var hud = new RenderedHud(services, renderer);
            hud._root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<HudHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Capture"] = (Action<HudHost>)(host => hud._host = host) })));
            return hud;
        }

        public Task ShowAsync(CyberDefenseRunState run) => renderer.Dispatcher.InvokeAsync(() => _host.Show(run));
        public Task ShowModelAsync(CyberDefenseHudViewModel model) => renderer.Dispatcher.InvokeAsync(() => _host.ShowModel(model));
        public Task ShowControlsAsync(long elapsed) => renderer.Dispatcher.InvokeAsync(() => _host.ShowControls(elapsed));
        public Task<string> HtmlAsync() => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_root.ToHtmlString()));
        public async ValueTask DisposeAsync()
        {
            await renderer.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
