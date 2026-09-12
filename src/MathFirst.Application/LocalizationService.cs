using System.Globalization;
using MathFirst.Domain;

namespace MathFirst.Application;

public sealed class LocalizationService : ILocalizationService
{
    private readonly IPreferenceStore? _preferenceStore;
    private string _persistedPreference = LanguagePreferencePolicy.SystemPreferenceCode;
    private string? _previewLanguage;
    private string? _deviceCultureName;

    public event EventHandler? UiLanguageChanged;

    public LocalizationService(IPreferenceStore? preferenceStore = null, string? initialDeviceCulture = null)
    {
        _preferenceStore = preferenceStore;
        _deviceCultureName = initialDeviceCulture;
        if (_preferenceStore is not null)
        {
            _persistedPreference = _preferenceStore.GetLanguagePreference();
        }
    }

    public string SelectedLanguagePreference => _previewLanguage ?? _persistedPreference;

    public string CurrentUiLanguage
    {
        get
        {
            var active = SelectedLanguagePreference;
            return LanguagePreferencePolicy.Resolve(active, _deviceCultureName ?? CultureInfo.CurrentUICulture.Name);
        }
    }

    public string this[string key] => GetString(key);

    public string this[string key, params object[] args] => GetString(key, args);

    public string GetString(string key, params object[] args)
    {
        var lang = CurrentUiLanguage;
        var dictionary = lang switch
        {
            LanguagePreferencePolicy.GermanLanguageCode => GermanStrings,
            LanguagePreferencePolicy.RussianLanguageCode => RussianStrings,
            _ => EnglishStrings
        };

        if (!dictionary.TryGetValue(key, out var template) && !EnglishStrings.TryGetValue(key, out template))
        {
            template = key;
        }

        if (args is { Length: > 0 })
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        return template;
    }

    public void ApplyLanguagePreference(string languagePreference, string? deviceCulture = null)
    {
        var oldLang = CurrentUiLanguage;
        var oldPref = _persistedPreference;
        var hadPreview = _previewLanguage is not null;

        if (deviceCulture is not null)
        {
            _deviceCultureName = deviceCulture;
        }

        _persistedPreference = languagePreference;
        _previewLanguage = null;
        _preferenceStore?.SetLanguagePreference(languagePreference);

        var newLang = CurrentUiLanguage;
        if (hadPreview || !string.Equals(oldPref, languagePreference, StringComparison.OrdinalIgnoreCase) || !string.Equals(oldLang, newLang, StringComparison.OrdinalIgnoreCase))
        {
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyPreviewLanguage(string languageCode)
    {
        if (string.Equals(_previewLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var oldLang = CurrentUiLanguage;
        _previewLanguage = languageCode;
        var newLang = CurrentUiLanguage;

        if (!string.Equals(oldLang, newLang, StringComparison.OrdinalIgnoreCase) || _previewLanguage is not null)
        {
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearPreview()
    {
        if (_previewLanguage is not null)
        {
            _previewLanguage = null;
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly Dictionary<string, string> EnglishStrings = new(StringComparer.Ordinal)
    {
        ["App_Title"] = "MathFirst",
        ["App_Subtitle"] = "Speed and accuracy in mental arithmetic.",
        ["NotFound_Title"] = "Page not found",
        ["NotFound_Description"] = "The requested page could not be found.",
        ["Training_Score"] = "Correct: {0} / {1}",
        ["Training_OperationProgressStage"] = "{0}: progression stage {1}",
        ["Training_OperationProgressUnavailable"] = "{0}: progression unavailable",
        ["Training_Correct"] = "Correct!",
        ["Training_Incorrect"] = "Incorrect. The answer is {0}.",
        ["Training_IncorrectTitle"] = "Incorrect",
        ["Training_TimeExpired"] = "Time expired.",
        ["Training_YourAnswer"] = "Your answer: {0}",
        ["Training_CorrectAnswer"] = "Correct answer: {0}",
        ["Training_TimerAriaLabel"] = "Time remaining: {0} seconds",
        ["Training_Loading"] = "Loading…",
        ["Training_AnswerInputAriaLabel"] = "Answer input",
        ["Training_EnterNumber"] = "Please enter a number.",
        ["Training_EnterValidNumber"] = "Please enter a valid decimal number.",
        ["Training_ReadyTitle"] = "Ready to practice?",
        ["Training_Start"] = "Start",
        ["Training_PausedTitle"] = "Paused",
        ["Training_ResumePractice"] = "Resume practice",
        ["Training_Pause"] = "Pause",
        ["Training_PersistenceFailureTitle"] = "Progress could not be saved.",
        ["Training_Retry"] = "Retry",
        ["Training_TeachingTitle"] = "Remember this fact",
        ["Training_TeachingExplanation"] = "Notice the equation and correct result before continuing.",
        ["Training_CheckInTitle"] = "Practice Check-in",
        ["Training_CheckInCorrect"] = "Correct: {0} / {1}",
        ["Training_CheckInSpeed"] = "Median correct speed: {0}",
        ["Training_KeepGoing"] = "Keep Going",
        ["Training_TakeBreak"] = "Take a Break",
        ["Training_Unavailable"] = "—",
        ["Training_SettingsAction"] = "Settings",
        ["Common_Back"] = "Back",
        ["Common_Continue"] = "Continue",
        ["Common_Save"] = "Save",
        ["Common_Start"] = "Get Started",
        ["Common_Close"] = "Close",
        ["Common_Cancel"] = "Cancel",
        ["Common_Confirm"] = "Confirm",
        ["Onboarding_WelcomeTitle"] = "Welcome to MathFirst",
        ["Onboarding_Concept"] = "Solve arithmetic facts, answer correctly and quickly, and MathFirst automatically adapts your practice.",
        ["Onboarding_SystemLanguageDetected"] = "Detected system language: {0}",
        ["Onboarding_KeypadTitle"] = "Choose your number keypad",
        ["Onboarding_TutorialTitle"] = "How MathFirst works",
        ["Onboarding_TutorialStep1Title"] = "Accuracy and Speed",
        ["Onboarding_TutorialStep1Text"] = "Correctness matters most. Answering correctly and quickly helps you progress faster, and available answer time adapts as you practice.",
        ["Onboarding_TutorialStep2Title"] = "Adaptive Repetition",
        ["Onboarding_TutorialStep2Text"] = "Secure facts appear less often, while mistakes and weaker facts return for more practice so you master them reliably.",
        ["Onboarding_TutorialStep3Title"] = "Independent Progress",
        ["Onboarding_TutorialStep3Text"] = "MathFirst guides each operation independently so you strengthen new and challenging facts step by step.",
        ["Onboarding_StepLanguage_Title"] = "Select Language",
        ["Onboarding_StepLanguage_Desc"] = "Choose the language for the MathFirst user interface.",
        ["Onboarding_StepTheme_Title"] = "Select Appearance",
        ["Onboarding_StepTheme_Desc"] = "Choose how MathFirst looks on your screen.",
        ["Onboarding_StepReady_Title"] = "Ready to practice",
        ["Onboarding_StepReady_Desc"] = "Your arithmetic practice is ready. The answer timer starts only when you select Get Started.",
        ["Settings_Title"] = "Settings",
        ["Settings_UILanguage"] = "UI Language",
        ["Settings_UILanguageSystem"] = "System",
        ["Settings_English"] = "English",
        ["Settings_German"] = "Deutsch",
        ["Settings_Russian"] = "Русский",
        ["Settings_Appearance"] = "Appearance",
        ["Settings_AppearanceSystem"] = "System",
        ["Settings_AppearanceLight"] = "Light",
        ["Settings_AppearanceDark"] = "Dark",
        ["Settings_AppearanceChangedTo"] = "Appearance changed to {0}.",
        ["Settings_VersionBuild"] = "Version {0} (Build {1})",
        ["Keypad_NumberKeypad"] = "Number keypad",
        ["Keypad_Phone"] = "Phone keypad",
        ["Keypad_Numpad"] = "PC numpad",
        ["Keypad_Explanation"] = "Controls the on-screen number layout shown during MathFirst practice.",
        ["Keypad_Backspace"] = "Backspace",
        ["Keypad_ChangedTo"] = "Number keypad changed to {0}.",
        ["Settings_LanguageChangedTo"] = "Language changed to {0}.",
        ["Settings_PracticeSectionTitle"] = "Practice",
        ["Settings_OperationsTitle"] = "Operations",
        ["Settings_OperationsHelp"] = "Choose which arithmetic operations are included in practice. At least one operation must remain enabled.",
        ["Settings_PracticeTimeTitle"] = "Practice time",
        ["Settings_PracticeTimeHelp"] = "Adjust the answer time available for each exercise.",
        ["PracticeTime_Standard"] = "Standard",
        ["PracticeTime_30s"] = "30 s",
        ["PracticeTime_45s"] = "45 s",
        ["PracticeTime_60s"] = "60 s",
        ["Settings_OperationsChanged"] = "Practice operations updated.",
        ["Settings_PracticeTimeChangedTo"] = "Practice time changed to {0}.",
        ["Operation_Addition"] = "Addition",
        ["Operation_Subtraction"] = "Subtraction",
        ["Operation_Multiplication"] = "Multiplication",
        ["Operation_Division"] = "Division",
        ["Diagnostics_Title"] = "Developer / Testing Diagnostics",
        ["Diagnostics_Description"] = "Read-only diagnostic telemetry from the learner database and practice session.",
        ["Diagnostics_TotalAttempts"] = "Total Attempts",
        ["Diagnostics_Accuracy"] = "Correct / Accuracy",
        ["Diagnostics_AnswerDeadline"] = "Current Answer Deadline",
        ["Diagnostics_LastLatency"] = "Last Response Latency",
        ["Diagnostics_LatencyFluent"] = "Fluent (≤ 2.50 s)",
        ["Diagnostics_LatencySlow"] = "Slow (> 2.50 s)",
        ["Diagnostics_DatabasePath"] = "Database File",
        ["Diagnostics_SchemaVersion"] = "Schema / Revision",
        ["Diagnostics_FsrsScheduler"] = "Spaced Repetition Scheduler",
        ["Diagnostics_FsrsModel"] = "FSRS-6 (FSRS.Core 1.0.7, 95% Retention)",
        ["Diagnostics_PracticePosition"] = "Practice Position (Task Counter)",
        ["Diagnostics_FsrsCardsTracked"] = "FSRS Cards Tracked",
        ["Diagnostics_FsrsDueCards"] = "FSRS Cards Due",
        ["Diagnostics_CurrentFactFsrs"] = "Current Fact FSRS State",
        ["Diagnostics_Group_Learning"] = "Operation Bands",
        ["Diagnostics_OperationProgress"] = "Band {0}: {1} · started at #{2}",
        ["Diagnostics_Unavailable"] = "Unavailable",
        ["Diagnostics_Group_Scheduler"] = "FSRS-6 Task Scheduler",
        ["Diagnostics_Group_Storage"] = "Timing & Storage",
        ["DangerZone_Title"] = "Danger Zone",
        ["DangerZone_Desc"] = "Testing and reset operations for progression and device storage.",
        ["Reset_LearningProgress_Title"] = "Reset Learning Progress",
        ["Reset_LearningProgress_Desc"] = "Clears learning state and attempt history. Language, appearance, and onboarding remain unchanged.",
        ["Reset_LearningProgress_Confirm"] = "Are you sure you want to clear all learning progress?",
        ["Reset_LearningProgress_Success"] = "Learning progress has been cleared and starts over.",
        ["Reset_UiPreferences_Title"] = "Restore Default Settings",
        ["Reset_UiPreferences_Desc"] = "Restores language and appearance to System, the number keypad to Phone keypad, and requires onboarding again. Learning progress is preserved.",
        ["Reset_UiPreferences_Confirm"] = "Restore default settings and show onboarding again? Your learning progress will remain intact.",
        ["Reset_UiPreferences_Action"] = "Restore Defaults",
        ["Reset_UiPreferences_Success"] = "Default settings have been restored.",
        ["Reset_FullLocal_Title"] = "Reset All Application Data",
        ["Reset_FullLocal_Desc"] = "Resets learner data, onboarding, language, appearance, and number keypad to a fresh installation state.",
        ["Reset_FullLocal_Confirm"] = "Reset all learner data and settings? This action cannot be undone.",
        ["Reset_FullLocal_Action"] = "Reset Data",
        ["Reset_FullLocal_Success"] = "Full reset complete. Application returned to fresh install state."
    };

    private static readonly Dictionary<string, string> GermanStrings = new(StringComparer.Ordinal)
    {
        ["App_Title"] = "MathFirst",
        ["App_Subtitle"] = "Schnelligkeit und Sicherheit beim Kopfrechnen.",
        ["NotFound_Title"] = "Seite nicht gefunden",
        ["NotFound_Description"] = "Die angeforderte Seite wurde nicht gefunden.",
        ["Training_Score"] = "Richtig: {0} / {1}",
        ["Training_OperationProgressStage"] = "{0}: Fortschrittsstufe {1}",
        ["Training_OperationProgressUnavailable"] = "{0}: Fortschritt nicht verfügbar",
        ["Training_Correct"] = "Richtig!",
        ["Training_Incorrect"] = "Falsch. Das Ergebnis ist {0}.",
        ["Training_IncorrectTitle"] = "Falsch",
        ["Training_TimeExpired"] = "Zeit abgelaufen.",
        ["Training_YourAnswer"] = "Deine Antwort: {0}",
        ["Training_CorrectAnswer"] = "Richtige Antwort: {0}",
        ["Training_TimerAriaLabel"] = "Verbleibende Zeit: {0} Sekunden",
        ["Training_Loading"] = "Wird geladen…",
        ["Training_AnswerInputAriaLabel"] = "Antwortfeld",
        ["Training_EnterNumber"] = "Bitte eine Zahl eingeben.",
        ["Training_EnterValidNumber"] = "Bitte eine gültige Dezimalzahl eingeben.",
        ["Training_ReadyTitle"] = "Bereit zum Üben?",
        ["Training_Start"] = "Los geht's",
        ["Training_PausedTitle"] = "Pausiert",
        ["Training_ResumePractice"] = "Weiterüben",
        ["Training_Pause"] = "Pausieren",
        ["Training_PersistenceFailureTitle"] = "Fortschritt konnte nicht gespeichert werden.",
        ["Training_Retry"] = "Erneut versuchen",
        ["Training_TeachingTitle"] = "Merke dir diese Aufgabe",
        ["Training_TeachingExplanation"] = "Präge dir die Gleichung und das richtige Ergebnis ein, bevor du weitermachst.",
        ["Training_CheckInTitle"] = "Übungs-Zwischenstand",
        ["Training_CheckInCorrect"] = "Richtig: {0} / {1}",
        ["Training_CheckInSpeed"] = "Mittlere Zeit (richtig): {0}",
        ["Training_KeepGoing"] = "Weiterüben",
        ["Training_TakeBreak"] = "Pause machen",
        ["Training_Unavailable"] = "—",
        ["Training_SettingsAction"] = "Einstellungen",
        ["Common_Back"] = "Zurück",
        ["Common_Continue"] = "Weiter",
        ["Common_Save"] = "Speichern",
        ["Common_Start"] = "Los geht's",
        ["Common_Close"] = "Schließen",
        ["Common_Cancel"] = "Abbrechen",
        ["Common_Confirm"] = "Bestätigen",
        ["Onboarding_WelcomeTitle"] = "Willkommen bei MathFirst",
        ["Onboarding_Concept"] = "Löse Rechenaufgaben richtig und schnell – MathFirst passt dein Training automatisch an.",
        ["Onboarding_SystemLanguageDetected"] = "Erkannte Systemsprache: {0}",
        ["Onboarding_KeypadTitle"] = "Zahlentastatur auswählen",
        ["Onboarding_TutorialTitle"] = "So funktioniert MathFirst",
        ["Onboarding_TutorialStep1Title"] = "Genauigkeit und Tempo",
        ["Onboarding_TutorialStep1Text"] = "Richtigkeit steht an erster Stelle. Richtiges und zügiges Antworten hilft dir, schneller voranzukommen, und die verfügbare Antwortzeit passt sich beim Üben an.",
        ["Onboarding_TutorialStep2Title"] = "Angepasste Wiederholung",
        ["Onboarding_TutorialStep2Text"] = "Sichere Aufgaben erscheinen seltener, während Fehler und unsichere Aufgaben für mehr Übung zurückkehren, damit du sie zuverlässig meisterst.",
        ["Onboarding_TutorialStep3Title"] = "Unabhängiger Fortschritt",
        ["Onboarding_TutorialStep3Text"] = "MathFirst führt jede Rechenart unabhängig, damit du neue und anspruchsvolle Aufgaben Schritt für Schritt festigst.",
        ["Onboarding_StepLanguage_Title"] = "Sprache auswählen",
        ["Onboarding_StepLanguage_Desc"] = "Wähle die Sprache für die MathFirst-Benutzeroberfläche.",
        ["Onboarding_StepTheme_Title"] = "Erscheinungsbild auswählen",
        ["Onboarding_StepTheme_Desc"] = "Wähle, wie MathFirst auf deinem Bildschirm dargestellt wird.",
        ["Onboarding_StepReady_Title"] = "Bereit zum Üben",
        ["Onboarding_StepReady_Desc"] = "Dein Rechentraining ist bereit. Der Antworttimer startet erst mit „Los geht's“.",
        ["Settings_Title"] = "Einstellungen",
        ["Settings_UILanguage"] = "Sprache",
        ["Settings_UILanguageSystem"] = "System",
        ["Settings_English"] = "English",
        ["Settings_German"] = "Deutsch",
        ["Settings_Russian"] = "Русский",
        ["Settings_Appearance"] = "Erscheinungsbild",
        ["Settings_AppearanceSystem"] = "System",
        ["Settings_AppearanceLight"] = "Hell",
        ["Settings_AppearanceDark"] = "Dunkel",
        ["Settings_AppearanceChangedTo"] = "Erscheinungsbild geändert auf {0}.",
        ["Settings_VersionBuild"] = "Version {0} (Build {1})",
        ["Keypad_NumberKeypad"] = "Zahlentastatur",
        ["Keypad_Phone"] = "Telefon-Tastatur",
        ["Keypad_Numpad"] = "PC-Ziffernblock",
        ["Keypad_Explanation"] = "Legt die Bildschirm-Zahlenanordnung für das Training mit MathFirst fest.",
        ["Keypad_Backspace"] = "Rücktaste",
        ["Keypad_ChangedTo"] = "Zahlentastatur geändert auf {0}.",
        ["Settings_LanguageChangedTo"] = "Sprache geändert auf {0}.",
        ["Settings_PracticeSectionTitle"] = "Training",
        ["Settings_OperationsTitle"] = "Rechenarten",
        ["Settings_OperationsHelp"] = "Wähle, welche Rechenarten im Training vorkommen. Mindestens eine Rechenart muss aktiv bleiben.",
        ["Settings_PracticeTimeTitle"] = "Übungszeit",
        ["Settings_PracticeTimeHelp"] = "Passe die verfügbare Antwortzeit für jede Aufgabe an.",
        ["PracticeTime_Standard"] = "Standard",
        ["PracticeTime_30s"] = "30 s",
        ["PracticeTime_45s"] = "45 s",
        ["PracticeTime_60s"] = "60 s",
        ["Settings_OperationsChanged"] = "Rechenarten für das Training aktualisiert.",
        ["Settings_PracticeTimeChangedTo"] = "Übungszeit geändert auf {0}.",
        ["Operation_Addition"] = "Addition",
        ["Operation_Subtraction"] = "Subtraktion",
        ["Operation_Multiplication"] = "Multiplikation",
        ["Operation_Division"] = "Division",
        ["Diagnostics_Title"] = "Entwickler- / Test-Diagnose",
        ["Diagnostics_Description"] = "Schreibgeschützte Diagnosewerte aus Lerndatenbank und Übungssitzung.",
        ["Diagnostics_TotalAttempts"] = "Gesamtversuche",
        ["Diagnostics_Accuracy"] = "Richtig / Trefferquote",
        ["Diagnostics_AnswerDeadline"] = "Aktuelle Antwortfrist",
        ["Diagnostics_LastLatency"] = "Letzte Antwortzeit",
        ["Diagnostics_LatencyFluent"] = "Flüssig (≤ 2,50 s)",
        ["Diagnostics_LatencySlow"] = "Langsam (> 2,50 s)",
        ["Diagnostics_DatabasePath"] = "Datenbankdatei",
        ["Diagnostics_SchemaVersion"] = "Schema / Revision",
        ["Diagnostics_FsrsScheduler"] = "Spaced-Repetition-Planer",
        ["Diagnostics_FsrsModel"] = "FSRS-6 (FSRS.Core 1.0.7, 95 % Retention)",
        ["Diagnostics_PracticePosition"] = "Übungsposition (Aufgabenzähler)",
        ["Diagnostics_FsrsCardsTracked"] = "FSRS-Karten verwaltet",
        ["Diagnostics_FsrsDueCards"] = "FSRS-Karten fällig",
        ["Diagnostics_CurrentFactFsrs"] = "Aktuelle Aufgabe FSRS-Status",
        ["Diagnostics_Group_Learning"] = "Operationsbänder",
        ["Diagnostics_OperationProgress"] = "Band {0}: {1} · begonnen bei #{2}",
        ["Diagnostics_Unavailable"] = "Nicht verfügbar",
        ["Diagnostics_Group_Scheduler"] = "FSRS-6-Aufgabenplaner",
        ["Diagnostics_Group_Storage"] = "Zeit & Speicher",
        ["DangerZone_Title"] = "Gefahrenbereich",
        ["DangerZone_Desc"] = "Test- und Reset-Aktionen für Lernfortschritt und Gerätespeicher.",
        ["Reset_LearningProgress_Title"] = "Lernfortschritt zurücksetzen",
        ["Reset_LearningProgress_Desc"] = "Löscht Lernstand und Versuchshistorie. Sprache, Erscheinungsbild und Onboarding bleiben erhalten.",
        ["Reset_LearningProgress_Confirm"] = "Möchtest du den gesamten Lernfortschritt wirklich löschen?",
        ["Reset_LearningProgress_Success"] = "Der Lernfortschritt wurde gelöscht und beginnt von vorn.",
        ["Reset_UiPreferences_Title"] = "Standardeinstellungen wiederherstellen",
        ["Reset_UiPreferences_Desc"] = "Stellt Sprache und Erscheinungsbild auf System sowie die Zahlentastatur auf Telefon-Tastatur zurück und macht das Onboarding erneut erforderlich. Der Lernfortschritt bleibt erhalten.",
        ["Reset_UiPreferences_Confirm"] = "Standardeinstellungen wiederherstellen und das Onboarding erneut anzeigen? Dein Lernfortschritt bleibt erhalten.",
        ["Reset_UiPreferences_Action"] = "Standards wiederherstellen",
        ["Reset_UiPreferences_Success"] = "Standardeinstellungen wurden wiederhergestellt.",
        ["Reset_FullLocal_Title"] = "Alle Anwendungsdaten zurücksetzen",
        ["Reset_FullLocal_Desc"] = "Setzt Lerndaten, Onboarding, Sprache, Erscheinungsbild und Zahlentastatur auf den Zustand einer Neuinstallation zurück.",
        ["Reset_FullLocal_Confirm"] = "Alle Lerndaten und Einstellungen zurücksetzen? Diese Aktion kann nicht rückgängig gemacht werden.",
        ["Reset_FullLocal_Action"] = "Daten zurücksetzen",
        ["Reset_FullLocal_Success"] = "Vollständiger Reset abgeschlossen. App befindet sich im Zustand der Neuinstallation."
    };

    private static readonly Dictionary<string, string> RussianStrings = new(StringComparer.Ordinal)
    {
        ["App_Title"] = "MathFirst",
        ["App_Subtitle"] = "Скорость и точность устного счета.",
        ["NotFound_Title"] = "Страница не найдена",
        ["NotFound_Description"] = "Запрошенная страница не найдена.",
        ["Training_Score"] = "Правильно: {0} / {1}",
        ["Training_OperationProgressStage"] = "{0}: этап прогресса {1}",
        ["Training_OperationProgressUnavailable"] = "{0}: прогресс недоступен",
        ["Training_Correct"] = "Правильно!",
        ["Training_Incorrect"] = "Неверно. Правильный ответ: {0}.",
        ["Training_IncorrectTitle"] = "Неверно",
        ["Training_TimeExpired"] = "Время вышло.",
        ["Training_YourAnswer"] = "Твой ответ: {0}",
        ["Training_CorrectAnswer"] = "Правильный ответ: {0}",
        ["Training_TimerAriaLabel"] = "Оставшееся время: {0} сек.",
        ["Training_Loading"] = "Загрузка…",
        ["Training_AnswerInputAriaLabel"] = "Поле ответа",
        ["Training_EnterNumber"] = "Пожалуйста, введите число.",
        ["Training_EnterValidNumber"] = "Пожалуйста, введите допустимое десятичное число.",
        ["Training_ReadyTitle"] = "Пора заниматься?",
        ["Training_Start"] = "Начать",
        ["Training_PausedTitle"] = "Пауза",
        ["Training_ResumePractice"] = "Продолжить занятие",
        ["Training_Pause"] = "Приостановить",
        ["Training_PersistenceFailureTitle"] = "Не удалось сохранить прогресс.",
        ["Training_Retry"] = "Повторить",
        ["Training_TeachingTitle"] = "Запомни этот пример",
        ["Training_TeachingExplanation"] = "Обрати внимание на пример и правильный ответ перед продолжением.",
        ["Training_CheckInTitle"] = "Промежуточный итог",
        ["Training_CheckInCorrect"] = "Правильно: {0} / {1}",
        ["Training_CheckInSpeed"] = "Медианное время (верно): {0}",
        ["Training_KeepGoing"] = "Продолжить",
        ["Training_TakeBreak"] = "Сделать перерыв",
        ["Training_Unavailable"] = "—",
        ["Training_SettingsAction"] = "Настройки",
        ["Common_Back"] = "Назад",
        ["Common_Continue"] = "Продолжить",
        ["Common_Save"] = "Сохранить",
        ["Common_Start"] = "Начать",
        ["Common_Close"] = "Закрыть",
        ["Common_Cancel"] = "Отмена",
        ["Common_Confirm"] = "Подтвердить",
        ["Onboarding_WelcomeTitle"] = "Добро пожаловать в MathFirst",
        ["Onboarding_Concept"] = "Решайте примеры правильно и быстро — MathFirst автоматически адаптирует тренировку.",
        ["Onboarding_SystemLanguageDetected"] = "Определённый язык системы: {0}",
        ["Onboarding_KeypadTitle"] = "Выберите цифровую клавиатуру",
        ["Onboarding_TutorialTitle"] = "Как работает MathFirst",
        ["Onboarding_TutorialStep1Title"] = "Точность и темп",
        ["Onboarding_TutorialStep1Text"] = "Правильность важнее всего. Правильные и быстрые ответы помогают продвигаться быстрее, а доступное время на ответ адаптируется по ходу тренировки.",
        ["Onboarding_TutorialStep2Title"] = "Адаптивное повторение",
        ["Onboarding_TutorialStep2Text"] = "Хорошо усвоенные примеры появляются реже, а ошибки и сложные примеры возвращаются для дополнительной тренировки, пока не закрепятся.",
        ["Onboarding_TutorialStep3Title"] = "Независимый прогресс",
        ["Onboarding_TutorialStep3Text"] = "MathFirst развивает каждую операцию независимо, помогая шаг за шагом осваивать новые и сложные примеры.",
        ["Onboarding_StepLanguage_Title"] = "Выберите язык",
        ["Onboarding_StepLanguage_Desc"] = "Выберите язык интерфейса MathFirst.",
        ["Onboarding_StepTheme_Title"] = "Оформление",
        ["Onboarding_StepTheme_Desc"] = "Выберите тему оформления MathFirst.",
        ["Onboarding_StepReady_Title"] = "Всё готово",
        ["Onboarding_StepReady_Desc"] = "Тренировка по арифметике готова. Таймер ответа запустится только после нажатия «Начать».",
        ["Settings_Title"] = "Настройки",
        ["Settings_UILanguage"] = "Язык интерфейса",
        ["Settings_UILanguageSystem"] = "Системный",
        ["Settings_English"] = "English",
        ["Settings_German"] = "Deutsch",
        ["Settings_Russian"] = "Русский",
        ["Settings_Appearance"] = "Оформление",
        ["Settings_AppearanceSystem"] = "Системная",
        ["Settings_AppearanceLight"] = "Светлая",
        ["Settings_AppearanceDark"] = "Темная",
        ["Settings_AppearanceChangedTo"] = "Тема изменена на: {0}.",
        ["Settings_VersionBuild"] = "Версия {0} (сборка {1})",
        ["Keypad_NumberKeypad"] = "Цифровая клавиатура",
        ["Keypad_Phone"] = "Телефонная клавиатура",
        ["Keypad_Numpad"] = "Цифровой блок ПК",
        ["Keypad_Explanation"] = "Определяет расположение экранных цифр во время тренировки MathFirst.",
        ["Keypad_Backspace"] = "Удалить символ",
        ["Keypad_ChangedTo"] = "Цифровая клавиатура изменена на: {0}.",
        ["Settings_LanguageChangedTo"] = "Язык изменен на: {0}.",
        ["Settings_PracticeSectionTitle"] = "Тренировка",
        ["Settings_OperationsTitle"] = "Арифметические действия",
        ["Settings_OperationsHelp"] = "Выберите действия для тренировки. Хотя бы одно действие должно оставаться включенным.",
        ["Settings_PracticeTimeTitle"] = "Время на ответ",
        ["Settings_PracticeTimeHelp"] = "Настройте доступное время для каждого примера.",
        ["PracticeTime_Standard"] = "Стандарт",
        ["PracticeTime_30s"] = "30 сек.",
        ["PracticeTime_45s"] = "45 сек.",
        ["PracticeTime_60s"] = "60 сек.",
        ["Settings_OperationsChanged"] = "Выбранные действия обновлены.",
        ["Settings_PracticeTimeChangedTo"] = "Время на ответ изменено на {0}.",
        ["Operation_Addition"] = "Сложение",
        ["Operation_Subtraction"] = "Вычитание",
        ["Operation_Multiplication"] = "Умножение",
        ["Operation_Division"] = "Деление",
        ["Diagnostics_Title"] = "Диагностика разработчика / тестирования",
        ["Diagnostics_Description"] = "Диагностические данные только для чтения из базы обучения и сеанса практики.",
        ["Diagnostics_TotalAttempts"] = "Всего попыток",
        ["Diagnostics_Accuracy"] = "Верно / Точность",
        ["Diagnostics_AnswerDeadline"] = "Текущий лимит ответа",
        ["Diagnostics_LastLatency"] = "Время последнего ответа",
        ["Diagnostics_LatencyFluent"] = "Бегло (≤ 2,50 с)",
        ["Diagnostics_LatencySlow"] = "Медленно (> 2,50 с)",
        ["Diagnostics_DatabasePath"] = "Файл базы данных",
        ["Diagnostics_SchemaVersion"] = "Схема / Ревизия",
        ["Diagnostics_FsrsScheduler"] = "Планировщик интервалов",
        ["Diagnostics_FsrsModel"] = "FSRS-6 (FSRS.Core 1.0.7, 95% удержание)",
        ["Diagnostics_PracticePosition"] = "Позиция практики (счетчик)",
        ["Diagnostics_FsrsCardsTracked"] = "FSRS карточек",
        ["Diagnostics_FsrsDueCards"] = "FSRS карточек к повторению",
        ["Diagnostics_CurrentFactFsrs"] = "Текущая карточка FSRS",
        ["Diagnostics_Group_Learning"] = "Банды операций",
        ["Diagnostics_OperationProgress"] = "Банд {0}: {1} · начат на #{2}",
        ["Diagnostics_Unavailable"] = "Недоступно",
        ["Diagnostics_Group_Scheduler"] = "Планировщик FSRS-6",
        ["Diagnostics_Group_Storage"] = "Время и хранилище",
        ["DangerZone_Title"] = "Опасная зона",
        ["DangerZone_Desc"] = "Действия по сбросу прогресса обучения и настроек устройства.",
        ["Reset_LearningProgress_Title"] = "Сбросить прогресс обучения",
        ["Reset_LearningProgress_Desc"] = "Очищает состояние обучения и историю попыток. Язык, оформление и онбординг сохраняются.",
        ["Reset_LearningProgress_Confirm"] = "Очистить весь прогресс обучения?",
        ["Reset_LearningProgress_Success"] = "Прогресс обучения очищен и начинается заново.",
        ["Reset_UiPreferences_Title"] = "Восстановить настройки по умолчанию",
        ["Reset_UiPreferences_Desc"] = "Возвращает язык и оформление к системным значениям, выбирает телефонную клавиатуру и снова требует пройти онбординг. Прогресс обучения сохраняется.",
        ["Reset_UiPreferences_Confirm"] = "Восстановить настройки по умолчанию и снова показать онбординг? Прогресс обучения сохранится.",
        ["Reset_UiPreferences_Action"] = "Восстановить настройки",
        ["Reset_UiPreferences_Success"] = "Настройки по умолчанию восстановлены.",
        ["Reset_FullLocal_Title"] = "Сбросить все данные приложения",
        ["Reset_FullLocal_Desc"] = "Сбрасывает данные обучения, онбординг, язык, оформление и цифровую клавиатуру до состояния новой установки.",
        ["Reset_FullLocal_Confirm"] = "Сбросить все данные обучения и настройки? Это действие нельзя отменить.",
        ["Reset_FullLocal_Action"] = "Сбросить данные",
        ["Reset_FullLocal_Success"] = "Полный сброс выполнен. Приложение возвращено в исходное состояние."
    };
}
