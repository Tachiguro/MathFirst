namespace MathFirst.Core.Tests;

using System;
using System.Threading.Tasks;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Microsoft.Data.Sqlite;

/// <summary>
/// Synthetic ADR-0013 prerequisite evidence for temporary test databases: every fact owned by an operation's
/// D01..D10 Dense bands is written as attempted once and answered correctly. Combine with
/// <see cref="CurriculumUnlockPolicy.PrerequisiteMasteryBandCount"/> as the operation's BandIndex so that the
/// next accepted submission satisfies the D10 unlock predicate.
/// </summary>
internal static class D10MasteryTestSeed
{
    public const int MasteredBandIndex = CurriculumUnlockPolicy.PrerequisiteMasteryBandCount;

    /// <summary>Writes mastered item states for the D10 mastery set. The store schema must already exist.</summary>
    public static async Task SeedMasteredAsync(string databasePath, ArithmeticOperation operation, string? exceptFactId = null)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        foreach (var factId in CurriculumUnlockPolicy.GetMasteryFactIds(operation))
        {
            if (string.Equals(factId, exceptFactId, StringComparison.Ordinal))
            {
                continue;
            }

            var fact = ParseFact(operation, factId);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT OR REPLACE INTO item_learning_state (
                    fact_id, operation, left_operand, right_operand,
                    total_attempts, correct_attempts, incorrect_attempts,
                    consecutive_correct, last_latency_ms, rolling_latency_ms,
                    fluent_streak, is_mastered, needs_remediation,
                    remediation_due_order, last_practiced_order, last_practiced_at
                ) VALUES (
                    @fact_id, @operation, @left, @right,
                    1, 1, 0,
                    1, 1500, 1500,
                    0, 0, 0,
                    0, 0, NULL
                );";
            command.Parameters.AddWithValue("@fact_id", fact.Id);
            command.Parameters.AddWithValue("@operation", operation.ToString());
            command.Parameters.AddWithValue("@left", fact.LeftOperand);
            command.Parameters.AddWithValue("@right", fact.RightOperand);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static ArithmeticFact ParseFact(ArithmeticOperation operation, string factId)
    {
        var operands = factId[(factId.IndexOf(':') + 1)..].Split(['+', '-', '*', '/']);
        return new ArithmeticFact(operation, int.Parse(operands[0]), int.Parse(operands[1]));
    }
}
