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

    /// <summary>
    /// Retrieves a stored receipt by its unique submission ID.
    /// Returns null if no receipt exists with the specified submission ID.
    /// </summary>
    public async Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                   response_latency_ms, reset_epoch, processed_at,
                   requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                   incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                   potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                   is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                   next_player_current_hp, terminal_sector, terminal_opponent_index,
                   terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                   terminal_player_current_hp
            FROM gameplay_receipt_ledger
            WHERE submission_id = @submissionId;
        ";
        cmd.Parameters.AddWithValue("@submissionId", submissionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadReceiptRecord(reader);
    }

    /// <summary>
    /// Narrowly scoped persistence operation for recording a validated receipt.
    /// Note: The atomic consumer in Slice 3 will perform multi-table atomic commits
    /// combining run state, receipt ledger, and progression revision.
    /// </summary>
    public async Task RecordReceiptDirectAsync(CyberDefenseReceiptRecord receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = _connection!.BeginTransaction();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            InsertReceiptRecord(cmd, receipt);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Binds an INSERT statement for the specified receipt to the provided command.
    /// Used by multi-statement atomic transactions.
    /// </summary>
    public static void InsertReceiptRecord(SqliteCommand cmd, CyberDefenseReceiptRecord receipt)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(receipt);

        cmd.CommandText = @"
            INSERT INTO gameplay_receipt_ledger (
                submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                response_latency_ms, reset_epoch, processed_at,
                requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                next_player_current_hp, terminal_sector, terminal_opponent_index,
                terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                terminal_player_current_hp
            ) VALUES (
                @submission_id, @receipt_kind, @fact_id, @is_correct, @is_eligible,
                @response_latency_ms, @reset_epoch, @processed_at,
                @requested_attack_damage, @applied_opponent_damage, @excess_opponent_damage,
                @incoming_enemy_damage, @applied_player_damage, @excess_enemy_damage,
                @potential_healing, @applied_healing, @is_opponent_defeated, @is_sector_completed,
                @is_game_over, @next_sector, @next_opponent_index, @next_opponent_current_hp,
                @next_player_current_hp, @terminal_sector, @terminal_opponent_index,
                @terminal_opponent_kind, @terminal_opponent_current_hp, @terminal_opponent_max_hp,
                @terminal_player_current_hp
            );
        ";

        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@submission_id", receipt.SubmissionId);
        cmd.Parameters.AddWithValue("@receipt_kind", receipt.ReceiptKind.ToString());
        cmd.Parameters.AddWithValue("@fact_id", receipt.FactId);
        cmd.Parameters.AddWithValue("@is_correct", receipt.IsCorrect ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_eligible", receipt.IsEligible ? 1 : 0);
        cmd.Parameters.AddWithValue("@response_latency_ms", receipt.ResponseLatencyMs);
        cmd.Parameters.AddWithValue("@reset_epoch", receipt.ResetEpoch);
        cmd.Parameters.AddWithValue("@processed_at", receipt.ProcessedAt.ToString("O"));

        if (receipt.ReceiptKind == CyberDefenseReceiptKind.Applied)
        {
            var t = receipt.TransitionResult ?? throw new InvalidOperationException("Applied receipt requires non-null transition result.");
            cmd.Parameters.AddWithValue("@requested_attack_damage", t.RequestedAttackDamage);
            cmd.Parameters.AddWithValue("@applied_opponent_damage", t.AppliedOpponentDamage);
            cmd.Parameters.AddWithValue("@excess_opponent_damage", t.ExcessOpponentDamage);
            cmd.Parameters.AddWithValue("@incoming_enemy_damage", t.IncomingEnemyDamage);
            cmd.Parameters.AddWithValue("@applied_player_damage", t.AppliedPlayerDamage);
            cmd.Parameters.AddWithValue("@excess_enemy_damage", t.ExcessEnemyDamage);
            cmd.Parameters.AddWithValue("@potential_healing", t.PotentialHealing);
            cmd.Parameters.AddWithValue("@applied_healing", t.AppliedHealing);
            cmd.Parameters.AddWithValue("@is_opponent_defeated", t.IsOpponentDefeated ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_sector_completed", t.IsSectorCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_game_over", t.IsGameOver ? 1 : 0);
            cmd.Parameters.AddWithValue("@next_sector", t.NextState.Sector);
            cmd.Parameters.AddWithValue("@next_opponent_index", t.NextState.OpponentIndex);
            cmd.Parameters.AddWithValue("@next_opponent_current_hp", t.NextState.CurrentOpponent.CurrentHp);
            cmd.Parameters.AddWithValue("@next_player_current_hp", t.NextState.PlayerCurrentHp);

            if (t.IsGameOver && t.TerminalSnapshot != null)
            {
                cmd.Parameters.AddWithValue("@terminal_sector", t.TerminalSnapshot.Sector);
                cmd.Parameters.AddWithValue("@terminal_opponent_index", t.TerminalSnapshot.OpponentIndex);
                cmd.Parameters.AddWithValue("@terminal_opponent_kind", t.TerminalSnapshot.Kind.ToString());
                cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", t.TerminalSnapshot.OpponentCurrentHp);
                cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", t.TerminalSnapshot.OpponentMaxHp);
                cmd.Parameters.AddWithValue("@terminal_player_current_hp", t.TerminalSnapshot.PlayerCurrentHp);
            }
            else
            {
                cmd.Parameters.AddWithValue("@terminal_sector", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_index", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_kind", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_player_current_hp", DBNull.Value);
            }
        }
        else
        {
            cmd.Parameters.AddWithValue("@requested_attack_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_opponent_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@excess_opponent_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@incoming_enemy_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_player_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@excess_enemy_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@potential_healing", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_healing", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_opponent_defeated", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_sector_completed", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_game_over", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_sector", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_index", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_player_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_sector", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_index", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_kind", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_player_current_hp", DBNull.Value);
        }
    }

    /// <summary>
    /// Rehydrates an immutable receipt record from a reader query on gameplay_receipt_ledger.
    /// </summary>
    public static CyberDefenseReceiptRecord ReadReceiptRecord(SqliteDataReader reader)
    {
        string subId = reader.GetString(0);
        string kindStr = reader.GetString(1);
        string factId = reader.GetString(2);
        bool isCorrect = reader.GetInt32(3) == 1;
        bool isEligible = reader.GetInt32(4) == 1;
        long responseLatencyMs = reader.GetInt64(5);
        long resetEpoch = reader.GetInt64(6);
        string processedAtStr = reader.GetString(7);

        if (!DateTimeOffset.TryParse(processedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var processedAt))
        {
            throw new InvalidOperationException($"Invalid processed_at timestamp '{processedAtStr}' in receipt ledger.");
        }

        if (!Enum.TryParse<CyberDefenseReceiptKind>(kindStr, ignoreCase: false, out var kind))
        {
            throw new InvalidOperationException($"Invalid receipt_kind '{kindStr}' in receipt ledger.");
        }

        if (kind == CyberDefenseReceiptKind.CalmModeSuppressed)
        {
            for (int i = 8; i <= 28; i++)
            {
                if (!reader.IsDBNull(i))
                {
                    throw new InvalidOperationException($"CalmModeSuppressed receipt row contains non-null combat transition column at index {i}.");
                }
            }

            return CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
                subId, factId, isCorrect, isEligible, responseLatencyMs, resetEpoch, processedAt);
        }

        if (kind == CyberDefenseReceiptKind.Applied)
        {
            for (int i = 8; i <= 22; i++)
            {
                if (reader.IsDBNull(i))
                {
                    throw new InvalidOperationException($"Applied receipt row contains null combat transition column at index {i}.");
                }
            }

            int requestedAttackDamage = reader.GetInt32(8);
            int appliedOpponentDamage = reader.GetInt32(9);
            int excessOpponentDamage = reader.GetInt32(10);
            int incomingEnemyDamage = reader.GetInt32(11);
            int appliedPlayerDamage = reader.GetInt32(12);
            int excessEnemyDamage = reader.GetInt32(13);
            int potentialHealing = reader.GetInt32(14);
            int appliedHealing = reader.GetInt32(15);
            bool isOpponentDefeated = reader.GetInt32(16) == 1;
            bool isSectorCompleted = reader.GetInt32(17) == 1;
            bool isGameOver = reader.GetInt32(18) == 1;
            int nextSector = reader.GetInt32(19);
            int nextOpponentIndex = reader.GetInt32(20);
            int nextOpponentCurrentHp = reader.GetInt32(21);
            int nextPlayerCurrentHp = reader.GetInt32(22);

            CyberDefenseRunState nextState;
            try
            {
                nextState = CyberDefenseRunState.Rehydrate(nextSector, nextOpponentIndex, nextOpponentCurrentHp, nextPlayerCurrentHp);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid domain next state in receipt ledger: {ex.Message}", ex);
            }

            CyberDefenseTerminalRunSnapshot? terminalSnapshot = null;
            if (isGameOver)
            {
                for (int i = 23; i <= 28; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Game-over receipt row contains null terminal column at index {i}.");
                    }
                }

                int termSector = reader.GetInt32(23);
                int termOppIndex = reader.GetInt32(24);
                string termKindStr = reader.GetString(25);
                int termOppCurrentHp = reader.GetInt32(26);
                int termOppMaxHp = reader.GetInt32(27);
                int termPlayerHp = reader.GetInt32(28);

                if (!Enum.TryParse<OpponentKind>(termKindStr, ignoreCase: false, out var termKind))
                {
                    throw new InvalidOperationException($"Invalid terminal_opponent_kind '{termKindStr}' in receipt ledger.");
                }

                try
                {
                    terminalSnapshot = new CyberDefenseTerminalRunSnapshot(
                        termSector, termOppIndex, termKind, termOppCurrentHp, termOppMaxHp, termPlayerHp);
                }
                catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                {
                    throw new InvalidOperationException($"Invalid terminal snapshot in receipt ledger: {ex.Message}", ex);
                }
            }
            else
            {
                for (int i = 23; i <= 28; i++)
                {
                    if (!reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Non-game-over receipt row contains non-null terminal column at index {i}.");
                    }
                }
            }

            CyberDefenseCombatTransitionResult transitionResult;
            try
            {
                transitionResult = new CyberDefenseCombatTransitionResult(
                    isCorrect,
                    requestedAttackDamage,
                    appliedOpponentDamage,
                    excessOpponentDamage,
                    incomingEnemyDamage,
                    appliedPlayerDamage,
                    excessEnemyDamage,
                    potentialHealing,
                    appliedHealing,
                    isOpponentDefeated,
                    isSectorCompleted,
                    isGameOver,
                    nextState,
                    terminalSnapshot);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid combat transition result in receipt ledger: {ex.Message}", ex);
            }

            return CyberDefenseReceiptRecord.CreateApplied(
                subId, factId, isCorrect, isEligible, responseLatencyMs, resetEpoch, processedAt, transitionResult);
        }

        throw new InvalidOperationException($"Unsupported receipt kind '{kind}'.");
    }

    public async Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = _connection!.BeginTransaction();
        try
        {
            using (var guardCmd = _connection.CreateCommand())
            {
                guardCmd.Transaction = transaction;
                guardCmd.CommandText = @"
                    SELECT r.is_pending, p.reset_epoch
                    FROM gameplay_reset_intent r
                    JOIN gameplay_progression p ON p.id = 1
                    WHERE r.id = 1;
                ";
                using var guardReader = await guardCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await guardReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing gameplay_reset_intent or gameplay_progression row.");
                }
                bool isResetPending = guardReader.GetInt32(0) == 1;
                long currentEpoch = guardReader.GetInt64(1);
                guardReader.Close();

                if (isResetPending)
                {
                    throw new InvalidOperationException("Cannot stage pending intent while a reset is pending.");
                }

                if (intent.ResetEpoch != currentEpoch)
                {
                    throw new InvalidOperationException(
                        $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
                }
            }

            using (var selectCmd = _connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = @"
                    SELECT fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch
                    FROM gameplay_pending_intent
                    WHERE submission_id = @id;
                ";
                selectCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string factId = reader.GetString(0);
                    bool isCorrect = reader.GetInt32(1) == 1;
                    bool isEligible = reader.GetInt32(2) == 1;
                    long latency = reader.GetInt64(3);
                    long epoch = reader.GetInt64(4);

                    if (!string.Equals(factId, intent.FactId, StringComparison.Ordinal) ||
                        isCorrect != intent.IsCorrect ||
                        isEligible != intent.IsEligible ||
                        latency != intent.ResponseLatencyMs ||
                        epoch != intent.ResetEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Conflicting pending intent already staged for submission '{intent.SubmissionId}'.");
                    }

                    transaction.Commit();
                    return;
                }
            }


            using (var insertCmd = _connection.CreateCommand())
            {
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO gameplay_pending_intent (
                        submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at
                    ) VALUES (
                        @submission_id, @fact_id, @is_correct, @is_eligible, @response_latency_ms, @reset_epoch, @created_at
                    );
                ";
                insertCmd.Parameters.AddWithValue("@submission_id", intent.SubmissionId);
                insertCmd.Parameters.AddWithValue("@fact_id", intent.FactId);
                insertCmd.Parameters.AddWithValue("@is_correct", intent.IsCorrect ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@is_eligible", intent.IsEligible ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@response_latency_ms", intent.ResponseLatencyMs);
                insertCmd.Parameters.AddWithValue("@reset_epoch", intent.ResetEpoch);
                insertCmd.Parameters.AddWithValue("@created_at", intent.CreatedAt.ToString("O"));
                await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at
            FROM gameplay_pending_intent
            WHERE submission_id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", submissionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadPendingIntent(reader);
    }

    public async Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at
            FROM gameplay_pending_intent
            WHERE reset_epoch = @epoch
            ORDER BY created_at ASC, submission_id ASC;
        ";
        cmd.Parameters.AddWithValue("@epoch", resetEpoch);

        var list = new List<CyberDefensePendingIntentRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadPendingIntent(reader));
        }

        return list;
    }

    public async Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
        cmd.Parameters.AddWithValue("@id", submissionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = _connection!.BeginTransaction();
        try
        {
            // 0. Check reset intent
            using (var guardCmd = _connection.CreateCommand())
            {
                guardCmd.Transaction = transaction;
                guardCmd.CommandText = "SELECT is_pending FROM gameplay_reset_intent WHERE id = 1;";
                using var guardReader = await guardCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await guardReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing gameplay_reset_intent row.");
                }
                bool isResetPending = guardReader.GetInt32(0) == 1;
                guardReader.Close();

                if (isResetPending)
                {
                    throw new InvalidOperationException("Cannot apply attempt transaction while a reset is pending.");
                }
            }

            // 1. Check existing receipt in ledger
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.Transaction = transaction;

                checkCmd.CommandText = @"
                    SELECT submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                           response_latency_ms, reset_epoch, processed_at,
                           requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                           incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                           potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                           is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                           next_player_current_hp, terminal_sector, terminal_opponent_index,
                           terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                           terminal_player_current_hp
                    FROM gameplay_receipt_ledger
                    WHERE submission_id = @id;
                ";
                checkCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                using var receiptReader = await checkCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await receiptReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var existingReceipt = ReadReceiptRecord(receiptReader);
                    receiptReader.Close();

                    if (!string.Equals(existingReceipt.FactId, intent.FactId, StringComparison.Ordinal) ||
                        existingReceipt.IsCorrect != intent.IsCorrect ||
                        existingReceipt.IsEligible != intent.IsEligible ||
                        existingReceipt.ResponseLatencyMs != intent.ResponseLatencyMs ||
                        existingReceipt.ResetEpoch != intent.ResetEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Conflicting receipt already exists for submission '{intent.SubmissionId}'.");
                    }

                    using (var cleanPendingCmd = _connection.CreateCommand())
                    {
                        cleanPendingCmd.Transaction = transaction;
                        cleanPendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                        cleanPendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                        await cleanPendingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    transaction.Commit();
                    return existingReceipt;
                }
            }

            // 2. Validate progression reset epoch and store revision
            long currentEpoch;
            long currentRevision;
            using (var progCmd = _connection.CreateCommand())
            {
                progCmd.Transaction = transaction;
                progCmd.CommandText = "SELECT reset_epoch, store_revision FROM gameplay_progression WHERE id = 1;";
                using var progReader = await progCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await progReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing progression row in 'gameplay_progression'.");
                }
                currentEpoch = progReader.GetInt64(0);
                currentRevision = progReader.GetInt64(1);
            }

            if (intent.ResetEpoch != currentEpoch)
            {
                throw new InvalidOperationException(
                    $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
            }

            var now = DateTimeOffset.UtcNow;
            CyberDefenseReceiptRecord receipt;

            if (!intent.IsEligible)
            {
                receipt = CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
                    intent.SubmissionId,
                    intent.FactId,
                    intent.IsCorrect,
                    intent.IsEligible,
                    intent.ResponseLatencyMs,
                    intent.ResetEpoch,
                    now);

                using (var insertReceiptCmd = _connection.CreateCommand())
                {
                    insertReceiptCmd.Transaction = transaction;
                    InsertReceiptRecord(insertReceiptCmd, receipt);
                    await insertReceiptCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                using (var deletePendingCmd = _connection.CreateCommand())
                {
                    deletePendingCmd.Transaction = transaction;
                    deletePendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                    deletePendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                    await deletePendingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                transaction.Commit();
                return receipt;
            }

            // 3. Applied Combat Attempt
            CyberDefenseRunState currentRunState;
            using (var runCmd = _connection.CreateCommand())
            {
                runCmd.Transaction = transaction;
                runCmd.CommandText = "SELECT sector, opponent_index, opponent_current_hp, player_current_hp FROM gameplay_run_state WHERE id = 1;";
                using var runReader = await runCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await runReader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing active run state row in 'gameplay_run_state'.");
                }
                int sector = runReader.GetInt32(0);
                int oppIndex = runReader.GetInt32(1);
                int oppHp = runReader.GetInt32(2);
                int playerHp = runReader.GetInt32(3);
                currentRunState = CyberDefenseRunState.Rehydrate(sector, oppIndex, oppHp, playerHp);
            }

            var transition = CyberDefenseStateMachine.ApplyAttempt(currentRunState, intent.IsCorrect, effectiveAttackDamage: 1);

            using (var updateRunCmd = _connection.CreateCommand())
            {
                updateRunCmd.Transaction = transaction;
                updateRunCmd.CommandText = @"
                    UPDATE gameplay_run_state
                    SET sector = @sector,
                        opponent_index = @opponentIndex,
                        opponent_current_hp = @opponentHp,
                        player_current_hp = @playerHp,
                        updated_at = @now
                    WHERE id = 1;
                ";
                updateRunCmd.Parameters.AddWithValue("@sector", transition.NextState.Sector);
                updateRunCmd.Parameters.AddWithValue("@opponentIndex", transition.NextState.OpponentIndex);
                updateRunCmd.Parameters.AddWithValue("@opponentHp", transition.NextState.CurrentOpponent.CurrentHp);
                updateRunCmd.Parameters.AddWithValue("@playerHp", transition.NextState.PlayerCurrentHp);
                updateRunCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                await updateRunCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var newRevision = currentRevision + 1;
            using (var updateProgCmd = _connection.CreateCommand())
            {
                updateProgCmd.Transaction = transaction;
                updateProgCmd.CommandText = @"
                    UPDATE gameplay_progression
                    SET store_revision = @rev,
                        updated_at = @now
                    WHERE id = 1;
                ";
                updateProgCmd.Parameters.AddWithValue("@rev", newRevision);
                updateProgCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                await updateProgCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            receipt = CyberDefenseReceiptRecord.CreateApplied(
                intent.SubmissionId,
                intent.FactId,
                intent.IsCorrect,
                intent.IsEligible,
                intent.ResponseLatencyMs,
                intent.ResetEpoch,
                now,
                transition);

            using (var insertReceiptCmd = _connection.CreateCommand())
            {
                insertReceiptCmd.Transaction = transaction;
                InsertReceiptRecord(insertReceiptCmd, receipt);
                await insertReceiptCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using (var deletePendingCmd = _connection.CreateCommand())
            {
                deletePendingCmd.Transaction = transaction;
                deletePendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                deletePendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                await deletePendingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            return receipt;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public static CyberDefensePendingIntentRecord ReadPendingIntent(SqliteDataReader reader)
    {
        string subId = reader.GetString(0);
        string factId = reader.GetString(1);
        bool isCorrect = reader.GetInt32(2) == 1;
        bool isEligible = reader.GetInt32(3) == 1;
        long latency = reader.GetInt64(4);
        long epoch = reader.GetInt64(5);
        string createdAtStr = reader.GetString(6);

        if (!DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            throw new InvalidOperationException($"Invalid created_at timestamp '{createdAtStr}' in pending intent table.");
        }

        return new CyberDefensePendingIntentRecord(
            subId, factId, isCorrect, isEligible, latency, epoch, createdAt);
    }

    public async Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT is_pending, current_epoch, target_epoch, created_at, updated_at FROM gameplay_reset_intent WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadResetIntent(reader);
        }

        throw new InvalidOperationException("No reset intent row found in 'gameplay_reset_intent'.");
    }

    public async Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using (var pragmaCmd = _connection!.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA synchronous = FULL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            long currentEpoch;
            long targetEpoch;
            bool isPending;
            DateTimeOffset? createdAt = null;

            using (var selectCmd = _connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT is_pending, current_epoch, target_epoch, created_at, updated_at FROM gameplay_reset_intent WHERE id = 1;";
                using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing reset intent row in 'gameplay_reset_intent'.");
                }
                isPending = reader.GetInt32(0) == 1;
                currentEpoch = reader.GetInt64(1);
                targetEpoch = reader.GetInt64(2);
                var createdStr = reader.IsDBNull(3) ? null : reader.GetString(3);
                if (!string.IsNullOrEmpty(createdStr) && DateTimeOffset.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreated))
                {
                    createdAt = parsedCreated;
                }
            }

            if (isPending)
            {
                transaction.Commit();
                using (var restoreCmd = _connection.CreateCommand())
                {
                    restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                    await restoreCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                return new GameplayResetIntentRecord(true, currentEpoch, targetEpoch, createdAt, DateTimeOffset.UtcNow);
            }

            var now = DateTimeOffset.UtcNow;
            targetEpoch = currentEpoch + 1;

            using (var updateCmd = _connection.CreateCommand())
            {
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = @"
                    UPDATE gameplay_reset_intent
                    SET is_pending = 1,
                        target_epoch = @targetEpoch,
                        created_at = @now,
                        updated_at = @now
                    WHERE id = 1;
                ";
                updateCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                updateCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();

            using (var restoreCmd = _connection.CreateCommand())
            {
                restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                await restoreCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return new GameplayResetIntentRecord(true, currentEpoch, targetEpoch, now, now);
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            try
            {
                using var restoreCmd = _connection.CreateCommand();
                restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                restoreCmd.ExecuteNonQuery();
            }
            catch { }
            throw;
        }
    }

    public async Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEpoch);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = _connection!.BeginTransaction();
        try
        {
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = "SELECT is_pending, target_epoch FROM gameplay_reset_intent WHERE id = 1;";
                using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing reset intent row in 'gameplay_reset_intent'.");
                }
                bool isPending = reader.GetInt32(0) == 1;
                long storedTargetEpoch = reader.GetInt64(1);
                if (!isPending || storedTargetEpoch != targetEpoch)
                {
                    throw new InvalidOperationException(
                        $"Cannot reset gameplay state to epoch {targetEpoch}. Active pending reset intent target is {storedTargetEpoch} (is_pending={isPending}).");
                }
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            var initialRun = CyberDefenseRunState.InitialRun();

            using (var resetCmd = _connection.CreateCommand())
            {
                resetCmd.Transaction = transaction;
                resetCmd.CommandText = @"
                    DELETE FROM gameplay_pending_intent;
                    DELETE FROM gameplay_receipt_ledger;

                    UPDATE gameplay_run_state
                    SET sector = @sector,
                        opponent_index = @opponentIndex,
                        opponent_current_hp = @opponentHp,
                        player_current_hp = @playerHp,
                        reset_epoch = @targetEpoch,
                        updated_at = @now
                    WHERE id = 1;

                    UPDATE gameplay_progression
                    SET store_revision = 1,
                        reset_epoch = @targetEpoch,
                        updated_at = @now
                    WHERE id = 1;

                    UPDATE gameplay_schema_info
                    SET value = '1'
                    WHERE key = 'store_revision';

                    UPDATE gameplay_schema_info
                    SET value = @targetEpochStr
                    WHERE key = 'reset_epoch';
                ";
                resetCmd.Parameters.AddWithValue("@sector", initialRun.Sector);
                resetCmd.Parameters.AddWithValue("@opponentIndex", initialRun.OpponentIndex);
                resetCmd.Parameters.AddWithValue("@opponentHp", initialRun.CurrentOpponent.CurrentHp);
                resetCmd.Parameters.AddWithValue("@playerHp", initialRun.PlayerCurrentHp);
                resetCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                resetCmd.Parameters.AddWithValue("@targetEpochStr", targetEpoch.ToString(CultureInfo.InvariantCulture));
                resetCmd.Parameters.AddWithValue("@now", now);
                await resetCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public async Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEpoch);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using (var pragmaCmd = _connection!.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA synchronous = FULL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            using (var checkCmd = _connection.CreateCommand())
            {
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = @"
                    SELECT r.is_pending, r.target_epoch, p.reset_epoch, rs.reset_epoch
                    FROM gameplay_reset_intent r
                    JOIN gameplay_progression p ON p.id = 1
                    JOIN gameplay_run_state rs ON rs.id = 1
                    WHERE r.id = 1;
                ";
                using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing reset intent or progression row.");
                }
                bool isPending = reader.GetInt32(0) == 1;
                long storedTargetEpoch = reader.GetInt64(1);
                long progressionEpoch = reader.GetInt64(2);
                long runStateEpoch = reader.GetInt64(3);

                if (!isPending || storedTargetEpoch != targetEpoch)
                {
                    throw new InvalidOperationException(
                        $"Cannot clear reset intent for epoch {targetEpoch}. Active pending target is {storedTargetEpoch} (is_pending={isPending}).");
                }

                if (progressionEpoch != targetEpoch || runStateEpoch != targetEpoch)
                {
                    throw new InvalidOperationException(
                        $"Cannot clear reset intent before gameplay state has been reset to target epoch {targetEpoch} (progression={progressionEpoch}, runState={runStateEpoch}).");
                }
            }

            var now = DateTimeOffset.UtcNow.ToString("O");

            using (var updateCmd = _connection.CreateCommand())
            {
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = @"
                    UPDATE gameplay_reset_intent
                    SET is_pending = 0,
                        current_epoch = @targetEpoch,
                        target_epoch = @targetEpoch,
                        created_at = NULL,
                        updated_at = @now
                    WHERE id = 1;
                ";
                updateCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                updateCmd.Parameters.AddWithValue("@now", now);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();

            using (var restoreCmd = _connection.CreateCommand())
            {
                restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                await restoreCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            try
            {
                using var restoreCmd = _connection.CreateCommand();
                restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                restoreCmd.ExecuteNonQuery();
            }
            catch { }
            throw;
        }
    }

    public static GameplayResetIntentRecord ReadResetIntent(SqliteDataReader reader)
    {
        bool isPending = reader.GetInt32(0) == 1;
        long currentEpoch = reader.GetInt64(1);
        long targetEpoch = reader.GetInt64(2);
        string? createdAtStr = reader.IsDBNull(3) ? null : reader.GetString(3);
        string updatedAtStr = reader.GetString(4);

        DateTimeOffset? createdAt = null;
        if (!string.IsNullOrEmpty(createdAtStr))
        {
            if (!DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreated))
            {
                throw new InvalidOperationException($"Invalid created_at timestamp '{createdAtStr}' in reset intent table.");
            }
            createdAt = parsedCreated;
        }

        if (!DateTimeOffset.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUpdated))
        {
            throw new InvalidOperationException($"Invalid updated_at timestamp '{updatedAtStr}' in reset intent table.");
        }

        return new GameplayResetIntentRecord(isPending, currentEpoch, targetEpoch, createdAt, parsedUpdated);
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
