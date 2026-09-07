namespace MathFirst.Application;

public interface IClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startTimestamp);
}

public sealed class MonotonicClock : IClock
{
    public static readonly MonotonicClock Instance = new();

    public long GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startTimestamp) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);
}
