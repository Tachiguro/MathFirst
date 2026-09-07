namespace MathFirst.Domain;

public sealed record AdditionFact
{
    public int LeftOperand { get; }
    public int RightOperand { get; }
    public int CorrectResult => LeftOperand + RightOperand;

    public AdditionFact(int leftOperand, int rightOperand)
    {
        LeftOperand = leftOperand;
        RightOperand = rightOperand;
    }

    public bool IsCorrect(int answer) => answer == CorrectResult;

    public override string ToString() => $"{LeftOperand} + {RightOperand} = {CorrectResult}";
}
