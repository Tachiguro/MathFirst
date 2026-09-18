namespace MathFirst.Domain;

public static class PracticeTimePreferencePolicy
{
    public const PracticeTimeSetting Default = PracticeTimeSetting.Standard;

    public static PracticeTimeSetting Normalize(int rawValue) => rawValue switch
    {
        (int)PracticeTimeSetting.NoTimePressure => PracticeTimeSetting.NoTimePressure,
        (int)PracticeTimeSetting.Seconds30 => PracticeTimeSetting.Seconds30,
        (int)PracticeTimeSetting.Seconds45 => PracticeTimeSetting.Seconds45,
        (int)PracticeTimeSetting.Seconds60 => PracticeTimeSetting.Seconds60,
        _ => PracticeTimeSetting.Standard
    };

    public static PracticeTimeSetting Normalize(PracticeTimeSetting setting) => Normalize((int)setting);

    public static bool HasEnforcedDeadline(PracticeTimeSetting setting) =>
        setting != PracticeTimeSetting.NoTimePressure;

    public static bool IsNoTimePressure(PracticeTimeSetting setting) =>
        setting == PracticeTimeSetting.NoTimePressure;

    public static long GetDeadlineFloorMs(PracticeTimeSetting setting) => setting switch
    {
        PracticeTimeSetting.Seconds30 => 30_000L,
        PracticeTimeSetting.Seconds45 => 45_000L,
        PracticeTimeSetting.Seconds60 => 60_000L,
        _ => 0L
    };
}
