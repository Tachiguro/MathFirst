namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-007 Slice 13A: Durable question-bound Critical Strike and Overdrive ranks in SQLite pending intents.
/// </summary>
public sealed class SqliteGameplayStoreModifierIntentPersistenceTests : IDisposable
{
    private const string IsoTimestamp = "2026-10-20T09:00:00.0000000+00:00";
    private static readonly DateTimeOffset FixedTime = DateTimeOffset.Parse(IsoTimestamp, CultureInfo.InvariantCulture);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MathFirst_ModifierIntent_" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_directory, "gameplay.db");
    private string LearnerDatabasePath => Path.Combine(_directory, "learner.db");

    public SqliteGameplayStoreModifierIntentPersistenceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Ignored in test cleanup; stores clear their own pool on Dispose and raw connections use Pooling=False.
        }
    }

    private async Task<SqliteGameplayStore> OpenStoreAsync(string? path = null)
    {
        var store = new SqliteGameplayStore(path ?? DatabasePath);
        await store.InitializeAsync();
        return store;
    }

    private async Task<SqliteLearnerStore> OpenLearnerStoreAsync(string? path = null)
    {
        var store = new SqliteLearnerStore(path ?? LearnerDatabasePath);
        await store.InitializeAsync();
        return store;
    }

    private async Task ExecuteRawAsync(string sql, string? path = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarRawAsync(string sql, string? path = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private Task SeedOwnedRanksAsync(int criticalRank, int overdriveRank, int attackRank = 0, long resetEpoch = 0)
    {
        return ExecuteRawAsync($"""
            UPDATE gameplay_progression
            SET critical_strike_rank = {criticalRank},
                overdrive_rank = {overdriveRank},
                attack_rank = {attackRank},
                reset_epoch = {resetEpoch}
            WHERE id = 1;
            UPDATE gameplay_run_state SET reset_epoch = {resetEpoch} WHERE id = 1;
            UPDATE gameplay_reset_intent SET current_epoch = {resetEpoch}, target_epoch = {resetEpoch} WHERE id = 1;
            """);
    }

    private async Task<string[]> DumpDatabaseAsync(string? path = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Pooling = false
        }.ToString());
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
                    if (reader.IsDBNull(i))
                    {
                        return "NULL";
                    }

                    object value = reader.GetValue(i);
                    return value.GetType().Name + ":" + (value is byte[] bytes
                        ? Convert.ToHexString(bytes)
                        : Convert.ToString(value, CultureInfo.InvariantCulture));
                })));
            }
        }

        return dump.ToArray();
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId = "sub-1",
        string factId = "add:2+3",
        bool isCorrect = true,
        bool isEligible = true,
        long responseLatencyMs = 450,
        long resetEpoch = 0,
        DateTimeOffset? createdAt = null,
        int effectiveAttackDamage = 1,
        int boundCriticalStrikeRank = 0,
        int boundOverdriveRank = 0)
    {
        return new CyberDefensePendingIntentRecord(
            submissionId,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            createdAt ?? FixedTime,
            effectiveAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank);
    }

    private static SubmissionChangeSet CreateLearnerChangeSet(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long practicePosition,
        long expectedRevision)
    {
        var stripped = factId.StartsWith("add:", StringComparison.Ordinal) ? factId.Substring(4) : factId;
        var parts = stripped.Split('+');
        int left = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int right = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int answer = left + right;

        var attempt = new AttemptRecord(
            submissionId: submissionId,
            factId: factId,
            operation: ArithmeticOperation.Addition,
            leftOperand: left,
            rightOperand: right,
            submittedAnswer: isCorrect ? answer : answer + 1,
            correctAnswer: answer,
            isCorrect: isCorrect,
            isFluent: isCorrect,
            responseLatencyMs: responseLatencyMs,
            timestamp: FixedTime,
            outcome: isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            practicePosition: practicePosition);

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.CurriculumStage = CurriculumStage.Stage1_Addition;

        var itemState = new ItemLearningState
        {
            FactId = factId,
            Operation = ArithmeticOperation.Addition,
            LeftOperand = left,
            RightOperand = right,
            TotalAttempts = 1,
            CorrectAttempts = isCorrect ? 1 : 0,
            IncorrectAttempts = isCorrect ? 0 : 1,
            ConsecutiveCorrectStreak = isCorrect ? 1 : 0,
            LastLatencyMs = responseLatencyMs,
            RollingLatencyMs = responseLatencyMs,
            FluentStreak = isCorrect ? 1 : 0,
            IsProvisionallyMastered = false,
            NeedsRemediation = !isCorrect,
            RemediationDueOrder = 0,
            LastPracticedOrder = (int)practicePosition,
            LastPracticedAt = FixedTime
        };

        var fsrsState = new FsrsCardState(
            FactId: factId,
            CardId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            State: 2,
            Step: null,
            Stability: 2.5,
            Difficulty: 5.0,
            DuePracticePosition: practicePosition + 10,
            LastReviewPracticePosition: practicePosition,
            LastRating: isCorrect ? FsrsRating.Good : FsrsRating.Again);

        return new SubmissionChangeSet(
            submissionId: submissionId,
            ExpectedRevision: expectedRevision,
            attempt: attempt,
            updatedItemState: itemState,
            updatedProgression: progression,
            updatedFsrsState: fsrsState,
            operationProgressions: progression.OperationProgressions);
    }

    // =========================================================================
    // 1. Record constructors, legacy defaults, and domain validation
    // =========================================================================

    [Fact]
    public void LegacyConstructors_DefaultBothModifierRanksToZero_AndPreserveCompatibility()
    {
        var sevenArg = new CyberDefensePendingIntentRecord(
            "sub-7", "add:1+1", isCorrect: true, isEligible: true, responseLatencyMs: 320, resetEpoch: 2, createdAt: FixedTime);

        Assert.Equal("sub-7", sevenArg.SubmissionId);
        Assert.Equal("add:1+1", sevenArg.FactId);
        Assert.True(sevenArg.IsCorrect);
        Assert.True(sevenArg.IsEligible);
        Assert.Equal(320, sevenArg.ResponseLatencyMs);
        Assert.Equal(2, sevenArg.ResetEpoch);
        Assert.Equal(FixedTime, sevenArg.CreatedAt);
        Assert.Equal(1, sevenArg.EffectiveAttackDamage);
        Assert.Equal(0, sevenArg.BoundCriticalStrikeRank);
        Assert.Equal(0, sevenArg.BoundOverdriveRank);

        var eightArg = new CyberDefensePendingIntentRecord(
            "sub-8", "add:2+2", isCorrect: false, isEligible: true, responseLatencyMs: 640, resetEpoch: 3, createdAt: FixedTime, effectiveAttackDamage: 4);

        Assert.Equal(4, eightArg.EffectiveAttackDamage);
        Assert.Equal(0, eightArg.BoundCriticalStrikeRank);
        Assert.Equal(0, eightArg.BoundOverdriveRank);

        var tenArgZero = new CyberDefensePendingIntentRecord(
            "sub-7", "add:1+1", isCorrect: true, isEligible: true, responseLatencyMs: 320, resetEpoch: 2, createdAt: FixedTime,
            effectiveAttackDamage: 1, boundCriticalStrikeRank: 0, boundOverdriveRank: 0);

        Assert.Equal(sevenArg, tenArgZero);
    }

    public static IEnumerable<object[]> ValidRankCombinations()
    {
        for (int crit = 0; crit <= CyberDefenseCriticalStrikePolicy.MaxRank; crit++)
        {
            for (int od = 0; od <= CyberDefenseOverdrivePolicy.MaxRank; od++)
            {
                yield return [crit, od];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValidRankCombinations))]
    public void ValidatedConstructor_AcceptsAllApprovedRankCombinations_AndPreservesBaseAttackDamage(int criticalRank, int overdriveRank)
    {
        var intent = new CyberDefensePendingIntentRecord(
            "sub-combo",
            "mul:3*4",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            resetEpoch: 4,
            createdAt: FixedTime,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: criticalRank,
            boundOverdriveRank: overdriveRank);

        Assert.Equal("sub-combo", intent.SubmissionId);
        Assert.Equal("mul:3*4", intent.FactId);
        Assert.True(intent.IsCorrect);
        Assert.True(intent.IsEligible);
        Assert.Equal(500, intent.ResponseLatencyMs);
        Assert.Equal(4, intent.ResetEpoch);
        Assert.Equal(FixedTime, intent.CreatedAt);
        Assert.Equal(2, intent.EffectiveAttackDamage);
        Assert.Equal(criticalRank, intent.BoundCriticalStrikeRank);
        Assert.Equal(overdriveRank, intent.BoundOverdriveRank);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-42)]
    [InlineData(int.MinValue)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void ValidatedConstructor_RejectsInvalidCriticalStrikeRank(int invalidCriticalRank)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateIntent(boundCriticalStrikeRank: invalidCriticalRank, boundOverdriveRank: 0));
        Assert.Equal("boundCriticalStrikeRank", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-42)]
    [InlineData(int.MinValue)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void ValidatedConstructor_RejectsInvalidOverdriveRank(int invalidOverdriveRank)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateIntent(boundCriticalStrikeRank: 0, boundOverdriveRank: invalidOverdriveRank));
        Assert.Equal("boundOverdriveRank", ex.ParamName);
    }

    [Fact]
    public void ValidatedConstructor_PreservesBaseFieldValidationAndValueInequality()
    {
        Assert.Throws<ArgumentException>(() => CreateIntent(submissionId: " ", boundCriticalStrikeRank: 1, boundOverdriveRank: 1));
        Assert.Throws<ArgumentException>(() => CreateIntent(factId: "", boundCriticalStrikeRank: 1, boundOverdriveRank: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateIntent(responseLatencyMs: -1, boundCriticalStrikeRank: 1, boundOverdriveRank: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateIntent(resetEpoch: -1, boundCriticalStrikeRank: 1, boundOverdriveRank: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateIntent(effectiveAttackDamage: 0, boundCriticalStrikeRank: 1, boundOverdriveRank: 1));

        var baseIntent = CreateIntent(boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
        var diffCrit = CreateIntent(boundCriticalStrikeRank: 2, boundOverdriveRank: 1);
        var diffOd = CreateIntent(boundCriticalStrikeRank: 1, boundOverdriveRank: 2);

        Assert.NotEqual(baseIntent, diffCrit);
        Assert.NotEqual(baseIntent, diffOd);
    }

    // =========================================================================
    // 2. StagePendingIntentAsync persistence, owned-rank gating, and base damage
    // =========================================================================

    public static IEnumerable<object[]> OwnedAndBoundValidCombinations()
    {
        for (int ownedCrit = 0; ownedCrit <= CyberDefenseCriticalStrikePolicy.MaxRank; ownedCrit++)
        {
            for (int ownedOd = 0; ownedOd <= CyberDefenseOverdrivePolicy.MaxRank; ownedOd++)
            {
                for (int boundCrit = 0; boundCrit <= ownedCrit; boundCrit++)
                {
                    for (int boundOd = 0; boundOd <= ownedOd; boundOd++)
                    {
                        yield return [ownedCrit, ownedOd, boundCrit, boundOd];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(OwnedAndBoundValidCombinations))]
    public async Task StagePendingIntent_PersistsOwnedModifierRanks_AndReturnsExactValues(
        int ownedCrit, int ownedOd, int boundCrit, int boundOd)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(ownedCrit, ownedOd, attackRank: 1, resetEpoch: 2);

        int baseDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(1);
        var intent = CreateIntent(
            submissionId: $"sub-{ownedCrit}-{ownedOd}-{boundCrit}-{boundOd}",
            resetEpoch: 2,
            effectiveAttackDamage: baseDamage,
            boundCriticalStrikeRank: boundCrit,
            boundOverdriveRank: boundOd);

        await store.StagePendingIntentAsync(intent);

        Assert.Equal((long)boundCrit, await ScalarRawAsync(
            $"SELECT bound_critical_strike_rank FROM gameplay_pending_intent WHERE submission_id = '{intent.SubmissionId}';"));
        Assert.Equal("integer", await ScalarRawAsync(
            $"SELECT typeof(bound_critical_strike_rank) FROM gameplay_pending_intent WHERE submission_id = '{intent.SubmissionId}';"));
        Assert.Equal((long)boundOd, await ScalarRawAsync(
            $"SELECT bound_overdrive_rank FROM gameplay_pending_intent WHERE submission_id = '{intent.SubmissionId}';"));
        Assert.Equal("integer", await ScalarRawAsync(
            $"SELECT typeof(bound_overdrive_rank) FROM gameplay_pending_intent WHERE submission_id = '{intent.SubmissionId}';"));
        Assert.Equal((long)baseDamage, await ScalarRawAsync(
            $"SELECT effective_attack_damage FROM gameplay_pending_intent WHERE submission_id = '{intent.SubmissionId}';"));

        var single = await store.GetPendingIntentAsync(intent.SubmissionId);
        Assert.Equal(intent, single);

        var all = await store.GetPendingIntentsAsync(2);
        var fromList = Assert.Single(all);
        Assert.Equal(intent, fromList);
    }

    [Fact]
    public async Task StagePendingIntent_AfterAtomicSkillPurchases_PersistsRanksAndPreservesBaseAttackDamage()
    {
        using var store = await OpenStoreAsync();
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET player_level = 80, earned_sp = 40, spent_sp = 0, available_sp = 40
            WHERE id = 1;
            """);

        await store.PurchaseCriticalStrikeUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        await store.PurchaseOverdriveUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
        await store.PurchaseOverdriveUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);

        // Attack rank is still 0 (base damage 1), while Critical is rank 1 and Overdrive is rank 2.
        var intent = CreateIntent(
            submissionId: "sub-purchased",
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 2);

        await store.StagePendingIntentAsync(intent);

        var loaded = await store.GetPendingIntentAsync("sub-purchased");
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.EffectiveAttackDamage);
        Assert.Equal(1, loaded.BoundCriticalStrikeRank);
        Assert.Equal(2, loaded.BoundOverdriveRank);

        // Attempting to stage pre-composed bonus damage (2 instead of base 1) is rejected even when modifiers are owned.
        var precomposedDamageIntent = CreateIntent(
            submissionId: "sub-precomposed-damage",
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(precomposedDamageIntent));
        Assert.Null(await store.GetPendingIntentAsync("sub-precomposed-damage"));
    }

    public static IEnumerable<object[]> ExcessiveOrUnownedRankCombinations()
    {
        for (int ownedCrit = 0; ownedCrit <= CyberDefenseCriticalStrikePolicy.MaxRank; ownedCrit++)
        {
            for (int ownedOd = 0; ownedOd <= CyberDefenseOverdrivePolicy.MaxRank; ownedOd++)
            {
                for (int boundCrit = 0; boundCrit <= CyberDefenseCriticalStrikePolicy.MaxRank; boundCrit++)
                {
                    for (int boundOd = 0; boundOd <= CyberDefenseOverdrivePolicy.MaxRank; boundOd++)
                    {
                        if (boundCrit > ownedCrit || boundOd > ownedOd)
                        {
                            yield return [ownedCrit, ownedOd, boundCrit, boundOd];
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExcessiveOrUnownedRankCombinations))]
    public async Task StagePendingIntent_RejectsBoundRanksExceedingOwnedProgressionRanks_WithoutMutation(
        int ownedCrit, int ownedOd, int boundCrit, int boundOd)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(ownedCrit, ownedOd);
        var before = await DumpDatabaseAsync();

        var intent = CreateIntent(
            submissionId: "sub-excessive",
            boundCriticalStrikeRank: boundCrit,
            boundOverdriveRank: boundOd);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(intent));

        Assert.Null(await store.GetPendingIntentAsync("sub-excessive"));
        Assert.Empty(await store.GetPendingIntentsAsync(0));
        Assert.Equal(before, await DumpDatabaseAsync());
    }

    // =========================================================================
    // 3. Duplicate idempotency and conflicting bound-rank rejection
    // =========================================================================

    [Fact]
    public async Task StagePendingIntent_DuplicateWithIdenticalModifierRanks_IsIdempotentEvenAfterRankPromotion()
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 1, overdriveRank: 1, attackRank: 1);

        var t1 = FixedTime;
        var t2 = FixedTime.AddSeconds(15);

        var first = CreateIntent(
            submissionId: "sub-idem",
            createdAt: t1,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(first);

        // Retry with identical payload and a retry timestamp is idempotent and preserves original CreatedAt.
        var retrySameOwned = CreateIntent(
            submissionId: "sub-idem",
            createdAt: t2,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(retrySameOwned);

        // Promote owned ranks to 2/2; retrying the original 1/1 intent remains idempotent.
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, attackRank: 1);
        await store.StagePendingIntentAsync(retrySameOwned);

        var stored = await store.GetPendingIntentAsync("sub-idem");
        Assert.Equal(first, stored);
        Assert.Single(await store.GetPendingIntentsAsync(0));
    }

    [Theory]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(1, 0, 2, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 1, 0, 2)]
    [InlineData(1, 1, 2, 1)]
    [InlineData(1, 1, 1, 2)]
    [InlineData(2, 2, 1, 2)]
    [InlineData(2, 2, 2, 1)]
    public async Task StagePendingIntent_DuplicateWithConflictingBoundRanks_IsRejectedWithoutOverwritingOriginal(
        int firstCrit, int firstOd, int secondCrit, int secondOd)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, attackRank: 1);

        var original = CreateIntent(
            submissionId: "sub-conflict",
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: firstCrit,
            boundOverdriveRank: firstOd);
        await store.StagePendingIntentAsync(original);
        var before = await DumpDatabaseAsync();

        var conflicting = CreateIntent(
            submissionId: "sub-conflict",
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: secondCrit,
            boundOverdriveRank: secondOd);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(conflicting));
        Assert.Contains("Conflicting pending intent", ex.Message, StringComparison.Ordinal);

        Assert.Equal(original, await store.GetPendingIntentAsync("sub-conflict"));
        Assert.Equal(before, await DumpDatabaseAsync());
    }

    // =========================================================================
    // 4. Reopen persistence, ordering preservation, and V6 -> V7 migration
    // =========================================================================

    [Fact]
    public async Task PendingIntents_PreserveBoundRanksAndOrdering_AcrossDatabaseReopen()
    {
        var i1 = CreateIntent("sub-b", createdAt: FixedTime.AddSeconds(10), effectiveAttackDamage: 1, boundCriticalStrikeRank: 0, boundOverdriveRank: 0);
        var i2 = CreateIntent("sub-a", createdAt: FixedTime.AddSeconds(10), effectiveAttackDamage: 2, boundCriticalStrikeRank: 1, boundOverdriveRank: 2);
        var i3 = CreateIntent("sub-c", createdAt: FixedTime.AddSeconds(5), effectiveAttackDamage: 2, boundCriticalStrikeRank: 2, boundOverdriveRank: 1);

        using (var store = await OpenStoreAsync())
        {
            await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, attackRank: 1);
            await store.StagePendingIntentAsync(i1);
            await store.StagePendingIntentAsync(i2);
            await store.StagePendingIntentAsync(i3);
            await store.CloseAsync();
        }

        using (var reopened = await OpenStoreAsync())
        {
            Assert.Equal(i1, await reopened.GetPendingIntentAsync("sub-b"));
            Assert.Equal(i2, await reopened.GetPendingIntentAsync("sub-a"));
            Assert.Equal(i3, await reopened.GetPendingIntentAsync("sub-c"));

            // Existing pending-intent ordering: created_at ASC, submission_id ASC.
            var ordered = await reopened.GetPendingIntentsAsync(0);
            Assert.Equal([i3, i2, i1], ordered);
        }
    }

    [Fact]
    public async Task V6MigratedPendingIntents_DefaultBothBoundModifierRanksToZero_AndRemainRecoverable()
    {
        string v6Ddl = (string)typeof(SqliteGameplayStore)
            .GetField("SchemaV6Ddl", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        await ExecuteRawAsync(v6Ddl + $"""
            INSERT INTO gameplay_schema_info VALUES
                ('schema_version', '6'),
                ('store_revision', '12'),
                ('reset_epoch', '3'),
                ('economy_version', 'ECONOMY_V1');
            INSERT INTO gameplay_progression (
                id, store_revision, reset_epoch, cumulative_xp, player_level,
                earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at,
                firewall_rank, consecutive_early_defeats, is_beginner_assistance_active, activated_firewall_rank
            ) VALUES (1, 12, 3, 50000, 60, 59, 1, 58, 1, 'ECONOMY_V1', '{IsoTimestamp}', 0, 0, 0, 0);
            INSERT INTO gameplay_run_state (
                id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at
            ) VALUES (1, 1, 0, 2, 100, 3, '{IsoTimestamp}');
            INSERT INTO gameplay_reset_intent VALUES (1, 0, 3, 3, NULL, '{IsoTimestamp}');
            INSERT INTO gameplay_pending_intent (
                submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
            ) VALUES
                ('sub-v6-1', 'add:2+2', 1, 1, 400, 3, '{IsoTimestamp}', 2),
                ('sub-v6-2', 'add:3+4', 0, 1, 900, 3, '{IsoTimestamp}', 2);
            """);

        using var store = await OpenStoreAsync();

        var p1 = await store.GetPendingIntentAsync("sub-v6-1");
        Assert.NotNull(p1);
        Assert.Equal(2, p1.EffectiveAttackDamage);
        Assert.Equal(0, p1.BoundCriticalStrikeRank);
        Assert.Equal(0, p1.BoundOverdriveRank);

        var list = await store.GetPendingIntentsAsync(3);
        Assert.Equal(2, list.Count);
        Assert.All(list, item =>
        {
            Assert.Equal(2, item.EffectiveAttackDamage);
            Assert.Equal(0, item.BoundCriticalStrikeRank);
            Assert.Equal(0, item.BoundOverdriveRank);
        });

        var receipt = await store.ApplyAttemptTransactionAsync(p1);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
        Assert.Equal(2, receipt.TransitionResult!.RequestedAttackDamage);
        Assert.Null(await store.GetPendingIntentAsync("sub-v6-1"));
    }

    // =========================================================================
    // 5. SQLite storage-class validation and malformed value rejection
    // =========================================================================

    public static IEnumerable<object[]> CorruptPendingIntentRankStorage()
    {
        string[] columns = ["bound_critical_strike_rank", "bound_overdrive_rank"];
        (string Sql, string ExpectedType)[] corruptions =
        [
            ("1.5", "real"),
            ("'corrupt'", "text"),
            ("X'0102'", "blob")
        ];

        foreach (string column in columns)
        {
            foreach (var (sql, expectedType) in corruptions)
            {
                yield return [column, sql, expectedType];
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorruptPendingIntentRankStorage))]
    public async Task PendingIntent_NonIntegerStorageClass_FailsClosedOnReadDuplicateStageApplyAndReopen(
        string column, string sqlValue, string expectedType)
    {
        using (var store = await OpenStoreAsync())
        {
            await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2);
            var staged = CreateIntent("sub-corrupt", boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
            await store.StagePendingIntentAsync(staged);

            await ExecuteRawAsync($"PRAGMA ignore_check_constraints = ON; UPDATE gameplay_pending_intent SET {column} = {sqlValue};");
            Assert.Equal(expectedType, await ScalarRawAsync($"SELECT typeof({column}) FROM gameplay_pending_intent WHERE submission_id = 'sub-corrupt';"));

            var before = await DumpDatabaseAsync();

            var readEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentAsync("sub-corrupt"));
            Assert.Contains(column, readEx.Message, StringComparison.Ordinal);

            var listEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentsAsync(0));
            Assert.Contains(column, listEx.Message, StringComparison.Ordinal);

            var stageEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(staged));
            Assert.Contains(column, stageEx.Message, StringComparison.Ordinal);

            var applyEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staged));
            Assert.Contains(column, applyEx.Message, StringComparison.Ordinal);

            Assert.Equal(before, await DumpDatabaseAsync());
            await store.CloseAsync();
        }

        using var reopened = new SqliteGameplayStore(DatabasePath);
        var reopenEx = await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.InitializeAsync());
        Assert.Contains(column, reopenEx.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bound_critical_strike_rank", "1.0", "real")]
    [InlineData("bound_overdrive_rank", "1.0", "real")]
    [InlineData("bound_critical_strike_rank", "'1'", "text")]
    [InlineData("bound_overdrive_rank", "'1'", "text")]
    [InlineData("bound_critical_strike_rank", "NULL", "null")]
    [InlineData("bound_overdrive_rank", "NULL", "null")]
    public async Task PendingIntent_AffinityBypassedIntegralRealNumericTextOrNull_FailsClosedWithoutCoercion(
        string column, string sqlValue, string expectedType)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2);
        var staged = CreateIntent("sub-affinity", boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(staged);

        // Rebuild table without column affinity or NOT NULL constraints so 1.0, '1', and NULL keep their raw storage classes.
        await ExecuteRawAsync($"""
            ALTER TABLE gameplay_pending_intent RENAME TO corrupt_pending_fixture;
            CREATE TABLE gameplay_pending_intent AS
            SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at,
                   effective_attack_damage,
                   bound_critical_strike_rank + 0 AS bound_critical_strike_rank,
                   bound_overdrive_rank + 0 AS bound_overdrive_rank
            FROM corrupt_pending_fixture;
            DROP TABLE corrupt_pending_fixture;
            UPDATE gameplay_pending_intent SET {column} = {sqlValue};
            """);

        Assert.Equal(expectedType, await ScalarRawAsync($"SELECT typeof({column}) FROM gameplay_pending_intent WHERE submission_id = 'sub-affinity';"));

        var readEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentAsync("sub-affinity"));
        Assert.Contains(column, readEx.Message, StringComparison.Ordinal);

        var listEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentsAsync(0));
        Assert.Contains(column, listEx.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bound_critical_strike_rank", "-1")]
    [InlineData("bound_critical_strike_rank", "3")]
    [InlineData("bound_critical_strike_rank", "2147483648")]
    [InlineData("bound_overdrive_rank", "-1")]
    [InlineData("bound_overdrive_rank", "3")]
    [InlineData("bound_overdrive_rank", "2147483648")]
    public async Task PendingIntent_OutOfRangePersistedRank_FailsClosedWithoutNormalization(string column, string sqlValue)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2);
        var staged = CreateIntent("sub-range", boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(staged);

        await ExecuteRawAsync($"PRAGMA ignore_check_constraints = ON; UPDATE gameplay_pending_intent SET {column} = {sqlValue};");
        var before = await DumpDatabaseAsync();

        var readEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentAsync("sub-range"));
        Assert.Contains(column, readEx.Message, StringComparison.Ordinal);

        var listEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetPendingIntentsAsync(0));
        Assert.Contains(column, listEx.Message, StringComparison.Ordinal);

        var applyEx = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staged));
        Assert.Contains(column, applyEx.Message, StringComparison.Ordinal);

        Assert.Equal(before, await DumpDatabaseAsync());
    }

    [Theory]
    [InlineData("critical_strike_rank", "1.5")]
    [InlineData("critical_strike_rank", "'corrupt'")]
    [InlineData("critical_strike_rank", "X'01'")]
    [InlineData("critical_strike_rank", "3")]
    [InlineData("overdrive_rank", "1.5")]
    [InlineData("overdrive_rank", "'corrupt'")]
    [InlineData("overdrive_rank", "X'01'")]
    [InlineData("overdrive_rank", "-1")]
    public async Task StagePendingIntent_CorruptProgressionRankStorage_FailsClosedWithoutStaging(string column, string sqlValue)
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 1, overdriveRank: 1);
        await ExecuteRawAsync($"PRAGMA ignore_check_constraints = ON; UPDATE gameplay_progression SET {column} = {sqlValue};");
        var before = await DumpDatabaseAsync();

        var intent = CreateIntent("sub-prog-corrupt", boundCriticalStrikeRank: 0, boundOverdriveRank: 0);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(intent));
        Assert.Contains(column, ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, await DumpDatabaseAsync());
    }

    // =========================================================================
    // 6. Reset-epoch and reset-pending fencing
    // =========================================================================

    [Fact]
    public async Task StageAndApplyPendingIntent_RejectStaleOrMismatchedResetEpoch()
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, resetEpoch: 2);
        var before = await DumpDatabaseAsync();

        var staleIntent = CreateIntent("sub-stale", resetEpoch: 1, boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
        var futureIntent = CreateIntent("sub-future", resetEpoch: 3, boundCriticalStrikeRank: 1, boundOverdriveRank: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(staleIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(futureIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staleIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(futureIntent));

        Assert.Equal(before, await DumpDatabaseAsync());
    }

    [Fact]
    public async Task StageAndApplyPendingIntent_RejectWhenResetIsPending_AndCompleteResetClearsModifierIntents()
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2);

        var preResetIntent = CreateIntent("sub-pre-reset", boundCriticalStrikeRank: 2, boundOverdriveRank: 1);
        await store.StagePendingIntentAsync(preResetIntent);

        var resetIntent = await store.BeginOrGetResetIntentAsync();
        Assert.True(resetIntent.IsPending);

        var whilePendingIntent = CreateIntent("sub-during-reset", boundCriticalStrikeRank: 1, boundOverdriveRank: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(whilePendingIntent));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(preResetIntent));
        Assert.Null(await store.GetPendingIntentAsync("sub-during-reset"));

        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        Assert.Null(await store.GetPendingIntentAsync("sub-pre-reset"));
        Assert.Empty(await store.GetPendingIntentsAsync(resetIntent.TargetEpoch));

        var snapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(0, snapshot.CriticalStrikeRank);
        Assert.Equal(0, snapshot.OverdriveRank);
        Assert.Equal(resetIntent.TargetEpoch, snapshot.ResetEpoch);
    }

    // =========================================================================
    // 7. Temporary combat safety, rank-zero parity, receipt replay, and learner isolation
    // =========================================================================

    [Fact]
    public async Task RankZeroPendingIntents_ExecuteExistingCombatAndCalmModeBehaviorUnchanged_EvenWhenModifiersAreOwned()
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, attackRank: 1);

        var correctRankZero = CreateIntent(
            submissionId: "sub-rank0-correct",
            isCorrect: true,
            isEligible: true,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(correctRankZero);

        var appliedReceipt = await store.ApplyAttemptTransactionAsync(correctRankZero);
        Assert.Equal(CyberDefenseReceiptKind.Applied, appliedReceipt.ReceiptKind);
        Assert.Equal(2, appliedReceipt.TransitionResult!.RequestedAttackDamage);
        Assert.Equal(2, appliedReceipt.TransitionResult.AppliedOpponentDamage);
        Assert.True(appliedReceipt.TransitionResult.IsOpponentDefeated);
        Assert.Null(await store.GetPendingIntentAsync("sub-rank0-correct"));

        // Receipt V7 modifier columns record rank-0 metadata on Applied receipts while run modifier counters remain 0.
        var modifierSnapshot = await store.GetCombatModifierSnapshotAsync();
        Assert.Equal(CyberDefenseCombatModifierState.Initial, modifierSnapshot.ModifierState);
        Assert.Equal(2, appliedReceipt.BaseAttackDamage);
        Assert.Equal(0, appliedReceipt.BoundCriticalStrikeRank);
        Assert.Equal(0, appliedReceipt.BoundOverdriveRank);
        Assert.Equal(0, appliedReceipt.CriticalBonusPercent);
        Assert.Equal(0, appliedReceipt.OverdriveBonusPercent);
        Assert.Equal(2L, await ScalarRawAsync("SELECT base_attack_damage FROM gameplay_receipt_ledger WHERE submission_id = 'sub-rank0-correct';"));

        var calmRankZero = CreateIntent(
            submissionId: "sub-rank0-calm",
            isCorrect: true,
            isEligible: false,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(calmRankZero);

        var calmReceipt = await store.ApplyAttemptTransactionAsync(calmRankZero);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, calmReceipt.ReceiptKind);
        Assert.Null(calmReceipt.TransitionResult);
        Assert.Null(calmReceipt.BaseAttackDamage);
        Assert.Null(await ScalarRawAsync("SELECT base_attack_damage FROM gameplay_receipt_ledger WHERE submission_id = 'sub-rank0-calm';"));
        Assert.Null(await store.GetPendingIntentAsync("sub-rank0-calm"));
    }

    public static IEnumerable<object[]> NonzeroModifierIntentCases()
    {
        (int Crit, int Od)[] ranks = [(1, 0), (2, 0), (0, 1), (0, 2), (1, 1), (2, 1), (1, 2), (2, 2)];
        foreach (var (crit, od) in ranks)
        {
            yield return [crit, od, true, true];
            yield return [crit, od, false, true];
            yield return [crit, od, true, false];
        }
    }

    [Theory]
    [MemberData(nameof(NonzeroModifierIntentCases))]
    public async Task NonzeroModifierIntents_FailClosedInApplyConsumeAndRecover_AndRemainDurableWithoutMutation(
        int boundCrit, int boundOd, bool isCorrect, bool isEligible)
    {
        string dbPath = Path.Combine(_directory, $"gameplay_{boundCrit}_{boundOd}_{isCorrect}_{isEligible}.db");
        string learnerPath = Path.Combine(_directory, $"learner_{boundCrit}_{boundOd}_{isCorrect}_{isEligible}.db");

        using var store = await OpenStoreAsync(dbPath);
        using var learnerStore = await OpenLearnerStoreAsync(learnerPath);
        await ExecuteRawAsync("""
            UPDATE gameplay_progression
            SET critical_strike_rank = 2, overdrive_rank = 2, attack_rank = 1
            WHERE id = 1;
            """, dbPath);

        var consumer = new CyberDefenseSubmissionConsumer(store, learnerStore);
        string submissionId = $"sub-nonzero-{boundCrit}-{boundOd}-{isCorrect}-{isEligible}";

        var intent = CreateIntent(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 450,
            resetEpoch: 0,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: boundCrit,
            boundOverdriveRank: boundOd);

        await consumer.StageIntentAsync(intent);

        var learnerChangeSet = CreateLearnerChangeSet(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: isCorrect,
            responseLatencyMs: 450,
            practicePosition: 1,
            expectedRevision: 1);
        var learnerResult = await learnerStore.CommitSubmissionAsync(learnerChangeSet);
        Assert.True(learnerResult.IsSuccess);

        var gameplayDumpBefore = await DumpDatabaseAsync(dbPath);
        var learnerDumpBefore = await DumpDatabaseAsync(learnerPath);

        // 1. Crafted rank-zero call for the same submission ID fails closed due to staged conflict.
        var craftedRankZero = CreateIntent(
            submissionId: submissionId,
            factId: "add:1+1",
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 450,
            resetEpoch: 0,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(craftedRankZero));

        // 2. Unstaged nonzero-rank intent or out-of-order attempt while head is staged also fails closed.
        var unstagedOther = CreateIntent(
            submissionId: submissionId + "-unstaged",
            factId: "add:1+1",
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 450,
            resetEpoch: 0,
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: boundCrit,
            boundOverdriveRank: boundOd);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(unstagedOther));

        // Staged intent remains durable with original ranks; no receipt written; gameplay and learner DBs untouched.
        var durableIntent = await store.GetPendingIntentAsync(submissionId);
        Assert.Equal(intent, durableIntent);
        Assert.Null(await store.GetReceiptAsync(submissionId));
        Assert.Equal(gameplayDumpBefore, await DumpDatabaseAsync(dbPath));
        Assert.Equal(learnerDumpBefore, await DumpDatabaseAsync(learnerPath));

        // 3. Valid staged nonzero-rank intent executes atomically via consumer and writes V7 receipt metadata.
        var consumed = await consumer.ConsumeAttemptAsync(submissionId);
        Assert.Equal(CyberDefenseConsumptionStatus.Success, consumed.Status);
        Assert.NotNull(consumed.Receipt);
        Assert.Null(await store.GetPendingIntentAsync(submissionId));

        if (isEligible)
        {
            Assert.Equal(CyberDefenseReceiptKind.Applied, consumed.Receipt.ReceiptKind);
            Assert.Equal(2, consumed.Receipt.BaseAttackDamage);
            Assert.Equal(boundCrit, consumed.Receipt.BoundCriticalStrikeRank);
            Assert.Equal(boundOd, consumed.Receipt.BoundOverdriveRank);
            Assert.Equal(0, consumed.Receipt.CriticalBonusPercent);
            Assert.Equal(0, consumed.Receipt.OverdriveBonusPercent);
        }
        else
        {
            Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, consumed.Receipt.ReceiptKind);
            Assert.Null(consumed.Receipt.BaseAttackDamage);
            Assert.Null(consumed.Receipt.BoundCriticalStrikeRank);
            Assert.Null(consumed.Receipt.BoundOverdriveRank);
            Assert.Null(consumed.Receipt.CriticalBonusPercent);
            Assert.Null(consumed.Receipt.OverdriveBonusPercent);
        }

        // 4. Replay is idempotent and learner snapshot/FSRS state remain intact.
        var replayed = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(consumed.Receipt, replayed);
        Assert.Equal(learnerDumpBefore, await DumpDatabaseAsync(learnerPath));

        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(1, learnerSnapshot.Progression.PracticePosition);
        Assert.True(learnerSnapshot.FsrsStates.ContainsKey("add:1+1"));
        Assert.Equal(11, learnerSnapshot.FsrsStates["add:1+1"].DuePracticePosition);
    }

    [Fact]
    public async Task ExistingReceiptReplay_RemainsIdempotentForMatchingIntent_AndRejectsConflictingModifierRanks()
    {
        using var store = await OpenStoreAsync();
        await SeedOwnedRanksAsync(criticalRank: 2, overdriveRank: 2, attackRank: 1);

        var intent = CreateIntent(
            submissionId: "sub-replay",
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0);
        await store.StagePendingIntentAsync(intent);

        var firstReceipt = await store.ApplyAttemptTransactionAsync(intent);
        long revisionAfterFirst = await store.GetStoreRevisionAsync();
        var runAfterFirst = await store.GetRunStateAsync();

        // Replaying the same rank-0 intent returns the existing receipt without mutating run state or revision.
        var replayedReceipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(firstReceipt, replayedReceipt);
        Assert.Equal(revisionAfterFirst, await store.GetStoreRevisionAsync());
        Assert.Equal(runAfterFirst, await store.GetRunStateAsync());

        // Replaying with conflicting nonzero bound ranks fails closed.
        var conflictingRankReplay = CreateIntent(
            submissionId: "sub-replay",
            effectiveAttackDamage: 2,
            boundCriticalStrikeRank: 1,
            boundOverdriveRank: 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictingRankReplay));
    }
}
