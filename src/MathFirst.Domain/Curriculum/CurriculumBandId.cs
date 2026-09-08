namespace MathFirst.Domain.Curriculum;

public sealed record CurriculumBandId
{
    public string Value { get; }

    public CurriculumBandId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Any(character =>
                character is not ('-' or >= '0' and <= '9' or >= 'A' and <= 'Z')))
        {
            throw new ArgumentException(
                "Curriculum band IDs may contain only uppercase ASCII letters, digits, and hyphens.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
