namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class TelemetryExportCoordinatorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstExportCoord_" + Guid.NewGuid().ToString("N"));

    public TelemetryExportCoordinatorTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ExportAndShareAsync_WhenHistoryIsEmpty_PreparesAndDispatchesValidEmptyExport()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id-123");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var fixedTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.True(result.Success);
        Assert.NotNull(result.FilePath);
        Assert.Equal(shareService.LastPreparedPath, result.FilePath);
        Assert.Null(result.ErrorMessage);

        Assert.Equal(1, shareService.PrepareCallCount);
        Assert.Equal(1, shareService.DispatchCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);

        // Verify prepared stream content
        Assert.NotNull(shareService.LastPreparedContent);
        using var doc = JsonDocument.Parse(shareService.LastPreparedContent);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("test-install-id-123", root.GetProperty("installation_id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("app_version").GetString());
        Assert.Equal("Release", root.GetProperty("build_classification").GetString());
        Assert.Equal(0, root.GetProperty("attempt_count").GetInt32());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("attempts").ValueKind);
        Assert.Empty(root.GetProperty("attempts").EnumerateArray());
    }

    [Fact]
    public async Task ExportAndShareAsync_TwoExportsAtSameClockInstant_UseDistinctFileNames()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var fixedTime = new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result1 = await coordinator.ExportAndShareAsync();
        var result2 = await coordinator.ExportAndShareAsync();

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(2, shareService.PreparedFileNames.Count);

        var fileName1 = shareService.PreparedFileNames[0];
        var fileName2 = shareService.PreparedFileNames[1];

        Assert.NotEqual(fileName1, fileName2);
        Assert.StartsWith("mathfirst-telemetry-", fileName1, StringComparison.Ordinal);
        Assert.StartsWith("mathfirst-telemetry-", fileName2, StringComparison.Ordinal);
        Assert.EndsWith(".json", fileName1, StringComparison.Ordinal);
        Assert.EndsWith(".json", fileName2, StringComparison.Ordinal);

        // Verify full 32-hex GUID N suffix
        AssertGuidNSuffix(fileName1);
        AssertGuidNSuffix(fileName2);
    }

    [Fact]
    public async Task ExportAndShareAsync_WhenCancelledBeforeDispatch_ThrowsOperationCanceledException()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource();
        shareService.OnPrepare = () =>
        {
            cts.Cancel();
        };

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ExportAndShareAsync(cts.Token));

        Assert.Equal(1, shareService.PrepareCallCount);
        Assert.Equal(0, shareService.DispatchCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task ExportAndShareAsync_WhenStoreThrows_ReturnsFailedResult()
    {
        var store = new FakeLearnerStore { ThrowOnLoad = new InvalidOperationException("Store connection failed") };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new FakeTelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Store connection failed", result.ErrorMessage, StringComparison.Ordinal);

        Assert.Equal(0, serializer.SerializeCallCount);
        Assert.Equal(0, shareService.PrepareCallCount);
        Assert.Equal(0, shareService.DispatchCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task ExportAndShareAsync_WhenSerializerThrows_ReturnsFailedResult()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new FakeTelemetryJsonSerializer
        {
            ThrowOnSerialize = new InvalidOperationException("Serialization corrupted")
        };
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Serialization corrupted", result.ErrorMessage, StringComparison.Ordinal);

        Assert.Equal(0, shareService.PrepareCallCount);
        Assert.Equal(0, shareService.DispatchCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task ExportAndShareAsync_WhenShareDispatchFails_ReturnsFailedResult()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService
        {
            ThrowOnDispatch = new InvalidOperationException("Native share sheet failed")
        };
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Native share sheet failed", result.ErrorMessage, StringComparison.Ordinal);

        Assert.Equal(1, shareService.PrepareCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task ExportAndShareAsync_DoesNotPurgeCacheImmediately()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.True(result.Success);
        Assert.Equal(0, shareService.PurgeCallCount);
    }

    [Fact]
    public async Task ExportAndShareAsync_SubsequentExportDoesNotPurgePriorCacheFiles()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result1 = await coordinator.ExportAndShareAsync();
        var result2 = await coordinator.ExportAndShareAsync();

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(2, shareService.PrepareCallCount);
        Assert.Equal(2, shareService.DispatchCallCount);
        Assert.Equal(0, shareService.PurgeCallCount);
        Assert.Equal(2, shareService.DispatchedFilePaths.Count);
        Assert.NotEqual(shareService.DispatchedFilePaths[0], shareService.DispatchedFilePaths[1]);
    }

    [Fact]
    public async Task ExportAndShareAsync_PopulatesEnvelopeWithInstallationIdAndAppVersion()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("custom-inst-id-xyz-987");
        var appBuildInfo = new FakeAppBuildInfo("3.1.4", "AlphaTest");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var fixedTime = new DateTimeOffset(2026, 10, 1, 8, 15, 30, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.True(result.Success);
        Assert.NotNull(shareService.LastPreparedContent);

        using var doc = JsonDocument.Parse(shareService.LastPreparedContent);
        var root = doc.RootElement;
        Assert.Equal("custom-inst-id-xyz-987", root.GetProperty("installation_id").GetString());
        Assert.Equal("3.1.4", root.GetProperty("app_version").GetString());
        Assert.Equal("AlphaTest", root.GetProperty("build_classification").GetString());
        Assert.Equal("2026-10-01T08:15:30.0000000+00:00", root.GetProperty("exported_at").GetString());
    }

    [Fact]
    public async Task ExportAndShareAsync_UsesPreparedFullPathForNativeDispatch()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var distinctivePreparedPath = "C:\\Cache\\Special\\PreparedExport_ExplicitFullPath.json";
        var shareService = new FakeTelemetryShareService
        {
            CustomPreparedPath = distinctivePreparedPath
        };
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.True(result.Success);
        Assert.Equal(distinctivePreparedPath, result.FilePath);
        Assert.Equal(distinctivePreparedPath, shareService.LastDispatchedFilePath);
    }

    [Fact]
    public async Task ExportAndShareAsync_PassesShareTitleToNativeDispatch()
    {
        var store = new FakeLearnerStore { AttemptsToReturn = [] };
        var installationIdProvider = new FakeInstallationIdProvider("test-install-id");
        var appBuildInfo = new FakeAppBuildInfo("1.0.0", "Release");
        var serializer = new TelemetryJsonSerializer();
        var shareService = new FakeTelemetryShareService();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        var coordinator = new TelemetryExportCoordinator(
            store,
            installationIdProvider,
            appBuildInfo,
            serializer,
            shareService,
            timeProvider);

        var result = await coordinator.ExportAndShareAsync();

        Assert.True(result.Success);
        Assert.NotNull(shareService.LastDispatchedTitle);
        Assert.False(string.IsNullOrWhiteSpace(shareService.LastDispatchedTitle));
    }

    [Fact]
    public async Task LoadCompleteAttemptTelemetry_LegacyRowsComeBeforePositionedRows()
    {
        var dbPath = Path.Combine(_tempDirectory, "legacy_before_positioned.db");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var tEarly = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var tLegacy = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        var tLate = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            // Insert positioned row with an earlier timestamp
            await InsertRawAttemptAsync(conn, "sub-pos-1", "add:1+1", ArithmeticOperation.Addition, 1, 1, 2, 2, true, true, 1000, tEarly, practicePosition: 1);
            // Insert legacy row with a later timestamp than pos-1
            await InsertRawAttemptAsync(conn, "sub-legacy-1", "add:2+2", ArithmeticOperation.Addition, 2, 2, 4, 4, true, true, 1100, tLegacy, practicePosition: null);
            // Insert positioned row with latest timestamp
            await InsertRawAttemptAsync(conn, "sub-pos-2", "add:3+3", ArithmeticOperation.Addition, 3, 3, 6, 6, true, true, 1200, tLate, practicePosition: 2);
        }

        var attempts = await store.LoadCompleteAttemptTelemetryAsync();

        Assert.Equal(3, attempts.Count);
        // Legacy row must come first despite later timestamp
        Assert.Equal("sub-legacy-1", attempts[0].SubmissionId);
        Assert.Null(attempts[0].PracticePosition);

        Assert.Equal("sub-pos-1", attempts[1].SubmissionId);
        Assert.Equal(1L, attempts[1].PracticePosition);

        Assert.Equal("sub-pos-2", attempts[2].SubmissionId);
        Assert.Equal(2L, attempts[2].PracticePosition);
    }

    [Fact]
    public async Task LoadCompleteAttemptTelemetry_LegacyRowsOrderByTimestampThenSubmissionId()
    {
        var dbPath = Path.Combine(_tempDirectory, "legacy_order.db");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var t1 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            // Deliberately insert in out-of-order sequence to ensure ORDER BY, not insert order
            await InsertRawAttemptAsync(conn, "sub-leg-t2", "add:2+2", ArithmeticOperation.Addition, 2, 2, 4, 4, true, true, 1000, t2, practicePosition: null);
            await InsertRawAttemptAsync(conn, "sub-leg-t3-b", "add:4+4", ArithmeticOperation.Addition, 4, 4, 8, 8, true, true, 1000, t3, practicePosition: null);
            await InsertRawAttemptAsync(conn, "sub-leg-t1", "add:1+1", ArithmeticOperation.Addition, 1, 1, 2, 2, true, true, 1000, t1, practicePosition: null);
            await InsertRawAttemptAsync(conn, "sub-leg-t3-a", "add:3+3", ArithmeticOperation.Addition, 3, 3, 6, 6, true, true, 1000, t3, practicePosition: null);
        }

        var attempts = await store.LoadCompleteAttemptTelemetryAsync();

        Assert.Equal(4, attempts.Count);
        Assert.Equal("sub-leg-t1", attempts[0].SubmissionId);
        Assert.Equal("sub-leg-t2", attempts[1].SubmissionId);
        Assert.Equal("sub-leg-t3-a", attempts[2].SubmissionId);
        Assert.Equal("sub-leg-t3-b", attempts[3].SubmissionId);
    }

    [Fact]
    public async Task LoadCompleteAttemptTelemetry_PositionedRowsOrderByPracticePositionDespiteReverseTimestamps()
    {
        var dbPath = Path.Combine(_tempDirectory, "positioned_order.db");
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        var tRecent = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);
        var tMiddle = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        var tOld = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            // Practice position 1 has the newest timestamp
            await InsertRawAttemptAsync(
                conn, "sub-pos-1", "add:1+1", ArithmeticOperation.Addition, 1, 1, 2, 2, true, true, 1000, tRecent, practicePosition: 1,
                contextVersion: 1, presentedDeadlineMs: 4000, expectedPaceMs: 3000, resolvedRole: "New", operationBandBefore: 0);
            // Practice position 2 has middle timestamp
            await InsertRawAttemptAsync(
                conn, "sub-pos-2", "add:2+2", ArithmeticOperation.Addition, 2, 2, 4, 4, true, true, 1100, tMiddle, practicePosition: 2,
                contextVersion: 1, presentedDeadlineMs: 3500, expectedPaceMs: 3000, resolvedRole: "Due", operationBandBefore: 0);
            // Practice position 3 has oldest timestamp
            await InsertRawAttemptAsync(
                conn, "sub-pos-3", "add:3+3", ArithmeticOperation.Addition, 3, 3, 6, 6, true, true, 1200, tOld, practicePosition: 3,
                contextVersion: 1, presentedDeadlineMs: 3000, expectedPaceMs: 2500, resolvedRole: "Frontier", operationBandBefore: 1);
        }

        var attempts = await store.LoadCompleteAttemptTelemetryAsync();

        Assert.Equal(3, attempts.Count);
        Assert.Equal("sub-pos-1", attempts[0].SubmissionId);
        Assert.Equal(1L, attempts[0].PracticePosition);
        Assert.Equal(1, attempts[0].ContextVersion);
        Assert.Equal(4000, attempts[0].PresentedDeadlineMs);
        Assert.Equal(3000, attempts[0].ExpectedPaceMs);
        Assert.Equal("New", attempts[0].ResolvedRole);
        Assert.Equal(0, attempts[0].OperationBandBefore);

        Assert.Equal("sub-pos-2", attempts[1].SubmissionId);
        Assert.Equal(2L, attempts[1].PracticePosition);
        Assert.Equal(1, attempts[1].ContextVersion);
        Assert.Equal("Due", attempts[1].ResolvedRole);

        Assert.Equal("sub-pos-3", attempts[2].SubmissionId);
        Assert.Equal(3L, attempts[2].PracticePosition);
        Assert.Equal(1, attempts[2].ContextVersion);
        Assert.Equal("Frontier", attempts[2].ResolvedRole);
        Assert.Equal(1, attempts[2].OperationBandBefore);
    }

    private static void AssertGuidNSuffix(string fileName)
    {
        var withoutPrefix = fileName.Substring("mathfirst-telemetry-".Length);
        var withoutExtension = withoutPrefix.Substring(0, withoutPrefix.Length - ".json".Length);
        var lastDashIndex = withoutExtension.LastIndexOf('-');
        Assert.True(lastDashIndex >= 0, $"Filename '{fileName}' missing dash before GUID suffix.");
        var guidPart = withoutExtension.Substring(lastDashIndex + 1);
        Assert.Equal(32, guidPart.Length);
        Assert.True(guidPart.All(c => "0123456789abcdefABCDEF".Contains(c)), $"GUID suffix '{guidPart}' is not valid 32-char hex.");
    }

    private static async Task InsertRawAttemptAsync(
        SqliteConnection conn,
        string submissionId,
        string factId,
        ArithmeticOperation operation,
        int leftOperand,
        int rightOperand,
        int? submittedAnswer,
        int correctAnswer,
        bool isCorrect,
        bool isFluent,
        long latencyMs,
        DateTimeOffset timestamp,
        long? practicePosition,
        int? contextVersion = null,
        int? presentedDeadlineMs = null,
        int? expectedPaceMs = null,
        string? resolvedRole = null,
        int? operationBandBefore = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO attempt_history (
                submission_id, fact_id, operation, left_operand, right_operand,
                submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                response_latency_ms, timestamp, practice_position,
                attempt_context_version, presented_deadline_ms, expected_pace_ms, resolved_role, operation_band_before
            ) VALUES (
                @sub_id, @fact_id, @operation, @left, @right,
                @submitted, @correct, @is_correct, @is_fluent, @outcome,
                @latency, @timestamp, @pos,
                @context_ver, @pres_dl, @exp_pace, @role, @band_before
            );";
        cmd.Parameters.AddWithValue("@sub_id", submissionId);
        cmd.Parameters.AddWithValue("@fact_id", factId);
        cmd.Parameters.AddWithValue("@operation", operation.ToString());
        cmd.Parameters.AddWithValue("@left", leftOperand);
        cmd.Parameters.AddWithValue("@right", rightOperand);
        cmd.Parameters.AddWithValue("@submitted", (object?)submittedAnswer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@correct", correctAnswer);
        cmd.Parameters.AddWithValue("@is_correct", isCorrect ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_fluent", isFluent ? 1 : 0);
        cmd.Parameters.AddWithValue("@outcome", isCorrect ? "Correct" : "Incorrect");
        cmd.Parameters.AddWithValue("@latency", latencyMs);
        cmd.Parameters.AddWithValue("@timestamp", timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@pos", (object?)practicePosition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@context_ver", (object?)contextVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pres_dl", (object?)presentedDeadlineMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@exp_pace", (object?)expectedPaceMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", (object?)resolvedRole ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@band_before", (object?)operationBandBefore ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedNow;
    }

    private sealed class FakeInstallationIdProvider(string installationId) : IInstallationIdProvider
    {
        public string GetOrCreateInstallationId() => installationId;
        public void ClearInstallationId() { }
    }

    private sealed class FakeAppBuildInfo(string displayVersion, string buildClassification) : IAppBuildInfo
    {
        public string DisplayVersion => displayVersion;
        public string BuildClassification => buildClassification;
    }

    private sealed class FakeTelemetryShareService : ITelemetryShareService
    {
        public int PrepareCallCount { get; private set; }
        public int DispatchCallCount { get; private set; }
        public int PurgeCallCount { get; private set; }

        public List<string> PreparedFileNames { get; } = [];
        public List<string> DispatchedFilePaths { get; } = [];
        public string? LastPreparedPath { get; private set; }
        public byte[]? LastPreparedContent { get; private set; }
        public string? LastDispatchedFilePath { get; private set; }
        public string? LastDispatchedTitle { get; private set; }

        public string? CustomPreparedPath { get; set; }
        public Action? OnPrepare { get; set; }
        public Exception? ThrowOnDispatch { get; set; }

        public async Task<string> PrepareShareFileAsync(
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            PrepareCallCount++;
            PreparedFileNames.Add(fileName);

            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            LastPreparedContent = ms.ToArray();

            OnPrepare?.Invoke();

            LastPreparedPath = CustomPreparedPath ?? ("C:\\ShareCache\\" + fileName);
            return LastPreparedPath;
        }

        public Task DispatchSystemShareAsync(string filePath, string title)
        {
            if (ThrowOnDispatch is not null)
            {
                throw ThrowOnDispatch;
            }

            DispatchCallCount++;
            DispatchedFilePaths.Add(filePath);
            LastDispatchedFilePath = filePath;
            LastDispatchedTitle = title;
            return Task.CompletedTask;
        }

        public void PurgeShareCache()
        {
            PurgeCallCount++;
        }
    }

    private sealed class FakeTelemetryJsonSerializer : ITelemetryJsonSerializer
    {
        public int SerializeCallCount { get; private set; }
        public Exception? ThrowOnSerialize { get; set; }

        public Task SerializeAsync(
            Stream output,
            string installationId,
            string appVersion,
            string buildClassification,
            DateTimeOffset exportedAt,
            IReadOnlyList<AttemptRecord> attempts,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSerialize is not null)
            {
                throw ThrowOnSerialize;
            }

            SerializeCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLearnerStore : ILearnerStore
    {
        public string StoragePath => "memory";
        public IReadOnlyList<AttemptRecord> AttemptsToReturn { get; set; } = [];
        public Exception? ThrowOnLoad { get; set; }

        public Task<IReadOnlyList<AttemptRecord>> LoadCompleteAttemptTelemetryAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad is not null)
            {
                throw ThrowOnLoad;
            }

            return Task.FromResult(AttemptsToReturn);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
