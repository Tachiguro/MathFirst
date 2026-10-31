namespace MathFirst.Domain.Curriculum;

using System.Globalization;

/// <summary>
/// Issue #126 incremental Addition curriculum. Band <c>B</c> has the maximum operand <c>N = B + 1</c> and owns exactly
/// the ordered pairs <c>a + b</c> with <c>max(a, b) = N</c> (<c>2N + 1</c> facts); band 0 owns the whole <c>0..1</c>
/// square. The cumulative eligible domain through band <c>B</c> is therefore every ordered pair with
/// <c>0 &lt;= a, b &lt;= B + 1</c>, and the operand set grows one integer at a time without any magnitude jump.
/// </summary>
public sealed class IncrementalAdditionCurriculum : IAnalyticCurriculumModel
{
    private const string FactIdPrefix = "add:";

    /// <summary>
    /// Explicit resource-safety bound, not a learning endpoint: the cumulative ordered-pair domain <c>(N + 1)^2</c>
    /// stays representable as an <see cref="int"/> count (46,340^2 = 2,147,395,600). No complete band exists beyond it,
    /// so the operation remains in review and maintenance exactly as for any other numerically unsafe next band.
    /// </summary>
    public const int MaximumSupportedOperand = 46_339;

    public const int MaximumBandIndex = MaximumSupportedOperand - 1;

    /// <summary>The frontier size of the last supported band, <c>2 * MaximumSupportedOperand + 1</c>.</summary>
    public const int MaximumFrontierSize = (2 * MaximumSupportedOperand) + 1;

    public static IncrementalAdditionCurriculum Instance { get; } = new();

    private IncrementalAdditionCurriculum()
    {
    }

    public static int GetMaximumOperand(int bandIndex)
    {
        ValidateBandIndex(bandIndex);
        return bandIndex + 1;
    }

    public static int GetFrontierSize(int bandIndex)
    {
        ValidateBandIndex(bandIndex);
        return bandIndex == 0 ? 4 : (2 * (bandIndex + 1)) + 1;
    }

    public static CurriculumBandId CreateBandId(int bandIndex) =>
        new(FormattableString.Invariant($"ADD-D{GetMaximumOperand(bandIndex):00}"));

    /// <summary>
    /// Generates one complete Dense band in O(frontier) time and memory. Returns null outside the supported range.
    /// </summary>
    public static CurriculumBand? TryCreateBand(int bandIndex)
    {
        if (bandIndex < 0 || bandIndex > MaximumBandIndex)
        {
            return null;
        }

        return new CurriculumBand(
            ArithmeticOperation.Addition,
            bandIndex,
            CreateBandId(bandIndex),
            CurriculumBandKind.Dense,
            CreateFrontier(bandIndex));
    }

    /// <summary>
    /// The canonical frontier order of the original square-frontier generator: ascending left operand, then ascending
    /// right operand, i.e. <c>0+N, 1+N, ..., (N-1)+N, N+0, N+1, ..., N+N</c>.
    /// </summary>
    public static IReadOnlyList<ArithmeticFact> CreateFrontier(int bandIndex)
    {
        var maximumOperand = GetMaximumOperand(bandIndex);
        var frontier = new ArithmeticFact[GetFrontierSize(bandIndex)];
        var next = 0;
        if (bandIndex == 0)
        {
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, 0, 0);
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, 1, 0);
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
            return Array.AsReadOnly(frontier);
        }

        for (var left = 0; left < maximumOperand; left++)
        {
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, left, maximumOperand);
        }

        for (var right = 0; right <= maximumOperand; right++)
        {
            frontier[next++] = new ArithmeticFact(ArithmeticOperation.Addition, maximumOperand, right);
        }

        return Array.AsReadOnly(frontier);
    }

    public static bool TryGetOwnerBandIndex(int leftOperand, int rightOperand, out int ownerBandIndex)
    {
        ownerBandIndex = -1;
        if (leftOperand < 0
            || rightOperand < 0
            || leftOperand > MaximumSupportedOperand
            || rightOperand > MaximumSupportedOperand)
        {
            return false;
        }

        ownerBandIndex = Math.Max(1, Math.Max(leftOperand, rightOperand)) - 1;
        return true;
    }

    public bool TryGetOwnerBandIndex(string factId, out int ownerBandIndex)
    {
        ownerBandIndex = -1;
        if (factId is null || !factId.StartsWith(FactIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var operands = factId.AsSpan(FactIdPrefix.Length);
        var separator = operands.IndexOf('+');
        if (separator < 0
            || !TryParseCanonicalOperand(operands[..separator], out var left)
            || !TryParseCanonicalOperand(operands[(separator + 1)..], out var right))
        {
            return false;
        }

        return TryGetOwnerBandIndex(left, right, out ownerBandIndex);
    }

    public int GetNumberSpaceCeiling(int throughBandIndex) => 2 * GetMaximumOperand(throughBandIndex);

    private static bool TryParseCanonicalOperand(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.IsEmpty || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static void ValidateBandIndex(int bandIndex)
    {
        if (bandIndex < 0 || bandIndex > MaximumBandIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandIndex),
                bandIndex,
                "The Addition band index is outside the supported incremental curriculum range.");
        }
    }
}
