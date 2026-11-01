namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Issue #122: the real Home page with synthetic stores and a fake clock. Covers the simplified header,
/// the persistent Level/XP display, the arithmetic Stage strip and the single Upgrade entry.
/// </summary>
public sealed class Issue122HomeHudTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // =========================================================================
    // HEADER
    // =========================================================================

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task Header_HasNoModeToggle_ButKeepsUpgradeAndTheSingleMenu(string language)
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Language: language, PlayerLevel: 6));
        await f.InvokeHomeAsync("StartOrResumePractice");

        var html = await f.GetHtmlAsync();
        var header = Regex.Match(html, @"<header class=""mathfirst-header""[\s\S]*?</header>").Value;

        Assert.DoesNotContain("mode-toggle", header);
        Assert.DoesNotContain("aria-pressed", header);
        Assert.DoesNotContain(f.Localizer["CyberDefense_Mode_Calm"], header);
        Assert.DoesNotContain("upgrade-nav-btn", header);
        Assert.Contains("pause-practice-btn", header);
        // Issue #125: Settings moved into the menu; the header keeps no separate Settings link.
        Assert.DoesNotContain("href=\"settings\"", header);
    }

    [Fact]
    public void Settings_StillOwnsTheCyberDefenseCalmPreference_AndHomeNoLongerWritesIt()
    {
        var settings = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "Components", "Pages", "Settings.razor");
        var home = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "Components", "Pages", "Home.razor");
        var css = ChromiumLayoutHarness.ReadRepositoryFile("src", "MathFirst.App", "wwwroot", "app.css");

        Assert.Contains("Settings_CyberDefenseModeTitle", settings, StringComparison.Ordinal);
        Assert.Contains("SetCyberDefenseMode(true)", settings, StringComparison.Ordinal);
        Assert.Contains("SetCyberDefenseMode(false)", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCyberDefenseEnabled", home, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleCyberDefenseMode", home, StringComparison.Ordinal);
        Assert.DoesNotContain("mode-toggle", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModePreference_IsNeverChangedByHeaderInteraction()
    {
        await using var f = await HomeFixture.CreateAsync(this, new());
        await f.InvokeHomeAsync("StartOrResumePractice");
        await f.InvokeHomeAsync("PausePractice");
        await f.InvokeHomeAsync("OpenUpgradeTerminalAsync");
        await f.InvokeHomeAsync("CloseUpgradeTerminal");

        Assert.Equal(0, f.Preferences.ModeWrites);
        Assert.True(f.Preferences.GetCyberDefenseEnabled());
    }

    [Theory]
    [InlineData("en", 6)]
    [InlineData("de", 6)]
    [InlineData("ru", 80)]
    public async Task Header_HasNoUpgradeButton_AndMenuOffersUpgradeTerminalWhenDiscovered(string language, int level)
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Language: language, PlayerLevel: level));
        await f.InvokeHomeAsync("StartOrResumePractice");

        var html = await f.GetHtmlAsync();
        var header = Regex.Match(html, @"<header class=""mathfirst-header""[\s\S]*?</header>").Value;
        Assert.DoesNotContain("upgrade-nav-btn", header);
        Assert.DoesNotContain("header-sp-badge", header);
        Assert.DoesNotContain("upgrade-indicator-badge", html);
        Assert.DoesNotContain("upgrade-indicator", html);

        await f.InvokeHomeAsync("PausePractice");
        var menu = await f.GetHtmlAsync();
        Assert.Contains("pause-upgrade-btn", menu);
    }

    [Fact]
    public async Task PrimaryUpgradeButton_ShowsNoBadgeWithoutSkillPoints()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(PlayerLevel: 1));
        await f.InvokeHomeAsync("StartOrResumePractice");

        Assert.DoesNotContain("header-sp-badge", await f.GetHtmlAsync());
    }

    [Fact]
    public async Task Upgrade_OpensAndClosesWithPauseAndTutorialGuards()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.InvokeHomeAsync("StartOrResumePractice");
        var revision = f.Session.FactInstanceRevision;

        // A tutorial in progress owns the screen: the Upgrade action is inert and Pause is not offered.
        Assert.True((bool)f.Property("CanPausePractice")!);
        f.SetHomeField("_isTutorialOpen", true);
        await f.InvokeHomeQuietAsync("OpenUpgradeTerminalAsync");
        Assert.False(f.IsUpgradeTerminalOpen);
        Assert.False((bool)f.Property("CanPausePractice")!);
        f.SetHomeField("_isTutorialOpen", false);

        await f.InvokeHomeAsync("OpenUpgradeTerminalAsync");
        Assert.True(f.IsUpgradeTerminalOpen);
        var open = await f.GetHtmlAsync();
        Assert.Contains("upgrade-terminal-dialog", open);
        Assert.DoesNotContain("pause-practice-btn", open);
        Assert.DoesNotContain("upgrade-nav-btn", open);

        await f.InvokeHomeAsync("CloseUpgradeTerminal");
        Assert.False(f.IsUpgradeTerminalOpen);
        var closed = await f.GetHtmlAsync();
        Assert.DoesNotContain("upgrade-terminal-dialog", closed);
        Assert.Contains("pause-practice-btn", closed);
        Assert.Equal(revision, f.Session.FactInstanceRevision);

        await f.InvokeHomeAsync("PausePractice");
        Assert.Equal(PracticeGateState.ManualPause, f.Session.PracticeGate);
        Assert.Contains("pause-upgrade-btn", await f.GetHtmlAsync());
    }

    // =========================================================================
    // PLAYER LEVEL / XP
    // =========================================================================

    [Fact]
    public async Task Hud_ShowsPersistedPlayerLevelAndXp_FromStartup()
    {
        var xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(8) + 45;
        await using var f = await HomeFixture.CreateAsync(this, new(CumulativeXp: xp));
        await f.InvokeHomeAsync("StartOrResumePractice");

        var progress = CyberDefenseLevelPolicy.CalculateProgress(xp);
        var html = await f.GetHtmlAsync();
        Assert.Contains("<span class=\"player-level-label\">Lv. 8</span>", html);
        Assert.Contains($"<span class=\"player-xp-value\">45 / {progress.XpRequiredForNextLevel}</span>", html);
        Assert.Contains("SECTOR 1", html);
    }

    [Fact]
    public async Task Hud_UpdatesFromTheCommittedReceipt_AfterEveryCorrectAnswer()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(PlayerLevel: 1));
        await f.InvokeHomeAsync("StartOrResumePractice");
        Assert.Contains("0 / ", await f.GetHtmlAsync());

        var sawGain = false;
        for (var i = 0; i < 12 && !sawGain; i++)
        {
            await f.SubmitAsync(correct: true);
            var committed = await f.Store.GetProgressionAsync();
            var progress = CyberDefenseLevelPolicy.CalculateProgress(committed.CumulativeXp);
            var html = await f.GetHtmlAsync();
            // After every committed receipt the HUD shows exactly the committed progression.
            Assert.Contains($"<span class=\"player-level-label\">Lv. {progress.Level}</span>", html);
            Assert.Contains($"<span class=\"player-xp-value\">{progress.XpIntoCurrentLevel} / {progress.XpRequiredForNextLevel}</span>", html);
            sawGain = committed.CumulativeXp > 0;
        }

        Assert.True(sawGain, "A defeated opponent must have awarded XP within a dozen correct answers.");
    }

    [Fact]
    public async Task Hud_FollowsMultipleLevelUps_AndNeverShowsMoreThanWasCommitted()
    {
        var nearThreshold = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(3) - 1;
        await using var f = await HomeFixture.CreateAsync(this, new(CumulativeXp: nearThreshold));
        await f.InvokeHomeAsync("StartOrResumePractice");
        Assert.Contains("Lv. 2</span>", await f.GetHtmlAsync());

        for (var i = 0; i < 12 && (await f.Store.GetProgressionAsync()).CumulativeXp == nearThreshold; i++)
        {
            await f.SubmitAsync(correct: true);
        }

        var committed = await f.Store.GetProgressionAsync();
        var expected = CyberDefenseLevelPolicy.CalculateProgress(committed.CumulativeXp);
        Assert.True(expected.Level >= 3);
        Assert.Contains($"Lv. {expected.Level}</span>", await f.GetHtmlAsync());
        Assert.Equal(committed.PlayerLevel, expected.Level);
    }

    [Fact]
    public async Task Hud_KeepsLevelAndXp_AcrossPauseUpgradePurchaseAndResume()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(PlayerLevel: 6));
        await f.InvokeHomeAsync("StartOrResumePractice");
        var before = await f.GetHtmlAsync();
        var level = Regex.Match(before, "player-level-label\">([^<]*)<").Groups[1].Value;
        var xp = Regex.Match(before, "player-xp-value\">([^<]*)<").Groups[1].Value;

        await f.InvokeHomeAsync("PausePractice");
        await f.InvokeHomeAsync("OpenUpgradeTerminalAsync");
        var vm = f.TerminalViewModel;
        var purchase = await vm.PurchaseUpgradeAsync();
        Assert.True(purchase.IsSuccess);
        await f.InvokeHomeAsync("CloseUpgradeTerminal");
        await f.InvokeHomeAsync("StartOrResumePractice");

        var after = await f.GetHtmlAsync();
        Assert.Equal(level, Regex.Match(after, "player-level-label\">([^<]*)<").Groups[1].Value);
        Assert.Equal(xp, Regex.Match(after, "player-xp-value\">([^<]*)<").Groups[1].Value);
        Assert.True(vm.AvailableSkillPoints < 5);
    }

    [Fact]
    public async Task Hud_RelocalizesInPlace_WithoutChangingTheCommittedValues()
    {
        var xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(8) + 45;
        await using var f = await HomeFixture.CreateAsync(this, new(CumulativeXp: xp));
        await f.InvokeHomeAsync("StartOrResumePractice");

        f.Localizer.ApplyLanguagePreference("ru");
        await f.InvokeHomeAsync("OnLanguageChanged", null, EventArgs.Empty);

        var html = await f.GetHtmlAsync();
        Assert.Contains("<span class=\"player-level-label\">Ур. 8</span>", html);
        Assert.Contains("aria-label=\"Уровень игрока 8\"", html);
        Assert.Contains("45 / ", html);
    }

    [Fact]
    public async Task Hud_RefreshesAfterBackgroundAndForegroundTransitions_FromCommittedStoreState()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(PlayerLevel: 2));
        await f.InvokeHomeAsync("StartOrResumePractice");
        Assert.Contains("Lv. 2</span>", await f.GetHtmlAsync());

        await f.SeedPlayerLevelAsync(5);
        f.Session.SetAppForeground(false);
        f.Session.SetAppForeground(true);
        await f.AwaitTerminalRefreshAsync();
        // Returning from the background lands on the explicit resume gate; resuming shows the refreshed HUD.
        await f.InvokeHomeAsync("StartOrResumePractice");

        Assert.Contains("Lv. 5</span>", await f.GetHtmlAsync());
    }

    // =========================================================================
    // ARITHMETIC STAGE STRIP
    // =========================================================================

    [Fact]
    public async Task StageStrip_AdditionOnlyCurriculum_ShowsOnlyAdditionWithItsOwnStage()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Stage: CurriculumStage.Stage1_Addition,
            Bands: [(ArithmeticOperation.Addition, 10)], Preferences: [ArithmeticOperation.Addition]));
        await f.InvokeHomeAsync("StartOrResumePractice");

        Assert.Equal(["+ Stage 11"], StageStrip(await f.GetHtmlAsync()));
    }

    [Fact]
    public async Task StageStrip_CurriculumManaged_ShowsUnlockedSubtractionEvenWhenPreferencesOnlyEnableAddition()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Stage: CurriculumStage.Stage2_Subtraction,
            Bands: [(ArithmeticOperation.Addition, 10)], Preferences: [ArithmeticOperation.Addition]));
        await f.InvokeHomeAsync("StartOrResumePractice");

        var html = await f.GetHtmlAsync();
        Assert.Equal(["+ Stage 11", "− Stage 1"], StageStrip(html));
        Assert.Contains("aria-label=\"Subtraction: Stage 1\"", html);
        Assert.DoesNotContain(f.Localizer["Settings_OperationLocked"], html);
    }

    [Theory]
    [InlineData("en", "+ Stage 11", "− Stage 1")]
    [InlineData("de", "+ Stufe 11", "− Stufe 1")]
    [InlineData("ru", "+ Уровень 11", "− Уровень 1")]
    public async Task StageStrip_IsLocalizedAndKeepsOperationSymbols(string language, string addition, string subtraction)
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Language: language, Stage: CurriculumStage.Stage2_Subtraction,
            Bands: [(ArithmeticOperation.Addition, 10)]));
        await f.InvokeHomeAsync("StartOrResumePractice");

        Assert.Equal([addition, subtraction], StageStrip(await f.GetHtmlAsync()));
    }

    [Fact]
    public async Task StageStrip_MultiplicationAndDivisionAppearOnlyWhenUnlocked_InCanonicalOrder()
    {
        await using (var stage3 = await HomeFixture.CreateAsync(this, new(Stage: CurriculumStage.Stage3_Multiplication,
            Bands: [(ArithmeticOperation.Addition, 11), (ArithmeticOperation.Subtraction, 4)])))
        {
            await stage3.InvokeHomeAsync("StartOrResumePractice");
            Assert.Equal(["+ Stage 12", "− Stage 5", "× Stage 1"], StageStrip(await stage3.GetHtmlAsync()));
        }

        await using var stage4 = await HomeFixture.CreateAsync(this, new(Stage: CurriculumStage.Stage4_Division,
            Bands: [(ArithmeticOperation.Addition, 11), (ArithmeticOperation.Subtraction, 4), (ArithmeticOperation.Multiplication, 2)]));
        await stage4.InvokeHomeAsync("StartOrResumePractice");
        Assert.Equal(["+ Stage 12", "− Stage 5", "× Stage 3", "÷ Stage 1"], StageStrip(await stage4.GetHtmlAsync()));
    }

    [Fact]
    public async Task StageStrip_CustomPractice_KeepsThePreferenceSemantics()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Mode: PracticeMode.Custom, Stage: CurriculumStage.Stage1_Addition,
            Bands: [(ArithmeticOperation.Subtraction, 6), (ArithmeticOperation.Division, 1)],
            Preferences: [ArithmeticOperation.Division, ArithmeticOperation.Subtraction]));
        await f.InvokeHomeAsync("StartOrResumePractice");

        Assert.Equal(["− Stage 7", "÷ Stage 2"], StageStrip(await f.GetHtmlAsync()));
    }

    [Fact]
    public async Task StageStrip_ShowsEveryUnlockedOperation_WhileThePracticedFactBelongsToAnotherOne()
    {
        await using var f = await HomeFixture.CreateAsync(this, new(Stage: CurriculumStage.Stage2_Subtraction,
            Bands: [(ArithmeticOperation.Addition, 10)]));
        await f.InvokeHomeAsync("StartOrResumePractice");

        var current = f.Session.CurrentFact!.Operation;
        var html = await f.GetHtmlAsync();
        Assert.Equal(["+ Stage 11", "− Stage 1"], StageStrip(html));
        Assert.Single(Regex.Matches(html, "operation-progress-entry op-current"));
        Assert.Contains(current is ArithmeticOperation.Addition ? "+" : "−",
            Regex.Match(html, @"operation-progress-entry op-current[\s\S]*?operation-progress-symbol[^>]*>([^<]*)<").Groups[1].Value);
    }

    private static string[] StageStrip(string html) => Regex.Matches(html,
            @"<span class=""operation-progress-symbol"" aria-hidden=""true"">([^<]*)</span>\s*<span class=""operation-progress-stage"">([^<]*)</span>")
        .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}").ToArray();

    // =========================================================================
    // FIXTURE
    // =========================================================================

    internal sealed record HomeOptions(
        string Language = "en",
        int PlayerLevel = 1,
        long? CumulativeXp = null,
        PracticeMode Mode = PracticeMode.CurriculumManaged,
        CurriculumStage Stage = CurriculumStage.Stage1_Addition,
        (ArithmeticOperation Operation, int Band)[]? Bands = null,
        ArithmeticOperation[]? Preferences = null,
        int FirewallPurchases = 0,
        // Issue #134: purchased Critical Strike ranks and an optional synthetic run-state seed, applied before Home mounts.
        int CriticalStrikePurchases = 0,
        Func<SqliteGameplayStore, Task>? SeedRunState = null);

    internal sealed class HomeFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly global::MathFirst.Core.Tests.Home _home;

        public TrainingSession Session { get; }
        public SqliteGameplayStore Store { get; }
        public LocalizationService Localizer { get; }
        public TestPreferences Preferences { get; }

        public bool IsUpgradeTerminalOpen => (bool)Field("_isUpgradeTerminalOpen")!;
        public UpgradeTerminalViewModel TerminalViewModel => (UpgradeTerminalViewModel)Field("_upgradeTerminalViewModel")!;

        private HomeFixture(ServiceProvider services, HtmlRenderer renderer, HtmlRootComponent root, global::MathFirst.Core.Tests.Home home,
            TrainingSession session, SqliteGameplayStore store, LocalizationService localizer, TestPreferences preferences)
        {
            _services = services;
            _renderer = renderer;
            _root = root;
            _home = home;
            Session = session;
            Store = store;
            Localizer = localizer;
            Preferences = preferences;
        }

        public static async Task<HomeFixture> CreateAsync(Issue122HomeHudTests suite, HomeOptions options)
        {
            var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue122Home_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            suite._tempDirectories.Add(directory);

            var clock = new FakeClock();
            var preferences = new TestPreferences(options.Language, options.Preferences ?? [ArithmeticOperation.Addition]);
            var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-issue122-home.db"));
            await store.InitializeAsync();
            await SeedAsync(store, options.CumulativeXp ?? CyberDefenseLevelPolicy.GetCumulativeXpThreshold(options.PlayerLevel));
            for (var rank = 0; rank < options.FirewallPurchases; rank++) await store.PurchaseFirewallUpgradeAsync(rank, 0);
            for (var rank = 0; rank < options.CriticalStrikePurchases; rank++) await store.PurchaseCriticalStrikeUpgradeAsync(rank, 0);
            if (options.SeedRunState is { } seedRunState) await seedRunState(store);

            var progression = LearnerProgression.CreateFresh();
            progression.CurriculumStage = options.Stage;
            foreach (var (operation, band) in options.Bands ?? [])
            {
                progression.OperationProgressions[operation] = new OperationProgression(operation, band, 0);
            }

            var learner = new TestLearnerStore(progression);
            var session = new TrainingSession(learner, clock, preferenceStore: preferences, practiceMode: options.Mode);
            var coordinator = new CyberDefenseCombatCoordinator(store, new CyberDefenseSubmissionConsumer(store, learner));
            var ceremony = new CyberDefensePresentationSequence((_, _) => Task.CompletedTask);
            var localizer = new LocalizationService();
            localizer.ApplyLanguagePreference(options.Language);

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(session)
                .AddSingleton(ceremony)
                .AddSingleton<IGameplayStore>(store)
                .AddSingleton<ICyberDefenseCombatCoordinator>(coordinator)
                .AddSingleton<ILocalizationService>(localizer)
                .AddSingleton<IPreferenceStore>(preferences)
                .AddSingleton<ICyberDefenseModePreferences>(preferences)
                .AddSingleton<ICyberDefenseTutorialPreferences>(preferences)
                .AddSingleton(new CyberDefenseSessionState(preferences))
                .AddSingleton<ICyberDefenseTutorialReplayRequests>(sp => sp.GetRequiredService<CyberDefenseSessionState>())
                .AddSingleton(new PracticeGateCopyState(new PracticeCopySelector(new PracticeCopyLibrary())))
                .AddSingleton<IAppResetCoordinator>(new NoResetCoordinator())
                .AddSingleton<MathFirst.Application.Navigation.IAppBackNavigationCoordinator>(new MathFirst.Application.Navigation.AppBackNavigationCoordinator())
                .AddSingleton<IHapticFeedbackService>(new SilentHaptics())
                .AddSingleton<IJSRuntime>(new NoJsRuntime())
                .BuildServiceProvider();

            var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            global::MathFirst.Core.Tests.Home captured = null!;
            var root = await renderer.Dispatcher.InvokeAsync(() =>
                renderer.RenderComponentAsync<HomeHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<global::MathFirst.Core.Tests.Home>)(h => captured = h)
                })));
            return new HomeFixture(services, renderer, root, captured, session, store, localizer, preferences);
        }

        public static async Task SeedAsync(SqliteGameplayStore store, long cumulativeXp)
        {
            var level = CyberDefenseLevelPolicy.GetLevel(cumulativeXp);
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.StoragePath}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = @level, earned_sp = @sp, spent_sp = 0, available_sp = @sp WHERE id = 1;";
            command.Parameters.AddWithValue("@xp", cumulativeXp);
            command.Parameters.AddWithValue("@level", level);
            command.Parameters.AddWithValue("@sp", level - 1);
            await command.ExecuteNonQueryAsync();
        }

        public Task SeedPlayerLevelAsync(int level) => SeedAsync(Store, CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level));

        public object? Field(string name) => typeof(global::MathFirst.Core.Tests.Home)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home);

        public object? Property(string name) => typeof(global::MathFirst.Core.Tests.Home)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_home);

        public Task InvokeHomeQuietAsync(string methodName) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var result = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null);
                if (result is Task task) await task;
            });

        public void SetHomeField(string name, object value) => typeof(global::MathFirst.Core.Tests.Home)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_home, value);

        public Task InvokeHomeAsync(string methodName, params object?[]? args) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var result = typeof(global::MathFirst.Core.Tests.Home)
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(_home, args is { Length: > 0 } ? args : null);
                if (result is Task task) await task;
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null);
            });

        public async Task AwaitTerminalRefreshAsync()
        {
            // The foreground handler marshals through the renderer dispatcher; let it enqueue the refresh first.
            await _renderer.Dispatcher.InvokeAsync(() => { });
            if (Field("_terminalRefreshTask") is Task refresh) await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            await _renderer.Dispatcher.InvokeAsync(() =>
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null));
        }

        public async Task SubmitAsync(bool correct)
        {
            await _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var answer = Session.CurrentFact!.CorrectResult + (correct ? 0 : 1);
                SetHomeField("_answerInput", answer.ToString(CultureInfo.InvariantCulture));
                var task = (Task)typeof(global::MathFirst.Core.Tests.Home).GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_home, null)!;
                await task;
            });
            await AwaitTerminalRefreshAsync();
        }

        public Task<string> GetHtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(_root.ToHtmlString()));

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
            Store.Dispose();
        }
    }

    internal sealed class HomeHost : ComponentBase
    {
        [Parameter] public Action<global::MathFirst.Core.Tests.Home> Capture { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<global::MathFirst.Core.Tests.Home>(0);
            builder.AddComponentReferenceCapture(1, instance => Capture((global::MathFirst.Core.Tests.Home)instance));
            builder.CloseComponent();
        }
    }

    internal sealed class TestPreferences(string language, ArithmeticOperation[] enabledOperations)
        : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        public int ModeWrites { get; private set; }
        public bool GetCyberDefenseEnabled() => true;
        public void SetCyberDefenseEnabled(bool enabled) => ModeWrites++;
        public bool GetTutorialCompleted() => true;
        public void SetTutorialCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => language;
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => enabledOperations.Contains(operation);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => enabledOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public bool GetHapticFeedbackEnabled() => false;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    internal sealed class TestLearnerStore(LearnerProgression initial) : ILearnerStore
    {
        private LearnerSnapshot _snapshot = new(initial, new Dictionary<string, ItemLearningState>(), [], 1, LearnerProgression.DefaultSchemaVersion);

        public string StoragePath => "synthetic://issue122-learner";
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(PracticeSelectionEvidenceRequest request, CancellationToken ct = default) =>
            Task.FromResult(PracticeSelectionEvidence.FromSnapshot(_snapshot, request));
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(ArithmeticOperation operation, long position, IReadOnlyList<string> ids, CancellationToken ct = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, position, ids));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet change, CancellationToken ct = default)
        {
            var items = _snapshot.ItemStates.ToDictionary(p => p.Key, p => p.Value);
            items[change.UpdatedItemState.FactId] = change.UpdatedItemState;
            _snapshot = new LearnerSnapshot(change.UpdatedProgression, items, _snapshot.RecentAttempts.Append(change.Attempt).ToArray(), change.ExpectedRevision + 1, LearnerProgression.DefaultSchemaVersion);
            _snapshot.Progression.StoreRevision = _snapshot.Revision;
            return Task.FromResult(PersistenceResult.Success(_snapshot.Revision));
        }
        public Task<CommittedLearnerAttemptEvidence?> GetCommittedAttemptEvidenceAsync(string id, CancellationToken ct = default)
        {
            var attempt = _snapshot.RecentAttempts.FirstOrDefault(a => a.SubmissionId == id);
            return Task.FromResult(attempt is null ? null : new CommittedLearnerAttemptEvidence(id, attempt.FactId, attempt.IsCorrect, attempt.ResponseLatencyMs, attempt.PracticePosition, attempt.Timestamp));
        }
        public Task ResetLearningProgressAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    internal sealed class FakeClock : IClock
    {
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.Zero;
    }

    private sealed class NoResetCoordinator : IAppResetCoordinator
    {
        public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SilentHaptics : IHapticFeedbackService
    {
        public bool IsSupported => false;
        public void PerformCue(HapticFeedbackCue cue) { }
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
