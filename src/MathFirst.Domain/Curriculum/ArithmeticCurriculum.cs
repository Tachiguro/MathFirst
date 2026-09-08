namespace MathFirst.Domain.Curriculum;

public sealed class ArithmeticCurriculum
{
    public OperationCurriculum Addition { get; }
    public OperationCurriculum Subtraction { get; }
    public OperationCurriculum Multiplication { get; }
    public OperationCurriculum Division { get; }

    public ArithmeticCurriculum()
    {
        Addition = new OperationCurriculum(
            ArithmeticOperation.Addition,
            CreateSquareFrontiers(ArithmeticOperation.Addition, "ADD", 10));
        Subtraction = new OperationCurriculum(
            ArithmeticOperation.Subtraction,
            CreateSubtractionFrontiers());
        Multiplication = new OperationCurriculum(
            ArithmeticOperation.Multiplication,
            CreateSquareFrontiers(ArithmeticOperation.Multiplication, "MUL", 12));
        Division = new OperationCurriculum(
            ArithmeticOperation.Division,
            CreateDivisionFrontiers());
    }

    public OperationCurriculum GetCurriculum(ArithmeticOperation operation) => operation switch
    {
        ArithmeticOperation.Addition => Addition,
        ArithmeticOperation.Subtraction => Subtraction,
        ArithmeticOperation.Multiplication => Multiplication,
        ArithmeticOperation.Division => Division,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
    };

    private static IReadOnlyList<CurriculumBand> CreateSquareFrontiers(
        ArithmeticOperation operation,
        string idPrefix,
        int maximumLevel)
    {
        var bands = new List<CurriculumBand>(maximumLevel);
        for (var level = 1; level <= maximumLevel; level++)
        {
            var frontier = new List<ArithmeticFact>();
            for (var left = 0; left <= level; left++)
            {
                for (var right = 0; right <= level; right++)
                {
                    if (level == 1 || left == level || right == level)
                    {
                        frontier.Add(new ArithmeticFact(operation, left, right));
                    }
                }
            }

            bands.Add(CreateDenseBand(
                operation,
                bands.Count,
                FormattableString.Invariant($"{idPrefix}-D{level:00}"),
                frontier));
        }

        return bands;
    }

    private static IReadOnlyList<CurriculumBand> CreateSubtractionFrontiers()
    {
        var bands = new List<CurriculumBand>(20);

        for (var level = 1; level <= 10; level++)
        {
            var frontier = new List<ArithmeticFact>();
            if (level == 1)
            {
                for (var left = 0; left <= 1; left++)
                {
                    for (var right = 0; right <= left; right++)
                    {
                        frontier.Add(new ArithmeticFact(ArithmeticOperation.Subtraction, left, right));
                    }
                }
            }
            else
            {
                for (var right = 0; right <= level; right++)
                {
                    frontier.Add(new ArithmeticFact(ArithmeticOperation.Subtraction, level, right));
                }
            }

            bands.Add(CreateDenseBand(
                ArithmeticOperation.Subtraction,
                bands.Count,
                FormattableString.Invariant($"SUB-D{level:00}"),
                frontier));
        }

        for (var minuend = 11; minuend <= 20; minuend++)
        {
            var frontier = new List<ArithmeticFact>();
            for (var subtrahend = minuend - 10; subtrahend <= 10; subtrahend++)
            {
                frontier.Add(new ArithmeticFact(ArithmeticOperation.Subtraction, minuend, subtrahend));
            }

            bands.Add(CreateDenseBand(
                ArithmeticOperation.Subtraction,
                bands.Count,
                FormattableString.Invariant($"SUB-I{minuend:00}"),
                frontier));
        }

        return bands;
    }

    private static IReadOnlyList<CurriculumBand> CreateDivisionFrontiers()
    {
        var bands = new List<CurriculumBand>(12);
        for (var level = 1; level <= 12; level++)
        {
            var frontier = new List<ArithmeticFact>();
            if (level == 1)
            {
                for (var quotient = 0; quotient <= 1; quotient++)
                {
                    frontier.Add(CreateDivisionFact(1, quotient));
                }
            }
            else
            {
                for (var divisor = 1; divisor < level; divisor++)
                {
                    frontier.Add(CreateDivisionFact(divisor, level));
                }

                for (var quotient = 0; quotient <= level; quotient++)
                {
                    frontier.Add(CreateDivisionFact(level, quotient));
                }
            }

            bands.Add(CreateDenseBand(
                ArithmeticOperation.Division,
                bands.Count,
                FormattableString.Invariant($"DIV-D{level:00}"),
                frontier));
        }

        return bands;
    }

    private static ArithmeticFact CreateDivisionFact(int divisor, int quotient) =>
        new(ArithmeticOperation.Division, checked(divisor * quotient), divisor);

    private static CurriculumBand CreateDenseBand(
        ArithmeticOperation operation,
        int bandIndex,
        string bandId,
        IReadOnlyList<ArithmeticFact> frontier) =>
        new(operation, bandIndex, new CurriculumBandId(bandId), CurriculumBandKind.Dense, frontier);
}
