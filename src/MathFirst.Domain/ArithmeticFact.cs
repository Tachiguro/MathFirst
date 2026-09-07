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

    public ArithmeticFact(ArithmeticOperation operation, int leftOperand, int rightOperand)
    {
        Operation = operation;
        LeftOperand = leftOperand;
        RightOperand = rightOperand;

        (Id, DisplaySymbol, CorrectResult) = operation switch
        {
            ArithmeticOperation.Addition => (
                $"add:{leftOperand}+{rightOperand}",
                "+",
                leftOperand + rightOperand
            ),
            ArithmeticOperation.Subtraction => (
                $"sub:{leftOperand}-{rightOperand}",
                "\u2212",
                leftOperand - rightOperand
            ),
            ArithmeticOperation.Multiplication => (
                $"mul:{leftOperand}*{rightOperand}",
                "\u00D7",
                leftOperand * rightOperand
            ),
            ArithmeticOperation.Division => (
                $"div:{leftOperand}/{rightOperand}",
                "\u00F7",
                rightOperand == 0 ? throw new DivideByZeroException("Division by zero is not permitted in arithmetic facts.") : leftOperand / rightOperand
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.")
        };

        ExpressionText = $"{leftOperand} {DisplaySymbol} {rightOperand}";
    }

    public static ArithmeticFact Create(ArithmeticOperation operation, int leftOperand, int rightOperand) =>
        new(operation, leftOperand, rightOperand);

    public override string ToString() => ExpressionText;
}
