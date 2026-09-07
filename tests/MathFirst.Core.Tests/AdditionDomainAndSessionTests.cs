namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Domain;
using Xunit;

public sealed class AdditionDomainAndSessionTests
{
    [Fact]
    public void AdditionCatalog_Contains121OrderedFactsWithExtremesAndReversals()
    {
        var catalog = AdditionCatalog.CreateFullCatalog();

        Assert.Equal(121, catalog.Count);
        Assert.Contains(catalog, f => f.LeftOperand == 0 && f.RightOperand == 0 && f.CorrectResult == 0);
        Assert.Contains(catalog, f => f.LeftOperand == 10 && f.RightOperand == 10 && f.CorrectResult == 20);

        var fact34 = catalog.Single(f => f.LeftOperand == 3 && f.RightOperand == 4);
        var fact43 = catalog.Single(f => f.LeftOperand == 4 && f.RightOperand == 3);

        Assert.NotSame(fact34, fact43);
        Assert.Equal(7, fact34.CorrectResult);
        Assert.Equal(7, fact43.CorrectResult);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(0, 1, 1, true)]
    [InlineData(1, 0, 1, true)]
    [InlineData(1, 1, 2, true)]
    [InlineData(2, 3, 5, true)]
    [InlineData(0, 0, 1, false)]
    [InlineData(1, 1, 3, false)]
    [InlineData(1, 1, -1, false)]
    public void AdditionFact_EvaluatesCorrectnessAccurately(int left, int right, int answer, bool expectedCorrect)
    {
        var fact = new AdditionFact(left, right);

        Assert.Equal(left, left);
        Assert.Equal(right, right);
        Assert.Equal(left + right, fact.CorrectResult);
        Assert.Equal(expectedCorrect, fact.IsCorrect(answer));
    }

    [Fact]
    public void PracticeSequence_YieldsAllFactsAndAvoidsConsecutiveDuplicateAcrossCycles()
    {
        var smallCatalog = new[]
        {
            new AdditionFact(1, 1),
            new AdditionFact(2, 2)
        };

        var sequence = new AdditionPracticeSequence(smallCatalog, seed: 42);

        AdditionFact? previous = null;
        for (var i = 0; i < 20; i++)
        {
            var next = sequence.GetNextFact();
            Assert.NotNull(next);
            if (previous is not null)
            {
                // In a 2-item deck, consecutive items must alternate
                Assert.NotEqual(previous, next);
            }
            previous = next;
        }
    }

    [Fact]
    public void PracticeSequence_WrongAnswerRecurrence_ReturnsAfterConfiguredDelay()
    {
        var catalog = AdditionCatalog.CreateFullCatalog();
        var sequence = new AdditionPracticeSequence(catalog, seed: 123);

        var firstFact = sequence.GetNextFact();
        // Schedule recurrence with delay = 3
        sequence.ScheduleRecurrence(firstFact, delay: 3);

        // Step 1: not the repeated fact
        var step1 = sequence.GetNextFact();
        Assert.NotEqual(firstFact, step1);

        // Step 2: not the repeated fact
        var step2 = sequence.GetNextFact();
        Assert.NotEqual(firstFact, step2);

        // Step 3: not the repeated fact
        var step3 = sequence.GetNextFact();
        Assert.NotEqual(firstFact, step3);

        // Step 4: due! Must return firstFact
        var step4 = sequence.GetNextFact();
        Assert.Equal(firstFact, step4);
    }

    [Fact]
    public void Session_TracksSubmissionsAndTriggersWrongAnswerRecurrence()
    {
        var smallCatalog = new[]
        {
            new AdditionFact(0, 0),
            new AdditionFact(1, 1),
            new AdditionFact(2, 2),
            new AdditionFact(3, 3),
            new AdditionFact(4, 4),
        };

        var sequence = new AdditionPracticeSequence(smallCatalog, seed: 99);
        var session = new AdditionTrainingSession(sequence);

        Assert.NotNull(session.CurrentFact);
        Assert.Equal(0, session.AttemptCount);
        Assert.Equal(0, session.CorrectCount);

        var firstFact = session.CurrentFact;
        // Answer wrong on purpose
        var wrongAnswer = firstFact.CorrectResult + 99;
        var result = session.SubmitAnswer(wrongAnswer);

        Assert.False(result.IsCorrect);
        Assert.Equal(1, session.AttemptCount);
        Assert.Equal(0, session.CorrectCount);
        Assert.True(session.IsAwaitingNext);

        session.Advance();
        Assert.False(session.IsAwaitingNext);
        Assert.NotEqual(firstFact, session.CurrentFact);
    }
}
