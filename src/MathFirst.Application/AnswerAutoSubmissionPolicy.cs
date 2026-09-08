namespace MathFirst.Application;

using System.Globalization;

public static class AnswerAutoSubmissionPolicy
{
    public static bool ShouldSubmit(string? validInput, int correctResult)
    {
        if (!NumericAnswerInputPolicy.TryParseSubmission(validInput, out var parsedAnswer))
        {
            return false;
        }

        if (parsedAnswer == correctResult)
        {
            return true;
        }

        // Decimal forms remain valid and can always be force-submitted with Enter.
        // Only canonical integer digit sequences have an unambiguous completion length.
        if (validInput!.Contains('.') || validInput.Contains(','))
        {
            return false;
        }

        var canonicalAnswer = correctResult.ToString(CultureInfo.InvariantCulture);
        if (canonicalAnswer.Length == 0 || canonicalAnswer[0] == '-')
        {
            return false;
        }

        return validInput.Length >= canonicalAnswer.Length ||
               !canonicalAnswer.StartsWith(validInput, StringComparison.Ordinal);
    }
}
