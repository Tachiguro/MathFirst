namespace MathFirst.Application;

public enum HapticFeedbackCue
{
    KeyTap,
    Correct,
    Incorrect,
    Timeout
}

public interface IHapticDriver
{
    void Perform(HapticFeedbackCue cue);
}

public interface IHapticFeedbackService
{
    void PerformCue(HapticFeedbackCue cue);
}

public sealed class HapticFeedbackService : IHapticFeedbackService
{
    private readonly IPreferenceStore? _preferenceStore;
    private readonly IHapticDriver? _driver;

    public HapticFeedbackService(IPreferenceStore? preferenceStore = null, IHapticDriver? driver = null)
    {
        _preferenceStore = preferenceStore;
        _driver = driver;
    }

    public void PerformCue(HapticFeedbackCue cue)
    {
        if (_preferenceStore is not null && !_preferenceStore.GetHapticFeedbackEnabled())
        {
            return;
        }

        try
        {
            _driver?.Perform(cue);
        }
        catch
        {
            // Failure isolation: platform haptic or driver errors must never disrupt user flow or learning progress.
        }
    }
}

public sealed class NoOpHapticDriver : IHapticDriver
{
    public static NoOpHapticDriver Instance { get; } = new();

    public void Perform(HapticFeedbackCue cue)
    {
        // Safe no-op for unsupported platforms or testing without feedback.
    }
}
