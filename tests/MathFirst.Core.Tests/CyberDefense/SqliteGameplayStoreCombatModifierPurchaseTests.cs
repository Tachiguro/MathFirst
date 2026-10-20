namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;

public sealed class SqliteGameplayStoreCombatModifierPurchaseTests : IDisposable
{
    private const string Timestamp = "2026-10-20T08:00:00.0000000+00:00";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirst_ModifierPurchase_" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "synthetic.db");

    public SqliteGameplayStoreCombatModifierPurchaseTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        // Windows may retain a native learner-store handle briefly after Dispose. Cleanup is
        // best-effort for these isolated synthetic fixtures, as in the existing purchase suites.
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private async Task<SqliteGameplayStore> OpenAsync()
    {
        var store = new SqliteGameplayStore(DatabasePath);
        await store.InitializeAsync();
        return store;
    }

    private async Task ExecuteAsync(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private Task SeedAsync(bool critical, int rank = 0, int level = 80, int available = 30) => ExecuteAsync($"""
        UPDATE gameplay_progression SET store_revision = 41, reset_epoch = 2, cumulative_xp = 1234,
            player_level = {level}, earned_sp = {available + 7}, spent_sp = 7, available_sp = {available},
            attack_rank = 2, firewall_rank = 2, activated_firewall_rank = 1,
            consecutive_early_defeats = 3, is_beginner_assistance_active = 0,
            critical_strike_rank = {(critical ? rank : 1)}, overdrive_rank = {(critical ? 1 : rank)},
            updated_at = '{Timestamp}';
        UPDATE gameplay_run_state SET reset_epoch = 2, critical_correct_attack_count = {(critical && rank == 0 ? 0 : 7)},
            overdrive_streak = {(critical || rank > 0 ? 3 : 0)}, overdrive_remaining_bonus_attacks = 0,
            updated_at = '{Timestamp}';
        UPDATE gameplay_reset_intent SET current_epoch = 2, target_epoch = 2, updated_at = '{Timestamp}';
        """);

    private static Task<CyberDefenseCombatModifierSnapshot> PurchaseAsync(SqliteGameplayStore store, bool critical, int rank = 0, long epoch = 2,
        CancellationToken cancellationToken = default) => critical
        ? store.PurchaseCriticalStrikeUpgradeAsync(rank, epoch, cancellationToken)
        : store.PurchaseOverdriveUpgradeAsync(rank, epoch, cancellationToken);

    // Include all values and storage classes so failed operations cannot hide partial writes.
    private async Task<string[]> DumpAsync(string? onlyTable = null, string? databasePath = null, bool excludePurchaseColumns = false)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath ?? DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        var dump = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (onlyTable == null || reader.GetString(0) == onlyTable)
                {
                    tables.Add(reader.GetString(0));
                    dump.Add(reader.GetString(1));
                }
        }
        foreach (string table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table}\" ORDER BY 1;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                dump.Add(table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Where(i =>
                    !excludePurchaseColumns || !(table == "gameplay_progression"
                        ? new[] { "critical_strike_rank", "overdrive_rank", "spent_sp", "available_sp", "store_revision", "updated_at" }
                        : table == "gameplay_run_state"
                            ? new[] { "critical_correct_attack_count", "overdrive_streak", "overdrive_remaining_bonus_attacks", "updated_at" }
                            : Array.Empty<string>()).Contains(reader.GetName(i))).Select(i =>
                {
                    object value = reader.GetValue(i);
                    return value.GetType().Name + ":" + (value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture));
                })));
        }
        return dump.ToArray();
    }

    [Theory]
    [InlineData(true, 0, 12, 6)]
    [InlineData(true, 1, 50, 12)]
    [InlineData(false, 0, 20, 5)]
    [InlineData(false, 1, 80, 10)]
    public async Task ExactUnlockAndCost_CommitsOneRankAndRevision(bool critical, int rank, int level, int cost)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical, rank, level, cost);
        var before = await store.GetProgressionAsync();
        var unrelatedBefore = await DumpAsync(excludePurchaseColumns: true);
        var result = await PurchaseAsync(store, critical, rank);
        Assert.Equal(rank + 1, critical ? result.CriticalStrikeRank : result.OverdriveRank);
        Assert.Equal(1, critical ? result.OverdriveRank : result.CriticalStrikeRank);
        Assert.Equal(42, result.StoreRevision);
        Assert.Equal(2, result.ResetEpoch);
        Assert.Equal(result, await store.GetCombatModifierSnapshotAsync());
        var after = await store.GetProgressionAsync();
        Assert.Equal(new CyberDefenseProgressionRecord(42, 2, 1234, level, 7 + cost, 7 + cost, 0,
            2, before.EconomyVersion, after.UpdatedAt, 2, 1), after);
        Assert.True(after.UpdatedAt > before.UpdatedAt);
        Assert.Equal(after.EarnedSkillPoints, after.SpentSkillPoints + after.AvailableSkillPoints);
        Assert.Equal(critical ? 0 : 7, result.ModifierState.CriticalCorrectAttackCount);
        Assert.Equal(critical || rank > 0 ? 3 : 0, result.ModifierState.OverdriveStreak);
        Assert.Equal(0, result.ModifierState.OverdriveRemainingBonusAttacks);
        Assert.Equal(unrelatedBefore, await DumpAsync(excludePurchaseColumns: true));
    }

    [Theory]
    [InlineData(true, 0, 11, 30)]
    [InlineData(true, 1, 49, 30)]
    [InlineData(false, 0, 19, 30)]
    [InlineData(false, 1, 79, 30)]
    [InlineData(true, 0, 12, 5)]
    [InlineData(true, 1, 50, 11)]
    [InlineData(false, 0, 20, 4)]
    [InlineData(false, 1, 80, 9)]
    public async Task IneligiblePurchase_LeavesEntireDatabaseUnchanged(bool critical, int rank, int level, int available)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical, rank, level, available);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, rank));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateStaleAndMaximumPurchases_DoNotChargeAgain(bool critical)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical);
        await PurchaseAsync(store, critical);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 1, 1));
        Assert.Equal(before, await DumpAsync());
        await PurchaseAsync(store, critical, 1);
        before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 2));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true, -1, 2)]
    [InlineData(true, 3, 2)]
    [InlineData(true, 0, -1)]
    [InlineData(false, -1, 2)]
    [InlineData(false, 3, 2)]
    [InlineData(false, 0, -1)]
    public async Task InvalidExpectedArguments_AreRejected(bool critical, int rank, long epoch)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PurchaseAsync(store, critical, rank, epoch));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConflictingRankAndResetFence_AreRejected(bool critical)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 0, 0));
        Assert.Equal(before, await DumpAsync());
        await ExecuteAsync($"UPDATE gameplay_reset_intent SET is_pending = 1, target_epoch = 3, created_at = '{Timestamp}';");
        before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    public async Task OverdrivePromotion_PreservesLegitimateStreakAndWindow(int streak, int remaining)
    {
        using var store = await OpenAsync();
        await SeedAsync(false, 1);
        await ExecuteAsync($"UPDATE gameplay_run_state SET overdrive_streak = {streak}, overdrive_remaining_bonus_attacks = {remaining};");
        var result = await PurchaseAsync(store, false, 1);
        Assert.Equal(new CyberDefenseCombatModifierState(7, streak, remaining), result.ModifierState);
        if (streak == 4)
        {
            var next = CyberDefenseCombatModifierTransition.ResolveAttempt(result.ModifierState, true, 1, 2);
            Assert.Equal(0, next.OverdriveBonusPercent);
            Assert.Equal(2, next.NextState.OverdriveRemainingBonusAttacks);
            Assert.Equal(0, next.NextState.OverdriveStreak);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(4, 0)]
    public async Task CriticalPromotion_ResetsOnlyCriticalPhase(int streak, int remaining)
    {
        using var store = await OpenAsync();
        await SeedAsync(true, 1);
        await ExecuteAsync($"UPDATE gameplay_run_state SET overdrive_streak = {streak}, overdrive_remaining_bonus_attacks = {remaining};");
        var result = await PurchaseAsync(store, true, 1);
        Assert.Equal(new CyberDefenseCombatModifierState(0, streak, remaining), result.ModifierState);
    }

    public static IEnumerable<object[]> CorruptStates()
    {
        string[] changes =
        [
            "UPDATE gameplay_progression SET critical_strike_rank = 0;",
            "UPDATE gameplay_progression SET overdrive_rank = 0;",
            "UPDATE gameplay_progression SET critical_strike_rank = 3;",
            "UPDATE gameplay_progression SET overdrive_rank = -1;",
            "UPDATE gameplay_run_state SET critical_correct_attack_count = -1;",
            "UPDATE gameplay_run_state SET critical_correct_attack_count = 2147483648;",
            "UPDATE gameplay_run_state SET overdrive_streak = 5;",
            "UPDATE gameplay_run_state SET overdrive_remaining_bonus_attacks = 2;",
            "UPDATE gameplay_run_state SET reset_epoch = 1;",
            "UPDATE gameplay_reset_intent SET current_epoch = 1, target_epoch = 1;",
            "UPDATE gameplay_reset_intent SET target_epoch = 3;",
            "UPDATE gameplay_progression SET earned_sp = 1;",
            "UPDATE gameplay_progression SET earned_sp = 10, spent_sp = -1, available_sp = 11;",
            "UPDATE gameplay_progression SET earned_sp = 2147483648, spent_sp = 2147483618;",
            "UPDATE gameplay_progression SET store_revision = 0;",
            "DELETE FROM gameplay_run_state;",
            "DELETE FROM gameplay_progression;",
            "DELETE FROM gameplay_reset_intent;"
        ];
        foreach (bool critical in new[] { true, false })
        foreach (string sql in changes) yield return [critical, sql];
    }

    [Theory]
    [MemberData(nameof(CorruptStates))]
    public async Task CorruptState_FailsClosedWithoutNormalization(bool critical, string sql)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical, 1);
        await ExecuteAsync("PRAGMA ignore_check_constraints = ON; " + sql);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 1));
        Assert.Equal(before, await DumpAsync());
    }

    public static IEnumerable<object[]> InvalidStorage()
    {
        (string Table, string Column)[] fields =
        [
            ("gameplay_progression", "critical_strike_rank"), ("gameplay_progression", "overdrive_rank"),
            ("gameplay_progression", "earned_sp"), ("gameplay_progression", "spent_sp"), ("gameplay_progression", "available_sp"),
            ("gameplay_progression", "player_level"), ("gameplay_progression", "store_revision"),
            ("gameplay_progression", "reset_epoch"), ("gameplay_run_state", "reset_epoch"),
            ("gameplay_run_state", "critical_correct_attack_count"), ("gameplay_run_state", "overdrive_streak"),
            ("gameplay_run_state", "overdrive_remaining_bonus_attacks"),
            ("gameplay_reset_intent", "is_pending"), ("gameplay_reset_intent", "current_epoch"), ("gameplay_reset_intent", "target_epoch")
        ];
        foreach (bool critical in new[] { true, false })
        foreach (var (table, column) in fields)
        foreach (string value in new[] { "1.5", "'corrupt'", "X'0102'" }) yield return [critical, table, column, value];
    }

    [Theory]
    [MemberData(nameof(InvalidStorage))]
    public async Task NonIntegerRankAccountingAndModifierState_AreRejected(bool critical, string table, string column, string value)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical, 1);
        await ExecuteAsync($"PRAGMA ignore_check_constraints = ON; UPDATE {table} SET {column} = {value};");
        var before = await DumpAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(store, critical, 1));
        Assert.Contains(column, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevisionOverflow_RollsBack(bool critical)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical);
        await ExecuteAsync("UPDATE gameplay_progression SET store_revision = 9223372036854775807;");
        var before = await DumpAsync();
        await Assert.ThrowsAsync<OverflowException>(() => PurchaseAsync(store, critical));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true, "progression")]
    [InlineData(true, "run")]
    [InlineData(false, "progression")]
    [InlineData(false, "run")]
    public async Task InjectedTransactionFailure_RollsBackEveryWrite(bool critical, string stage)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical, critical ? 1 : 0);
        await ExecuteAsync($"""
            CREATE TRIGGER fail_purchase AFTER UPDATE ON gameplay_{(stage == "run" ? "run_state" : "progression")}
            BEGIN SELECT RAISE(ABORT, 'synthetic purchase failure'); END;
            """);
        var before = await DumpAsync();
        await Assert.ThrowsAsync<SqliteException>(() => PurchaseAsync(store, critical, critical ? 1 : 0));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentConnections_OnlyOnePurchaseCommits(bool critical)
    {
        using var first = await OpenAsync();
        using var second = await OpenAsync();
        await SeedAsync(critical);
        async Task<bool> TryPurchase(SqliteGameplayStore store)
        {
            try { await PurchaseAsync(store, critical); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => TryPurchase(first)), Task.Run(() => TryPurchase(second)));
        Assert.Single(outcomes, succeeded => succeeded);
        Assert.Equal(42, (await first.GetCombatModifierSnapshotAsync()).StoreRevision);
        Assert.Equal(critical ? 13 : 12, (await first.GetProgressionAsync()).SpentSkillPoints);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledPurchase_ChangesNothing(bool critical)
    {
        using var store = await OpenAsync();
        await SeedAsync(critical);
        var before = await DumpAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PurchaseAsync(store, critical, cancellationToken: cancellation.Token));
        Assert.Equal(before, await DumpAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reopen_PreservesCommittedSnapshotAndUnrelatedRows(bool critical)
    {
        CyberDefenseCombatModifierSnapshot result;
        string[] pendingBefore, receiptsBefore, resetBefore, schemaBefore;
        using (var store = await OpenAsync())
        {
            await SeedAsync(critical);
            await ExecuteAsync($"""
                INSERT INTO gameplay_receipt_ledger (submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at)
                    VALUES ('old-receipt', 'CalmModeSuppressed', 'synthetic-fact', 1, 1, 100, 2, '{Timestamp}');
                INSERT INTO gameplay_pending_intent (submission_id, fact_id, is_correct, is_eligible, response_latency_ms,
                    reset_epoch, effective_attack_damage, created_at, bound_critical_strike_rank, bound_overdrive_rank)
                    VALUES ('old-intent', 'synthetic-fact', 1, 1, 100, 2, 3, '{Timestamp}', 0, 0);
                """);
            pendingBefore = await DumpAsync("gameplay_pending_intent");
            receiptsBefore = await DumpAsync("gameplay_receipt_ledger");
            resetBefore = await DumpAsync("gameplay_reset_intent");
            schemaBefore = await DumpAsync("gameplay_schema_info");
            result = await PurchaseAsync(store, critical);
        }
        using var reopened = await OpenAsync();
        Assert.Equal(result, await reopened.GetCombatModifierSnapshotAsync());
        Assert.Equal(pendingBefore, await DumpAsync("gameplay_pending_intent"));
        Assert.Equal(receiptsBefore, await DumpAsync("gameplay_receipt_ledger"));
        Assert.Equal(resetBefore, await DumpAsync("gameplay_reset_intent"));
        Assert.Equal(schemaBefore, await DumpAsync("gameplay_schema_info"));
    }

    [Fact]
    public async Task Purchases_DoNotMutateSyntheticLearnerOrFsrsData()
    {
        string learnerPath = Path.Combine(_directory, "synthetic-learner.db");
        using var learner = new SqliteLearnerStore(learnerPath);
        await learner.InitializeAsync();
        var before = await DumpAsync(databasePath: learnerPath);
        using var store = await OpenAsync();
        await SeedAsync(true);
        await PurchaseAsync(store, true);
        await PurchaseAsync(store, false, 1);
        Assert.Equal(before, await DumpAsync(databasePath: learnerPath));
    }
}
