namespace MathFirst.Domain;

public sealed record ArithmeticFact
{
    public string Id { get; }
    public ArithmeticOperation Operation { get; }
    public int LeftOperand { get; }
    public int RightOperand { get; }
    public int CorrectResult { get; }
    public string DisplaySymbol { get; }
    public string ExpressionText { get; }
    public string EquationText => $"{ExpressionText} = {CorrectResult}";

    public ArithmeticFact(ArithmeticOperation operation, int leftOperand, int rightOperand)
    {
        Operation = operation;
        LeftOperand = leftOperand;
        RightOperand = rightOperand;

        (Id, DisplaySymbol, CorrectResult) = operation switch
        {
            ArithmeticOperation.Addition => (
                $"add:{RequireNonNegative(leftOperand, nameof(leftOperand))}+{RequireNonNegative(rightOperand, nameof(rightOperand))}",
                "+",
                checked(leftOperand + rightOperand)
            ),
            ArithmeticOperation.Subtraction => (
                $"sub:{RequireNonNegative(leftOperand, nameof(leftOperand))}-{RequireNonNegative(rightOperand, nameof(rightOperand))}",
                "\u2212",
                leftOperand < rightOperand
                    ? throw new ArgumentException("Subtraction facts must have a non-negative result.", nameof(rightOperand))
                    : checked(leftOperand - rightOperand)
            ),
            ArithmeticOperation.Multiplication => (
                $"mul:{RequireNonNegative(leftOperand, nameof(leftOperand))}*{RequireNonNegative(rightOperand, nameof(rightOperand))}",
                "\u00D7",
                checked(leftOperand * rightOperand)
            ),
            ArithmeticOperation.Division => (
                $"div:{RequireNonNegative(leftOperand, nameof(leftOperand))}/{RequirePositive(rightOperand, nameof(rightOperand))}",
                "\u00F7",
                leftOperand % rightOperand != 0
                    ? throw new ArgumentException("Division facts must have an exact integer result.", nameof(leftOperand))
                    : checked(leftOperand / rightOperand)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
        };

        ExpressionText = $"{leftOperand} {DisplaySymbol} {rightOperand}";
    }

    public static ArithmeticFact Create(ArithmeticOperation operation, int leftOperand, int rightOperand) =>
        new(operation, leftOperand, rightOperand);

    public override string ToString() => ExpressionText;

    private static int RequireNonNegative(int value, string parameterName) =>
        value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Arithmetic fact operands must be non-negative.");

    private static int RequirePositive(int value, string parameterName) =>
        value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Division facts require a positive divisor.");
}
