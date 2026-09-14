namespace MathFirst.Application.Practice;

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MathFirst.Domain;

public static class DeterministicOperationScheduler
{
    private const string ScheduleDomain = "MathFirst.OperationSchedule.v1";

    public static ArithmeticOperation GetScheduledOperation(
        long prospectivePracticePosition,
        IReadOnlyList<ArithmeticOperation>? enabledOperations = null)
    {
        if (prospectivePracticePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prospectivePracticePosition),
                prospectivePracticePosition,
                "Practice position must be positive.");
        }

        var enabled = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(enabledOperations);
        var k = enabled.Count;
        if (k == 1)
        {
            return enabled[0];
        }

        var bagIndex = checked((prospectivePracticePosition - 1) / k);
        var positionInBag = checked((int)((prospectivePracticePosition - 1) % k));

        var orderedBag = OrderBag(enabled, bagIndex);
        return orderedBag[positionInBag];
    }

    public static IReadOnlyList<ArithmeticOperation> OrderBag(
        IReadOnlyList<ArithmeticOperation> enabledOperations,
        long bagIndex)
    {
        ArgumentNullException.ThrowIfNull(enabledOperations);
        if (bagIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bagIndex),
                bagIndex,
                "Bag index must be non-negative.");
        }

        var normalized = PracticeOperationPreferencePolicy.NormalizeEnabledOperations(enabledOperations);
        if (normalized.Count <= 1)
        {
            return normalized;
        }

        var signature = string.Join(',', normalized.Select(GetOperationToken));

        var ranked = normalized.Select(op => new RankedOperation(
            op,
            ComputeScheduleDigest(op, bagIndex, signature)
        )).ToArray();

        Array.Sort(ranked, (left, right) => CompareRankKeys(
            left.Digest,
            left.Operation,
            right.Digest,
            right.Operation));

        return ranked.Select(r => r.Operation).ToArray();
    }

    public static byte[] ComputeScheduleDigest(
        ArithmeticOperation operation,
        long bagIndex,
        string enabledSignature)
    {
        if (bagIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bagIndex),
                bagIndex,
                "Bag index must be non-negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(enabledSignature);

        var input = string.Join(
            '\0',
            ScheduleDomain,
            enabledSignature,
            bagIndex.ToString(CultureInfo.InvariantCulture),
            GetOperationToken(operation));

        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    public static int CompareRankKeys(
        ReadOnlySpan<byte> leftDigest,
        ArithmeticOperation leftOp,
        ReadOnlySpan<byte> rightDigest,
        ArithmeticOperation rightOp)
    {
        if (leftDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A rank digest must contain exactly 32 bytes.", nameof(leftDigest));
        }

        if (rightDigest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A rank digest must contain exactly 32 bytes.", nameof(rightDigest));
        }

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

        return leftOp.CompareTo(rightOp);
    }

    private static string GetOperationToken(ArithmeticOperation operation) => operation switch
    {
        ArithmeticOperation.Addition => "addition",
        ArithmeticOperation.Subtraction => "subtraction",
        ArithmeticOperation.Multiplication => "multiplication",
        ArithmeticOperation.Division => "division",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
    };

    private sealed record RankedOperation(ArithmeticOperation Operation, byte[] Digest);
}
