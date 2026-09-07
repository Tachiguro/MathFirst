namespace MathFirst.Application;

using MathFirst.Domain;

public sealed class AdditionTrainingSession
{
    private readonly AdditionPracticeSequence _sequence;

    public AdditionTrainingSession(AdditionPracticeSequence? sequence = null)
    {
        _sequence = sequence ?? new AdditionPracticeSequence();
        CurrentFact = _sequence.GetNextFact();
    }

    public AdditionFact CurrentFact { get; private set; }
    public int AttemptCount { get; private set; }
    public int CorrectCount { get; private set; }
    public SubmissionResult? LastSubmission { get; private set; }
    public bool IsAwaitingNext => LastSubmission is not null;

    public SubmissionResult SubmitAnswer(int answer)
    {
        var isCorrect = CurrentFact.IsCorrect(answer);
        AttemptCount++;
        if (isCorrect)
        {
            CorrectCount++;
        }
        else
        {
            _sequence.ScheduleRecurrence(CurrentFact);
        }

        var result = new SubmissionResult(isCorrect, answer, CurrentFact.CorrectResult);
        LastSubmission = result;
        return result;
    }

    public bool TrySubmitAnswer(string? rawInput, out SubmissionResult? result)
    {
        if (int.TryParse(rawInput?.Trim(), out var parsedAnswer))
        {
            result = SubmitAnswer(parsedAnswer);
            return true;
        }

        result = null;
        return false;
    }

    public void Advance()
    {
        LastSubmission = null;
        CurrentFact = _sequence.GetNextFact();
    }
}
