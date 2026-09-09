namespace MathFirst.Application.Copy;

/// <summary>
/// Initial V1 EN/DE/RU contextual copy corpus for MF-UX-002.
///
/// Message ID convention: {Trigger}.{Tone}.{NNN}  (NNN = 3-digit zero-padded index)
/// Tone codes: Neutral, Welcoming, DryHumor, LightlyCheeky
///
/// Corpus rules (enforced by PracticeCopyLibraryTests):
/// - All IDs must be present in EN, DE, and RU dictionaries.
/// - No EN variant may exceed 55 characters.
/// - {N} placeholders must be identical across locales for the same ID.
/// - No sycophantic praise, no guilt/shame, no age/gender assumptions.
/// - Tone: calm, dry, occasionally cheeky — never hostile, never patronizing.
///
/// V1 corpus: ~55 variants per locale × 3 locales = ~165 strings total.
/// </summary>
public sealed class PracticeCopyLibrary : IPracticeCopyLibrary
{
    // -------------------------------------------------------------------------
    // Message ID pools per trigger (language-independent)
    // -------------------------------------------------------------------------

    private static readonly string[] FirstEverReadyIds =
    [
        "FirstEverReady.Welcoming.001",
        "FirstEverReady.Welcoming.002",
        "FirstEverReady.Neutral.001",
        "FirstEverReady.Neutral.002",
        "FirstEverReady.DryHumor.001",
        "FirstEverReady.LightlyCheeky.001",
    ];

    private static readonly string[] InitialReadyIds =
    [
        "InitialReady.Neutral.001",
        "InitialReady.Neutral.002",
        "InitialReady.Neutral.003",
        "InitialReady.Neutral.004",
        "InitialReady.Welcoming.001",
        "InitialReady.Welcoming.002",
        "InitialReady.DryHumor.001",
        "InitialReady.DryHumor.002",
        "InitialReady.DryHumor.003",
        "InitialReady.LightlyCheeky.001",
        "InitialReady.LightlyCheeky.002",
    ];

    private static readonly string[] ReturnShortAbsenceIds =
    [
        "ReturnShortAbsence.Neutral.001",
        "ReturnShortAbsence.Neutral.002",
        "ReturnShortAbsence.Neutral.003",
        "ReturnShortAbsence.Welcoming.001",
        "ReturnShortAbsence.Welcoming.002",
        "ReturnShortAbsence.DryHumor.001",
        "ReturnShortAbsence.DryHumor.002",
        "ReturnShortAbsence.DryHumor.003",
        "ReturnShortAbsence.LightlyCheeky.001",
        "ReturnShortAbsence.LightlyCheeky.002",
    ];

    private static readonly string[] ReturnLongAbsenceIds =
    [
        "ReturnLongAbsence.Neutral.001",
        "ReturnLongAbsence.Neutral.002",
        "ReturnLongAbsence.Neutral.003",
        "ReturnLongAbsence.Welcoming.001",
        "ReturnLongAbsence.Welcoming.002",
        "ReturnLongAbsence.DryHumor.001",
        "ReturnLongAbsence.DryHumor.002",
        "ReturnLongAbsence.DryHumor.003",
        "ReturnLongAbsence.LightlyCheeky.001",
        "ReturnLongAbsence.LightlyCheeky.002",
    ];

    private static readonly string[] ResumeManualPauseIds =
    [
        "ResumeManualPause.Neutral.001",
        "ResumeManualPause.Neutral.002",
        "ResumeManualPause.Neutral.003",
        "ResumeManualPause.Welcoming.001",
        "ResumeManualPause.Welcoming.002",
        "ResumeManualPause.DryHumor.001",
        "ResumeManualPause.DryHumor.002",
        "ResumeManualPause.DryHumor.003",
        "ResumeManualPause.LightlyCheeky.001",
        "ResumeManualPause.LightlyCheeky.002",
    ];

    private static readonly string[] ResumeBackgroundIds =
    [
        "ResumeBackground.Neutral.001",
        "ResumeBackground.Neutral.002",
        "ResumeBackground.Neutral.003",
        "ResumeBackground.Welcoming.001",
        "ResumeBackground.Welcoming.002",
        "ResumeBackground.DryHumor.001",
        "ResumeBackground.DryHumor.002",
        "ResumeBackground.LightlyCheeky.001",
    ];

    private static readonly string[] NeutralReadyIds =
    [
        "NeutralReady.Neutral.001",
        "NeutralReady.Neutral.002",
        "NeutralReady.DryHumor.001",
    ];

    private static readonly string[] NeutralPausedIds =
    [
        "NeutralPaused.Neutral.001",
        "NeutralPaused.Neutral.002",
        "NeutralPaused.DryHumor.001",
    ];

    // -------------------------------------------------------------------------
    // English strings
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> EnglishStrings = new(StringComparer.Ordinal)
    {
        // FirstEverReady — learner's very first session
        ["FirstEverReady.Welcoming.001"] = "Your first practice is ready.",
        ["FirstEverReady.Welcoming.002"] = "First fact is ready.",
        ["FirstEverReady.Neutral.001"] = "Practice starts now.",
        ["FirstEverReady.Neutral.002"] = "Ready to begin.",
        ["FirstEverReady.DryHumor.001"] = "Arithmetic is ready. So is the timer.",
        ["FirstEverReady.LightlyCheeky.001"] = "No pressure. (There's a timer.)",

        // InitialReady — same-session return, short gap
        ["InitialReady.Neutral.001"] = "Ready to practice?",
        ["InitialReady.Neutral.002"] = "Practice is ready.",
        ["InitialReady.Neutral.003"] = "Start when ready.",
        ["InitialReady.Neutral.004"] = "Arithmetic is waiting.",
        ["InitialReady.Welcoming.001"] = "Ready to continue.",
        ["InitialReady.Welcoming.002"] = "Let's keep going.",
        ["InitialReady.DryHumor.001"] = "The numbers have been patient.",
        ["InitialReady.DryHumor.002"] = "Still here. So are the facts.",
        ["InitialReady.DryHumor.003"] = "The problem set is ready.",
        ["InitialReady.LightlyCheeky.001"] = "The facts won't practice themselves.",
        ["InitialReady.LightlyCheeky.002"] = "Ready when you are.",

        // ReturnShortAbsence — 30 min to 3 days
        ["ReturnShortAbsence.Neutral.001"] = "Ready.",
        ["ReturnShortAbsence.Neutral.002"] = "Back to practice.",
        ["ReturnShortAbsence.Neutral.003"] = "Practice is ready.",
        ["ReturnShortAbsence.Welcoming.001"] = "Good to have you back.",
        ["ReturnShortAbsence.Welcoming.002"] = "Let's continue.",
        ["ReturnShortAbsence.DryHumor.001"] = "The facts waited.",
        ["ReturnShortAbsence.DryHumor.002"] = "Numbers: still here.",
        ["ReturnShortAbsence.DryHumor.003"] = "Arithmetic does not expire.",
        ["ReturnShortAbsence.LightlyCheeky.001"] = "Back already?",
        ["ReturnShortAbsence.LightlyCheeky.002"] = "Ready when you are.",

        // ReturnLongAbsence — more than 3 days
        ["ReturnLongAbsence.Neutral.001"] = "Welcome back.",
        ["ReturnLongAbsence.Neutral.002"] = "Ready to practice.",
        ["ReturnLongAbsence.Neutral.003"] = "Practice is ready.",
        ["ReturnLongAbsence.Welcoming.001"] = "Good to see you again.",
        ["ReturnLongAbsence.Welcoming.002"] = "Ready whenever you are.",
        ["ReturnLongAbsence.DryHumor.001"] = "The facts haven't moved.",
        ["ReturnLongAbsence.DryHumor.002"] = "Numbers: still reliable.",
        ["ReturnLongAbsence.DryHumor.003"] = "Arithmetic waited patiently.",
        ["ReturnLongAbsence.LightlyCheeky.001"] = "Long time no practice.",
        ["ReturnLongAbsence.LightlyCheeky.002"] = "The facts kept your seat warm.",

        // ResumeManualPause — user pressed pause, now resuming
        ["ResumeManualPause.Neutral.001"] = "Whenever you're ready.",
        ["ResumeManualPause.Neutral.002"] = "Practice is paused.",
        ["ResumeManualPause.Neutral.003"] = "Resume when ready.",
        ["ResumeManualPause.Welcoming.001"] = "No rush. Ready when you are.",
        ["ResumeManualPause.Welcoming.002"] = "Take your time.",
        ["ResumeManualPause.DryHumor.001"] = "The timer is very patient.",
        ["ResumeManualPause.DryHumor.002"] = "The problem is still there.",
        ["ResumeManualPause.DryHumor.003"] = "Paused. Fact: unchanged.",
        ["ResumeManualPause.LightlyCheeky.001"] = "The problem hasn't solved itself.",
        ["ResumeManualPause.LightlyCheeky.002"] = "Still here. So is the fact.",

        // ResumeBackground — app was backgrounded, returning to foreground
        ["ResumeBackground.Neutral.001"] = "Back to practice.",
        ["ResumeBackground.Neutral.002"] = "Practice is ready.",
        ["ResumeBackground.Neutral.003"] = "Ready to continue.",
        ["ResumeBackground.Welcoming.001"] = "Ready whenever you are.",
        ["ResumeBackground.Welcoming.002"] = "Continue when ready.",
        ["ResumeBackground.DryHumor.001"] = "Still here. The fact too.",
        ["ResumeBackground.DryHumor.002"] = "The fact waited.",
        ["ResumeBackground.LightlyCheeky.001"] = "Back so soon?",

        // NeutralReady — fallback for InitialReadyGate
        ["NeutralReady.Neutral.001"] = "Ready to practice?",
        ["NeutralReady.Neutral.002"] = "Start when ready.",
        ["NeutralReady.DryHumor.001"] = "The numbers are ready.",

        // NeutralPaused — fallback for pause gates
        ["NeutralPaused.Neutral.001"] = "Paused.",
        ["NeutralPaused.Neutral.002"] = "Resume when ready.",
        ["NeutralPaused.DryHumor.001"] = "Practice is on hold.",
    };

    // -------------------------------------------------------------------------
    // German strings
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> GermanStrings = new(StringComparer.Ordinal)
    {
        // FirstEverReady
        ["FirstEverReady.Welcoming.001"] = "Deine erste Übung ist bereit.",
        ["FirstEverReady.Welcoming.002"] = "Erste Aufgabe ist bereit.",
        ["FirstEverReady.Neutral.001"] = "Übung beginnt jetzt.",
        ["FirstEverReady.Neutral.002"] = "Bereit zum Starten.",
        ["FirstEverReady.DryHumor.001"] = "Aufgabe bereit. Timer auch.",
        ["FirstEverReady.LightlyCheeky.001"] = "Kein Druck. (Es gibt einen Timer.)",

        // InitialReady
        ["InitialReady.Neutral.001"] = "Bereit zum Üben?",
        ["InitialReady.Neutral.002"] = "Übung ist bereit.",
        ["InitialReady.Neutral.003"] = "Starten, wenn bereit.",
        ["InitialReady.Neutral.004"] = "Aufgaben warten.",
        ["InitialReady.Welcoming.001"] = "Bereit weiterzumachen.",
        ["InitialReady.Welcoming.002"] = "Weiter geht's.",
        ["InitialReady.DryHumor.001"] = "Die Zahlen waren geduldig.",
        ["InitialReady.DryHumor.002"] = "Noch da. Die Aufgaben auch.",
        ["InitialReady.DryHumor.003"] = "Die Aufgaben sind bereit.",
        ["InitialReady.LightlyCheeky.001"] = "Die Aufgaben üben sich nicht selbst.",
        ["InitialReady.LightlyCheeky.002"] = "Bereit, wenn du es bist.",

        // ReturnShortAbsence
        ["ReturnShortAbsence.Neutral.001"] = "Bereit.",
        ["ReturnShortAbsence.Neutral.002"] = "Zurück zum Üben.",
        ["ReturnShortAbsence.Neutral.003"] = "Übung ist bereit.",
        ["ReturnShortAbsence.Welcoming.001"] = "Schön, dass du zurück bist.",
        ["ReturnShortAbsence.Welcoming.002"] = "Weiter geht's.",
        ["ReturnShortAbsence.DryHumor.001"] = "Die Aufgaben haben gewartet.",
        ["ReturnShortAbsence.DryHumor.002"] = "Zahlen: immer noch da.",
        ["ReturnShortAbsence.DryHumor.003"] = "Arithmetik verfällt nicht.",
        ["ReturnShortAbsence.LightlyCheeky.001"] = "Schon wieder zurück?",
        ["ReturnShortAbsence.LightlyCheeky.002"] = "Bereit, wenn du es bist.",

        // ReturnLongAbsence
        ["ReturnLongAbsence.Neutral.001"] = "Willkommen zurück.",
        ["ReturnLongAbsence.Neutral.002"] = "Bereit zum Üben.",
        ["ReturnLongAbsence.Neutral.003"] = "Übung ist bereit.",
        ["ReturnLongAbsence.Welcoming.001"] = "Schön, dich wieder zu sehen.",
        ["ReturnLongAbsence.Welcoming.002"] = "Bereit, wenn du es bist.",
        ["ReturnLongAbsence.DryHumor.001"] = "Die Aufgaben haben sich nicht verändert.",
        ["ReturnLongAbsence.DryHumor.002"] = "Zahlen: weiterhin zuverlässig.",
        ["ReturnLongAbsence.DryHumor.003"] = "Arithmetik hat geduldig gewartet.",
        ["ReturnLongAbsence.LightlyCheeky.001"] = "Lang nicht geübt.",
        ["ReturnLongAbsence.LightlyCheeky.002"] = "Die Aufgaben haben deinen Platz freigehalten.",

        // ResumeManualPause
        ["ResumeManualPause.Neutral.001"] = "Wenn du bereit bist.",
        ["ResumeManualPause.Neutral.002"] = "Übung pausiert.",
        ["ResumeManualPause.Neutral.003"] = "Weitermachen, wenn bereit.",
        ["ResumeManualPause.Welcoming.001"] = "Kein Eile. Bereit, wenn du es bist.",
        ["ResumeManualPause.Welcoming.002"] = "Lass dir Zeit.",
        ["ResumeManualPause.DryHumor.001"] = "Der Timer ist sehr geduldig.",
        ["ResumeManualPause.DryHumor.002"] = "Die Aufgabe ist noch da.",
        ["ResumeManualPause.DryHumor.003"] = "Pausiert. Aufgabe: unverändert.",
        ["ResumeManualPause.LightlyCheeky.001"] = "Die Aufgabe löst sich nicht von selbst.",
        ["ResumeManualPause.LightlyCheeky.002"] = "Noch da. Die Aufgabe auch.",

        // ResumeBackground
        ["ResumeBackground.Neutral.001"] = "Zurück zum Üben.",
        ["ResumeBackground.Neutral.002"] = "Übung ist bereit.",
        ["ResumeBackground.Neutral.003"] = "Bereit weiterzumachen.",
        ["ResumeBackground.Welcoming.001"] = "Bereit, wenn du es bist.",
        ["ResumeBackground.Welcoming.002"] = "Weitermachen, wenn bereit.",
        ["ResumeBackground.DryHumor.001"] = "Noch da. Die Aufgabe auch.",
        ["ResumeBackground.DryHumor.002"] = "Die Aufgabe hat gewartet.",
        ["ResumeBackground.LightlyCheeky.001"] = "Schon zurück?",

        // NeutralReady
        ["NeutralReady.Neutral.001"] = "Bereit zum Üben?",
        ["NeutralReady.Neutral.002"] = "Starten, wenn bereit.",
        ["NeutralReady.DryHumor.001"] = "Die Zahlen sind bereit.",

        // NeutralPaused
        ["NeutralPaused.Neutral.001"] = "Pausiert.",
        ["NeutralPaused.Neutral.002"] = "Weitermachen, wenn bereit.",
        ["NeutralPaused.DryHumor.001"] = "Übung pausiert.",
    };

    // -------------------------------------------------------------------------
    // Russian strings
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> RussianStrings = new(StringComparer.Ordinal)
    {
        // FirstEverReady
        ["FirstEverReady.Welcoming.001"] = "Первая тренировка готова.",
        ["FirstEverReady.Welcoming.002"] = "Первый пример готов.",
        ["FirstEverReady.Neutral.001"] = "Тренировка начинается.",
        ["FirstEverReady.Neutral.002"] = "Готовы начать.",
        ["FirstEverReady.DryHumor.001"] = "Пример готов. Таймер тоже.",
        ["FirstEverReady.LightlyCheeky.001"] = "Без давления. (Есть таймер.)",

        // InitialReady
        ["InitialReady.Neutral.001"] = "Готовы заниматься?",
        ["InitialReady.Neutral.002"] = "Тренировка готова.",
        ["InitialReady.Neutral.003"] = "Начните, когда будете готовы.",
        ["InitialReady.Neutral.004"] = "Примеры ждут.",
        ["InitialReady.Welcoming.001"] = "Готовы продолжить.",
        ["InitialReady.Welcoming.002"] = "Продолжаем.",
        ["InitialReady.DryHumor.001"] = "Числа терпеливо ждали.",
        ["InitialReady.DryHumor.002"] = "Всё ещё здесь. Примеры тоже.",
        ["InitialReady.DryHumor.003"] = "Набор примеров готов.",
        ["InitialReady.LightlyCheeky.001"] = "Примеры сами себя не решат.",
        ["InitialReady.LightlyCheeky.002"] = "Готовы, когда вы готовы.",

        // ReturnShortAbsence
        ["ReturnShortAbsence.Neutral.001"] = "Готово.",
        ["ReturnShortAbsence.Neutral.002"] = "Возвращаемся к тренировке.",
        ["ReturnShortAbsence.Neutral.003"] = "Тренировка готова.",
        ["ReturnShortAbsence.Welcoming.001"] = "Рады видеть вас снова.",
        ["ReturnShortAbsence.Welcoming.002"] = "Продолжаем.",
        ["ReturnShortAbsence.DryHumor.001"] = "Примеры подождали.",
        ["ReturnShortAbsence.DryHumor.002"] = "Числа: по-прежнему здесь.",
        ["ReturnShortAbsence.DryHumor.003"] = "Арифметика не устаревает.",
        ["ReturnShortAbsence.LightlyCheeky.001"] = "Уже вернулись?",
        ["ReturnShortAbsence.LightlyCheeky.002"] = "Готовы, когда вы готовы.",

        // ReturnLongAbsence
        ["ReturnLongAbsence.Neutral.001"] = "Добро пожаловать обратно.",
        ["ReturnLongAbsence.Neutral.002"] = "Готовы заниматься.",
        ["ReturnLongAbsence.Neutral.003"] = "Тренировка готова.",
        ["ReturnLongAbsence.Welcoming.001"] = "Рады снова видеть вас.",
        ["ReturnLongAbsence.Welcoming.002"] = "Готовы, когда вы готовы.",
        ["ReturnLongAbsence.DryHumor.001"] = "Примеры не изменились.",
        ["ReturnLongAbsence.DryHumor.002"] = "Числа: по-прежнему надёжны.",
        ["ReturnLongAbsence.DryHumor.003"] = "Арифметика терпеливо ждала.",
        ["ReturnLongAbsence.LightlyCheeky.001"] = "Давно не занимались.",
        ["ReturnLongAbsence.LightlyCheeky.002"] = "Примеры держали ваше место.",

        // ResumeManualPause
        ["ResumeManualPause.Neutral.001"] = "Когда будете готовы.",
        ["ResumeManualPause.Neutral.002"] = "Тренировка на паузе.",
        ["ResumeManualPause.Neutral.003"] = "Продолжите, когда будете готовы.",
        ["ResumeManualPause.Welcoming.001"] = "Не торопитесь. Готовы, когда вы готовы.",
        ["ResumeManualPause.Welcoming.002"] = "Не спешите.",
        ["ResumeManualPause.DryHumor.001"] = "Таймер очень терпелив.",
        ["ResumeManualPause.DryHumor.002"] = "Пример всё ещё здесь.",
        ["ResumeManualPause.DryHumor.003"] = "Пауза. Пример: без изменений.",
        ["ResumeManualPause.LightlyCheeky.001"] = "Пример сам себя не решит.",
        ["ResumeManualPause.LightlyCheeky.002"] = "Здесь. Пример тоже.",

        // ResumeBackground
        ["ResumeBackground.Neutral.001"] = "Возвращаемся к тренировке.",
        ["ResumeBackground.Neutral.002"] = "Тренировка готова.",
        ["ResumeBackground.Neutral.003"] = "Готовы продолжить.",
        ["ResumeBackground.Welcoming.001"] = "Готовы, когда вы готовы.",
        ["ResumeBackground.Welcoming.002"] = "Продолжите, когда будете готовы.",
        ["ResumeBackground.DryHumor.001"] = "Здесь. Пример тоже.",
        ["ResumeBackground.DryHumor.002"] = "Пример подождал.",
        ["ResumeBackground.LightlyCheeky.001"] = "Уже вернулись?",

        // NeutralReady
        ["NeutralReady.Neutral.001"] = "Готовы заниматься?",
        ["NeutralReady.Neutral.002"] = "Начните, когда будете готовы.",
        ["NeutralReady.DryHumor.001"] = "Числа готовы.",

        // NeutralPaused
        ["NeutralPaused.Neutral.001"] = "Пауза.",
        ["NeutralPaused.Neutral.002"] = "Продолжите, когда будете готовы.",
        ["NeutralPaused.DryHumor.001"] = "Тренировка приостановлена.",
    };

    // -------------------------------------------------------------------------
    // IPracticeCopyLibrary implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IReadOnlyList<string> GetMessageIds(PracticeCopyTrigger trigger, string locale) =>
        trigger switch
        {
            PracticeCopyTrigger.FirstEverReady => FirstEverReadyIds,
            PracticeCopyTrigger.InitialReady => InitialReadyIds,
            PracticeCopyTrigger.ReturnShortAbsence => ReturnShortAbsenceIds,
            PracticeCopyTrigger.ReturnLongAbsence => ReturnLongAbsenceIds,
            PracticeCopyTrigger.ResumeManualPause => ResumeManualPauseIds,
            PracticeCopyTrigger.ResumeBackground => ResumeBackgroundIds,
            PracticeCopyTrigger.NeutralReady => NeutralReadyIds,
            PracticeCopyTrigger.NeutralPaused => NeutralPausedIds,
            _ => []
        };

    /// <inheritdoc/>
    public string? GetText(string messageId, string locale)
    {
        var lang = locale.Length >= 2 ? locale[..2].ToLowerInvariant() : "en";

        var dictionary = lang switch
        {
            "de" => GermanStrings,
            "ru" => RussianStrings,
            _ => EnglishStrings,
        };

        if (dictionary.TryGetValue(messageId, out var text))
        {
            return text;
        }

        // Fallback: try English for missing locale entry.
        return EnglishStrings.TryGetValue(messageId, out var enText) ? enText : null;
    }
}
