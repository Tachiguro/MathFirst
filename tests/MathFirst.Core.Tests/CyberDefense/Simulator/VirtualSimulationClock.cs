namespace MathFirst.Core.Tests.CyberDefense.Simulator;

using System;

/// <summary>
/// Virtual elapsed-time accumulator for simulated player interactions.
/// Decoupled from system wall clocks, Stopwatch, and task delays.
/// </summary>
public sealed class VirtualSimulationClock
{
    private long _elapsedMilliseconds;

    /// <summary>
    /// Initializes a new instance of <see cref="VirtualSimulationClock"/> starting at zero elapsed milliseconds.
    /// </summary>
    public VirtualSimulationClock()
    {
        _elapsedMilliseconds = 0;
    }

    /// <summary>
    /// Gets the current accumulated virtual elapsed milliseconds.
    /// </summary>
    public long ElapsedMilliseconds => _elapsedMilliseconds;

    /// <summary>
    /// Advances the virtual clock by the specified duration in milliseconds.
    /// </summary>
    /// <param name="milliseconds">Non-negative milliseconds to advance.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when milliseconds is negative.</exception>
    /// <exception cref="OverflowException">Thrown when advance causes long arithmetic overflow.</exception>
    public void Advance(long milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                milliseconds,
                "Advance duration cannot be negative.");
        }

        if (milliseconds == 0)
        {
            return;
        }

        checked
        {
            _elapsedMilliseconds += milliseconds;
        }
    }

    /// <summary>
    /// Resets the virtual clock to zero elapsed milliseconds.
    /// </summary>
    public void Reset()
    {
        _elapsedMilliseconds = 0;
    }
}
