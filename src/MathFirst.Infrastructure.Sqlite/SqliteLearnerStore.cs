namespace MathFirst.Infrastructure.Sqlite;

using System.Data;
using System.Globalization;
using System.Text.Json;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Microsoft.Data.Sqlite;

public sealed class SqliteLearnerStore : ILearnerStore
{
    private readonly string _connectionString;
    private readonly string _storagePath;
    private SqliteConnection? _connection;
    private bool _isInitialized;
    private readonly object _lock = new();

    public SqliteLearnerStore(string storagePath)
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

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // An existing newer database is rejected before any DDL or PRAGMA can mutate it.
        using (var versionProbe = _connection.CreateCommand())
        {
            versionProbe.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'schema_info';";
            if (await versionProbe.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                var (existingVersion, _) = await ReadSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
                if (existingVersion > LearnerProgression.DefaultSchemaVersion)
                {
                    throw new InvalidOperationException($"Unsupported database schema version {existingVersion}. Maximum supported version is {LearnerProgression.DefaultSchemaVersion}.");
                }
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
            );

            CREATE TABLE IF NOT EXISTS item_learning_state (
                fact_id TEXT PRIMARY KEY,
                operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL,
                total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL,
                incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL,
                last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL,
                fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL,
                needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL,
                last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT
            );

            CREATE TABLE IF NOT EXISTS attempt_history (
                submission_id TEXT PRIMARY KEY,
                fact_id TEXT NOT NULL,
                operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL,
                submitted_answer INTEGER,
                correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL,
                outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0)
            );

            CREATE TABLE IF NOT EXISTS fsrs_card_state (
                fact_id TEXT PRIMARY KEY,
                card_id TEXT NOT NULL,
                state INTEGER NOT NULL,
                step INTEGER,
                stability REAL,
                difficulty REAL,
                due_practice_position INTEGER NOT NULL,
                last_review_practice_position INTEGER,
                last_rating INTEGER
            );
        ";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Check or initialize schema version and revision
        var (version, revision) = await ReadSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
        if (version == 0)
        {
            // Fresh DB
            using var initCmd = _connection.CreateCommand();
            initCmd.CommandText = $@"
                INSERT OR REPLACE INTO schema_info (key, value) VALUES ('schema_version', '{LearnerProgression.DefaultSchemaVersion}');
                INSERT OR REPLACE INTO schema_info (key, value) VALUES ('store_revision', '1');
            ";
            await initCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var defaultProgression = LearnerProgression.CreateFresh();
            await SaveProgressionAsync(defaultProgression, cancellationToken).ConfigureAwait(false);
            await CreateV5IndexesAsync(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 1)
        {
            await MigrateV1ToV2Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV2ToV3Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV3ToV4Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 2)
        {
            await MigrateV2ToV3Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV3ToV4Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 3)
        {
            await MigrateV3ToV4Async(_connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 4)
        {
            await MigrateV4ToV5Async(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == LearnerProgression.DefaultSchemaVersion)
        {
            await CreateV5IndexesAsync(_connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version > LearnerProgression.DefaultSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported database schema version {version}. Maximum supported version is {LearnerProgression.DefaultSchemaVersion}.");
        }

        lock (_lock)
        {
            _isInitialized = true;
        }
    }

    public async Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var (version, revision) = await ReadSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
        if (version > LearnerProgression.DefaultSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported database schema version {version}.");
        }

        var progression = await ReadProgressionAsync(cancellationToken).ConfigureAwait(false);
        progression.StoreRevision = revision;
        progression.SchemaVersion = version;

        var itemStates = await ReadAllItemStatesAsync(cancellationToken).ConfigureAwait(false);
        var fsrsStates = await ReadAllFsrsStatesAsync(cancellationToken).ConfigureAwait(false);
        var operationProgressions = await ReadOperationProgressionsAsync(cancellationToken).ConfigureAwait(false);
        progression.OperationProgressions = operationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value);
        var recentAttempts = await ReadBoundedRecentAttemptsAsync(cancellationToken).ConfigureAwait(false);
        var latestAcceptedPracticeAt = await ReadLatestAcceptedPracticeAtAsync(cancellationToken).ConfigureAwait(false);

        return new LearnerSnapshot(progression, itemStates, fsrsStates, recentAttempts, revision, version, operationProgressions, latestAcceptedPracticeAt);
    }

    public async Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var (version, revision) = await ReadSchemaInfoAsync(cancellationToken).ConfigureAwait(false);
        if (version > LearnerProgression.DefaultSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported database schema version {version}.");
        }

        var progression = await ReadProgressionAsync(cancellationToken).ConfigureAwait(false);
        progression.StoreRevision = revision;
        progression.SchemaVersion = version;
        var operationProgressions = await ReadOperationProgressionsAsync(cancellationToken).ConfigureAwait(false);
        progression.OperationProgressions = operationProgressions.ToDictionary(pair => pair.Key, pair => pair.Value);
        var recentAttempts = await ReadBoundedRecentAttemptsAsync(cancellationToken).ConfigureAwait(false);
        var latestAcceptedPracticeAt = await ReadLatestAcceptedPracticeAtAsync(cancellationToken).ConfigureAwait(false);

        return new LearnerSnapshot(
            progression,
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
            new Dictionary<string, FsrsCardState>(StringComparer.Ordinal),
            recentAttempts,
            revision,
            version,
            operationProgressions,
            latestAcceptedPracticeAt);
    }

    public async Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
        PracticeSelectionEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var currentBand = await ReadCurrentBandCandidatesAsync(request, cancellationToken).ConfigureAwait(false);
        var due = await ReadCandidatesAsync(@"
            WHERE item.operation = @operation
              AND card.due_practice_position <= @practice_position
            ORDER BY card.due_practice_position ASC, item.fact_id ASC
            LIMIT @limit;", request, cancellationToken).ConfigureAwait(false);
        var maintenance = await ReadCandidatesAsync(@"
            WHERE item.operation = @operation
              AND item.needs_remediation = 0
              AND (card.fact_id IS NULL OR card.due_practice_position > @practice_position)
            ORDER BY item.last_practiced_order ASC,
                     CASE WHEN card.fact_id IS NULL THEN 1 ELSE 0 END ASC,
                     card.due_practice_position ASC,
                     item.fact_id ASC
            LIMIT @limit;", request, cancellationToken).ConfigureAwait(false);
        var remediation = await ReadCandidatesAsync(@"
            WHERE item.operation = @operation
              AND item.needs_remediation = 1
              AND item.remediation_due_order <= @session_order
            ORDER BY item.remediation_due_order ASC, item.fact_id ASC
            LIMIT @limit;", request, cancellationToken).ConfigureAwait(false);
        var anyMaterialized = await ReadCandidatesAsync(@"
            WHERE item.operation = @operation
            ORDER BY item.fact_id ASC
            LIMIT @limit;", request, cancellationToken).ConfigureAwait(false);

        return new PracticeSelectionEvidence(currentBand, due, maintenance, remediation, anyMaterialized);
    }

    public async Task<PersistenceResult> CommitSubmissionAsync(
        SubmissionChangeSet changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_connection is null)
        {
            return PersistenceResult.Unavailable("Database connection is not open.");
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            var (version, revision) = await ReadSchemaInfoInTxAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (version > LearnerProgression.DefaultSchemaVersion)
            {
                transaction.Rollback();
                return PersistenceResult.UnsupportedVersion($"Unsupported schema version {version}.");
            }

            // A known submission ID is a committed replay even when its original revision and
            // position are stale relative to the current durable state.
            var alreadyCommitted = await IsSubmissionCommittedInTxAsync(transaction, changeSet.SubmissionId, cancellationToken).ConfigureAwait(false);
            if (alreadyCommitted)
            {
                transaction.Commit();
                return PersistenceResult.Success(revision);
            }

            if (revision != changeSet.ExpectedRevision)
            {
                transaction.Rollback();
                return PersistenceResult.Conflict($"Revision conflict: expected {changeSet.ExpectedRevision} but database is at revision {revision}.");
            }

            try
            {
                var storedPracticePosition = await ReadPracticePositionInTxAsync(transaction, cancellationToken).ConfigureAwait(false);
                var storedOperationProgressions = await ReadOperationProgressionsInTxAsync(transaction, cancellationToken).ConfigureAwait(false);
                ValidateNewAcceptedSubmission(changeSet, storedPracticePosition, storedOperationProgressions);
            }
            catch (InvalidOperationException ex)
            {
                transaction.Rollback();
                return PersistenceResult.InvalidSubmission(ex.Message);
            }
            catch (OverflowException ex)
            {
                transaction.Rollback();
                return PersistenceResult.InvalidSubmission(ex.Message);
            }

            // 1. Record Attempt
            using (var attCmd = _connection.CreateCommand())
            {
                attCmd.Transaction = transaction;
                attCmd.CommandText = @"
                    INSERT INTO attempt_history (
                        submission_id, fact_id, operation, left_operand, right_operand,
                        submitted_answer, correct_answer, is_correct, outcome, response_latency_ms, timestamp, practice_position
                    ) VALUES (
                        @submission_id, @fact_id, @operation, @left_operand, @right_operand,
                        @submitted_answer, @correct_answer, @is_correct, @outcome, @response_latency_ms, @timestamp, @practice_position
                    );
                ";
                attCmd.Parameters.AddWithValue("@submission_id", changeSet.Attempt.SubmissionId);
                attCmd.Parameters.AddWithValue("@fact_id", changeSet.Attempt.FactId);
                attCmd.Parameters.AddWithValue("@operation", changeSet.Attempt.Operation.ToString());
                attCmd.Parameters.AddWithValue("@left_operand", changeSet.Attempt.LeftOperand);
                attCmd.Parameters.AddWithValue("@right_operand", changeSet.Attempt.RightOperand);
                attCmd.Parameters.AddWithValue("@submitted_answer", (object?)changeSet.Attempt.SubmittedAnswer ?? DBNull.Value);
                attCmd.Parameters.AddWithValue("@correct_answer", changeSet.Attempt.CorrectAnswer);
                attCmd.Parameters.AddWithValue("@is_correct", changeSet.Attempt.IsCorrect ? 1 : 0);
                attCmd.Parameters.AddWithValue("@outcome", changeSet.Attempt.Outcome.ToString());
                attCmd.Parameters.AddWithValue("@response_latency_ms", changeSet.Attempt.ResponseLatencyMs);
                attCmd.Parameters.AddWithValue("@timestamp", changeSet.Attempt.Timestamp.ToString("O"));
                attCmd.Parameters.AddWithValue("@practice_position", (object?)changeSet.Attempt.PracticePosition ?? DBNull.Value);
                await attCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Upsert Item Learning State
            using (var itemCmd = _connection.CreateCommand())
            {
                itemCmd.Transaction = transaction;
                itemCmd.CommandText = @"
                    INSERT INTO item_learning_state (
                        fact_id, operation, left_operand, right_operand,
                        total_attempts, correct_attempts, incorrect_attempts,
                        consecutive_correct, last_latency_ms, rolling_latency_ms,
                        fluent_streak, is_mastered, needs_remediation,
                        remediation_due_order, last_practiced_order, last_practiced_at
                    ) VALUES (
                        @fact_id, @operation, @left_operand, @right_operand,
                        @total_attempts, @correct_attempts, @incorrect_attempts,
                        @consecutive_correct, @last_latency_ms, @rolling_latency_ms,
                        @fluent_streak, @is_mastered, @needs_remediation,
                        @remediation_due_order, @last_practiced_order, @last_practiced_at
                    )
                    ON CONFLICT(fact_id) DO UPDATE SET
                        total_attempts = excluded.total_attempts,
                        correct_attempts = excluded.correct_attempts,
                        incorrect_attempts = excluded.incorrect_attempts,
                        consecutive_correct = excluded.consecutive_correct,
                        last_latency_ms = excluded.last_latency_ms,
                        rolling_latency_ms = excluded.rolling_latency_ms,
                        fluent_streak = excluded.fluent_streak,
                        is_mastered = excluded.is_mastered,
                        needs_remediation = excluded.needs_remediation,
                        remediation_due_order = excluded.remediation_due_order,
                        last_practiced_order = excluded.last_practiced_order,
                        last_practiced_at = excluded.last_practiced_at;
                ";
                var st = changeSet.UpdatedItemState;
                itemCmd.Parameters.AddWithValue("@fact_id", st.FactId);
                itemCmd.Parameters.AddWithValue("@operation", st.Operation.ToString());
                itemCmd.Parameters.AddWithValue("@left_operand", st.LeftOperand);
                itemCmd.Parameters.AddWithValue("@right_operand", st.RightOperand);
                itemCmd.Parameters.AddWithValue("@total_attempts", st.TotalAttempts);
                itemCmd.Parameters.AddWithValue("@correct_attempts", st.CorrectAttempts);
                itemCmd.Parameters.AddWithValue("@incorrect_attempts", st.IncorrectAttempts);
                itemCmd.Parameters.AddWithValue("@consecutive_correct", st.ConsecutiveCorrectStreak);
                itemCmd.Parameters.AddWithValue("@last_latency_ms", st.LastLatencyMs);
                itemCmd.Parameters.AddWithValue("@rolling_latency_ms", st.RollingLatencyMs);
                itemCmd.Parameters.AddWithValue("@fluent_streak", st.FluentStreak);
                itemCmd.Parameters.AddWithValue("@is_mastered", st.IsProvisionallyMastered ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@needs_remediation", st.NeedsRemediation ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@remediation_due_order", st.RemediationDueOrder);
                itemCmd.Parameters.AddWithValue("@last_practiced_order", st.LastPracticedOrder);
                itemCmd.Parameters.AddWithValue("@last_practiced_at", st.LastPracticedAt?.ToString("O") ?? (object)DBNull.Value);
                await itemCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Upsert the V5 global and normalized per-operation progression.
            using (var progCmd = _connection.CreateCommand())
            {
                progCmd.Transaction = transaction;
                progCmd.CommandText = @"
                    INSERT INTO learner_progression (
                        id, practice_position, updated_at
                    ) VALUES (
                        1, @practice_position, @updated_at
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        practice_position = excluded.practice_position,
                        updated_at = excluded.updated_at;
                ";
                var p = changeSet.UpdatedProgression;
                progCmd.Parameters.AddWithValue("@practice_position", p.PracticePosition);
                progCmd.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
                await progCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var operationProgressions = ResolveOperationProgressions(changeSet);
            ValidateOperationProgressions(operationProgressions, changeSet.Attempt.Operation, changeSet.Attempt.PracticePosition);
            foreach (var operationProgression in operationProgressions.Values)
            {
                using var operationCmd = _connection.CreateCommand();
                operationCmd.Transaction = transaction;
                operationCmd.CommandText = @"
                    INSERT INTO operation_progression (operation, band_index, band_started_practice_position)
                    VALUES (@operation, @band_index, @band_started_practice_position)
                    ON CONFLICT(operation) DO UPDATE SET
                        band_index = excluded.band_index,
                        band_started_practice_position = excluded.band_started_practice_position;";
                operationCmd.Parameters.AddWithValue("@operation", operationProgression.Operation.ToString());
                operationCmd.Parameters.AddWithValue("@band_index", operationProgression.BandIndex);
                operationCmd.Parameters.AddWithValue("@band_started_practice_position", operationProgression.BandStartedPracticePosition);
                await operationCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 4. Upsert FSRS Card State (if present)
            if (changeSet.UpdatedFsrsState is not null)
            {
                using var fsrsCmd = _connection.CreateCommand();
                fsrsCmd.Transaction = transaction;
                fsrsCmd.CommandText = @"
                    INSERT INTO fsrs_card_state (
                        fact_id, card_id, state, step, stability, difficulty,
                        due_practice_position, last_review_practice_position, last_rating
                    ) VALUES (
                        @fact_id, @card_id, @state, @step, @stability, @difficulty,
                        @due_practice_position, @last_review_practice_position, @last_rating
                    )
                    ON CONFLICT(fact_id) DO UPDATE SET
                        card_id = excluded.card_id,
                        state = excluded.state,
                        step = excluded.step,
                        stability = excluded.stability,
                        difficulty = excluded.difficulty,
                        due_practice_position = excluded.due_practice_position,
                        last_review_practice_position = excluded.last_review_practice_position,
                        last_rating = excluded.last_rating;
                ";
                var fsrs = changeSet.UpdatedFsrsState;
                fsrsCmd.Parameters.AddWithValue("@fact_id", fsrs.FactId);
                fsrsCmd.Parameters.AddWithValue("@card_id", fsrs.CardId.ToString());
                fsrsCmd.Parameters.AddWithValue("@state", fsrs.State);
                fsrsCmd.Parameters.AddWithValue("@step", (object?)fsrs.Step ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@stability", (object?)fsrs.Stability ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@difficulty", (object?)fsrs.Difficulty ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@due_practice_position", fsrs.DuePracticePosition);
                fsrsCmd.Parameters.AddWithValue("@last_review_practice_position", (object?)fsrs.LastReviewPracticePosition ?? DBNull.Value);
                fsrsCmd.Parameters.AddWithValue("@last_rating", (object?)(int?)fsrs.LastRating ?? DBNull.Value);
                await fsrsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 5. Increment Revision
            var newRevision = revision + 1;
            using (var revCmd = _connection.CreateCommand())
            {
                revCmd.Transaction = transaction;
                revCmd.CommandText = "UPDATE schema_info SET value = @value WHERE key = 'store_revision';";
                revCmd.Parameters.AddWithValue("@value", newRevision.ToString());
                await revCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
            return PersistenceResult.Success(newRevision);
        }
        catch (Exception ex)
        {
            try
            {
                transaction.Rollback();
            }
            catch
            {
                // ignored
            }

            if (ex is SqliteException sqlEx && sqlEx.SqliteErrorCode == 11) // SQLITE_CORRUPT
            {
                return PersistenceResult.Corrupt("Database file is corrupted.");
            }

            throw;
        }
    }

    public async Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_connection is null)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                DELETE FROM attempt_history;
                DELETE FROM item_learning_state;
                DELETE FROM fsrs_card_state;
                DELETE FROM operation_progression;
                DELETE FROM learner_progression;
                UPDATE schema_info SET value = '1' WHERE key = 'store_revision';
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var fresh = LearnerProgression.CreateFresh();
            using var progCmd = _connection.CreateCommand();
            progCmd.Transaction = transaction;
            progCmd.CommandText = @"
                INSERT INTO learner_progression (id, practice_position, updated_at)
                VALUES (1, @practice_position, @updated_at);
            ";
            progCmd.Parameters.AddWithValue("@practice_position", fresh.PracticePosition);
            progCmd.Parameters.AddWithValue("@updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await progCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var operation in fresh.OperationProgressions.Values)
            {
                using var operationCmd = _connection.CreateCommand();
                operationCmd.Transaction = transaction;
                operationCmd.CommandText = "INSERT INTO operation_progression (operation, band_index, band_started_practice_position) VALUES (@operation, @band, @start);";
                operationCmd.Parameters.AddWithValue("@operation", operation.Operation.ToString());
                operationCmd.Parameters.AddWithValue("@band", operation.BandIndex);
                operationCmd.Parameters.AddWithValue("@start", operation.BandStartedPracticePosition);
                await operationCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_connection is not null)
            {
                SqliteConnection.ClearPool(_connection);
                _connection.Close();
                _connection.Dispose();
                _connection = null;
            }
            _isInitialized = false;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_isInitialized || _connection is null)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(int version, long revision)> ReadSchemaInfoAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return (0, 0);
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM schema_info;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var version = 0;
        long revision = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var val = reader.GetString(1);
            if (key == "schema_version" && int.TryParse(val, out var v))
            {
                version = v;
            }
            else if (key == "store_revision" && long.TryParse(val, out var r))
            {
                revision = r;
            }
        }

        return (version, revision);
    }

    private static async Task<(int version, long revision)> ReadSchemaInfoInTxAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var cmd = transaction.Connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT key, value FROM schema_info;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var version = 0;
        long revision = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var val = reader.GetString(1);
            if (key == "schema_version" && int.TryParse(val, out var v))
            {
                version = v;
            }
            else if (key == "store_revision" && long.TryParse(val, out var r))
            {
                revision = r;
            }
        }

        return (version, revision);
    }

    private static async Task<bool> IsSubmissionCommittedInTxAsync(
        SqliteTransaction transaction,
        string submissionId,
        CancellationToken cancellationToken)
    {
        var cmd = transaction.Connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT 1 FROM attempt_history WHERE submission_id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", submissionId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result != null;
    }

    private static async Task<long> ReadPracticePositionInTxAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT practice_position FROM learner_progression WHERE id = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static async Task<IReadOnlyDictionary<ArithmeticOperation, OperationProgression>> ReadOperationProgressionsInTxAsync(
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ArithmeticOperation, OperationProgression>();
        var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT operation, band_index, band_started_practice_position FROM operation_progression;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Enum.TryParse<ArithmeticOperation>(reader.GetString(0), out var operation))
            {
                throw new InvalidOperationException("Stored operation progression has an unknown operation.");
            }

            result.Add(operation, new OperationProgression(operation, reader.GetInt32(1), reader.GetInt64(2)));
        }

        return result;
    }

    private static void ValidateNewAcceptedSubmission(
        SubmissionChangeSet changeSet,
        long storedPracticePosition,
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> storedOperationProgressions)
    {
        var attemptPracticePosition = changeSet.Attempt.PracticePosition
            ?? throw new InvalidOperationException("New Schema V5 submissions require a practice position.");
        var requiredPracticePosition = checked(storedPracticePosition + 1);
        if (attemptPracticePosition != requiredPracticePosition)
        {
            throw new InvalidOperationException($"Expected next practice position {requiredPracticePosition}, but received {attemptPracticePosition}.");
        }

        if (changeSet.UpdatedProgression.PracticePosition != attemptPracticePosition)
        {
            throw new InvalidOperationException("Updated learner progression practice position must match the accepted attempt.");
        }

        var scheduledOperation = new[]
        {
            ArithmeticOperation.Addition,
            ArithmeticOperation.Subtraction,
            ArithmeticOperation.Multiplication,
            ArithmeticOperation.Division
        }[(int)((attemptPracticePosition - 1) % 4)];
        if (changeSet.Attempt.Operation != scheduledOperation)
        {
            throw new InvalidOperationException($"Practice position {attemptPracticePosition} requires {scheduledOperation}, but the attempt is {changeSet.Attempt.Operation}.");
        }

        var candidateOperationProgressions = ResolveOperationProgressions(changeSet);
        ValidateOperationProgressions(storedOperationProgressions, changeSet.Attempt.Operation, storedPracticePosition);
        ValidateOperationProgressions(candidateOperationProgressions, changeSet.Attempt.Operation, attemptPracticePosition);
        ValidateOperationProgressions(changeSet.UpdatedProgression.OperationProgressions, changeSet.Attempt.Operation, attemptPracticePosition);

        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            var stored = storedOperationProgressions[operation];
            var candidate = candidateOperationProgressions[operation];
            var progressionCandidate = changeSet.UpdatedProgression.OperationProgressions[operation];
            if (candidate != progressionCandidate)
            {
                throw new InvalidOperationException("Normalized operation progression must match the updated learner progression.");
            }

            if (candidate.BandIndex < stored.BandIndex || candidate.BandIndex > stored.BandIndex + 1)
            {
                throw new InvalidOperationException("Operation band index may advance by at most one and may not regress.");
            }

            if (operation != scheduledOperation)
            {
                if (candidate != stored)
                {
                    throw new InvalidOperationException("Only the scheduled operation may change progression.");
                }

                continue;
            }

            if (candidate.BandIndex == stored.BandIndex && candidate.BandStartedPracticePosition != stored.BandStartedPracticePosition)
            {
                throw new InvalidOperationException("A non-advancing scheduled operation must preserve its band start position.");
            }

            if (candidate.BandIndex == stored.BandIndex + 1 && candidate.BandStartedPracticePosition != attemptPracticePosition)
            {
                throw new InvalidOperationException("An advancing scheduled operation must start its new band at the accepted practice position.");
            }
        }
    }

    private static void ValidateOperationProgressions(
        IReadOnlyDictionary<ArithmeticOperation, OperationProgression> progressions,
        ArithmeticOperation attemptOperation,
        long? practicePosition)
    {
        ArgumentNullException.ThrowIfNull(progressions);
        var expected = Enum.GetValues<ArithmeticOperation>();
        if (progressions.Count != expected.Length || expected.Any(operation =>
                !progressions.TryGetValue(operation, out var progression) || progression.Operation != operation))
        {
            throw new InvalidOperationException("Schema V5 requires exactly one valid progression row for every arithmetic operation.");
        }

        if (practicePosition is > 0 && progressions[attemptOperation].BandStartedPracticePosition > practicePosition)
        {
            throw new InvalidOperationException("An operation band cannot start after the accepted practice position.");
        }
    }

    private static IReadOnlyDictionary<ArithmeticOperation, OperationProgression> ResolveOperationProgressions(SubmissionChangeSet changeSet)
    {
        return changeSet.OperationProgressions;
    }

    private static async Task CreateV5IndexesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE UNIQUE INDEX IF NOT EXISTS ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_item_learning_state_operation_fact
                ON item_learning_state(operation, fact_id);
            CREATE INDEX IF NOT EXISTS ix_item_learning_state_operation_remediation_order_fact
                ON item_learning_state(operation, needs_remediation, remediation_due_order, fact_id);
            CREATE INDEX IF NOT EXISTS ix_item_learning_state_operation_maintenance_fact
                ON item_learning_state(operation, needs_remediation, last_practiced_order, fact_id);
            CREATE INDEX IF NOT EXISTS ix_fsrs_card_state_due_fact
                ON fsrs_card_state(due_practice_position, fact_id);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<LearnerProgression> ReadProgressionAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return LearnerProgression.CreateFresh();
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT practice_position, updated_at FROM learner_progression WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var practicePosition = reader.GetInt64(0);
            var updatedStr = reader.GetString(1);
            var updated = DateTimeOffset.TryParse(updatedStr, out var dto) ? dto : DateTimeOffset.UtcNow;

            return new LearnerProgression
            {
                PracticePosition = practicePosition,
                UpdatedAt = updated
            };
        }

        return LearnerProgression.CreateFresh();
    }

    private async Task SaveProgressionAsync(LearnerProgression progression, CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO learner_progression (id, practice_position, updated_at)
            VALUES (1, @practice_position, @updated_at)
            ON CONFLICT(id) DO UPDATE SET practice_position = excluded.practice_position, updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("@practice_position", progression.PracticePosition);
        cmd.Parameters.AddWithValue("@updated_at", progression.UpdatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var operation in progression.OperationProgressions.Values)
        {
            using var operationCmd = _connection.CreateCommand();
            operationCmd.CommandText = "INSERT INTO operation_progression (operation, band_index, band_started_practice_position) VALUES (@operation, @band, @start);";
            operationCmd.Parameters.AddWithValue("@operation", operation.Operation.ToString());
            operationCmd.Parameters.AddWithValue("@band", operation.BandIndex);
            operationCmd.Parameters.AddWithValue("@start", operation.BandStartedPracticePosition);
            await operationCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<PracticeSelectionCandidate>> ReadCurrentBandCandidatesAsync(
        PracticeSelectionEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CurrentBandOwnedFrontier.Count == 0)
        {
            return [];
        }

        using var command = _connection!.CreateCommand();
        var parameters = request.CurrentBandOwnedFrontier
            .Select((_, index) => $"@fact_{index}")
            .ToArray();
        command.CommandText = $@"
            {CandidateSelectSql}
            WHERE item.operation = @operation
              AND item.fact_id IN ({string.Join(", ", parameters)})
            ORDER BY item.fact_id ASC;";
        command.Parameters.AddWithValue("@operation", request.Operation.ToString());
        for (var index = 0; index < request.CurrentBandOwnedFrontier.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], request.CurrentBandOwnedFrontier[index].Id);
        }

        return await ReadCandidateRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PracticeSelectionCandidate>> ReadCandidatesAsync(
        string predicateAndOrder,
        PracticeSelectionEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = CandidateSelectSql + predicateAndOrder;
        command.Parameters.AddWithValue("@operation", request.Operation.ToString());
        command.Parameters.AddWithValue("@practice_position", request.ProspectivePracticePosition);
        command.Parameters.AddWithValue("@session_order", request.CurrentSessionOrder);
        command.Parameters.AddWithValue("@limit", PracticeSelectionEvidenceRequest.CandidateWindowSize);
        return await ReadCandidateRowsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<PracticeSelectionCandidate>> ReadCandidateRowsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var candidates = new List<PracticeSelectionCandidate>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var operation = Enum.Parse<ArithmeticOperation>(reader.GetString(1));
            var fact = new ArithmeticFact(operation, reader.GetInt32(2), reader.GetInt32(3));
            var item = new ItemLearningState
            {
                FactId = reader.GetString(0),
                Operation = operation,
                LeftOperand = fact.LeftOperand,
                RightOperand = fact.RightOperand,
                TotalAttempts = reader.GetInt32(4),
                CorrectAttempts = reader.GetInt32(5),
                IncorrectAttempts = reader.GetInt32(6),
                ConsecutiveCorrectStreak = reader.GetInt32(7),
                LastLatencyMs = reader.GetInt64(8),
                RollingLatencyMs = reader.GetInt64(9),
                FluentStreak = reader.GetInt32(10),
                IsProvisionallyMastered = reader.GetInt32(11) == 1,
                NeedsRemediation = reader.GetInt32(12) == 1,
                RemediationDueOrder = reader.GetInt32(13),
                LastPracticedOrder = reader.GetInt32(14),
                LastPracticedAt = reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15))
            };
            FsrsCardState? card = null;
            if (!reader.IsDBNull(16))
            {
                card = new FsrsCardState(
                    reader.GetString(16),
                    Guid.Parse(reader.GetString(17)),
                    reader.GetInt32(18),
                    reader.IsDBNull(19) ? null : reader.GetInt32(19),
                    reader.IsDBNull(20) ? null : reader.GetDouble(20),
                    reader.IsDBNull(21) ? null : reader.GetDouble(21),
                    reader.GetInt64(22),
                    reader.IsDBNull(23) ? null : reader.GetInt64(23),
                    reader.IsDBNull(24) ? null : (FsrsRating)reader.GetInt32(24));
            }

            candidates.Add(new PracticeSelectionCandidate(fact, item, card));
        }

        return candidates;
    }

    private const string CandidateSelectSql = @"
        SELECT item.fact_id, item.operation, item.left_operand, item.right_operand,
               item.total_attempts, item.correct_attempts, item.incorrect_attempts,
               item.consecutive_correct, item.last_latency_ms, item.rolling_latency_ms,
               item.fluent_streak, item.is_mastered, item.needs_remediation,
               item.remediation_due_order, item.last_practiced_order, item.last_practiced_at,
               card.fact_id, card.card_id, card.state, card.step, card.stability, card.difficulty,
               card.due_practice_position, card.last_review_practice_position, card.last_rating
        FROM item_learning_state AS item
        LEFT JOIN fsrs_card_state AS card ON card.fact_id = item.fact_id";

    private async Task<IReadOnlyDictionary<string, ItemLearningState>> ReadAllItemStatesAsync(CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, ItemLearningState>(StringComparer.Ordinal);
        if (_connection is null)
        {
            return dict;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT fact_id, operation, left_operand, right_operand,
                   total_attempts, correct_attempts, incorrect_attempts,
                   consecutive_correct, last_latency_ms, rolling_latency_ms,
                   fluent_streak, is_mastered, needs_remediation,
                   remediation_due_order, last_practiced_order, last_practiced_at
            FROM item_learning_state;
        ";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var factId = reader.GetString(0);
            var opStr = reader.GetString(1);
            var left = reader.GetInt32(2);
            var right = reader.GetInt32(3);
            var total = reader.GetInt32(4);
            var correct = reader.GetInt32(5);
            var incorrect = reader.GetInt32(6);
            var streak = reader.GetInt32(7);
            var lastLat = reader.GetInt64(8);
            var rollingLat = reader.GetInt64(9);
            var fluent = reader.GetInt32(10);
            var mastered = reader.GetInt32(11) == 1;
            var remed = reader.GetInt32(12) == 1;
            var remedOrder = reader.GetInt32(13);
            var lastOrder = reader.GetInt32(14);
            var lastPracticedAt = reader.IsDBNull(15) ? null : (DateTimeOffset?)DateTimeOffset.Parse(reader.GetString(15));

            var op = Enum.TryParse<ArithmeticOperation>(opStr, out var parsedOp) ? parsedOp : ArithmeticOperation.Addition;

            dict[factId] = new ItemLearningState
            {
                FactId = factId,
                Operation = op,
                LeftOperand = left,
                RightOperand = right,
                TotalAttempts = total,
                CorrectAttempts = correct,
                IncorrectAttempts = incorrect,
                ConsecutiveCorrectStreak = streak,
                LastLatencyMs = lastLat,
                RollingLatencyMs = rollingLat,
                FluentStreak = fluent,
                IsProvisionallyMastered = mastered,
                NeedsRemediation = remed,
                RemediationDueOrder = remedOrder,
                LastPracticedOrder = lastOrder,
                LastPracticedAt = lastPracticedAt
            };
        }

        return dict;
    }

    private async Task<IReadOnlyDictionary<string, FsrsCardState>> ReadAllFsrsStatesAsync(CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
        if (_connection is null)
        {
            return dict;
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT fact_id, card_id, state, step, stability, difficulty,
                   due_practice_position, last_review_practice_position, last_rating
            FROM fsrs_card_state;
        ";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var factId = reader.GetString(0);
            var cardId = Guid.Parse(reader.GetString(1));
            var state = reader.GetInt32(2);
            int? step = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            double? stability = reader.IsDBNull(4) ? null : reader.GetDouble(4);
            double? difficulty = reader.IsDBNull(5) ? null : reader.GetDouble(5);
            var duePracticePosition = reader.GetInt64(6);
            long? lastReviewPracticePosition = reader.IsDBNull(7) ? null : reader.GetInt64(7);
            FsrsRating? lastRating = reader.IsDBNull(8) ? null : (FsrsRating)reader.GetInt32(8);

            dict[factId] = new FsrsCardState(
                factId,
                cardId,
                state,
                step,
                stability,
                difficulty,
                duePracticePosition,
                lastReviewPracticePosition,
                lastRating);
        }

        return dict;
    }

    private async Task<IReadOnlyDictionary<ArithmeticOperation, OperationProgression>> ReadOperationProgressionsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<ArithmeticOperation, OperationProgression>();
        using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT operation, band_index, band_started_practice_position FROM operation_progression;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Enum.TryParse<ArithmeticOperation>(reader.GetString(0), out var operation)
                || !result.TryAdd(operation, new OperationProgression(operation, reader.GetInt32(1), reader.GetInt64(2))))
            {
                throw new InvalidOperationException("Schema V5 operation progression contains an unknown or duplicate operation.");
            }
        }

        ValidateOperationProgressions(result, ArithmeticOperation.Addition, null);
        return result;
    }

    private async Task<IReadOnlyList<AttemptRecord>> ReadBoundedRecentAttemptsAsync(CancellationToken cancellationToken)
    {
        var list = new Dictionary<string, AttemptRecord>(StringComparer.Ordinal);
        if (_connection is null)
        {
            return [];
        }
        foreach (var operation in Enum.GetValues<ArithmeticOperation>())
        {
            await ReadAttemptsAsync("operation = @operation", "@operation", operation.ToString(), 40, list, cancellationToken).ConfigureAwait(false);
        }
        await ReadAttemptsAsync("1 = 1", null, null, 3, list, cancellationToken).ConfigureAwait(false);
        if (list.Count == 0)
        {
            await ReadLegacyAttemptsAsync(50, list, cancellationToken).ConfigureAwait(false);
        }
        return list.Values.OrderBy(attempt => attempt.PracticePosition).ToArray();
    }

    private async Task<DateTimeOffset?> ReadLatestAcceptedPracticeAtAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return null;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT timestamp
            FROM attempt_history
            ORDER BY CASE WHEN practice_position IS NULL THEN 0 ELSE 1 END DESC,
                     practice_position DESC,
                     timestamp DESC
            LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string timestamp && DateTimeOffset.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private async Task ReadLegacyAttemptsAsync(int limit, IDictionary<string, AttemptRecord> destination, CancellationToken cancellationToken)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"SELECT submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, outcome, response_latency_ms, timestamp FROM attempt_history WHERE practice_position IS NULL ORDER BY timestamp DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int? submitted = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            var outcome = Enum.Parse<AttemptOutcome>(reader.GetString(8));
            var attempt = new AttemptRecord(reader.GetString(0), reader.GetString(1), Enum.Parse<ArithmeticOperation>(reader.GetString(2)), reader.GetInt32(3), reader.GetInt32(4), submitted, reader.GetInt32(6), reader.GetInt32(7) == 1, reader.GetInt64(9), DateTimeOffset.Parse(reader.GetString(10)), outcome);
            destination.TryAdd(attempt.SubmissionId, attempt);
        }
    }

    private async Task ReadAttemptsAsync(string predicate, string? parameter, string? value, int limit, IDictionary<string, AttemptRecord> destination, CancellationToken cancellationToken)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = $@"SELECT submission_id, fact_id, operation, left_operand, right_operand, submitted_answer, correct_answer, is_correct, outcome, response_latency_ms, timestamp, practice_position FROM attempt_history WHERE practice_position IS NOT NULL AND {predicate} ORDER BY practice_position DESC LIMIT @limit;";
        if (parameter is not null) cmd.Parameters.AddWithValue(parameter, value!);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var op = Enum.Parse<ArithmeticOperation>(reader.GetString(2));
            int? submitted = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            var correct = reader.GetInt32(7) == 1;
            var outcome = Enum.Parse<AttemptOutcome>(reader.GetString(8));
            var attempt = new AttemptRecord(reader.GetString(0), reader.GetString(1), op, reader.GetInt32(3), reader.GetInt32(4), submitted, reader.GetInt32(6), correct, reader.GetInt64(9), DateTimeOffset.Parse(reader.GetString(10)), outcome, reader.GetInt64(11));
            destination.TryAdd(attempt.SubmissionId, attempt);
        }
    }

    private static async Task MigrateV1ToV2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                CREATE TABLE attempt_history_v2 (
                    submission_id TEXT PRIMARY KEY,
                    fact_id TEXT NOT NULL,
                    operation TEXT NOT NULL,
                    left_operand INTEGER NOT NULL,
                    right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER,
                    correct_answer INTEGER NOT NULL,
                    is_correct INTEGER NOT NULL,
                    outcome TEXT NOT NULL,
                    response_latency_ms INTEGER NOT NULL,
                    timestamp TEXT NOT NULL
                );

                INSERT INTO attempt_history_v2 (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, outcome,
                    response_latency_ms, timestamp
                )
                SELECT
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct,
                    CASE WHEN is_correct = 1 THEN 'Correct' ELSE 'Incorrect' END,
                    response_latency_ms, timestamp
                FROM attempt_history;

                DROP TABLE attempt_history;
                ALTER TABLE attempt_history_v2 RENAME TO attempt_history;

                UPDATE schema_info SET value = '2' WHERE key = 'schema_version';
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV2ToV3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            // 1. Ensure practice_position column exists on learner_progression
            using (var alterCmd = connection.CreateCommand())
            {
                alterCmd.Transaction = tx;
                alterCmd.CommandText = @"
                    CREATE TABLE learner_progression_v3 (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        current_operation TEXT NOT NULL,
                        current_max_operand INTEGER NOT NULL,
                        operation_max_operands_json TEXT NOT NULL,
                        practice_position INTEGER NOT NULL DEFAULT 0,
                        updated_at TEXT NOT NULL
                    );

                    INSERT INTO learner_progression_v3 (
                        id, current_operation, current_max_operand, operation_max_operands_json, practice_position, updated_at
                    )
                    SELECT
                        id, current_operation, current_max_operand, operation_max_operands_json, 0, updated_at
                    FROM learner_progression;

                    DROP TABLE learner_progression;
                    ALTER TABLE learner_progression_v3 RENAME TO learner_progression;
                ";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Create fsrs_card_state table
            using (var createFsrsCmd = connection.CreateCommand())
            {
                createFsrsCmd.Transaction = tx;
                createFsrsCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS fsrs_card_state (
                        fact_id TEXT PRIMARY KEY,
                        card_id TEXT NOT NULL,
                        state INTEGER NOT NULL,
                        step INTEGER,
                        stability REAL,
                        difficulty REAL,
                        due_practice_position INTEGER NOT NULL,
                        last_review_practice_position INTEGER,
                        last_rating INTEGER
                    );
                ";
                await createFsrsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Chronological attempt replay for FSRS state reconstruction
            var scheduler = new FsrsSchedulerAdapter();
            var fsrsStates = new Dictionary<string, FsrsCardState>(StringComparer.Ordinal);
            long practicePosition = 0;

            using (var readAttemptsCmd = connection.CreateCommand())
            {
                readAttemptsCmd.Transaction = tx;
                readAttemptsCmd.CommandText = @"
                    SELECT fact_id, is_correct, outcome, response_latency_ms, submitted_answer
                    FROM attempt_history
                    ORDER BY timestamp ASC, rowid ASC;
                ";
                using var reader = await readAttemptsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    practicePosition++;
                    var factId = reader.GetString(0);
                    var isCorr = reader.GetInt32(1) == 1;
                    var outcomeStr = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var latency = reader.GetInt64(3);
                    int? subAns = reader.IsDBNull(4) ? null : reader.GetInt32(4);

                    var outcome = (!string.IsNullOrEmpty(outcomeStr) && Enum.TryParse<AttemptOutcome>(outcomeStr, out var parsedOutcome))
                        ? parsedOutcome
                        : (isCorr ? AttemptOutcome.Correct : (subAns is null ? AttemptOutcome.Timeout : AttemptOutcome.Incorrect));

                    var rating = FsrsRatingMapper.MapRating(outcome, latency);
                    fsrsStates.TryGetValue(factId, out var existingCard);
                    var updatedCard = scheduler.ReviewCard(existingCard, factId, rating, practicePosition, latency);
                    fsrsStates[factId] = updatedCard;
                }
            }

            // 4. Save replayed FSRS states
            foreach (var fsrs in fsrsStates.Values)
            {
                using var insertFsrsCmd = connection.CreateCommand();
                insertFsrsCmd.Transaction = tx;
                insertFsrsCmd.CommandText = @"
                    INSERT OR REPLACE INTO fsrs_card_state (
                        fact_id, card_id, state, step, stability, difficulty,
                        due_practice_position, last_review_practice_position, last_rating
                    ) VALUES (
                        @fact_id, @card_id, @state, @step, @stability, @difficulty,
                        @due_practice_position, @last_review_practice_position, @last_rating
                    );
                ";
                insertFsrsCmd.Parameters.AddWithValue("@fact_id", fsrs.FactId);
                insertFsrsCmd.Parameters.AddWithValue("@card_id", fsrs.CardId.ToString());
                insertFsrsCmd.Parameters.AddWithValue("@state", fsrs.State);
                insertFsrsCmd.Parameters.AddWithValue("@step", (object?)fsrs.Step ?? DBNull.Value);
                insertFsrsCmd.Parameters.AddWithValue("@stability", (object?)fsrs.Stability ?? DBNull.Value);
                insertFsrsCmd.Parameters.AddWithValue("@difficulty", (object?)fsrs.Difficulty ?? DBNull.Value);
                insertFsrsCmd.Parameters.AddWithValue("@due_practice_position", fsrs.DuePracticePosition);
                insertFsrsCmd.Parameters.AddWithValue("@last_review_practice_position", (object?)fsrs.LastReviewPracticePosition ?? DBNull.Value);
                insertFsrsCmd.Parameters.AddWithValue("@last_rating", (object?)(int?)fsrs.LastRating ?? DBNull.Value);
                await insertFsrsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 5. Update learner_progression practice_position
            using (var updateProgCmd = connection.CreateCommand())
            {
                updateProgCmd.Transaction = tx;
                updateProgCmd.CommandText = "UPDATE learner_progression SET practice_position = @practice_position WHERE id = 1;";
                updateProgCmd.Parameters.AddWithValue("@practice_position", practicePosition);
                await updateProgCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 6. Update schema_version to 3
            using (var updateSchemaCmd = connection.CreateCommand())
            {
                updateSchemaCmd.Transaction = tx;
                updateSchemaCmd.CommandText = "UPDATE schema_info SET value = '3' WHERE key = 'schema_version';";
                await updateSchemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV3ToV4Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var tx = connection.BeginTransaction();
        try
        {
            using (var alterCmd = connection.CreateCommand())
            {
                alterCmd.Transaction = tx;
                alterCmd.CommandText = @"
                    CREATE TABLE learner_progression_v4 (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        current_operation TEXT NOT NULL,
                        current_max_operand INTEGER NOT NULL,
                        operation_max_operands_json TEXT NOT NULL,
                        practice_position INTEGER NOT NULL DEFAULT 0,
                        completed_checkpoint_level INTEGER NOT NULL DEFAULT 0,
                        active_checkpoint_level INTEGER,
                        checkpoint_attempt_count INTEGER NOT NULL DEFAULT 0,
                        checkpoint_correct_count INTEGER NOT NULL DEFAULT 0,
                        updated_at TEXT NOT NULL
                    );

                    INSERT INTO learner_progression_v4 (
                        id, current_operation, current_max_operand, operation_max_operands_json,
                        practice_position, completed_checkpoint_level, active_checkpoint_level,
                        checkpoint_attempt_count, checkpoint_correct_count, updated_at
                    )
                    SELECT
                        id, current_operation, current_max_operand, operation_max_operands_json,
                        practice_position, 0, NULL, 0, 0, updated_at
                    FROM learner_progression;

                    DROP TABLE learner_progression;
                    ALTER TABLE learner_progression_v4 RENAME TO learner_progression;
                ";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Derive sensible completed_checkpoint_level from existing progression
            using (var readProgCmd = connection.CreateCommand())
            {
                readProgCmd.Transaction = tx;
                readProgCmd.CommandText = "SELECT operation_max_operands_json FROM learner_progression WHERE id = 1;";
                var json = (string?)await readProgCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(json))
                {
                    var map = JsonSerializer.Deserialize<Dictionary<ArithmeticOperation, int>>(json);
                    if (map is not null)
                    {
                        var minOp = map.Values.DefaultIfEmpty(1).Min();
                        var completedLevel = Math.Max(0, minOp - 1);
                        if (completedLevel > 0)
                        {
                            using var updateLevelCmd = connection.CreateCommand();
                            updateLevelCmd.Transaction = tx;
                            updateLevelCmd.CommandText = "UPDATE learner_progression SET completed_checkpoint_level = @level WHERE id = 1;";
                            updateLevelCmd.Parameters.AddWithValue("@level", completedLevel);
                            await updateLevelCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            using (var updateSchemaCmd = connection.CreateCommand())
            {
                updateSchemaCmd.Transaction = tx;
                updateSchemaCmd.CommandText = "UPDATE schema_info SET value = '4' WHERE key = 'schema_version';";
                await updateSchemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV4ToV5Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            Dictionary<ArithmeticOperation, int> maximums;
            long practicePosition;
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT operation_max_operands_json, practice_position FROM learner_progression WHERE id = 1;";
                using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("V4 migration requires learner progression.");
                maximums = JsonSerializer.Deserialize<Dictionary<ArithmeticOperation, int>>(reader.GetString(0)) ?? new();
                practicePosition = reader.GetInt64(1);
            }

            var mapped = new Dictionary<ArithmeticOperation, int>();
            foreach (var operation in Enum.GetValues<ArithmeticOperation>())
            {
                var maximum = maximums.TryGetValue(operation, out var present) ? present : 1;
                if (maximum is < 1 or > 10)
                    throw new InvalidOperationException($"V4 maximum operand for {operation} is outside 1..10.");
                mapped.Add(operation, maximum - 1);
            }

            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction;
                schema.CommandText = @"
                    ALTER TABLE attempt_history ADD COLUMN practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0);";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                schema.CommandText = "DROP TABLE operation_progression;";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                schema.CommandText = @"
                    CREATE TABLE operation_progression (
                        operation TEXT PRIMARY KEY,
                        band_index INTEGER NOT NULL CHECK (band_index >= 0),
                        band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0));
                    CREATE TABLE learner_progression_v5 (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        practice_position INTEGER NOT NULL DEFAULT 0,
                        updated_at TEXT NOT NULL);
                    INSERT INTO learner_progression_v5 (id, practice_position, updated_at)
                        SELECT id, practice_position, updated_at FROM learner_progression;";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                schema.CommandText = "DROP TABLE learner_progression;";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                schema.CommandText = "ALTER TABLE learner_progression_v5 RENAME TO learner_progression;";
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var (operation, bandIndex) in mapped)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO operation_progression (operation, band_index, band_started_practice_position) VALUES (@operation, @band, @start);";
                insert.Parameters.AddWithValue("@operation", operation.ToString());
                insert.Parameters.AddWithValue("@band", bandIndex);
                insert.Parameters.AddWithValue("@start", practicePosition);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using (var indexes = connection.CreateCommand())
            {
                indexes.Transaction = transaction;
                indexes.CommandText = @"
                    CREATE UNIQUE INDEX ux_attempt_history_practice_position
                        ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
                    CREATE INDEX ix_attempt_history_operation_practice_position
                        ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;
                    CREATE INDEX ix_item_learning_state_operation_fact
                        ON item_learning_state(operation, fact_id);
                    CREATE INDEX ix_item_learning_state_operation_remediation_order_fact
                        ON item_learning_state(operation, needs_remediation, remediation_due_order, fact_id);
                    CREATE INDEX ix_item_learning_state_operation_maintenance_fact
                        ON item_learning_state(operation, needs_remediation, last_practiced_order, fact_id);
                    CREATE INDEX ix_fsrs_card_state_due_fact
                        ON fsrs_card_state(due_practice_position, fact_id);
                    UPDATE schema_info SET value = '5' WHERE key = 'schema_version';";
                await indexes.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }
}
