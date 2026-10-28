namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-009 Slice 4A: Achievement Read Model and Reset integration tests.
/// Tests ICyberDefenseAchievementReader contract, deterministic ordering, pending reset fencing,
/// cross-epoch row rejection, and atomic Full Local Reset deletion using real SQLite.
/// </summary>
public sealed class SqliteGameplayStoreAchievementReadTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreAchievementReadTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_Read_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    private string GetTempDbPath(string name) =>
        Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");

    private static SqliteConnection OpenRaw(string dbPath)
    {
        var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static async Task ExecuteRawAsync(string dbPath, string sql)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> CreateFreshStoreAsync(string name)
    {
        var dbPath = GetTempDbPath(name);
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        return dbPath;
    }

    private static ICyberDefenseAchievementReader AsAchievementReader(SqliteGameplayStore store)
    {
        var reader = (store as object) as ICyberDefenseAchievementReader;
        Assert.NotNull(reader);
        return reader;
    }

    // =========================================================================
    // 1. Interface implementation & empty reads
    // =========================================================================

    [Fact]
    public void R01_StoreImplementsICyberDefenseAchievementReader()
    {
        var dbPath = GetTempDbPath("r01_interface");
        using var store = new SqliteGameplayStore(dbPath);

        Assert.True((store as object) is ICyberDefenseAchievementReader, "SqliteGameplayStore must implement ICyberDefenseAchievementReader.");
    }

    [Fact]
    public async Task R02_FreshStore_GetUnlockedAchievementsAsync_ReturnsEmptyList()
    {
        var dbPath = await CreateFreshStoreAsync("r02_empty");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.NotNull(achievements);
        Assert.Empty(achievements);
    }

    // =========================================================================
    // 2. Deterministic ordering and UTC preservation
    // =========================================================================

    [Fact]
    public async Task R03_GetUnlockedAchievementsAsync_ReturnsDeterministicOrdering()
    {
        var dbPath = await CreateFreshStoreAsync("r03_ordering");

        // Insert synthetic rows out of order (by time and ID)
        await ExecuteRawAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('first_upgrade', 0, '2026-10-28T12:00:00.0000000+00:00');

            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '2026-10-28T10:00:00.0000000+00:00');

            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('boss_slayer', 0, '2026-10-28T10:00:00.0000000+00:00');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.Equal(3, achievements.Count);
        // Ordering: unlocked_at_utc ASC, achievement_id ASC
        // boss_slayer and sector_10 both have 10:00:00, boss_slayer comes first alphabetically
        Assert.Equal("boss_slayer", achievements[0].AchievementId);
        Assert.Equal("sector_10", achievements[1].AchievementId);
        Assert.Equal("first_upgrade", achievements[2].AchievementId);
    }

    [Fact]
    public async Task R04_GetUnlockedAchievementsAsync_PreservesUtcTimestampExactly()
    {
        var dbPath = await CreateFreshStoreAsync("r04_utc_timestamp");
        var expectedTime = DateTimeOffset.Parse("2026-10-28T14:30:45.1234567+00:00", CultureInfo.InvariantCulture);

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{expectedTime:O}');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();

        var single = Assert.Single(achievements);
        Assert.Equal("sector_10", single.AchievementId);
        Assert.Equal(0, single.ResetEpoch);
        Assert.Equal(TimeSpan.Zero, single.UnlockedAtUtc.Offset);
        Assert.Equal(expectedTime, single.UnlockedAtUtc);
    }

    [Fact]
    public async Task R05_GetUnlockedAchievementsAsync_ReturnsUnknownFutureAchievementId_WhenStructurallyValid()
    {
        var dbPath = await CreateFreshStoreAsync("r05_unknown_id");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('future_dlc_achievement', 0, '{now}');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();

        var single = Assert.Single(achievements);
        Assert.Equal("future_dlc_achievement", single.AchievementId);
        Assert.False(CyberDefenseAchievementIds.IsKnown(single.AchievementId));
    }

    // =========================================================================
    // 3. Fail-closed cross-epoch and fencing rules
    // =========================================================================

    [Fact]
    public async Task R06_GetUnlockedAchievementsAsync_CrossEpochRow_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r06_cross_epoch");
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Progression reset_epoch is 0, insert achievement with reset_epoch = 1 while store is open
        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 1, '{now}');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("epoch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R07_GetUnlockedAchievementsAsync_PendingReset_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r07_pending_reset");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
            UPDATE gameplay_reset_intent SET is_pending = 1, target_epoch = 1, created_at = '{now}', updated_at = '{now}' WHERE id = 1;
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("reset is pending", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 4. Full Local Reset and isolation
    // =========================================================================

    [Fact]
    public async Task R08_FullLocalReset_ClearsAchievementsAtomically()
    {
        var dbPath = await CreateFreshStoreAsync("r08_full_reset");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('boss_slayer', 0, '{now}');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Stage reset intent
        await store.BeginOrGetResetIntentAsync();

        // Perform reset
        await store.ResetGameplayStateAsync(1);
        await store.ClearResetIntentAsync(1);

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();
        Assert.Empty(achievements);

        using var conn = OpenRaw(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM local_achievements;";
        long count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task R09_InterruptedReset_StartupRecovery_EventuallyClearsAchievements()
    {
        var dbPath = await CreateFreshStoreAsync("r09_interrupted_reset");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
        ");

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            // Stage reset to epoch 1
            await store1.BeginOrGetResetIntentAsync();
            // Simulate crash before ResetGameplayStateAsync
        }

        // Reopen store
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();

            var intent = await store2.GetResetIntentAsync();
            Assert.True(intent.IsPending);
            Assert.Equal(1, intent.TargetEpoch);

            // Reads are fenced
            var reader = AsAchievementReader(store2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());

            // AppResetCoordinator-style recovery: execute ResetGameplayStateAsync and clear intent
            await store2.ResetGameplayStateAsync(1);
            await store2.ClearResetIntentAsync(1);

            // Now achievements are cleared and reader works
            var achievements = await reader.GetUnlockedAchievementsAsync();
            Assert.Empty(achievements);
        }
    }

    [Fact]
    public async Task R10_LearnerStoreReset_DoesNotClearGameplayAchievements()
    {
        var gameplayDbPath = await CreateFreshStoreAsync("r10_gameplay");
        var learnerDbPath = Path.Combine(_testDirectory, $"learner_{Guid.NewGuid():N}.db");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(gameplayDbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
        ");

        using (var learnerStore = new SqliteLearnerStore(learnerDbPath))
        {
            await learnerStore.InitializeAsync();
            await learnerStore.ResetLearningProgressAsync();
        }

        // Gameplay achievements must be completely unaffected
        using (var gameplayStore = new SqliteGameplayStore(gameplayDbPath))
        {
            await gameplayStore.InitializeAsync();
            var reader = AsAchievementReader(gameplayStore);
            var achievements = await reader.GetUnlockedAchievementsAsync();
            var single = Assert.Single(achievements);
            Assert.Equal("sector_10", single.AchievementId);
        }
    }

    // =========================================================================
    // 5. Strict canonical UTC timestamp parsing and ordering regressions
    // =========================================================================

    [Fact]
    public async Task R11_GetUnlockedAchievementsAsync_ParseableHumanReadableDate_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r11_human_readable");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await ExecuteRawAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, 'October 28, 2026 12:00:00 +00:00');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R12_GetUnlockedAchievementsAsync_UnknownOffsetNotation_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r12_unknown_offset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await ExecuteRawAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '2026-10-28T12:00:00.0000000-00:00');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R13_GetUnlockedAchievementsAsync_NonUtcOffset_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r13_non_utc");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await ExecuteRawAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '2026-10-28T14:00:00.0000000+02:00');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R14_GetUnlockedAchievementsAsync_InvalidCalendarDate_FailsClosed()
    {
        var dbPath = await CreateFreshStoreAsync("r14_invalid_date");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await ExecuteRawAsync(dbPath, @"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '2026-02-30T12:00:00.0000000+00:00');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-10-28 12:00:00+00:00")]
    [InlineData("2026-10-28T12:00:00Z")]
    [InlineData("2026-10-28T12:00:00+00:00")]
    [InlineData("10/28/2026 12:00:00 PM")]
    public async Task R15_GetUnlockedAchievementsAsync_NoncanonicalIsoFormat_FailsClosed(string noncanonicalTimestamp)
    {
        var dbPath = await CreateFreshStoreAsync("r15_noncanonical");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{noncanonicalTimestamp}');
        ");

        var reader = AsAchievementReader(store);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetUnlockedAchievementsAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R16_CanonicalUtcTimestamp_GuaranteesChronologicalLexicalOrdering()
    {
        var dbPath = await CreateFreshStoreAsync("r16_chronological_ordering");

        // Two canonical timestamps at distinct times
        var earlyTime = new DateTimeOffset(2026, 10, 28, 9, 15, 30, TimeSpan.Zero);
        var laterTime = new DateTimeOffset(2026, 10, 28, 18, 45, 0, TimeSpan.Zero);
        string earlyStr = earlyTime.ToString("O", CultureInfo.InvariantCulture);
        string laterStr = laterTime.ToString("O", CultureInfo.InvariantCulture);

        // Insert in reverse chronological order
        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('later_ach', 0, '{laterStr}');
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('early_ach', 0, '{earlyStr}');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var reader = AsAchievementReader(store);
        var achievements = await reader.GetUnlockedAchievementsAsync();

        Assert.Equal(2, achievements.Count);
        Assert.Equal("early_ach", achievements[0].AchievementId);
        Assert.Equal(earlyTime, achievements[0].UnlockedAtUtc);
        Assert.Equal("later_ach", achievements[1].AchievementId);
        Assert.Equal(laterTime, achievements[1].UnlockedAtUtc);
    }
}
