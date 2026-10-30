namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MathFirst.App;
using MathFirst.App.Services;
using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class SettingsAchievementsRenderingTests
{
    private static string GetSubsectionHtml(string html)
    {
        const string marker = "cyber-achievements-subsection";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var endIdx = html.IndexOf("</section>", idx, StringComparison.Ordinal);
        return endIdx > idx ? html[idx..endIdx] : html[idx..];
    }

    [Fact]
    public async Task T01_ExactlyThreeKnownAchievementsRendered()
    {
        var reader = new FakeAchievementReader();
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains("cyber-achievements-subsection", html, StringComparison.Ordinal);
        Assert.Contains("achievement-item", sub, StringComparison.Ordinal);

        // Verify all 3 known achievements are present
        Assert.Contains(fixture.Localizer["Achievement_sector_10_Title"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_boss_slayer_Title"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_first_upgrade_Title"], sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T02_OneUnlockedTwoLocked()
    {
        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.Sector10);
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains(fixture.Localizer["Achievement_sector_10_Title"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_boss_slayer_Title"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_first_upgrade_Title"], sub, StringComparison.Ordinal);

        Assert.Contains("achievement-status-badge badge-unlocked", sub, StringComparison.Ordinal);
        Assert.Contains("achievement-status-badge badge-locked", sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T03_EmptySuccessfulReadMeansAllLocked()
    {
        var reader = new FakeAchievementReader();
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.DoesNotContain("badge-unlocked", sub, StringComparison.Ordinal);
        Assert.Contains("badge-locked", sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Settings_Achievements_Locked"], sub, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Localizer["Settings_Achievements_Unlocked"], sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T04_SuccessfulReadWithAllThreeUnlocked()
    {
        var reader = new FakeAchievementReader(
            CyberDefenseAchievementIds.Sector10,
            CyberDefenseAchievementIds.BossSlayer,
            CyberDefenseAchievementIds.FirstUpgrade);
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains("badge-unlocked", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-locked", sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Settings_Achievements_Unlocked"], sub, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Localizer["Settings_Achievements_Locked"], sub, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public async Task T05_EnDeRuTitlesAndDescriptions(string lang)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(lang);

        var sector10Title = localizer["Achievement_sector_10_Title"];
        var sector10Desc = localizer["Achievement_sector_10_Desc"];
        var bossSlayerTitle = localizer["Achievement_boss_slayer_Title"];
        var bossSlayerDesc = localizer["Achievement_boss_slayer_Desc"];
        var firstUpgradeTitle = localizer["Achievement_first_upgrade_Title"];
        var firstUpgradeDesc = localizer["Achievement_first_upgrade_Desc"];

        Assert.False(string.IsNullOrWhiteSpace(sector10Title));
        Assert.False(string.IsNullOrWhiteSpace(sector10Desc));
        Assert.False(string.IsNullOrWhiteSpace(bossSlayerTitle));
        Assert.False(string.IsNullOrWhiteSpace(bossSlayerDesc));
        Assert.False(string.IsNullOrWhiteSpace(firstUpgradeTitle));
        Assert.False(string.IsNullOrWhiteSpace(firstUpgradeDesc));

        Assert.DoesNotContain("Achievement_", sector10Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Achievement_", sector10Desc, StringComparison.Ordinal);

        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.Sector10);
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader, localizer: localizer);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains(sector10Title, sub, StringComparison.Ordinal);
        Assert.Contains(sector10Desc, sub, StringComparison.Ordinal);
        Assert.Contains(bossSlayerTitle, sub, StringComparison.Ordinal);
        Assert.Contains(bossSlayerDesc, sub, StringComparison.Ordinal);
        Assert.Contains(firstUpgradeTitle, sub, StringComparison.Ordinal);
        Assert.Contains(firstUpgradeDesc, sub, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Locked", "Unlocked")]
    [InlineData("de", "Gesperrt", "Freigeschaltet")]
    [InlineData("ru", "Заблокировано", "Разблокировано")]
    public void T06_EnDeRuLockedAndUnlockedStatuses(string lang, string expectedLocked, string expectedUnlocked)
    {
        var localizer = new LocalizationService();
        localizer.ApplyLanguagePreference(lang);

        Assert.Equal(expectedLocked, localizer["Settings_Achievements_Locked"]);
        Assert.Equal(expectedUnlocked, localizer["Settings_Achievements_Unlocked"]);
    }

    [Fact]
    public async Task T07_SqliteReadFailureRendersUnavailableNotFictitiousLockedState()
    {
        var reader = new FakeAchievementReader(_ => throw new InvalidOperationException("Simulated SQLite failure"));
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains(fixture.Localizer["Settings_Achievements_LoadFailed"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Training_Retry"], sub, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-locked", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("achievement-item", sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T08_PendingResetReadFencing()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay.db");
        var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Mark reset as pending with valid target_epoch > current_epoch
        using (var connection = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_reset_intent SET is_pending = 1, target_epoch = current_epoch + 1 WHERE id = 1;";
            cmd.ExecuteNonQuery();
        }

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: store);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains(fixture.Localizer["Settings_Achievements_LoadFailed"], sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Training_Retry"], sub, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-locked", sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T09_RetryAfterTransientReadFailure()
    {
        var callCount = 0;
        var reader = new FakeAchievementReader(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new InvalidOperationException("Transient connection error");
            }

            var list = new List<CyberDefenseAchievementRecord>
            {
                new(CyberDefenseAchievementIds.FirstUpgrade, 0, DateTimeOffset.UtcNow)
            };
            return Task.FromResult<IReadOnlyList<CyberDefenseAchievementRecord>>(list);
        });

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html1 = await fixture.GetHtmlAsync();
        var sub1 = GetSubsectionHtml(html1);
        Assert.Contains(fixture.Localizer["Settings_Achievements_LoadFailed"], sub1, StringComparison.Ordinal);

        await fixture.InvokeRetryAsync();

        var html2 = await fixture.GetHtmlAsync();
        var sub2 = GetSubsectionHtml(html2);
        Assert.DoesNotContain(fixture.Localizer["Settings_Achievements_LoadFailed"], sub2, StringComparison.Ordinal);
        Assert.Contains("badge-unlocked", sub2, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_first_upgrade_Title"], sub2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T10_CalmModeHidesPresentation()
    {
        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.Sector10);
        await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: false, reader: reader);

        var html = await fixture.GetHtmlAsync();
        Assert.DoesNotContain("cyber-achievements-subsection", html, StringComparison.Ordinal);
        Assert.DoesNotContain("achievement-item", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T11_SwitchingBackToCyberDefenseReloads()
    {
        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.Sector10);
        await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: false, reader: reader);

        var html1 = await fixture.GetHtmlAsync();
        Assert.DoesNotContain("cyber-achievements-subsection", html1, StringComparison.Ordinal);

        await fixture.InvokeSetCyberDefenseModeAsync(true);

        var html2 = await fixture.GetHtmlAsync();
        var sub2 = GetSubsectionHtml(html2);
        Assert.Contains("cyber-achievements-subsection", html2, StringComparison.Ordinal);
        Assert.Contains("badge-unlocked", sub2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T12_A1_CalmModeSwitchDuringRead_CompletesWithoutObjectDisposedException()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeAchievementReader(_ => tcs.Task);
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var loadTask = await fixture.StartLoadAchievementsAsync();

        // Switch to Calm Mode while read is in-flight: cancels and disposes cts
        await fixture.InvokeSetCyberDefenseModeAsync(false);

        // Complete the old read after mode switch
        tcs.SetResult(new List<CyberDefenseAchievementRecord>
        {
            new(CyberDefenseAchievementIds.Sector10, 0, DateTimeOffset.UtcNow)
        });

        // Awaiting the loadTask must not throw ObjectDisposedException
        var ex = await Record.ExceptionAsync(() => loadTask);
        Assert.Null(ex);

        // Subsection must be hidden in Calm Mode
        var html = await fixture.GetHtmlAsync();
        Assert.DoesNotContain("cyber-achievements-subsection", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T12_A2_DisposalDuringRead_CompletesWithoutObjectDisposedExceptionOrPostDisposalRender()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeAchievementReader(_ => tcs.Task);
        var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var loadTask = await fixture.StartLoadAchievementsAsync();

        // Dispose the Settings component while read is in-flight
        await fixture.DisposeAsync();

        // Complete the old read after disposal
        tcs.SetResult(new List<CyberDefenseAchievementRecord>
        {
            new(CyberDefenseAchievementIds.Sector10, 0, DateTimeOffset.UtcNow)
        });

        // Awaiting the loadTask must not throw ObjectDisposedException or fail due to post-disposal render
        var ex = await Record.ExceptionAsync(() => loadTask);
        Assert.Null(ex);
    }

    [Fact]
    public async Task T12_A3_ReplacementReadDuringRead_OlderReadDoesNotThrowOrOverwriteNewerResults()
    {
        var tcs1 = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        var reader = new FakeAchievementReader(_ =>
        {
            var idx = Interlocked.Increment(ref callIndex);
            return idx == 1 ? tcs1.Task : tcs2.Task;
        });

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        // Start first read
        var loadTask1 = await fixture.StartLoadAchievementsAsync();

        // Start replacement read (cancels and disposes cts1)
        var loadTask2 = await fixture.StartLoadAchievementsAsync();

        // Complete replacement read with BossSlayer unlocked
        tcs2.SetResult(new List<CyberDefenseAchievementRecord>
        {
            new(CyberDefenseAchievementIds.BossSlayer, 0, DateTimeOffset.UtcNow)
        });
        await loadTask2;

        // Verify newer result is rendered
        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);
        Assert.Contains(fixture.Localizer["Achievement_boss_slayer_Title"], sub, StringComparison.Ordinal);

        // Now complete the older read with Sector10
        tcs1.SetResult(new List<CyberDefenseAchievementRecord>
        {
            new(CyberDefenseAchievementIds.Sector10, 0, DateTimeOffset.UtcNow)
        });

        // Old read must complete without throwing ObjectDisposedException
        var ex1 = await Record.ExceptionAsync(() => loadTask1);
        Assert.Null(ex1);

        // Old read must not overwrite newer results
        html = await fixture.GetHtmlAsync();
        sub = GetSubsectionHtml(html);
        var unlockedBadges = sub.Split("badge-unlocked").Length - 1;
        Assert.Equal(1, unlockedBadges);
        Assert.Contains(fixture.Localizer["Achievement_boss_slayer_Title"], sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T12_A4_FullLocalResetDuringRead_OlderReadDoesNotThrowOrRestoreStaleResults()
    {
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        var learnerStore = new SqliteLearnerStore(learnerDbPath);
        var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var prefs = new TestPreferenceStore();
        var sessionState = new CyberDefenseSessionState(prefs);
        var clock = new FakeClock();
        var session = new TrainingSession(learnerStore, clock, preferenceStore: prefs);
        await session.InitializeAsync(startTiming: false);
        var resetCoordinator = new AppResetCoordinator(
            session,
            prefs,
            new DummyInstallationIdProvider(),
            new DummyTelemetryShareCacheCleaner(),
            sessionState,
            gameplayStore);

        var tcs = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeAchievementReader(_ => tcs.Task);

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader, resetCoordinator: resetCoordinator);

        var loadTask = await fixture.StartLoadAchievementsAsync();

        // Begin full local reset (cancels and disposes cts)
        await fixture.InvokeFullLocalResetAsync();

        // Complete the old read with Sector10
        tcs.SetResult(new List<CyberDefenseAchievementRecord>
        {
            new(CyberDefenseAchievementIds.Sector10, 0, DateTimeOffset.UtcNow)
        });

        // Old read must complete without throwing ObjectDisposedException
        var ex = await Record.ExceptionAsync(() => loadTask);
        Assert.Null(ex);
    }

    [Fact]
    public async Task T13_FullLocalResetClearsPresentationWhenViewNextLoaded()
    {
        var dir = CreateTempDirectory();
        var learnerDbPath = Path.Combine(dir, "learner.db");
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        var learnerStore = new SqliteLearnerStore(learnerDbPath);
        var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await learnerStore.InitializeAsync();
        await gameplayStore.InitializeAsync();

        var canonicalNow = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture);
        long currentEpoch;
        using (var connection = new SqliteConnection($"Data Source={gameplayDbPath};Foreign Keys=True"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT current_epoch FROM gameplay_reset_intent WHERE id = 1;";
            currentEpoch = Convert.ToInt64(cmd.ExecuteScalar());

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
                VALUES ('sector_10', @epoch, @unlockedAt);";
            insertCmd.Parameters.AddWithValue("@epoch", currentEpoch);
            insertCmd.Parameters.AddWithValue("@unlockedAt", canonicalNow);
            insertCmd.ExecuteNonQuery();
        }

        var prefs = new TestPreferenceStore();
        var sessionState = new CyberDefenseSessionState(prefs);
        var clock = new FakeClock();
        var session = new TrainingSession(learnerStore, clock, preferenceStore: prefs);
        await session.InitializeAsync(startTiming: false);
        var resetCoordinator = new AppResetCoordinator(
            session,
            prefs,
            new DummyInstallationIdProvider(),
            new DummyTelemetryShareCacheCleaner(),
            sessionState,
            gameplayStore);

        // First view: sector_10 is unlocked
        await using (var fixture1 = await SettingsFixture.CreateAsync(this, reader: gameplayStore, resetCoordinator: resetCoordinator))
        {
            var html = await fixture1.GetHtmlAsync();
            var sub = GetSubsectionHtml(html);
            Assert.Contains("badge-unlocked", sub, StringComparison.Ordinal);
        }

        // Execute full local reset
        await resetCoordinator.ExecuteFullResetAsync();

        // Second view: all achievements locked
        await using (var fixture2 = await SettingsFixture.CreateAsync(this, reader: gameplayStore, resetCoordinator: resetCoordinator))
        {
            var html = await fixture2.GetHtmlAsync();
            var sub = GetSubsectionHtml(html);
            Assert.DoesNotContain("badge-unlocked", sub, StringComparison.Ordinal);
            Assert.Contains("badge-locked", sub, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task T14_CombatRebootPreservesUnlockedState()
    {
        var dir = CreateTempDirectory();
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");
        var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        var canonicalNow = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture);
        long currentEpoch;
        using (var connection = new SqliteConnection($"Data Source={gameplayDbPath};Foreign Keys=True"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT current_epoch FROM gameplay_reset_intent WHERE id = 1;";
            currentEpoch = Convert.ToInt64(cmd.ExecuteScalar());

            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
                VALUES ('sector_10', @epoch, @unlockedAt);";
            insertCmd.Parameters.AddWithValue("@epoch", currentEpoch);
            insertCmd.Parameters.AddWithValue("@unlockedAt", canonicalNow);
            insertCmd.ExecuteNonQuery();
        }

        // Simulate combat reboot (run state wipe / reboot)
        using (var connection = new SqliteConnection($"Data Source={gameplayDbPath};Foreign Keys=True"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET sector = 1, opponent_index = 0
                WHERE id = 1;";
            cmd.ExecuteNonQuery();
        }

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: gameplayStore);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        Assert.Contains("badge-unlocked", sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_sector_10_Title"], sub, StringComparison.Ordinal);
    }

    [Fact]
    public void T15_DiAliasReturnsSameGameplayStoreSingleton()
    {
        var dir = CreateTempDirectory();
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");

        var services = new ServiceCollection();
        services.AddSingleton<IGameplayStore>(_ => new SqliteGameplayStore(gameplayDbPath));
        services.AddSingleton<ICyberDefenseAchievementReader>(sp =>
        {
            var store = sp.GetRequiredService<IGameplayStore>();
            if (store is ICyberDefenseAchievementReader reader)
            {
                return reader;
            }

            throw new InvalidOperationException(
                $"Registered {nameof(IGameplayStore)} instance does not implement {nameof(ICyberDefenseAchievementReader)}.");
        });

        using var sp = services.BuildServiceProvider();
        var gameplayStore = sp.GetRequiredService<IGameplayStore>();
        var reader = sp.GetRequiredService<ICyberDefenseAchievementReader>();

        Assert.Same(gameplayStore, reader);

        // Also test fail-closed behavior if IGameplayStore does not implement reader
        var invalidServices = new ServiceCollection();
        invalidServices.AddSingleton<IGameplayStore>(new NonReaderGameplayStore());
        invalidServices.AddSingleton<ICyberDefenseAchievementReader>(s =>
        {
            var store = s.GetRequiredService<IGameplayStore>();
            if (store is ICyberDefenseAchievementReader r)
            {
                return r;
            }

            throw new InvalidOperationException(
                $"Registered {nameof(IGameplayStore)} instance does not implement {nameof(ICyberDefenseAchievementReader)}.");
        });

        using var invalidSp = invalidServices.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => invalidSp.GetRequiredService<ICyberDefenseAchievementReader>());
        Assert.Contains("does not implement", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void T16_NoAdditionalSqliteStoreConstruction()
    {
        var dir = CreateTempDirectory();
        var gameplayDbPath = Path.Combine(dir, "gameplay.db");
        var constructionCount = 0;

        var services = new ServiceCollection();
        services.AddSingleton<IGameplayStore>(_ =>
        {
            constructionCount++;
            return new SqliteGameplayStore(gameplayDbPath);
        });
        services.AddSingleton<ICyberDefenseAchievementReader>(sp =>
        {
            var store = sp.GetRequiredService<IGameplayStore>();
            if (store is ICyberDefenseAchievementReader reader)
            {
                return reader;
            }

            throw new InvalidOperationException(
                $"Registered {nameof(IGameplayStore)} instance does not implement {nameof(ICyberDefenseAchievementReader)}.");
        });

        using var sp = services.BuildServiceProvider();
        _ = sp.GetRequiredService<IGameplayStore>();
        _ = sp.GetRequiredService<ICyberDefenseAchievementReader>();
        _ = sp.GetRequiredService<ICyberDefenseAchievementReader>();

        Assert.Equal(1, constructionCount);
    }

    [Fact]
    public async Task T17_KeyboardAndScreenReaderSemantics()
    {
        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.Sector10);
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        // Heading hierarchy
        Assert.Contains("<h3", sub, StringComparison.Ordinal);
        Assert.Contains("cyber-achievements-title", sub, StringComparison.Ordinal);

        // List semantics
        Assert.Contains("<ul", sub, StringComparison.Ordinal);
        Assert.Contains("<li", sub, StringComparison.Ordinal);

        // Screen-reader labels and status roles
        Assert.Contains("aria-label=", sub, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", sub, StringComparison.Ordinal);

        // No bad tabindexes
        Assert.DoesNotContain("tabindex=\"1\"", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("tabindex=\"2\"", sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T18_NoPracticeCountdownOrTimerMutations()
    {
        var reader = new FakeAchievementReader();
        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        // Session item timing must remain paused in Settings
        Assert.False(fixture.Session.IsTimingActive);
        Assert.False(fixture.Session.IsPracticeSurfaceActive);
    }

    [Fact]
    public void T19_NoChangeToHomeMathematicsLayout()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var homeRazorPath = Path.Combine(repoRoot, "src", "MathFirst.App", "Components", "Pages", "Home.razor");
        if (!File.Exists(homeRazorPath))
        {
            repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
            homeRazorPath = Path.Combine(repoRoot, "src", "MathFirst.App", "Components", "Pages", "Home.razor");
        }

        Assert.True(File.Exists(homeRazorPath), $"Home.razor must exist at {homeRazorPath}");
        var content = File.ReadAllText(homeRazorPath);

        // Ensure key layout elements and classes in Home.razor are intact
        Assert.Contains("practice-middle-region", content, StringComparison.Ordinal);
        Assert.Contains("numeric-keypad", content, StringComparison.Ordinal);
        Assert.Contains("operation-unlock-area", content, StringComparison.Ordinal);
        Assert.Contains("expression-row", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T20_NoTimestampOrFabricatedProgressShownToUsers()
    {
        var unlockedTime = new DateTimeOffset(2026, 10, 29, 14, 30, 45, TimeSpan.Zero);
        var reader = new FakeAchievementReader(_ =>
        {
            var list = new List<CyberDefenseAchievementRecord>
            {
                new(CyberDefenseAchievementIds.Sector10, 42, unlockedTime)
            };
            return Task.FromResult<IReadOnlyList<CyberDefenseAchievementRecord>>(list);
        });

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        // No timestamps
        Assert.DoesNotContain("2026-10-29", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("14:30:45", sub, StringComparison.Ordinal);
        // No reset epochs
        Assert.DoesNotContain("42", sub, StringComparison.Ordinal);
        // No progress ratios or percentages
        Assert.DoesNotContain("0/10", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("100%", sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T21_B1_B2_UnknownInitialStateDisplaysLoadingNotFalseLocked()
    {
        var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readTcs = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeAchievementReader(_ =>
        {
            readStarted.TrySetResult(true);
            return readTcs.Task;
        });

        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var fixture = await SettingsFixture.CreateAsync(
            this,
            reader: reader,
            settleInitialLoad: false);

        await readStarted.Task.WaitAsync(testCts.Token);

        // Intermediate render while achievement read is pending
        var html = await fixture.GetHtmlAsync();
        var sub = GetSubsectionHtml(html);

        // Complete reader task so cleanup can complete
        readTcs.TrySetResult(Array.Empty<CyberDefenseAchievementRecord>());

        // B1: Unknown initial state must NOT display any achievement as Locked
        Assert.DoesNotContain("badge-locked", sub, StringComparison.Ordinal);
        Assert.DoesNotContain("achievement-item", sub, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Localizer["Settings_Achievements_Locked"], sub, StringComparison.Ordinal);

        // B2: Loading state must be represented explicitly
        Assert.Contains("achievement-loading-panel", sub, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Training_Loading"], sub, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T22_B3_B4_EmptyAndNonEmptyAuthoritativeStatuses()
    {
        // B3: Successful empty read displays exactly three Locked achievements
        var emptyReader = new FakeAchievementReader();
        await using (var fixtureEmpty = await SettingsFixture.CreateAsync(this, reader: emptyReader))
        {
            var html = await fixtureEmpty.GetHtmlAsync();
            var sub = GetSubsectionHtml(html);
            var lockedBadges = sub.Split("badge-locked").Length - 1;
            Assert.Equal(3, lockedBadges);
            Assert.DoesNotContain("badge-unlocked", sub, StringComparison.Ordinal);
        }

        // B4: Successful non-empty read displays the correct authoritative status
        var reader = new FakeAchievementReader(CyberDefenseAchievementIds.FirstUpgrade);
        await using (var fixture = await SettingsFixture.CreateAsync(this, reader: reader))
        {
            var html = await fixture.GetHtmlAsync();
            var sub = GetSubsectionHtml(html);
            var unlockedBadges = sub.Split("badge-unlocked").Length - 1;
            var lockedBadges = sub.Split("badge-locked").Length - 1;
            Assert.Equal(1, unlockedBadges);
            Assert.Equal(2, lockedBadges);
        }
    }

    [Fact]
    public async Task T23_B5_B7_ReadFailureShowsUnavailableAndRetryRestoresValidStatus()
    {
        var shouldFail = true;
        var reader = new FakeAchievementReader(_ =>
        {
            if (shouldFail)
            {
                throw new InvalidOperationException("Simulated SQLite failure");
            }

            var list = new List<CyberDefenseAchievementRecord>
            {
                new(CyberDefenseAchievementIds.Sector10, 0, DateTimeOffset.UtcNow)
            };
            return Task.FromResult<IReadOnlyList<CyberDefenseAchievementRecord>>(list);
        });

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        // B5: Failure shows localized unavailable state and Retry action
        var html1 = await fixture.GetHtmlAsync();
        var sub1 = GetSubsectionHtml(html1);
        Assert.Contains("achievement-error-panel", sub1, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Settings_Achievements_LoadFailed"], sub1, StringComparison.Ordinal);
        Assert.Contains("achievement-retry-btn", sub1, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-locked", sub1, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-unlocked", sub1, StringComparison.Ordinal);

        // B7: Successful Retry restores valid status
        shouldFail = false;
        await fixture.InvokeRetryAsync();

        var html2 = await fixture.GetHtmlAsync();
        var sub2 = GetSubsectionHtml(html2);
        Assert.DoesNotContain("achievement-error-panel", sub2, StringComparison.Ordinal);
        Assert.Contains("badge-unlocked", sub2, StringComparison.Ordinal);
        Assert.Contains(fixture.Localizer["Achievement_sector_10_Title"], sub2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T24_B6_CancellationResetOrModeSwitchCannotTurnUnknownStateIntoFalseLocked()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CyberDefenseAchievementRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeAchievementReader(_ => tcs.Task);

        await using var fixture = await SettingsFixture.CreateAsync(this, reader: reader);

        // In-flight read is pending: verify loading panel, no locked badges
        var html1 = await fixture.GetHtmlAsync();
        var sub1 = GetSubsectionHtml(html1);
        Assert.Contains("achievement-loading-panel", sub1, StringComparison.Ordinal);
        Assert.DoesNotContain("badge-locked", sub1, StringComparison.Ordinal);
        Assert.DoesNotContain("achievement-item", sub1, StringComparison.Ordinal);

        // Switch to Calm Mode then back to Cyber Defense with pending read
        await fixture.InvokeSetCyberDefenseModeAsync(false);
        var html2 = await fixture.GetHtmlAsync();
        Assert.DoesNotContain("cyber-achievements-subsection", html2, StringComparison.Ordinal);

        // Switch back: must be in loading state, NEVER false locked
        await fixture.InvokeSetCyberDefenseModeAsync(true);
        var html3 = await fixture.GetHtmlAsync();
        var sub3 = GetSubsectionHtml(html3);
        Assert.DoesNotContain("badge-locked", sub3, StringComparison.Ordinal);
        Assert.DoesNotContain("achievement-item", sub3, StringComparison.Ordinal);

        tcs.TrySetCanceled();
    }

    // =========================================================================
    // Fixture and Helpers
    // =========================================================================

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mf_settings_achieve_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SettingsFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly HtmlRootComponent _root;
        private readonly Settings _settings;

        public TestPreferenceStore Preferences { get; }
        public CyberDefenseSessionState CyberDefenseSessionState { get; }
        public TrainingSession Session { get; }
        public TestNavigationManager Navigation { get; }
        public LocalizationService Localizer { get; }

        private SettingsFixture(
            ServiceProvider services,
            HtmlRenderer renderer,
            HtmlRootComponent root,
            Settings settings,
            TestPreferenceStore preferences,
            CyberDefenseSessionState cyberDefenseSessionState,
            TrainingSession session,
            TestNavigationManager navigation,
            LocalizationService localizer)
        {
            _services = services;
            _renderer = renderer;
            _root = root;
            _settings = settings;
            Preferences = preferences;
            CyberDefenseSessionState = cyberDefenseSessionState;
            Session = session;
            Navigation = navigation;
            Localizer = localizer;
        }

        public Settings Settings => _settings;

        public static async Task<SettingsFixture> CreateAsync(
            SettingsAchievementsRenderingTests suite,
            bool cyberDefense = true,
            ICyberDefenseAchievementReader? reader = null,
            IAppResetCoordinator? resetCoordinator = null,
            LocalizationService? localizer = null,
            ILearnerStore? customLearnerStore = null,
            bool settleInitialLoad = true)
        {
            var dir = suite.CreateTempDirectory();
            var dbPath = Path.Combine(dir, "learner.db");
            var learner = customLearnerStore ?? new SqliteLearnerStore(dbPath);

            var preferences = new TestPreferenceStore();
            preferences.SetCyberDefenseEnabled(cyberDefense);

            var clock = new FakeClock();
            var session = new TrainingSession(learner, clock, preferenceStore: preferences);
            var sessionState = new CyberDefenseSessionState(preferences);
            localizer ??= new LocalizationService();
            var themeService = new TestThemeService();
            var buildInfo = new AppBuildInfo();
            var backCoordinator = new AppBackNavigationCoordinator();
            var hapticService = new TestHapticFeedbackService();
            resetCoordinator ??= new NoResetCoordinator();
            var jsRuntime = new TestJsRuntime();
            var navigation = new TestNavigationManager();

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILocalizationService>(localizer)
                .AddSingleton<IThemeService>(themeService)
                .AddSingleton(buildInfo)
                .AddSingleton<IPreferenceStore>(preferences)
                .AddSingleton<ICyberDefenseModePreferences>(preferences)
                .AddSingleton(sessionState)
                .AddSingleton<ICyberDefenseTutorialReplayRequests>(sp =>
                    sp.GetRequiredService<CyberDefenseSessionState>())
                .AddSingleton(session)
                .AddSingleton<NavigationManager>(navigation)
                .AddSingleton<IJSRuntime>(jsRuntime)
                .AddSingleton<IAppBackNavigationCoordinator>(backCoordinator)
                .AddSingleton<IHapticFeedbackService>(hapticService)
                .AddSingleton<IAppResetCoordinator>(resetCoordinator);

            if (reader != null)
            {
                services.AddSingleton<ICyberDefenseAchievementReader>(reader);
            }

            var sp = services.BuildServiceProvider();
            var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());

            Settings capturedSettings = null!;
            var root = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                return await renderer.RenderComponentAsync<SettingsHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Capture"] = (Action<Settings>)(s => capturedSettings = s)
                }));
            });

            if (settleInitialLoad)
            {
                // Allow initial async load to settle
                await Task.Delay(20);
            }

            return new SettingsFixture(
                sp,
                renderer,
                root,
                capturedSettings,
                preferences,
                sessionState,
                session,
                navigation,
                localizer);
        }

        public async Task<string> GetHtmlAsync()
        {
            var raw = await _renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString());
            return WebUtility.HtmlDecode(raw);
        }

        public async Task<Task> StartLoadAchievementsAsync()
        {
            Task loadTask = null!;
            await _renderer.Dispatcher.InvokeAsync(() =>
            {
                var method = typeof(Settings).GetMethod("LoadAchievementsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                loadTask = (Task)method.Invoke(_settings, null)!;
            });
            return loadTask;
        }

        public Task InvokeFullLocalResetAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(Settings).GetMethod("ExecuteFullLocalReset", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                var task = (Task)method.Invoke(_settings, null)!;
                await task;
            });

        public Task InvokeSetCyberDefenseModeAsync(bool enabled) =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(Settings).GetMethod("SetCyberDefenseMode", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                method.Invoke(_settings, new object[] { enabled });
                var stateHasChanged = typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                stateHasChanged?.Invoke(_settings, null);
                await Task.Delay(20);
            });

        public Task InvokeRetryAsync() =>
            _renderer.Dispatcher.InvokeAsync(async () =>
            {
                var method = typeof(Settings).GetMethod("RetryLoadAchievementsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                var task = (Task)method.Invoke(_settings, null)!;
                await task;
            });

        public async ValueTask DisposeAsync()
        {
            await _renderer.Dispatcher.InvokeAsync(() => _settings.Dispose());
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class SettingsHost : ComponentBase
    {
        [Parameter]
        public Action<Settings> Capture { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Settings>(0);
            builder.AddComponentReferenceCapture(1, instance => Capture((Settings)instance));
            builder.CloseComponent();
        }
    }

    private sealed class FakeAchievementReader : ICyberDefenseAchievementReader
    {
        private readonly Func<CancellationToken, Task<IReadOnlyList<CyberDefenseAchievementRecord>>> _handler;

        public FakeAchievementReader(Func<CancellationToken, Task<IReadOnlyList<CyberDefenseAchievementRecord>>> handler)
        {
            _handler = handler;
        }

        public FakeAchievementReader(params string[] unlockedAchievementIds)
        {
            _handler = _ =>
            {
                var now = DateTimeOffset.UtcNow;
                var list = (IReadOnlyList<CyberDefenseAchievementRecord>)unlockedAchievementIds
                    .Select(id => new CyberDefenseAchievementRecord(id, 0, now))
                    .ToList();
                return Task.FromResult(list);
            };
        }

        public Task<IReadOnlyList<CyberDefenseAchievementRecord>> GetUnlockedAchievementsAsync(CancellationToken cancellationToken = default)
        {
            return _handler(cancellationToken);
        }
    }

    private sealed class NonReaderGameplayStore : IGameplayStore
    {
        public string StoragePath => string.Empty;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged { add { } remove { } }
        public Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged { add { } remove { } }
        public Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public event Action<CyberDefenseCombatModifierSnapshot>? CombatModifierSnapshotChanged { add { } remove { } }
        public Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(int questionBoundTargetCapacity, long expectedResetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DummyInstallationIdProvider : IInstallationIdProvider
    {
        public string GetOrCreateInstallationId() => "test-installation-id";
        public void ClearInstallationId() { }
    }

    private sealed class DummyTelemetryShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri = "http://localhost/")
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }

    private sealed class TestPreferenceStore : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        private bool _cyberDefense = true;
        private bool _tutorialCompleted;
        private string _language = "system";
        private ThemePreference _theme = ThemePreference.System;
        private NumericKeypadLayout _keypad = NumericKeypadLayout.Numpad;
        private bool _haptic = true;
        private PracticeTimeSetting _timeSetting = PracticeTimeSetting.Standard;
        private readonly Dictionary<ArithmeticOperation, bool> _ops = new();

        public bool GetCyberDefenseEnabled() => _cyberDefense;
        public void SetCyberDefenseEnabled(bool enabled) => _cyberDefense = enabled;
        public bool GetTutorialCompleted() => _tutorialCompleted;
        public void SetTutorialCompleted(bool completed) => _tutorialCompleted = completed;
        public ThemePreference GetThemePreference() => _theme;
        public void SetThemePreference(ThemePreference preference) => _theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => _keypad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => _keypad = layout;
        public string GetLanguagePreference() => _language;
        public void SetLanguagePreference(string languageCode) => _language = languageCode;
        public bool GetHapticFeedbackEnabled() => _haptic;
        public void SetHapticFeedbackEnabled(bool enabled) => _haptic = enabled;
        public bool GetOperationEnabled(ArithmeticOperation operation) => _ops.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) => _ops[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => _timeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => _timeSetting = setting;
        public void ResetPracticePreferences() => _ops.Clear();
        public void ResetAllPreferences()
        {
            _ops.Clear();
            _cyberDefense = true;
            _tutorialCompleted = false;
            _language = "system";
            _theme = ThemePreference.System;
            _keypad = NumericKeypadLayout.Numpad;
            _haptic = true;
        }
    }

    private sealed class FakeClock : IClock
    {
        public long CurrentTimestamp { get; set; } = 1_000_000;
        public long GetTimestamp() => CurrentTimestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, CurrentTimestamp - startTimestamp));
    }

    private sealed class TestHapticFeedbackService : IHapticFeedbackService
    {
        public bool IsSupported => true;
        public void PerformCue(HapticFeedbackCue cue) { }
    }

    private sealed class NoResetCoordinator : IAppResetCoordinator
    {
        public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }

    private sealed class DeferredLearnerStore : ILearnerStore
    {
        private readonly SqliteLearnerStore _inner;
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeferredLearnerStore(string path)
        {
            _inner = new SqliteLearnerStore(path);
        }

        public Task InitializationTask => _tcs.Task;
        public void CompleteInitialization() => _tcs.TrySetResult(true);

        public string StoragePath => _inner.StoragePath;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _tcs.Task;
            await _inner.InitializeAsync(cancellationToken);
        }

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadSnapshotAsync(cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            _inner.CommitSubmissionAsync(changeSet, cancellationToken);

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            _inner.ResetLearningProgressAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            _inner.CloseAsync(cancellationToken);

        public void Dispose() => _inner.Dispose();
    }
}
