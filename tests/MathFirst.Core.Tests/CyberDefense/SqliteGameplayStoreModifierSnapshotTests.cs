namespace MathFirst.Core.Tests.CyberDefense;

using System.Globalization;
using System.Reflection;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;

public sealed class SqliteGameplayStoreModifierSnapshotTests : IDisposable
{
    private const string Timestamp = "2026-10-20T08:00:00.0000000+00:00";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirst_ModifierSnapshot_" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "synthetic.db");

    public SqliteGameplayStoreModifierSnapshotTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        // Stores clear their own pool on disposal; raw fixture connections are not pooled.
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("modifier-snapshot")]
    [InlineData("schema-v7")]
    [InlineData("corrupt-schema")]
    public async Task FixtureCleanup_PreservesUnrelatedPooledConnection(string cleanup)
    {
        using var unrelated = new SqliteConnection($"Data Source={DatabasePath};Pooling=True");
        try
        {
            await unrelated.OpenAsync();
            var handle = unrelated.Handle!;
            unrelated.Close();
            Assert.False(handle.IsClosed);

            switch (cleanup)
            {
                case "modifier-snapshot":
                    using (var fixture = new SqliteGameplayStoreModifierSnapshotTests()) { }
                    break;
                case "schema-v7":
                    using (var fixture = new SqliteGameplayStoreSchemaV7MigrationTests()) { }
                    break;
                case "corrupt-schema":
                    using (var fixture = new SqliteGameplayStoreSchemaTests())
                    {
                        await fixture.SchemaV1_InitializationFailure_DoesNotLeavePartiallyVersionedSchema();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cleanup));
            }

            // This fixture does not own the other database's idle native handle.
            Assert.False(handle.IsClosed);
            await unrelated.OpenAsync();
            Assert.Same(handle, unrelated.Handle);
            using var command = unrelated.CreateCommand();
            command.CommandText = "SELECT 42;";
            Assert.Equal(42L, await command.ExecuteScalarAsync());
        }
        finally
        {
            unrelated.Close();
            SqliteConnection.ClearPool(unrelated);
        }
    }

    private async Task<SqliteGameplayStore> OpenStoreAsync()
    {
        var store = new SqliteGameplayStore(DatabasePath);
        await store.InitializeAsync();
        return store;
    }

    private async Task ExecuteAsync(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private Task SeedModifiersAsync(int criticalRank, int overdriveRank, int count, int streak, int remaining) =>
        ExecuteAsync($"""
            UPDATE gameplay_progression SET critical_strike_rank = {criticalRank}, overdrive_rank = {overdriveRank};
            UPDATE gameplay_run_state SET critical_correct_attack_count = {count}, overdrive_streak = {streak},
                overdrive_remaining_bonus_attacks = {remaining};
            """);

    private async Task<byte[]> ReadDatabaseBytesAsync()
    {
        await using var file = new FileStream(DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var contents = new MemoryStream();
        await file.CopyToAsync(contents);
        return contents.ToArray();
    }

    // A logical dump includes every table, storage class, value and DDL, including pending intents/receipts.
    private async Task<string[]> DumpDatabaseAsync()
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        var dump = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
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
            {
                dump.Add(table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i =>
                {
                    object value = reader.GetValue(i);
                    return value.GetType().Name + ":" + (value is byte[] bytes
                        ? Convert.ToHexString(bytes)
                        : Convert.ToString(value, CultureInfo.InvariantCulture));
                })));
            }
        }
        return dump.ToArray();
    }

    [Fact]
    public async Task FreshV7_ReturnsDisabledModifiersAndResetContext()
    {
        using var store = await OpenStoreAsync();
        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(0, snapshot.CriticalStrikeRank);
        Assert.Equal(0, snapshot.OverdriveRank);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, snapshot.ModifierState);
        Assert.Equal(1, snapshot.StoreRevision);
        Assert.Equal(0, snapshot.ResetEpoch);
        Assert.False(snapshot.ResetIntent.IsPending);
        Assert.Equal(0, snapshot.ResetIntent.CurrentEpoch);
        Assert.Equal(0, snapshot.ResetIntent.TargetEpoch);
        Assert.Null(snapshot.ResetIntent.CreatedAt);
    }

    [Fact]
    public async Task MigratedV6_ReturnsZeroModifiersWithPreservedRevisionAndEpoch()
    {
        // Use the retained pre-V7 DDL, rather than a V7 database relabeled as V6.
        string ddl = (string)typeof(SqliteGameplayStore).GetField("SchemaV6Ddl", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        await ExecuteAsync(ddl + $"""
            INSERT INTO gameplay_schema_info VALUES ('schema_version', '6'), ('store_revision', '41'),
                ('reset_epoch', '2'), ('economy_version', 'ECONOMY_V1');
            INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at) VALUES (1, 41, 2, '{Timestamp}');
            INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 1, 0, 2, 100, 2, '{Timestamp}');
            INSERT INTO gameplay_reset_intent VALUES (1, 0, 2, 2, NULL, '{Timestamp}');
            """);
        using var store = await OpenStoreAsync();
        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(0, snapshot.CriticalStrikeRank);
        Assert.Equal(0, snapshot.OverdriveRank);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, snapshot.ModifierState);
        Assert.Equal(41, snapshot.StoreRevision);
        Assert.Equal(2, snapshot.ResetEpoch);
        Assert.Equal(2, snapshot.ResetIntent.CurrentEpoch);
        Assert.Equal(2, snapshot.ResetIntent.TargetEpoch);
        Assert.Equal(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture), snapshot.ResetIntent.UpdatedAt);
    }

    [Theory]
    [InlineData(1, 1, 4, 0, 0)]
    [InlineData(2, 2, 3, 3, 0)]
    [InlineData(1, 1, 2147483647, 4, 0)]
    [InlineData(2, 2, 2147483647, 4, 0)]
    [InlineData(0, 1, 0, 0, 2)]
    [InlineData(0, 2, 0, 1, 1)]
    [InlineData(2, 1, 5, 1, 1)]
    [InlineData(1, 2, 7, 0, 2)]
    [InlineData(1, 0, 1, 0, 0)]
    public async Task ValidPurchasedRanksAndBoundaryStates_RoundTrip(int critical, int overdrive, int count, int streak, int remaining)
    {
        using var store = await OpenStoreAsync();
        await SeedModifiersAsync(critical, overdrive, count, streak, remaining);
        await ExecuteAsync("""
            UPDATE gameplay_progression SET store_revision = 41, reset_epoch = 2;
            UPDATE gameplay_run_state SET reset_epoch = 2;
            UPDATE gameplay_reset_intent SET current_epoch = 2, target_epoch = 2;
            """);
        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(critical, snapshot.CriticalStrikeRank);
        Assert.Equal(overdrive, snapshot.OverdriveRank);
        Assert.Equal(new CyberDefenseCombatModifierState(count, streak, remaining), snapshot.ModifierState);
        Assert.Equal(41, snapshot.StoreRevision);
        Assert.Equal(2, snapshot.ResetEpoch);
    }

    public static IEnumerable<object[]> InvalidIntegerStorage()
    {
        (string Table, string Column)[] fields =
        [
            ("gameplay_progression", "critical_strike_rank"), ("gameplay_progression", "overdrive_rank"),
            ("gameplay_run_state", "critical_correct_attack_count"), ("gameplay_run_state", "overdrive_streak"),
            ("gameplay_run_state", "overdrive_remaining_bonus_attacks"),
            ("gameplay_progression", "store_revision"), ("gameplay_progression", "reset_epoch"),
            ("gameplay_run_state", "reset_epoch"), ("gameplay_reset_intent", "is_pending"),
            ("gameplay_reset_intent", "current_epoch"), ("gameplay_reset_intent", "target_epoch")
        ];
        foreach (var (table, column) in fields)
        foreach (var (sql, type) in new[] { ("1.5", "real"), ("'corrupt'", "text"), ("X'0102'", "blob") })
            yield return [table, column, sql, type];
    }

    [Theory]
    [MemberData(nameof(InvalidIntegerStorage))]
    public async Task NonIntegerStorage_IsRejectedBeforeCoercion(string table, string column, string sql, string type)
    {
        using var store = await OpenStoreAsync();
        await SeedModifiersAsync(1, 1, 1, 1, 0);
        await ExecuteAsync($"PRAGMA ignore_check_constraints = ON; UPDATE {table} SET {column} = {sql};");
        // Verify the fixture really stored the corrupt class; affinity may otherwise normalize inputs.
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT typeof({column}) FROM {table};";
            Assert.Equal(type, await command.ExecuteScalarAsync());
        });
        var before = await DumpDatabaseAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.Contains(column, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await DumpDatabaseAsync());
    }

    [Theory]
    [InlineData("gameplay_progression", "critical_strike_rank")]
    [InlineData("gameplay_progression", "overdrive_rank")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count")]
    [InlineData("gameplay_run_state", "overdrive_streak")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks")]
    [InlineData("gameplay_progression", "store_revision")]
    [InlineData("gameplay_progression", "reset_epoch")]
    [InlineData("gameplay_run_state", "reset_epoch")]
    [InlineData("gameplay_reset_intent", "is_pending")]
    [InlineData("gameplay_reset_intent", "current_epoch")]
    [InlineData("gameplay_reset_intent", "target_epoch")]
    public async Task RequiredNull_IsRejected(string table, string column)
    {
        using var store = await OpenStoreAsync();
        // Rebuild only this synthetic fixture table to simulate corruption bypassing NOT NULL.
        await ExecuteAsync($"""
            ALTER TABLE {table} RENAME TO corrupt_fixture;
            CREATE TABLE {table} AS SELECT * FROM corrupt_fixture;
            DROP TABLE corrupt_fixture;
            UPDATE {table} SET {column} = NULL;
            """);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.Contains(column, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntegralRealStorage_IsRejectedEvenWithoutFractionalPart()
    {
        using var store = await OpenStoreAsync();
        // The expression gives the synthetic replacement column no INTEGER affinity.
        await ExecuteAsync("""
            ALTER TABLE gameplay_progression RENAME TO corrupt_fixture;
            CREATE TABLE gameplay_progression AS SELECT id, store_revision, reset_epoch,
                critical_strike_rank + 0 AS critical_strike_rank, overdrive_rank FROM corrupt_fixture;
            DROP TABLE corrupt_fixture;
            UPDATE gameplay_progression SET critical_strike_rank = 1.0;
            """);
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT typeof(critical_strike_rank) FROM gameplay_progression;";
            Assert.Equal("real", await command.ExecuteScalarAsync());
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.Contains("critical_strike_rank", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gameplay_progression", "critical_strike_rank", "-1")]
    [InlineData("gameplay_progression", "critical_strike_rank", "3")]
    [InlineData("gameplay_progression", "overdrive_rank", "-1")]
    [InlineData("gameplay_progression", "overdrive_rank", "3")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "-1")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "2147483648")]
    [InlineData("gameplay_run_state", "overdrive_streak", "-1")]
    [InlineData("gameplay_run_state", "overdrive_streak", "2147483648")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "-1")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "3")]
    [InlineData("gameplay_progression", "store_revision", "0")]
    [InlineData("gameplay_progression", "reset_epoch", "-1")]
    [InlineData("gameplay_run_state", "reset_epoch", "-1")]
    [InlineData("gameplay_reset_intent", "is_pending", "2")]
    [InlineData("gameplay_reset_intent", "current_epoch", "-1")]
    [InlineData("gameplay_reset_intent", "target_epoch", "-1")]
    public async Task OutOfRangeIntegers_AreRejected(string table, string column, string sql)
    {
        using var store = await OpenStoreAsync();
        await ExecuteAsync($"PRAGMA ignore_check_constraints = ON; UPDATE {table} SET {column} = {sql};");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(0, 0, 0, 1, 0)]
    [InlineData(0, 0, 0, 0, 2)]
    [InlineData(1, 0, 2, 1, 1)]
    [InlineData(1, 1, 2, 1, 2)]
    [InlineData(1, 2, 2, 0, 1)]
    [InlineData(1, 1, 2, 2, 1)]
    [InlineData(1, 1, 2, 5, 0)]
    [InlineData(2, 2, 2, 5, 0)]
    [InlineData(2, 2, 2, 2147483647, 0)]
    public async Task ImpossibleCrossRowStates_AreRejected(int critical, int overdrive, int count, int streak, int remaining)
    {
        using var store = await OpenStoreAsync();
        await SeedModifiersAsync(critical, overdrive, count, streak, remaining);
        var before = await DumpDatabaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.Equal(before, await DumpDatabaseAsync());
    }

    [Fact]
    public async Task Rank1Streak4_PreservedAtRank2_ArmsOnNextCorrectAnswer()
    {
        using var store = await OpenStoreAsync();
        var rank1 = CyberDefenseCombatModifierState.Initial;
        for (int i = 0; i < 4; i++)
            rank1 = CyberDefenseCombatModifierTransition.ResolveAttempt(rank1, true, 0, 1).NextState;
        Assert.Equal(new CyberDefenseCombatModifierState(0, 4, 0), rank1);
        await SeedModifiersAsync(0, 2, 0, rank1.OverdriveStreak, 0);
        var restored = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(rank1, restored.ModifierState);
        var next = CyberDefenseCombatModifierTransition.ResolveAttempt(restored.ModifierState, true, restored.CriticalStrikeRank, restored.OverdriveRank);
        Assert.Equal(0, next.OverdriveBonusPercent);
        Assert.Equal(new CyberDefenseCombatModifierState(0, 0, 2), next.NextState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingReset_BlocksSnapshotBeforeAndAfterGameplayReset(bool applyReset)
    {
        using var store = await OpenStoreAsync();
        await SeedModifiersAsync(1, 1, 3, 4, 0);
        var intent = await store.BeginOrGetResetIntentAsync();
        if (applyReset) await store.ResetGameplayStateAsync(intent.TargetEpoch);
        var before = await DumpDatabaseAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.Contains("pending", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await DumpDatabaseAsync());
        if (!applyReset) await store.ResetGameplayStateAsync(intent.TargetEpoch);
        await store.ClearResetIntentAsync(intent.TargetEpoch);
        var restored = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(1, restored.ResetEpoch);
        Assert.Equal(1, restored.StoreRevision);
        Assert.Equal(CyberDefenseCombatModifierState.Initial, restored.ModifierState);
        Assert.Equal(0, restored.CriticalStrikeRank);
        Assert.Equal(0, restored.OverdriveRank);
    }

    [Theory]
    [InlineData("UPDATE gameplay_run_state SET reset_epoch = 1;")]
    [InlineData("UPDATE gameplay_progression SET reset_epoch = 1; UPDATE gameplay_run_state SET reset_epoch = 1;")]
    [InlineData("UPDATE gameplay_reset_intent SET current_epoch = 1, target_epoch = 1;")]
    [InlineData("UPDATE gameplay_reset_intent SET target_epoch = 1;")]
    [InlineData("UPDATE gameplay_reset_intent SET is_pending = 1;")]
    [InlineData("UPDATE gameplay_reset_intent SET created_at = '2026-10-20T08:00:00.0000000+00:00';")]
    [InlineData("UPDATE gameplay_reset_intent SET updated_at = 'invalid';")]
    [InlineData("UPDATE gameplay_reset_intent SET updated_at = X'0102';")]
    public async Task InvalidEpochOrResetMetadata_IsRejected(string sql)
    {
        using var store = await OpenStoreAsync();
        await ExecuteAsync("PRAGMA ignore_check_constraints = ON; " + sql);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
    }

    [Theory]
    [InlineData("gameplay_progression")]
    [InlineData("gameplay_run_state")]
    [InlineData("gameplay_reset_intent")]
    public async Task MissingAuthoritativeRow_IsRejected(string table)
    {
        using var store = await OpenStoreAsync();
        await ExecuteAsync($"DELETE FROM {table};");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
    }

    [Fact]
    public async Task RepeatedReads_AreEquivalentAndCannotWriteEvenOnQueryOnlyConnection()
    {
        using var store = await OpenStoreAsync();
        await SeedModifiersAsync(2, 2, 23, 1, 1);
        var before = await DumpDatabaseAsync();
        var bytesBefore = await ReadDatabaseBytesAsync();
        await store.ExecuteUnderGateForTestAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA query_only = ON;";
            await command.ExecuteNonQueryAsync();
        });
        var first = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(new CyberDefenseCombatModifierState(23, 1, 1), first.ModifierState);
        Assert.Equal(first, await store.GetCombatModifierSnapshotAsync());
        Assert.Equal(before, await DumpDatabaseAsync());
        Assert.Equal(bytesBefore, await ReadDatabaseBytesAsync());
    }

    [Fact]
    public async Task ExternalTransaction_CannotExposePartialRanksRevisionOrEpoch()
    {
        using var store = await OpenStoreAsync();
        using var writer = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await writer.OpenAsync();
        using var transaction = writer.BeginTransaction();
        using var command = writer.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE gameplay_progression SET critical_strike_rank = 2, overdrive_rank = 2, store_revision = 41, reset_epoch = 2;";
        await command.ExecuteNonQueryAsync();
        var old = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(1, old.StoreRevision);
        Assert.Equal(0, old.ResetEpoch);
        Assert.Equal(0, old.CriticalStrikeRank);
        command.CommandText = """
            UPDATE gameplay_run_state SET critical_correct_attack_count = 7, overdrive_streak = 4, reset_epoch = 2;
            UPDATE gameplay_reset_intent SET current_epoch = 2, target_epoch = 2;
            """;
        await command.ExecuteNonQueryAsync();
        transaction.Commit();
        var current = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(41, current.StoreRevision);
        Assert.Equal(2, current.ResetEpoch);
        Assert.Equal(2, current.CriticalStrikeRank);
        Assert.Equal(2, current.OverdriveRank);
        Assert.Equal(2, current.ResetIntent.CurrentEpoch);
        Assert.Equal(new CyberDefenseCombatModifierState(7, 4, 0), current.ModifierState);
    }

    [Fact]
    public async Task Snapshot_UsesExistingGateAndReadsStateAfterQueuedReset()
    {
        using var store = await OpenStoreAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = store.ExecuteUnderGateForTestAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        var reset = store.BeginOrGetResetIntentAsync();
        var read = store.GetCombatModifierSnapshotAsync();
        bool wasBlocked = !read.IsCompleted;
        release.SetResult();
        await blocker;
        await reset;
        Assert.True(wasBlocked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
    }

    [Fact]
    public async Task SnapshotDoesNotReinterpretPendingBindingsOrReceiptReplay()
    {
        using var store = await OpenStoreAsync();
        var submitted = new CyberDefensePendingIntentRecord("receipt", "1+1", true, true, 100, 0,
            DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture), effectiveAttackDamage: 1);
        var receipt = await store.ApplyAttemptTransactionAsync(submitted);
        var pending = new CyberDefensePendingIntentRecord("pending", "2+2", true, true, 100, 0,
            DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture), effectiveAttackDamage: 1);
        await store.StagePendingIntentAsync(pending);
        await SeedModifiersAsync(2, 2, 7, 4, 0);
        var before = await DumpDatabaseAsync();
        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(new CyberDefenseCombatModifierState(7, 4, 0), snapshot.ModifierState);
        Assert.Equal(before, await DumpDatabaseAsync());
        Assert.Equal(pending, await store.GetPendingIntentAsync("pending"));
        Assert.Equal(receipt, await store.ApplyAttemptTransactionAsync(submitted));
        Assert.Equal(snapshot, await store.GetCombatModifierSnapshotAsync());
    }

    [Fact]
    public async Task UninitializedRead_DoesNotCreateOrMigrateDatabase()
    {
        using var store = new SqliteGameplayStore(DatabasePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCombatModifierSnapshotAsync());
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task CancelledRead_DoesNotChangeDatabaseAndGateRemainsUsable()
    {
        using var store = await OpenStoreAsync();
        var before = await DumpDatabaseAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetCombatModifierSnapshotAsync(cancellation.Token));
        Assert.Equal(before, await DumpDatabaseAsync());
        Assert.Equal(1, (await store.GetCombatModifierSnapshotAsync()).StoreRevision);
    }
}
