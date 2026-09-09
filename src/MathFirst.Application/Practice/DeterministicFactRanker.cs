namespace MathFirst.Application.Practice;

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;

public enum FactSelectionRole
{
    New,
    Due,
    Maintenance,
    Frontier,
    Any
}

public static class DeterministicFactRanker
{
    private const string SelectionDomain = "MathFirst.Selection.v1";
    private const string StructuredSampleDomain = "MathFirst.StructuredSample.v1";

    public static IReadOnlyList<ArithmeticFact> Order(
        IEnumerable<ArithmeticFact> facts,
        ArithmeticOperation operation,
        CurriculumBandId? bandId,
        FactSelectionRole role,
        long practicePosition)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (practicePosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(practicePosition),
                practicePosition,
                "Practice position must be non-negative.");
        }

        return Rank(
            facts,
            operation,
            fact => ComputeSelectionDigest(operation, bandId, role, practicePosition, fact.Id));
    }

    public static IReadOnlyList<ArithmeticFact> SelectStructuredSample(
        IEnumerable<ArithmeticFact> facts,
        ArithmeticOperation operation,
        CurriculumBandId bandId)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(bandId);

        var distinctFacts = new Dictionary<string, ArithmeticFact>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            ValidateOperation(fact, operation);
            distinctFacts.TryAdd(fact.Id, fact);
        }

        return Rank(
                distinctFacts.Values,
                operation,
                fact => ComputeStructuredSampleDigest(operation, bandId, fact.Id))
            .Take(Math.Min(16, distinctFacts.Count))
            .ToArray();
    }

    public static byte[] ComputeSelectionDigest(
        ArithmeticOperation operation,
        CurriculumBandId? bandId,
        FactSelectionRole role,
        long practicePosition,
        string factId)
    {
        if (practicePosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(practicePosition),
                practicePosition,
                "Practice position must be non-negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        var input = string.Join(
            '\0',
            SelectionDomain,
            GetOperationToken(operation),
            bandId?.Value ?? "-",
            GetRoleToken(role),
            practicePosition.ToString(CultureInfo.InvariantCulture),
            factId);
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    public static byte[] ComputeStructuredSampleDigest(
        ArithmeticOperation operation,
        CurriculumBandId bandId,
        string factId)
    {
        ArgumentNullException.ThrowIfNull(bandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        var input = string.Join(
            '\0',
            StructuredSampleDomain,
            GetOperationToken(operation),
            bandId.Value,
            factId);
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    public static int CompareRankKeys(
        ReadOnlySpan<byte> leftDigest,
        string leftFactId,
        ReadOnlySpan<byte> rightDigest,
        string rightFactId)
    {
        if (leftDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A rank digest must contain exactly 32 bytes.", nameof(leftDigest));
        }

        if (rightDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A rank digest must contain exactly 32 bytes.", nameof(rightDigest));
        }

        ArgumentNullException.ThrowIfNull(leftFactId);
        ArgumentNullException.ThrowIfNull(rightFactId);

        var primaryComparison = BinaryPrimitives.ReadUInt64BigEndian(leftDigest)
            .CompareTo(BinaryPrimitives.ReadUInt64BigEndian(rightDigest));
        if (primaryComparison != 0)
        {
            return primaryComparison;
        }

        for (var index = sizeof(ulong); index < SHA256.HashSizeInBytes; index++)
        {
            var byteComparison = leftDigest[index].CompareTo(rightDigest[index]);
            if (byteComparison != 0)
            {
                return byteComparison;
            }
        }

        return StringComparer.Ordinal.Compare(leftFactId, rightFactId);
    }

    private static IReadOnlyList<ArithmeticFact> Rank(
        IEnumerable<ArithmeticFact> facts,
        ArithmeticOperation operation,
        Func<ArithmeticFact, byte[]> digestFactory)
    {
        var ranked = facts.Select(fact =>
        {
            ValidateOperation(fact, operation);
            return new RankedFact(fact, digestFactory(fact));
        }).ToArray();

        Array.Sort(ranked, (left, right) => CompareRankKeys(
            left.Digest,
            left.Fact.Id,
            right.Digest,
            right.Fact.Id));
        return ranked.Select(item => item.Fact).ToArray();
    }

    private static void ValidateOperation(ArithmeticFact fact, ArithmeticOperation operation)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Operation != operation)
        {
            throw new ArgumentException("Every ranked fact must match the requested operation.", nameof(fact));
        }
    }

    private static string GetOperationToken(ArithmeticOperation operation) => operation switch
    {
        ArithmeticOperation.Addition => "addition",
        ArithmeticOperation.Subtraction => "subtraction",
        ArithmeticOperation.Multiplication => "multiplication",
        ArithmeticOperation.Division => "division",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
    };

    private static string GetRoleToken(FactSelectionRole role) => role switch
    {
        FactSelectionRole.New => "new",
        FactSelectionRole.Due => "due",
        FactSelectionRole.Maintenance => "maintenance",
        FactSelectionRole.Frontier => "frontier",
        FactSelectionRole.Any => "any",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown selection role.")
    };

    private sealed record RankedFact(ArithmeticFact Fact, byte[] Digest);
}
