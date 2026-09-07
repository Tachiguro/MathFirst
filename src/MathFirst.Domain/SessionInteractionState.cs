namespace MathFirst.Domain;

public enum SessionInteractionState
{
    AwaitingAnswer,
    CorrectFeedback,
    IncorrectFeedback,
    TimeoutFeedback
}
