namespace MathFirst.Domain;

public enum SessionInteractionState
{
    AwaitingAnswer,
    CorrectFeedback,
    PersistenceFailure,
    IncorrectFeedback,
    TimeoutFeedback,
    TeachingIntervention,
    SessionCheckIn
}
