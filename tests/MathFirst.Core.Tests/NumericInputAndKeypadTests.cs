namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class NumericInputAndKeypadTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstNumericInputTests_" + Guid.NewGuid().ToString("N"));

    public NumericInputAndKeypadTests() => Directory.CreateDirectory(_testDirectory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.")]
    [InlineData("0,")]
    [InlineData("0.0")]
    [InlineData("0,0")]
    [InlineData("0.5")]
    [InlineData("0,5")]
    [InlineData("1")]
    [InlineData("10")]
    [InlineData("100")]
    [InlineData("12.")]
    [InlineData("12,")]
    [InlineData(".5")]
    [InlineData(",5")]
    [InlineData("12.5")]
    [InlineData("12,5")]
    public void NumericPolicy_AcceptsValidEditingStates(string value)
    {
        Assert.True(NumericAnswerInputPolicy.IsValidEdit(value));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("000")]
    [InlineData("0000")]
    [InlineData("01")]
    [InlineData("001")]
    [InlineData("00.5")]
    [InlineData("00,5")]
    [InlineData("01.5")]
    [InlineData("01,5")]
    [InlineData("a")]
    [InlineData("12a")]
    [InlineData("1e3")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("12 3")]
    [InlineData("12..3")]
    [InlineData("12,,3")]
    [InlineData("12.,3")]
    [InlineData("12,.3")]
    public void NumericPolicy_RejectsInvalidEditingStatesAndKeepsPreviousValue(string value)
    {
        Assert.False(NumericAnswerInputPolicy.IsValidEdit(value));
        Assert.Equal("12", NumericAnswerInputPolicy.AcceptEditOrKeep("12", value));
    }

    [Theory]
    [InlineData("14")]
    [InlineData("14.0")]
    [InlineData("14,0")]
    [InlineData("14.00")]
    [InlineData("14,00")]
    public void NumericPolicy_ParsesEquivalentIntegerRepresentationsExactly(string value)
    {
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(value, out var parsed));
        Assert.Equal(14m, parsed);
    }

    [Theory]
    [InlineData("12.")]
    [InlineData("12,")]
    public void NumericPolicy_SubmitsUnambiguousTrailingSeparatorAsExactInteger(string value)
    {
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(value, out var parsed));
        Assert.Equal(12m, parsed);
    }

    [Theory]
    [InlineData("14.5")]
    [InlineData("14,5")]
    public void NumericPolicy_ParsesFractionalAnswersAsValidButNotEquivalentToInteger(string value)
    {
        Assert.True(NumericAnswerInputPolicy.TryParseSubmission(value, out var parsed));
        Assert.NotEqual(14m, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData(",")]
    [InlineData("1e3")]
    public void NumericPolicy_IncompleteOrInvalidSubmissionDoesNotParse(string value)
    {
        Assert.False(NumericAnswerInputPolicy.TryParseSubmission(value, out _));
    }

    [Fact]
    public void NumericPolicy_KeypadAppendBackspaceAndMaximumLengthAreDeterministic()
    {
        var value = NumericAnswerInputPolicy.Append(string.Empty, "1");
        value = NumericAnswerInputPolicy.Append(value, "4");
        value = NumericAnswerInputPolicy.Append(value, ",");
        value = NumericAnswerInputPolicy.Append(value, "5");

        Assert.Equal("14,5", value);
        Assert.Equal(value, NumericAnswerInputPolicy.Append(value, "."));
        Assert.Equal("14,", NumericAnswerInputPolicy.Backspace(value));

        var atLimit = new string('9', NumericAnswerInputPolicy.MaximumLength);
        Assert.True(NumericAnswerInputPolicy.IsValidEdit(atLimit));
        Assert.Equal(atLimit, NumericAnswerInputPolicy.Append(atLimit, "1"));
        Assert.False(NumericAnswerInputPolicy.IsValidEdit(atLimit + "1"));
    }

    [Fact]
    public void NumericPolicy_KeypadBlocksRedundantLeadingZerosAndPreservesDecimalEntry()
    {
        var value = NumericAnswerInputPolicy.Append(string.Empty, "0");
        value = NumericAnswerInputPolicy.Append(value, "0");
        value = NumericAnswerInputPolicy.Append(value, "0");

        Assert.Equal("0", value);
        Assert.Equal("0", NumericAnswerInputPolicy.Append(value, "5"));

        value = NumericAnswerInputPolicy.Append(value, ".");
        value = NumericAnswerInputPolicy.Append(value, "5");

        Assert.Equal("0.5", value);
        Assert.Equal("0.", NumericAnswerInputPolicy.Backspace(value));
    }

    [Fact]
    public void KeypadPolicy_ExposesPhoneAndNumpadOrders()
    {
        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "7", "8", "9"],
            NumericKeypadLayoutPolicy.GetPrimaryDigits(NumericKeypadLayout.Phone));
        Assert.Equal(
            ["7", "8", "9", "4", "5", "6", "1", "2", "3"],
            NumericKeypadLayoutPolicy.GetPrimaryDigits(NumericKeypadLayout.Numpad));
        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", ",", "0", "backspace"],
            NumericKeypadLayoutPolicy.GetKeys(NumericKeypadLayout.Phone, ","));
        Assert.Equal(
            ["7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "backspace"],
            NumericKeypadLayoutPolicy.GetKeys(NumericKeypadLayout.Numpad, "."));
    }

    [Theory]
    [InlineData(0, NumericKeypadLayout.Phone)]
    [InlineData(1, NumericKeypadLayout.Numpad)]
    [InlineData(-1, NumericKeypadLayout.Phone)]
    [InlineData(99, NumericKeypadLayout.Phone)]
    public void KeypadPreference_DefaultsAndNormalizesToPhone(int rawValue, NumericKeypadLayout expected)
    {
        Assert.Equal(expected, NumericKeypadLayoutPolicy.Normalize(rawValue));
    }

    [Fact]
    public void KeypadPreference_ExposesExactlyTwoLayouts()
    {
        Assert.Equal(
            [NumericKeypadLayout.Phone, NumericKeypadLayout.Numpad],
            Enum.GetValues<NumericKeypadLayout>());
    }

    [Fact]
    public async Task EquivalentDecimalRepresentations_SubmitAsCorrect()
    {
        foreach (var suffix in new[] { "", ".0", ",0", ".00", ",00" })
        {
            var dbPath = Path.Combine(_testDirectory, Guid.NewGuid().ToString("N") + ".db");
            using var store = new SqliteLearnerStore(dbPath);
            var session = new TrainingSession(store);
            await session.InitializeAsync();
            var input = session.CurrentFact.CorrectResult + suffix;

            Assert.True(NumericAnswerInputPolicy.TryParseSubmission(input, out var parsed));
            var evaluation = session.SubmitAnswer(parsed);

            Assert.True(evaluation.IsCorrect);
            Assert.Equal(AttemptOutcome.Correct, evaluation.Outcome);
        }
    }

    [Fact]
    public async Task FractionalSubmission_IsAnIncorrectAttemptWithoutChangingTheIntegerSchema()
    {
        var dbPath = Path.Combine(_testDirectory, "fractional_submission.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();
        var submitted = session.CurrentFact.CorrectResult + 0.5m;

        var evaluation = session.SubmitAnswer(submitted);
        await session.CommitCurrentEvaluationAsync();

        Assert.Equal(AttemptOutcome.Incorrect, evaluation.Outcome);
        Assert.False(evaluation.IsCorrect);
        Assert.Equal(submitted, evaluation.SubmittedNumericAnswer);
        Assert.Null(evaluation.SubmittedAnswer);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(1, session.SessionTotalCount);

        var snapshot = await store.LoadSnapshotAsync();
        var attempt = Assert.Single(snapshot.RecentAttempts);
        Assert.Equal(AttemptOutcome.Incorrect, attempt.Outcome);
        Assert.Null(attempt.SubmittedAnswer);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1e3")]
    [InlineData("12..3")]
    [InlineData("+")]
    [InlineData("0004")]
    [InlineData("01")]
    public async Task InvalidInput_DoesNotCreateSemanticLearningAttempt(string invalidInput)
    {
        var dbPath = Path.Combine(_testDirectory, Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();
        var practicePosition = session.Progression.PracticePosition;
        var checkpointAttemptCount = session.Progression.CheckpointAttemptCount;
        var checkpointCorrectCount = session.Progression.CheckpointCorrectCount;
        var fsrsStateCount = session.FsrsStates.Count;

        var accepted = NumericAnswerInputPolicy.TryParseSubmission(invalidInput, out var parsed);
        if (accepted)
        {
            session.SubmitAnswer(parsed);
        }

        Assert.False(accepted);
        Assert.Equal(practicePosition, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(checkpointAttemptCount, session.Progression.CheckpointAttemptCount);
        Assert.Equal(checkpointCorrectCount, session.Progression.CheckpointCorrectCount);
        Assert.Equal(fsrsStateCount, session.FsrsStates.Count);
        Assert.Null(session.LastEvaluation);
        Assert.Empty((await store.LoadSnapshotAsync()).RecentAttempts);
    }
}
