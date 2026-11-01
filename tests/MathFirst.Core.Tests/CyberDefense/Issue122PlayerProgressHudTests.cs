namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using MathFirst.Application;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Issue #122: permanent player Level/XP in the combat HUD. The HUD only presents authoritative persisted
/// progression (<see cref="PlayerLevelProgress"/>); it never manufactures XP or levels.
/// Synthetic temporary stores only.
/// </summary>
public sealed class Issue122PlayerProgressHudTests : IDisposable
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
    // HUD RENDERING OF PlayerLevelProgress
    // =========================================================================

    [Fact]
    public async Task Hud_LevelOneWithZeroXp_ShowsLevelOneAndEmptyBar()
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(0);

        var html = await RenderHudAsync("en", progress);

        Assert.Contains("<span class=\"player-level-label\">Lv. 1</span>", html);
        Assert.Contains($"<span class=\"player-xp-value\">0 / {progress.XpRequiredForNextLevel}</span>", html);
        var bar = Bar(html);
        Assert.Contains("role=\"progressbar\"", bar);
        Assert.Contains("aria-valuemin=\"0\"", bar);
        Assert.Contains("aria-valuenow=\"0\"", bar);
        Assert.Contains($"aria-valuemax=\"{progress.XpRequiredForNextLevel}\"", bar);
        Assert.Contains("style=\"width: 0%\"", html);
    }

    [Fact]
    public async Task Hud_XpJustBelowTheNextThreshold_StaysOnTheCurrentLevelWithAnAlmostFullBar()
    {
        var threshold = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2);
        var progress = CyberDefenseLevelPolicy.CalculateProgress(threshold - 1);

        var html = await RenderHudAsync("en", progress);

        Assert.Equal(1, progress.Level);
        Assert.Contains("Lv. 1</span>", html);
        Assert.Contains($"<span class=\"player-xp-value\">{progress.XpRequiredForNextLevel - 1} / {progress.XpRequiredForNextLevel}</span>", html);
        Assert.Contains($"aria-valuenow=\"{progress.XpRequiredForNextLevel - 1}\"", Bar(html));
        var width = double.Parse(Regex.Match(html, @"player-xp-fill""[^>]*style=""width: ([0-9.]+)%""").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(width, 50, 99.99);
    }

    [Fact]
    public async Task Hud_ExactLevelThreshold_ShowsTheNextLevelWithAnEmptyBar()
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(CyberDefenseLevelPolicy.GetCumulativeXpThreshold(2));

        var html = await RenderHudAsync("en", progress);

        Assert.Equal(2, progress.Level);
        Assert.Contains("Lv. 2</span>", html);
        Assert.Contains($"<span class=\"player-xp-value\">0 / {progress.XpRequiredForNextLevel}</span>", html);
        Assert.Contains("aria-valuenow=\"0\"", Bar(html));
        Assert.Contains("style=\"width: 0%\"", html);
    }

    [Fact]
    public async Task Hud_MultipleLevelUpsInOneAward_ShowTheResultingLevelAndRemainder()
    {
        var xp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(6) + 7;
        var progress = CyberDefenseLevelPolicy.CalculateProgress(xp);

        var html = await RenderHudAsync("en", progress);

        Assert.Equal(6, progress.Level);
        Assert.Contains("Lv. 6</span>", html);
        Assert.Contains($"<span class=\"player-xp-value\">7 / {progress.XpRequiredForNextLevel}</span>", html);
    }

    [Theory]
    [InlineData("en", "Lv. 8", "Player level 8")]
    [InlineData("de", "Lv. 8", "Spielerlevel 8")]
    [InlineData("ru", "Ур. 8", "Уровень игрока 8")]
    public async Task Hud_LevelAndXpAreLocalizedWithScreenReaderSemantics(string language, string visibleLevel, string levelAria)
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(CyberDefenseLevelPolicy.GetCumulativeXpThreshold(8) + 45);

        var html = await RenderHudAsync(language, progress);

        Assert.Contains($"<span class=\"player-level-label\">{visibleLevel}</span>", html);
        var bar = Bar(html);
        Assert.Contains($"aria-label=\"{levelAria}\"", bar);
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        Assert.Contains($"aria-valuetext=\"{localizer["CyberDefense_PlayerXpAria", 45, progress.XpRequiredForNextLevel]}\"", bar);
        Assert.Contains($"<span class=\"player-xp-value\">45 / {progress.XpRequiredForNextLevel}</span>", html);
        Assert.DoesNotContain("aria-hidden", Regex.Match(html, @"<div class=""player-xp-cluster""[^>]*>").Value);
    }

    [Fact]
    public async Task Hud_WithoutAuthoritativeProgress_ShowsAnUnavailablePlaceholderInsteadOfLevelOne()
    {
        var html = await RenderHudAsync("en", progress: null);

        Assert.Contains("<span class=\"player-level-label\">Lv. —</span>", html);
        Assert.DoesNotContain("Lv. 1", html);
        Assert.DoesNotContain("aria-valuenow", Bar(html));
        Assert.Contains("aria-valuetext=\"Player level unavailable\"", Bar(html));
        Assert.Contains("style=\"width: 0%\"", html);
    }

    [Fact]
    public async Task Hud_DistinguishesRunSectorFromPersistentPlayerLevel_AndShowsNoMathStage()
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(CyberDefenseLevelPolicy.GetCumulativeXpThreshold(8));

        var html = await RenderHudAsync("en", progress);

        Assert.Contains("SECTOR 1", html);
        Assert.Contains("Lv. 8</span>", html);
        Assert.DoesNotMatch(@">[^<]*(?:Stage|Stufe)[^<]*<", html);
        // Issue #136: Level/XP is the top row of the player panel, above the shield row and the HP row.
        Assert.Matches(@"class=""player-xp-cluster""[\s\S]*?class=""player-status-heading""[\s\S]*?class=""player-hp-cluster""", html);
    }

    [Fact]
    public async Task Hud_DoesNotRenderADuplicateSectorUpgradeEntry()
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(0);

        var html = await RenderHudAsync("en", progress);

        Assert.DoesNotContain("upgrade-indicator-badge", html);
        Assert.DoesNotContain("<button", html);
    }

    // =========================================================================
    // AUTHORITATIVE SOURCE: UpgradeTerminalViewModel PROJECTION OF COMMITTED PROGRESSION
    // =========================================================================

    [Fact]
    public async Task ViewModel_HasNoAuthoritativeStatusUntilTheFirstCommittedReadCompletes()
    {
        using var store = await CreateStoreAsync();
        using var vm = new UpgradeTerminalViewModel(store);
        Assert.False(vm.HasAuthoritativeStatus);

        await vm.InitializeAsync();

        Assert.True(vm.HasAuthoritativeStatus);
        var persisted = await store.GetProgressionAsync();
        Assert.Equal(CyberDefenseLevelPolicy.CalculateProgress(persisted.CumulativeXp), vm.LevelProgress);
    }

    [Fact]
    public async Task ViewModel_FailedFirstReadNeverProducesAnAuthoritativeStatus()
    {
        using var store = await CreateStoreAsync();
        var proxy = DispatchProxy.Create<IGameplayStore, ControlledProgressionStore>();
        var controlled = (ControlledProgressionStore)(object)proxy;
        controlled.Inner = store;
        controlled.FailReads = true;
        using var vm = new UpgradeTerminalViewModel(proxy);

        await Assert.ThrowsAsync<IOException>(() => vm.InitializeAsync());

        Assert.False(vm.HasAuthoritativeStatus);
    }

    [Fact]
    public async Task ViewModel_XpProjectionFollowsCommittedAttemptsOnly_AndIgnoresStagedIntents()
    {
        using var store = await CreateStoreAsync();
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        var initial = vm.LevelProgress;

        var intent = Intent("committed", correct: true);
        await store.StagePendingIntentAsync(intent);
        await vm.RefreshAsync();
        Assert.Equal(initial, vm.LevelProgress);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        await vm.RefreshAsync();

        Assert.True(receipt.CumulativeXpAfter > 0);
        Assert.Equal(receipt.CumulativeXpAfter, vm.LevelProgress.CumulativeXp);
        Assert.Equal(CyberDefenseLevelPolicy.CalculateProgress(receipt.CumulativeXpAfter!.Value), vm.LevelProgress);
    }

    [Fact]
    public async Task ViewModel_StaleAsynchronousRefreshCannotReplaceNewerCommittedProgress()
    {
        using var store = await CreateStoreAsync();
        var proxy = DispatchProxy.Create<IGameplayStore, ControlledProgressionStore>();
        var controlled = (ControlledProgressionStore)(object)proxy;
        controlled.Inner = store;
        using var vm = new UpgradeTerminalViewModel(proxy);
        await vm.InitializeAsync();
        var before = vm.LevelProgress;

        // Refresh A reads the old committed XP and parks before it can publish.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controlled.HoldNext(gate);
        var stale = vm.RefreshAsync();
        await controlled.HeldReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var receipt = await store.ApplyAttemptTransactionAsync(Intent("newer", correct: true));
        await vm.RefreshAsync();
        Assert.Equal(receipt.CumulativeXpAfter, vm.LevelProgress.CumulativeXp);

        gate.SetResult();
        await stale;

        Assert.NotEqual(before.CumulativeXp, vm.LevelProgress.CumulativeXp);
        Assert.Equal(receipt.CumulativeXpAfter, vm.LevelProgress.CumulativeXp);
    }

    [Fact]
    public async Task GameOver_ResetsTheRunSector_ButKeepsPlayerLevelXpUpgradesAndSkillPoints()
    {
        using var store = await CreateStoreAsync();
        await SeedPlayerLevelAsync(store, 80);
        await store.PurchaseAttackUpgradeAsync(0, 0);
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        for (var i = 0; i < 21; i++) await store.ApplyAttemptTransactionAsync(Intent("win-" + i, correct: true));
        await vm.RefreshAsync();
        var beforeRun = await store.GetRunStateAsync();
        var before = vm.Status;
        Assert.True(beforeRun.Sector >= 1);

        CyberDefenseReceiptRecord? gameOver = null;
        for (var i = 0; i < 400 && gameOver is null; i++)
        {
            var receipt = await store.ApplyAttemptTransactionAsync(Intent("miss-" + i, correct: false));
            if (receipt.TransitionResult is { IsGameOver: true }) gameOver = receipt;
        }
        Assert.NotNull(gameOver);
        await vm.RefreshAsync();

        var persisted = await store.GetProgressionAsync();
        Assert.Equal(1, (await store.GetRunStateAsync()).Sector);
        // Durable progression is monotonic: defeat never lowers XP or the player level.
        Assert.True(persisted.CumulativeXp >= before.CumulativeXp);
        Assert.True(persisted.PlayerLevel >= before.PlayerLevel);
        Assert.Equal(persisted.PlayerLevel, vm.PlayerLevel);
        Assert.Equal(persisted.CumulativeXp, vm.CumulativeXp);
        Assert.Equal(CyberDefenseLevelPolicy.CalculateProgress(persisted.CumulativeXp), vm.LevelProgress);
        Assert.Equal(before.CurrentAttackRank, vm.CurrentAttackRank);
        Assert.Equal(before.AvailableSkillPoints, vm.AvailableSkillPoints);
        Assert.Equal(before.EarnedSkillPoints, vm.Status.EarnedSkillPoints);
        Assert.Equal(before.SpentSkillPoints, vm.Status.SpentSkillPoints);
    }

    [Fact]
    public async Task UpgradePurchase_ChangesRanksAndSkillPoints_ButNeverPlayerLevelOrXp()
    {
        using var store = await CreateStoreAsync();
        await SeedPlayerLevelAsync(store, 6);
        using var vm = new UpgradeTerminalViewModel(store);
        await vm.InitializeAsync();
        var before = vm.LevelProgress;
        var pointsBefore = vm.AvailableSkillPoints;

        var result = await vm.PurchaseUpgradeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(before, vm.LevelProgress);
        Assert.True(vm.AvailableSkillPoints < pointsBefore);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static string Bar(string html) => Regex.Match(html, @"<div class=""meter-bar player-xp-bar""[^>]*>").Value;

    private static CyberDefensePendingIntentRecord Intent(string id, bool correct) =>
        new(id, "1+1", correct, true, 1000, 0, DateTimeOffset.UnixEpoch);

    private async Task<SqliteGameplayStore> CreateStoreAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstIssue122_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        var store = new SqliteGameplayStore(Path.Combine(directory, "synthetic-gameplay.db"));
        await store.InitializeAsync();
        return store;
    }

    private static async Task SeedPlayerLevelAsync(SqliteGameplayStore store, int level)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.StoragePath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE gameplay_progression SET cumulative_xp = @xp, player_level = @level, earned_sp = @sp, spent_sp = 0, available_sp = @sp WHERE id = 1;";
        command.Parameters.AddWithValue("@xp", CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level));
        command.Parameters.AddWithValue("@level", level);
        command.Parameters.AddWithValue("@sp", level - 1);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> RenderHudAsync(string language, PlayerLevelProgress? progress)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(language);
        await using var services = new ServiceCollection().AddLogging().AddSingleton<ILocalizationService>(localizer).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<global::MathFirst.Core.Tests.CyberDefenseHud>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ViewModel"] = CyberDefenseHudViewModel.Initial(),
                ["PlayerProgress"] = progress
            }))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    /// <summary>Forwards to the real store, optionally parking or failing progression reads.</summary>
    public class ControlledProgressionStore : DispatchProxy
    {
        public IGameplayStore Inner { get; set; } = null!;
        public bool FailReads { get; set; }
        public void HoldNext(TaskCompletionSource gate) => _holdNext = gate;
        public TaskCompletionSource HeldReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IGameplayStore.GetProgressionAsync))
            {
                if (FailReads) return Task.FromException<CyberDefenseProgressionRecord>(new IOException("Synthetic read failure."));
                if (Interlocked.Exchange(ref _holdNext, null) is { } hold)
                {
                    return ReadThenWaitAsync((Task<CyberDefenseProgressionRecord>)targetMethod.Invoke(Inner, args)!, hold);
                }
            }
            try { return targetMethod.Invoke(Inner, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
        }

        private TaskCompletionSource? _holdNext;

        private async Task<CyberDefenseProgressionRecord> ReadThenWaitAsync(Task<CyberDefenseProgressionRecord> read, TaskCompletionSource gate)
        {
            var value = await read;
            HeldReadStarted.TrySetResult();
            await gate.Task;
            return value;
        }
    }
}
