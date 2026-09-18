namespace MathFirst.App.Services;

using MathFirst.Application;
using Microsoft.Maui.Devices;

public sealed class MauiHapticDriver : IHapticDriver
{
    public void Perform(HapticFeedbackCue cue)
    {
        try
        {
            switch (cue)
            {
                case HapticFeedbackCue.KeyTap:
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                    break;
                case HapticFeedbackCue.Correct:
                    Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(40));
                    break;
                case HapticFeedbackCue.Incorrect:
                case HapticFeedbackCue.Timeout:
                    Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(120));
                    break;
            }
        }
        catch
        {
            // Unsupported platforms (e.g. Windows without haptic hardware) or device errors safely no-op.
        }
    }
}
