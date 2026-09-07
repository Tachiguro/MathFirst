namespace MathFirst.Application.Scheduling;

public interface IFsrsScheduler
{
    double DesiredRetention { get; }
    IReadOnlyList<double> Parameters { get; }
    bool EnableFuzzing { get; }
    DateTime VirtualEpoch { get; }

    FsrsCardState ReviewCard(
        FsrsCardState? existingState,
        string factId,
        FsrsRating rating,
        long reviewPracticePosition,
        long reviewDurationMs);
}
