namespace MathFirst.Infrastructure.Sqlite.Gameplay;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using Microsoft.Data.Sqlite;

public sealed class SqliteGameplayStore : IGameplayStore
{
    public const string DefaultDatabaseFileName = "mathfirst_gameplay.db";
    public const int DefaultSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly string _storagePath;
    private SqliteConnection? _connection;
    private bool _isInitialized;
    private readonly object _lock = new();

    private const string SchemaV1Ddl = @"
        CREATE TABLE gameplay_schema_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE gameplay_progression (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            store_revision INTEGER NOT NULL CHECK (store_revision >= 1),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            updated_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_run_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            sector INTEGER NOT NULL CHECK (sector >= 1),
            opponent_index INTEGER NOT NULL CHECK (opponent_index >= 0),
            opponent_current_hp INTEGER NOT NULL CHECK (opponent_current_hp >= 1),
            player_current_hp INTEGER NOT NULL CHECK (player_current_hp >= 1 AND player_current_hp <= 100),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            updated_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_receipt_ledger (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            receipt_kind TEXT NOT NULL CHECK (receipt_kind IN ('Applied', 'CalmModeSuppressed')),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            processed_at TEXT NOT NULL,
            requested_attack_damage INTEGER CHECK (requested_attack_damage IS NULL OR requested_attack_damage >= 0),
            applied_opponent_damage INTEGER CHECK (applied_opponent_damage IS NULL OR applied_opponent_damage >= 0),
            excess_opponent_damage INTEGER CHECK (excess_opponent_damage IS NULL OR excess_opponent_damage >= 0),
            incoming_enemy_damage INTEGER CHECK (incoming_enemy_damage IS NULL OR incoming_enemy_damage >= 0),
            applied_player_damage INTEGER CHECK (applied_player_damage IS NULL OR applied_player_damage >= 0),
            excess_enemy_damage INTEGER CHECK (excess_enemy_damage IS NULL OR excess_enemy_damage >= 0),
            potential_healing INTEGER CHECK (potential_healing IS NULL OR potential_healing >= 0),
            applied_healing INTEGER CHECK (applied_healing IS NULL OR applied_healing >= 0),
            is_opponent_defeated INTEGER CHECK (is_opponent_defeated IS NULL OR is_opponent_defeated IN (0, 1)),
            is_sector_completed INTEGER CHECK (is_sector_completed IS NULL OR is_sector_completed IN (0, 1)),
            is_game_over INTEGER CHECK (is_game_over IS NULL OR is_game_over IN (0, 1)),
            next_sector INTEGER CHECK (next_sector IS NULL OR next_sector >= 1),
            next_opponent_index INTEGER CHECK (next_opponent_index IS NULL OR next_opponent_index >= 0),
            next_opponent_current_hp INTEGER CHECK (next_opponent_current_hp IS NULL OR next_opponent_current_hp >= 1),
            next_player_current_hp INTEGER CHECK (next_player_current_hp IS NULL OR (next_player_current_hp >= 1 AND next_player_current_hp <= 100)),
            terminal_sector INTEGER CHECK (terminal_sector IS NULL OR terminal_sector >= 1),
            terminal_opponent_index INTEGER CHECK (terminal_opponent_index IS NULL OR terminal_opponent_index >= 0),
            terminal_opponent_kind TEXT CHECK (terminal_opponent_kind IS NULL OR terminal_opponent_kind IN ('Normal', 'Boss')),
            terminal_opponent_current_hp INTEGER CHECK (terminal_opponent_current_hp IS NULL OR terminal_opponent_current_hp >= 1),
            terminal_opponent_max_hp INTEGER CHECK (terminal_opponent_max_hp IS NULL OR terminal_opponent_max_hp >= 1),
            terminal_player_current_hp INTEGER CHECK (terminal_player_current_hp IS NULL OR terminal_player_current_hp = 0),
            CHECK (
                (receipt_kind = 'CalmModeSuppressed' AND
                 requested_attack_damage IS NULL AND
                 applied_opponent_damage IS NULL AND
                 excess_opponent_damage IS NULL AND
                 incoming_enemy_damage IS NULL AND
                 applied_player_damage IS NULL AND
                 excess_enemy_damage IS NULL AND
                 potential_healing IS NULL AND
                 applied_healing IS NULL AND
                 is_opponent_defeated IS NULL AND
                 is_sector_completed IS NULL AND
                 is_game_over IS NULL AND
                 next_sector IS NULL AND
                 next_opponent_index IS NULL AND
                 next_opponent_current_hp IS NULL AND
                 next_player_current_hp IS NULL AND
                 terminal_sector IS NULL AND
                 terminal_opponent_index IS NULL AND
                 terminal_opponent_kind IS NULL AND
                 terminal_opponent_current_hp IS NULL AND
                 terminal_opponent_max_hp IS NULL AND
                 terminal_player_current_hp IS NULL)
                OR
                (receipt_kind = 'Applied' AND
                 requested_attack_damage IS NOT NULL AND
                 applied_opponent_damage IS NOT NULL AND
                 excess_opponent_damage IS NOT NULL AND
                 incoming_enemy_damage IS NOT NULL AND
                 applied_player_damage IS NOT NULL AND
                 excess_enemy_damage IS NOT NULL AND
                 potential_healing IS NOT NULL AND
                 applied_healing IS NOT NULL AND
                 is_opponent_defeated IS NOT NULL AND
                 is_sector_completed IS NOT NULL AND
                 is_game_over IS NOT NULL AND
                 next_sector IS NOT NULL AND
                 next_opponent_index IS NOT NULL AND
                 next_opponent_current_hp IS NOT NULL AND
                 next_player_current_hp IS NOT NULL AND
                 ((is_game_over = 0 AND
                   terminal_sector IS NULL AND
                   terminal_opponent_index IS NULL AND
                   terminal_opponent_kind IS NULL AND
                   terminal_opponent_current_hp IS NULL AND
                   terminal_opponent_max_hp IS NULL AND
                   terminal_player_current_hp IS NULL)
                  OR
                  (is_game_over = 1 AND
                   terminal_sector IS NOT NULL AND
                   terminal_opponent_index IS NOT NULL AND
                   terminal_opponent_kind IS NOT NULL AND
                   terminal_opponent_current_hp IS NOT NULL AND
                   terminal_opponent_max_hp IS NOT NULL AND
                   terminal_player_current_hp = 0)))
            )
        );

        CREATE TABLE gameplay_pending_intent (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            created_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_reset_intent (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            is_pending INTEGER NOT NULL CHECK (is_pending IN (0, 1)),
            current_epoch INTEGER NOT NULL CHECK (current_epoch >= 0),
            target_epoch INTEGER NOT NULL CHECK (target_epoch >= current_epoch),
            created_at TEXT,
            updated_at TEXT NOT NULL,
            CHECK (
                (is_pending = 0 AND target_epoch = current_epoch)
                OR
                (is_pending = 1 AND target_epoch > current_epoch)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
            ON gameplay_receipt_ledger(reset_epoch, processed_at);

        CREATE INDEX IF NOT EXISTS ix_gameplay_pending_intent_epoch_created
            ON gameplay_pending_intent(reset_epoch, created_at);
    ";

    public SqliteGameplayStore(string storagePath)
    {
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        var dir = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = storagePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true
        };
        _connectionString = builder.ToString();
    }

    public string StoragePath => _storagePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isInitialized)
            {
                return;
            }
        }

        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 5000;
                ";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var probeCmd = connection.CreateCommand())
            {
                probeCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                using var reader = await probeCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingTables.Add(reader.GetString(0));
                }
            }

            if (existingTables.Count == 0)
            {
                await InitializeFreshDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ValidateExistingDatabaseAsync(connection, existingTables, cancellationToken).ConfigureAwait(false);
            }

            lock (_lock)
            {
                _connection = connection;
                _isInitialized = true;
            }
        }
        catch
        {
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            throw;
        }
    }

    private static async Task InitializeFreshDatabaseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var ddlCmd = connection.CreateCommand())
            {
                ddlCmd.Transaction = transaction;
                ddlCmd.CommandText = SchemaV1Ddl;
                await ddlCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            var initialRun = CyberDefenseRunState.InitialRun();

            using (var seedCmd = connection.CreateCommand())
            {
                seedCmd.Transaction = transaction;
                seedCmd.CommandText = @"
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                    INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                    VALUES (1, 1, 0, @now);

                    INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                    VALUES (1, @sector, @opponentIndex, @opponentHp, @playerHp, 0, @now);

                    INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                    VALUES (1, 0, 0, 0, NULL, @now);
                ";
                seedCmd.Parameters.AddWithValue("@now", now);
                seedCmd.Parameters.AddWithValue("@sector", initialRun.Sector);
                seedCmd.Parameters.AddWithValue("@opponentIndex", initialRun.OpponentIndex);
                seedCmd.Parameters.AddWithValue("@opponentHp", initialRun.CurrentOpponent.CurrentHp);
                seedCmd.Parameters.AddWithValue("@playerHp", initialRun.PlayerCurrentHp);
                await seedCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task ValidateExistingDatabaseAsync(SqliteConnection connection, HashSet<string> existingTables, CancellationToken cancellationToken)
    {
        if (!existingTables.Contains("gameplay_schema_info"))
        {
            throw new InvalidOperationException(
                "Existing database is missing required 'gameplay_schema_info' metadata table.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var metaCmd = connection.CreateCommand())
        {
            metaCmd.CommandText = "SELECT key, value FROM gameplay_schema_info;";
            using var reader = await metaCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                metadata[reader.GetString(0)] = reader.GetString(1);
            }
        }

        if (!metadata.TryGetValue("schema_version", out var versionStr)
            || !int.TryParse(versionStr, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException(
                "Existing database has malformed or missing 'schema_version' in 'gameplay_schema_info'.");
        }

        if (version > DefaultSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported database schema version {version}. Maximum supported version is {DefaultSchemaVersion}.");
        }

        if (version < DefaultSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Invalid database schema version {version}. Supported version is {DefaultSchemaVersion}.");
        }

        var requiredTables = new[]
        {
            "gameplay_schema_info",
            "gameplay_progression",
            "gameplay_run_state",
            "gameplay_receipt_ledger",
            "gameplay_pending_intent",
            "gameplay_reset_intent"
        };

        foreach (var table in requiredTables)
        {
            if (!existingTables.Contains(table))
            {
                throw new InvalidOperationException(
                    $"Existing database is incomplete: missing table '{table}'.");
            }
        }

        using (var runCmd = connection.CreateCommand())
        {
            runCmd.CommandText = "SELECT sector, opponent_index, opponent_current_hp, player_current_hp FROM gameplay_run_state WHERE id = 1;";
            using var reader = await runCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing active run state row in 'gameplay_run_state'.");
            }

            int sector = reader.GetInt32(0);
            int opponentIndex = reader.GetInt32(1);
            int opponentHp = reader.GetInt32(2);
            int playerHp = reader.GetInt32(3);

            try
            {
                CyberDefenseRunState.Rehydrate(sector, opponentIndex, opponentHp, playerHp);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid domain run state in database: {ex.Message}", ex);
            }
        }

        using (var progCmd = connection.CreateCommand())
        {
            progCmd.CommandText = "SELECT store_revision, reset_epoch FROM gameplay_progression WHERE id = 1;";
            using var reader = await progCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing row in 'gameplay_progression'.");
            }

            long storeRevision = reader.GetInt64(0);
            long resetEpoch = reader.GetInt64(1);
            if (storeRevision < 1 || resetEpoch < 0)
            {
                throw new InvalidOperationException("Existing database has invalid progression revision or epoch.");
            }
        }

        using (var resetCmd = connection.CreateCommand())
        {
            resetCmd.CommandText = "SELECT is_pending, current_epoch, target_epoch FROM gameplay_reset_intent WHERE id = 1;";
            using var reader = await resetCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing row in 'gameplay_reset_intent'.");
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_isInitialized && _connection != null)
            {
                return;
            }
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT sector, opponent_index, opponent_current_hp, player_current_hp FROM gameplay_run_state WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int sector = reader.GetInt32(0);
            int opponentIndex = reader.GetInt32(1);
            int opponentHp = reader.GetInt32(2);
            int playerHp = reader.GetInt32(3);

            return CyberDefenseRunState.Rehydrate(sector, opponentIndex, opponentHp, playerHp);
        }

        throw new InvalidOperationException("No active run state row found in 'gameplay_run_state'.");
    }

    public async Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT reset_epoch FROM gameplay_progression WHERE id = 1;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not null and not DBNull)
        {
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException("No progression row found in 'gameplay_progression'.");
    }

    public async Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT store_revision FROM gameplay_progression WHERE id = 1;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not null and not DBNull)
        {
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException("No progression row found in 'gameplay_progression'.");
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_connection != null)
            {
                _connection.Close();
                _connection.Dispose();
                SqliteConnection.ClearPool(_connection);
                _connection = null;
            }
            _isInitialized = false;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_connection != null)
            {
                _connection.Dispose();
                SqliteConnection.ClearPool(_connection);
                _connection = null;
            }
            _isInitialized = false;
        }
    }
}
