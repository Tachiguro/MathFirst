namespace MathFirst.Domain;

public sealed record OperationProgression
{
    public ArithmeticOperation Operation { get; }
    public int BandIndex { get; }
    public long BandStartedPracticePosition { get; }

    public OperationProgression(
        ArithmeticOperation operation,
        int bandIndex,
        long bandStartedPracticePosition)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown arithmetic operation.");
        }

        if (bandIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex), bandIndex, "Band index must be non-negative.");
        }

        if (bandStartedPracticePosition < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandStartedPracticePosition),
                bandStartedPracticePosition,
                "Band-started practice position must be non-negative.");
        }

        Operation = operation;
        BandIndex = bandIndex;
        BandStartedPracticePosition = bandStartedPracticePosition;
    }
}
