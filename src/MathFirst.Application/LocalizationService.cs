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
        ["Training_Score"] = "Correct: {0} / {1}",
        ["Training_Correct"] = "Correct!",
        ["Training_Incorrect"] = "Incorrect. The answer is {0}.",
        ["Training_IncorrectTitle"] = "Incorrect",
        ["Training_TimeExpired"] = "Time expired.",
        ["Training_YourAnswer"] = "Your answer: {0}",
        ["Training_CorrectAnswer"] = "Correct answer: {0}",
        ["Training_TimerAriaLabel"] = "Time remaining: {0} seconds",
        ["Training_EnterNumber"] = "Please enter a number.",
        ["Training_EnterValidNumber"] = "Please enter a valid whole number.",
        ["Training_SubmitAction"] = "Submit",
        ["Training_NextAction"] = "Next",
        ["Training_SettingsAction"] = "Settings",
        ["Common_Back"] = "Back",
        ["Common_Continue"] = "Continue",
        ["Common_Save"] = "Save",
        ["Common_Start"] = "Get Started",
        ["Common_Close"] = "Close",
        ["Common_Cancel"] = "Cancel",
        ["Common_Confirm"] = "Confirm",
        ["Onboarding_WelcomeTitle"] = "Welcome to MathFirst",
        ["Onboarding_Concept"] = "MathFirst develops fluent mental arithmetic through focused practice.",
        ["Onboarding_StepLanguage_Title"] = "Select Language",
        ["Onboarding_StepLanguage_Desc"] = "Choose the language for the MathFirst user interface.",
        ["Onboarding_StepTheme_Title"] = "Select Appearance",
        ["Onboarding_StepTheme_Desc"] = "Choose how MathFirst looks on your screen.",
        ["Onboarding_StepReady_Title"] = "Ready to Practice!",
        ["Onboarding_StepReady_Desc"] = "This preview begins with 0..1 facts across all four operations and expands automatically as you demonstrate fast, accurate mastery.",
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
        ["Settings_LanguageChangedTo"] = "Language changed to {0}.",
        ["Operation_Addition"] = "Addition",
        ["Operation_Subtraction"] = "Subtraction",
        ["Operation_Multiplication"] = "Multiplication",
        ["Operation_Division"] = "Division",
        ["Phase_IntroducingAddition"] = "Introducing Addition",
        ["Phase_IntroducingSubtraction"] = "Introducing Subtraction",
        ["Phase_IntroducingMultiplication"] = "Introducing Multiplication",
        ["Phase_IntroducingDivision"] = "Introducing Division",
        ["Phase_Checkpoint"] = "Mixed Checkpoint (0..{0})",
        ["Phase_MixedPractice"] = "Adaptive Mixed Practice",
        ["Training_CheckpointBadge"] = "Checkpoint {0} / {1}",
        ["Diagnostics_CheckpointStatus"] = "Checkpoint Phase",
        ["Diagnostics_CheckpointLevel"] = "Checkpoint Level",
        ["Diagnostics_CheckpointProgress"] = "Checkpoint Progress",
        ["Diagnostics_CheckpointScore"] = "Checkpoint Accuracy",
        ["Diagnostics_CheckpointInactive"] = "None (Standard Practice)",
        ["Diagnostics_AllIntroductionsComplete"] = "All Levels Complete",
        ["Diagnostics_Title"] = "Developer / Testing Diagnostics",
        ["Diagnostics_CurrentPhase"] = "Current Phase",
        ["Diagnostics_CurrentOperation"] = "Current Operation",
        ["Diagnostics_Turn"] = "Introduction Turn",
        ["Diagnostics_Ranges"] = "Operand Ranges (+ / - / × / ÷)",
        ["Diagnostics_CurrentRange"] = "Current Operand Range",
        ["Diagnostics_ActiveFacts"] = "Total Active Facts",
        ["Diagnostics_MasteredFacts"] = "Mastered Facts",
        ["Diagnostics_LearningFacts"] = "Learning / Unmastered",
        ["Diagnostics_NewFactsRemaining"] = "Turn New Facts Remaining",
        ["Diagnostics_OperationsIntroduced"] = "Operations Introduced",
        ["Diagnostics_TotalAttempts"] = "Total Attempts",
        ["Diagnostics_Accuracy"] = "Correct / Accuracy",
        ["Diagnostics_Remediation"] = "Pending Remediation",
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
        ["Diagnostics_Group_Learning"] = "Learning Progression",
        ["Diagnostics_Group_Checkpoint"] = "Checkpoint Status",
        ["Diagnostics_Group_Scheduler"] = "FSRS-6 Task Scheduler",
        ["Diagnostics_Group_Storage"] = "Timing & Storage",
        ["DangerZone_Title"] = "Danger Zone",
        ["DangerZone_Desc"] = "Testing and reset operations for progression and device storage.",
        ["Reset_LearningProgress_Title"] = "Reset Learning Progress",
        ["Reset_LearningProgress_Desc"] = "Deletes attempt history, item mastery, and range progress. Resets to Addition 0..1 introduction. Preserves language, theme, and onboarding.",
        ["Reset_LearningProgress_Confirm"] = "Are you sure you want to reset all learning progress? This will return progression to Addition 0..1 introduction.",
        ["Reset_LearningProgress_Success"] = "Learning progress has been reset to Addition 0..1 introduction.",
        ["Reset_UiPreferences_Title"] = "Reset Onboarding & Preferences",
        ["Reset_UiPreferences_Desc"] = "Resets theme and language to System, and restarts onboarding. Preserves learner database.",
        ["Reset_UiPreferences_Confirm"] = "Are you sure you want to reset onboarding and UI preferences?",
        ["Reset_UiPreferences_Success"] = "Onboarding and UI preferences have been reset.",
        ["Reset_FullLocal_Title"] = "Full Local Reset",
        ["Reset_FullLocal_Desc"] = "Resets learner database, onboarding, language, and theme to fresh install state.",
        ["Reset_FullLocal_Confirm"] = "Are you sure you want to perform a FULL reset of all data and settings? The app will return to a fresh install state.",
        ["Reset_FullLocal_Success"] = "Full reset complete. Application returned to fresh install state."
    };

    private static readonly Dictionary<string, string> GermanStrings = new(StringComparer.Ordinal)
    {
        ["App_Title"] = "MathFirst",
        ["App_Subtitle"] = "Schnelligkeit und Sicherheit beim Kopfrechnen.",
        ["Training_Score"] = "Richtig: {0} / {1}",
        ["Training_Correct"] = "Richtig!",
        ["Training_Incorrect"] = "Falsch. Das Ergebnis ist {0}.",
        ["Training_IncorrectTitle"] = "Falsch",
        ["Training_TimeExpired"] = "Zeit abgelaufen.",
        ["Training_YourAnswer"] = "Deine Antwort: {0}",
        ["Training_CorrectAnswer"] = "Richtige Antwort: {0}",
        ["Training_TimerAriaLabel"] = "Verbleibende Zeit: {0} Sekunden",
        ["Training_EnterNumber"] = "Bitte eine Zahl eingeben.",
        ["Training_EnterValidNumber"] = "Bitte eine gültige ganze Zahl eingeben.",
        ["Training_SubmitAction"] = "Bestätigen",
        ["Training_NextAction"] = "Weiter",
        ["Training_SettingsAction"] = "Einstellungen",
        ["Common_Back"] = "Zurück",
        ["Common_Continue"] = "Weiter",
        ["Common_Save"] = "Speichern",
        ["Common_Start"] = "Loslegen",
        ["Common_Close"] = "Schließen",
        ["Common_Cancel"] = "Abbrechen",
        ["Common_Confirm"] = "Bestätigen",
        ["Onboarding_WelcomeTitle"] = "Willkommen bei MathFirst",
        ["Onboarding_Concept"] = "MathFirst trainiert mathematische Grundrechenarten mit direkter Abfrage.",
        ["Onboarding_StepLanguage_Title"] = "Sprache auswählen",
        ["Onboarding_StepLanguage_Desc"] = "Wähle die Sprache für die MathFirst-Benutzeroberfläche.",
        ["Onboarding_StepTheme_Title"] = "Erscheinungsbild auswählen",
        ["Onboarding_StepTheme_Desc"] = "Wähle, wie MathFirst auf deinem Bildschirm dargestellt wird.",
        ["Onboarding_StepReady_Title"] = "Bereit zum Üben!",
        ["Onboarding_StepReady_Desc"] = "Diese Vorschau beginnt mit 0..1-Aufgaben über alle vier Rechenarten und erweitert sich automatisch bei schneller und sicherer Beherrschung.",
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
        ["Settings_LanguageChangedTo"] = "Sprache geändert auf {0}.",
        ["Operation_Addition"] = "Addition",
        ["Operation_Subtraction"] = "Subtraktion",
        ["Operation_Multiplication"] = "Multiplikation",
        ["Operation_Division"] = "Division",
        ["Phase_IntroducingAddition"] = "Einführung Addition",
        ["Phase_IntroducingSubtraction"] = "Einführung Subtraktion",
        ["Phase_IntroducingMultiplication"] = "Einführung Multiplikation",
        ["Phase_IntroducingDivision"] = "Einführung Division",
        ["Phase_Checkpoint"] = "Gemischter Checkpoint (0..{0})",
        ["Phase_MixedPractice"] = "Adaptive gemischte Übung",
        ["Training_CheckpointBadge"] = "Checkpoint {0} / {1}",
        ["Diagnostics_CheckpointStatus"] = "Checkpoint-Phase",
        ["Diagnostics_CheckpointLevel"] = "Checkpoint-Stufe",
        ["Diagnostics_CheckpointProgress"] = "Checkpoint-Fortschritt",
        ["Diagnostics_CheckpointScore"] = "Checkpoint-Genauigkeit",
        ["Diagnostics_CheckpointInactive"] = "Keine (Standardübung)",
        ["Diagnostics_AllIntroductionsComplete"] = "Alle Stufen abgeschlossen",
        ["Diagnostics_Title"] = "Entwickler- / Test-Diagnose",
        ["Diagnostics_CurrentPhase"] = "Aktuelle Phase",
        ["Diagnostics_CurrentOperation"] = "Aktuelle Rechenart",
        ["Diagnostics_Turn"] = "Einführungs-Runde",
        ["Diagnostics_Ranges"] = "Zahlenbereiche (+ / - / × / ÷)",
        ["Diagnostics_CurrentRange"] = "Aktueller Zahlenbereich",
        ["Diagnostics_ActiveFacts"] = "Aktive Aufgaben gesamt",
        ["Diagnostics_MasteredFacts"] = "Gemeisterte Aufgaben",
        ["Diagnostics_LearningFacts"] = "Im Lernprozess",
        ["Diagnostics_NewFactsRemaining"] = "Verbleibende neue Aufgaben",
        ["Diagnostics_OperationsIntroduced"] = "Eingeführte Rechenarten",
        ["Diagnostics_TotalAttempts"] = "Gesamtversuche",
        ["Diagnostics_Accuracy"] = "Richtig / Trefferquote",
        ["Diagnostics_Remediation"] = "Wiederholungen ausstehend",
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
        ["Diagnostics_Group_Learning"] = "Lernfortschritt",
        ["Diagnostics_Group_Checkpoint"] = "Checkpoint-Status",
        ["Diagnostics_Group_Scheduler"] = "FSRS-6 Task-Planer",
        ["Diagnostics_Group_Storage"] = "Zeit & Speicher",
        ["DangerZone_Title"] = "Gefahrenbereich",
        ["DangerZone_Desc"] = "Test- und Reset-Aktionen für Lernfortschritt und Gerätespeicher.",
        ["Reset_LearningProgress_Title"] = "Lernfortschritt zurücksetzen",
        ["Reset_LearningProgress_Desc"] = "Löscht alle Versuche, Meisterschaften und Bereichsfortschritte. Setzt zurück auf Addition 0..1 Einführung. Behält Sprache, Erscheinungsbild und Onboarding.",
        ["Reset_LearningProgress_Confirm"] = "Möchtest du den gesamten Lernfortschritt wirklich unwiderruflich auf Addition 0..1 Einführung zurücksetzen?",
        ["Reset_LearningProgress_Success"] = "Lernfortschritt wurde auf Addition 0..1 Einführung zurückgesetzt.",
        ["Reset_UiPreferences_Title"] = "Onboarding & Einstellungen zurücksetzen",
        ["Reset_UiPreferences_Desc"] = "Setzt Erscheinungsbild und Sprache auf System sowie das Onboarding zurück. Behält die Lerndatenbank.",
        ["Reset_UiPreferences_Confirm"] = "Möchtest du Onboarding und UI-Einstellungen wirklich zurücksetzen?",
        ["Reset_UiPreferences_Success"] = "Onboarding und Einstellungen wurden zurückgesetzt.",
        ["Reset_FullLocal_Title"] = "Vollständiger lokaler Reset",
        ["Reset_FullLocal_Desc"] = "Setzt Lerndatenbank, Onboarding, Sprache und Erscheinungsbild auf den Zustand einer Neuinstallation zurück.",
        ["Reset_FullLocal_Confirm"] = "Möchtest du wirklich einen VOLLSTÄNDIGEN Reset aller Daten und Einstellungen durchführen? Die App startet anschließend wie neu installiert.",
        ["Reset_FullLocal_Success"] = "Vollständiger Reset abgeschlossen. App befindet sich im Zustand der Neuinstallation."
    };

    private static readonly Dictionary<string, string> RussianStrings = new(StringComparer.Ordinal)
    {
        ["App_Title"] = "MathFirst",
        ["App_Subtitle"] = "Скорость и точность устного счета.",
        ["Training_Score"] = "Правильно: {0} / {1}",
        ["Training_Correct"] = "Правильно!",
        ["Training_Incorrect"] = "Неверно. Правильный ответ: {0}.",
        ["Training_IncorrectTitle"] = "Неверно",
        ["Training_TimeExpired"] = "Время вышло.",
        ["Training_YourAnswer"] = "Твой ответ: {0}",
        ["Training_CorrectAnswer"] = "Правильный ответ: {0}",
        ["Training_TimerAriaLabel"] = "Оставшееся время: {0} сек.",
        ["Training_EnterNumber"] = "Пожалуйста, введите число.",
        ["Training_EnterValidNumber"] = "Пожалуйста, введите целое число.",
        ["Training_SubmitAction"] = "Ответить",
        ["Training_NextAction"] = "Далее",
        ["Training_SettingsAction"] = "Настройки",
        ["Common_Back"] = "Назад",
        ["Common_Continue"] = "Продолжить",
        ["Common_Save"] = "Сохранить",
        ["Common_Start"] = "Начать",
        ["Common_Close"] = "Закрыть",
        ["Common_Cancel"] = "Отмена",
        ["Common_Confirm"] = "Подтвердить",
        ["Onboarding_WelcomeTitle"] = "Добро пожаловать в MathFirst",
        ["Onboarding_Concept"] = "MathFirst развивает беглость устного счета через целенаправленную практику.",
        ["Onboarding_StepLanguage_Title"] = "Выберите язык",
        ["Onboarding_StepLanguage_Desc"] = "Выберите язык интерфейса MathFirst.",
        ["Onboarding_StepTheme_Title"] = "Оформление",
        ["Onboarding_StepTheme_Desc"] = "Выберите тему оформления MathFirst.",
        ["Onboarding_StepReady_Title"] = "Готовы к тренировке!",
        ["Onboarding_StepReady_Desc"] = "Эта версия начинается с примеров 0..1 по всем четырем операциям и автоматически расширяется по мере быстрого и безошибочного освоения.",
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
        ["Settings_LanguageChangedTo"] = "Язык изменен на: {0}.",
        ["Operation_Addition"] = "Сложение",
        ["Operation_Subtraction"] = "Вычитание",
        ["Operation_Multiplication"] = "Умножение",
        ["Operation_Division"] = "Деление",
        ["Phase_IntroducingAddition"] = "Знакомство со сложением",
        ["Phase_IntroducingSubtraction"] = "Знакомство с вычитанием",
        ["Phase_IntroducingMultiplication"] = "Знакомство с умножением",
        ["Phase_IntroducingDivision"] = "Знакомство с делением",
        ["Phase_Checkpoint"] = "Контрольная проверка (0..{0})",
        ["Phase_MixedPractice"] = "Адаптивная смешанная практика",
        ["Training_CheckpointBadge"] = "Контроль {0} / {1}",
        ["Diagnostics_CheckpointStatus"] = "Фаза контрольной проверки",
        ["Diagnostics_CheckpointLevel"] = "Уровень контрольной проверки",
        ["Diagnostics_CheckpointProgress"] = "Прогресс проверки",
        ["Diagnostics_CheckpointScore"] = "Точность проверки",
        ["Diagnostics_CheckpointInactive"] = "Нет (обычная практика)",
        ["Diagnostics_AllIntroductionsComplete"] = "Все уровни пройдены",
        ["Diagnostics_Title"] = "Диагностика разработчика / тестирования",
        ["Diagnostics_CurrentPhase"] = "Текущая фаза",
        ["Diagnostics_CurrentOperation"] = "Текущая операция",
        ["Diagnostics_Turn"] = "Текущая очередь",
        ["Diagnostics_Ranges"] = "Диапазоны (+ / - / × / ÷)",
        ["Diagnostics_CurrentRange"] = "Текущий диапазон чисел",
        ["Diagnostics_ActiveFacts"] = "Всего активных примеров",
        ["Diagnostics_MasteredFacts"] = "Освоенные примеры",
        ["Diagnostics_LearningFacts"] = "В процессе изучения",
        ["Diagnostics_NewFactsRemaining"] = "Осталось новых примеров",
        ["Diagnostics_OperationsIntroduced"] = "Введено операций",
        ["Diagnostics_TotalAttempts"] = "Всего попыток",
        ["Diagnostics_Accuracy"] = "Верно / Точность",
        ["Diagnostics_Remediation"] = "Ожидают повторения",
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
        ["Diagnostics_Group_Learning"] = "Прогресс обучения",
        ["Diagnostics_Group_Checkpoint"] = "Контрольная проверка",
        ["Diagnostics_Group_Scheduler"] = "Планировщик FSRS-6",
        ["Diagnostics_Group_Storage"] = "Время и хранилище",
        ["DangerZone_Title"] = "Опасная зона",
        ["DangerZone_Desc"] = "Действия по сбросу прогресса обучения и настроек устройства.",
        ["Reset_LearningProgress_Title"] = "Сбросить прогресс обучения",
        ["Reset_LearningProgress_Desc"] = "Удаляет историю попыток, освоение и прогресс диапазона. Возвращает к началу знакомства со сложением 0..1. Сохраняет язык, тему и онбординг.",
        ["Reset_LearningProgress_Confirm"] = "Вы уверены, что хотите сбросить весь прогресс обучения к началу сложения 0..1?",
        ["Reset_LearningProgress_Success"] = "Прогресс обучения сброшен к началу сложения 0..1.",
        ["Reset_UiPreferences_Title"] = "Сбросить онбординг и настройки",
        ["Reset_UiPreferences_Desc"] = "Сбрасывает тему и язык на системные и сбрасывает онбординг. Сохраняет базу данных обучения.",
        ["Reset_UiPreferences_Confirm"] = "Вы уверены, что хотите сбросить онбординг и настройки интерфейса?",
        ["Reset_UiPreferences_Success"] = "Онбординг и настройки интерфейса сброшены.",
        ["Reset_FullLocal_Title"] = "Полный локальный сброс",
        ["Reset_FullLocal_Desc"] = "Сбрасывает базу данных обучения, онбординг, язык и тему до состояния чистой установки.",
        ["Reset_FullLocal_Confirm"] = "Вы уверены, что хотите выполнить ПОЛНЫЙ сброс всех данных и настроек? Приложение запустится как при чистой установке.",
        ["Reset_FullLocal_Success"] = "Полный сброс выполнен. Приложение возвращено в исходное состояние."
    };
}
