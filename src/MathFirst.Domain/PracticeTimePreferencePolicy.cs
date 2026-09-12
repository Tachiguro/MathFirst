namespace MathFirst.Domain;

public static class PracticeTimePreferencePolicy
{
    public const PracticeTimeSetting Default = PracticeTimeSetting.Standard;

    public static PracticeTimeSetting Normalize(int rawValue) => rawValue switch
    {
        30 => PracticeTimeSetting.Seconds30,
        45 => PracticeTimeSetting.Seconds45,
        60 => PracticeTimeSetting.Seconds60,
        _ => PracticeTimeSetting.Standard
    };

    public static PracticeTimeSetting Normalize(PracticeTimeSetting setting) => Normalize((int)setting);

    public static long GetDeadlineFloorMs(PracticeTimeSetting setting) => setting switch
    {
        PracticeTimeSetting.Seconds30 => 30_000L,
        PracticeTimeSetting.Seconds45 => 45_000L,
        PracticeTimeSetting.Seconds60 => 60_000L,
        _ => 0L
    };
}
