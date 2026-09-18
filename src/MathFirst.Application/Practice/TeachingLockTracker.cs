namespace MathFirst.Application.Practice;

/// <summary>
/// Tracks visible, active foreground dwell time for teaching interventions using monotonic clock timing.
/// </summary>
public sealed class TeachingLockTracker
{
    public const int DefaultLockDurationMs = 3000;

    private readonly IClock _clock;
    private readonly long _requiredDurationTicks;
    private long _activeStartTimestamp = -1;
    private long _accumulatedElapsedTicks;
    private bool _isActive;
    private bool _isForeground = true;

    public TeachingLockTracker(IClock? clock = null, int lockDurationMs = DefaultLockDurationMs)
    {
        _clock = clock ?? MonotonicClock.Instance;
        _requiredDurationTicks = lockDurationMs * TimeSpan.TicksPerMillisecond;
    }

    public bool IsActive => _isActive;
    public bool IsForeground => _isForeground;
    public bool IsUnlocked => GetRemainingTicks() <= 0;

    public TimeSpan RemainingTime => TimeSpan.FromTicks(Math.Max(0, GetRemainingTicks()));

    public int RemainingSecondsCeiling
    {
        get
        {
            var remainingTicks = GetRemainingTicks();
            if (remainingTicks <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling((double)remainingTicks / TimeSpan.TicksPerSecond);
        }
    }

    public void Activate()
    {
        _isActive = true;
        _accumulatedElapsedTicks = 0;
        _activeStartTimestamp = _isForeground ? _clock.GetTimestamp() : -1;
    }

    public void Deactivate()
    {
        _isActive = false;
        _activeStartTimestamp = -1;
        _accumulatedElapsedTicks = 0;
    }

    public void SetForeground(bool isForeground)
    {
        if (_isForeground == isForeground)
        {
            return;
        }

        if (_isActive && _isForeground && _activeStartTimestamp >= 0)
        {
            _accumulatedElapsedTicks += _clock.GetElapsedTime(_activeStartTimestamp).Ticks;
            _activeStartTimestamp = -1;
        }
        else if (_isActive && !_isForeground && isForeground)
        {
            _activeStartTimestamp = _clock.GetTimestamp();
        }

        _isForeground = isForeground;
    }

    public bool TryAcknowledge()
    {
        return IsUnlocked;
    }

    private long GetRemainingTicks()
    {
        if (!_isActive)
        {
            return 0;
        }

        var totalElapsedTicks = _accumulatedElapsedTicks;
        if (_isForeground && _activeStartTimestamp >= 0)
        {
            totalElapsedTicks += _clock.GetElapsedTime(_activeStartTimestamp).Ticks;
        }

        return Math.Max(0, _requiredDurationTicks - totalElapsedTicks);
    }
}
