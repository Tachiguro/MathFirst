namespace MathFirst.Domain.Curriculum;

public static class CurriculumGenerators
{
    private const int AdditionDenseBandCount = 10;
    private const int SubtractionDenseBandCount = 20;
    private const int MultiplicationDenseBandCount = 12;
    private const int DivisionDenseBandCount = 12;

    public static CurriculumBand? TryCreateAdditionBand(int bandIndex) =>
        TryCreateAdditiveBand(ArithmeticOperation.Addition, AdditionDenseBandCount, "ADD", bandIndex);

    public static CurriculumBand? TryCreateSubtractionBand(int bandIndex) =>
        TryCreateAdditiveBand(ArithmeticOperation.Subtraction, SubtractionDenseBandCount, "SUB", bandIndex);

    public static CurriculumBand? TryCreateMultiplicationBand(int bandIndex) =>
        TryCreateMultiplicativeBand(ArithmeticOperation.Multiplication, MultiplicationDenseBandCount, "MUL", bandIndex);

    public static CurriculumBand? TryCreateDivisionBand(int bandIndex) =>
        TryCreateMultiplicativeBand(ArithmeticOperation.Division, DivisionDenseBandCount, "DIV", bandIndex);

    private static CurriculumBand? TryCreateAdditiveBand(
        ArithmeticOperation operation,
        int denseBandCount,
        string idPrefix,
        int bandIndex)
    {
        if (bandIndex < denseBandCount || !TryResolveAdditiveDescriptor(bandIndex - denseBandCount, out var descriptor))
        {
            return null;
        }

        try
        {
            var additionFacts = CreateAdditionFacts(descriptor);
            var facts = operation == ArithmeticOperation.Addition
                ? additionFacts
                : CreateSubtractionInverses(additionFacts);
            var id = descriptor.Family == AdditiveFamily.Anchor
                ? FormattableString.Invariant($"{idPrefix}-P{descriptor.Magnitude}-ANCHOR")
                : FormattableString.Invariant(
                    $"{idPrefix}-P{descriptor.Magnitude}-{descriptor.Family}{descriptor.LowerPlace}");

            return CreateBand(operation, bandIndex, id, facts);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static CurriculumBand? TryCreateMultiplicativeBand(
        ArithmeticOperation operation,
        int denseBandCount,
        string idPrefix,
        int bandIndex)
    {
        if (bandIndex < denseBandCount)
        {
            return null;
        }

        try
        {
            MultiplicativeDescriptor descriptor;
            if (bandIndex == denseBandCount)
            {
                descriptor = new MultiplicativeDescriptor(0, MultiplicativeFamily.TwoDigit);
            }
            else
            {
                var offset = bandIndex - denseBandCount - 1;
                descriptor = new MultiplicativeDescriptor(
                    checked((offset / 3) + 1),
                    (MultiplicativeFamily)((offset % 3) + 1));
            }

            var multiplicationFacts = CreateMultiplicationFacts(descriptor);
            var facts = operation == ArithmeticOperation.Multiplication
                ? multiplicationFacts
                : CreateDivisionInverses(multiplicationFacts);
            var id = descriptor.Family == MultiplicativeFamily.TwoDigit
                ? $"{idPrefix}-2D"
                : FormattableString.Invariant(
                    $"{idPrefix}-P{descriptor.Magnitude}-{descriptor.Family.ToString().ToUpperInvariant()}");

            return CreateBand(operation, bandIndex, id, facts);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool TryResolveAdditiveDescriptor(int offset, out AdditiveDescriptor descriptor)
    {
        descriptor = default;
        if (offset < 0)
        {
            return false;
        }

        for (var magnitude = 1; TryPowerOfTen(magnitude, out _); magnitude++)
        {
            var bandCount = checked(1 + (3 * magnitude));
            if (offset >= bandCount)
            {
                offset -= bandCount;
                continue;
            }

            if (offset == 0)
            {
                descriptor = new AdditiveDescriptor(magnitude, magnitude - 1, AdditiveFamily.Anchor);
                return true;
            }

            var familyOffset = offset - 1;
            descriptor = new AdditiveDescriptor(
                magnitude,
                magnitude - 1 - (familyOffset / 3),
                (AdditiveFamily)((familyOffset % 3) + 1));
            return true;
        }

        return false;
    }

    private static IReadOnlyList<ArithmeticFact> CreateAdditionFacts(AdditiveDescriptor descriptor)
    {
        var power = PowerOfTen(descriptor.Magnitude);
        var lowerPower = PowerOfTen(descriptor.LowerPlace);
        var facts = new List<ArithmeticFact>();

        checked
        {
            switch (descriptor.Family)
            {
                case AdditiveFamily.Anchor:
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = u; v <= 9; v++)
                        {
                            AddBothDirections(facts, ArithmeticOperation.Addition, u * power, v * power);
                        }
                    }

                    break;

                case AdditiveFamily.T:
                case AdditiveFamily.R:
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = 1; v <= 9; v++)
                        {
                            var c = 1 + ((3 * u + 5 * v + descriptor.Magnitude + descriptor.LowerPlace) % 9);
                            var left = c * power + u * lowerPower;
                            var right = descriptor.Family == AdditiveFamily.T
                                ? v * lowerPower
                                : v * power;
                            AddBothDirections(facts, ArithmeticOperation.Addition, left, right);
                        }
                    }

                    break;

                case AdditiveFamily.D:
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = u; v <= 9; v++)
                        {
                            var a = 1 + ((2 * u + 3 * v + descriptor.Magnitude + descriptor.LowerPlace) % 9);
                            var b0 = 1 + ((5 * u + 7 * v + descriptor.Magnitude + descriptor.LowerPlace) % 9);
                            var b = u == v && a == b0 ? 1 + (b0 % 9) : b0;
                            var left = a * power + u * lowerPower;
                            var right = b * power + v * lowerPower;
                            AddBothDirections(facts, ArithmeticOperation.Addition, left, right);
                        }
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor));
            }
        }

        return facts;
    }

    private static IReadOnlyList<ArithmeticFact> CreateSubtractionInverses(IEnumerable<ArithmeticFact> additions)
    {
        var facts = new List<ArithmeticFact>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addition in additions)
        {
            var sum = checked(addition.LeftOperand + addition.RightOperand);
            AddDistinct(facts, seen, new ArithmeticFact(ArithmeticOperation.Subtraction, sum, addition.LeftOperand));
            AddDistinct(facts, seen, new ArithmeticFact(ArithmeticOperation.Subtraction, sum, addition.RightOperand));
        }

        return facts;
    }

    private static IReadOnlyList<ArithmeticFact> CreateMultiplicationFacts(MultiplicativeDescriptor descriptor)
    {
        var facts = new List<ArithmeticFact>();
        checked
        {
            switch (descriptor.Family)
            {
                case MultiplicativeFamily.TwoDigit:
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = 2; v <= 9; v++)
                        {
                            var t = 2 + ((3 * u + 5 * v) % 8);
                            AddBothDirections(facts, ArithmeticOperation.Multiplication, 10 * t + u, v);
                        }
                    }

                    break;

                case MultiplicativeFamily.Round:
                {
                    var power = PowerOfTen(descriptor.Magnitude);
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = 2; v <= 9; v++)
                        {
                            AddBothDirections(facts, ArithmeticOperation.Multiplication, u * power, v);
                        }
                    }

                    break;
                }

                case MultiplicativeFamily.Shift:
                {
                    var power = PowerOfTen(descriptor.Magnitude);
                    var first = descriptor.Magnitude == 1 ? 13 : 1;
                    for (var n = first; n <= 99; n++)
                    {
                        AddBothDirections(facts, ArithmeticOperation.Multiplication, n, power);
                    }

                    break;
                }

                case MultiplicativeFamily.Scaled:
                {
                    var power = PowerOfTen(descriptor.Magnitude);
                    for (var u = 1; u <= 9; u++)
                    {
                        for (var v = 2; v <= 9; v++)
                        {
                            var t = 2 + ((3 * u + 5 * v) % 8);
                            var significand = 10 * t + u;
                            AddBothDirections(facts, ArithmeticOperation.Multiplication, significand * power, v);
                        }
                    }

                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor));
            }
        }

        return facts;
    }

    private static IReadOnlyList<ArithmeticFact> CreateDivisionInverses(IEnumerable<ArithmeticFact> multiplications)
    {
        var facts = new List<ArithmeticFact>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var multiplication in multiplications)
        {
            var product = checked(multiplication.LeftOperand * multiplication.RightOperand);
            AddDistinct(facts, seen, new ArithmeticFact(ArithmeticOperation.Division, product, multiplication.LeftOperand));
            AddDistinct(facts, seen, new ArithmeticFact(ArithmeticOperation.Division, product, multiplication.RightOperand));
        }

        return facts;
    }

    private static void AddBothDirections(
        ICollection<ArithmeticFact> facts,
        ArithmeticOperation operation,
        int left,
        int right)
    {
        facts.Add(new ArithmeticFact(operation, left, right));
        if (left != right)
        {
            facts.Add(new ArithmeticFact(operation, right, left));
        }
    }

    private static void AddDistinct(
        ICollection<ArithmeticFact> facts,
        ISet<string> seen,
        ArithmeticFact fact)
    {
        if (seen.Add(fact.Id))
        {
            facts.Add(fact);
        }
    }

    private static CurriculumBand CreateBand(
        ArithmeticOperation operation,
        int bandIndex,
        string id,
        IReadOnlyList<ArithmeticFact> facts) =>
        new(operation, bandIndex, new CurriculumBandId(id), CurriculumBandKind.Structured, facts);

    private static int PowerOfTen(int exponent)
    {
        if (!TryPowerOfTen(exponent, out var value))
        {
            throw new OverflowException();
        }

        return value;
    }

    private static bool TryPowerOfTen(int exponent, out int value)
    {
        value = 1;
        if (exponent < 0)
        {
            return false;
        }

        try
        {
            for (var index = 0; index < exponent; index++)
            {
                value = checked(value * 10);
            }

            return true;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }

    private enum AdditiveFamily
    {
        Anchor = 0,
        T = 1,
        R = 2,
        D = 3
    }

    private enum MultiplicativeFamily
    {
        TwoDigit = 0,
        Round = 1,
        Shift = 2,
        Scaled = 3
    }

    private readonly record struct AdditiveDescriptor(int Magnitude, int LowerPlace, AdditiveFamily Family);

    private readonly record struct MultiplicativeDescriptor(int Magnitude, MultiplicativeFamily Family);
}
