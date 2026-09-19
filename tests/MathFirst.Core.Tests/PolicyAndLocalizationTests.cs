namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Domain;
using Xunit;

public sealed class PolicyAndLocalizationTests
{
    [Theory]
    [InlineData(0, ThemePreference.System)]
    [InlineData(1, ThemePreference.Light)]
    [InlineData(2, ThemePreference.Dark)]
    [InlineData(99, ThemePreference.System)]
    [InlineData(-1, ThemePreference.System)]
    public void ThemePreferencePolicy_NormalizesIntegers(int input, ThemePreference expected)
    {
        Assert.Equal(expected, ThemePreferencePolicy.Normalize(input));
    }

    [Theory]
    [InlineData("system", ThemePreference.System)]
    [InlineData("Light", ThemePreference.Light)]
    [InlineData("dark", ThemePreference.Dark)]
    [InlineData("1", ThemePreference.Light)]
    [InlineData("invalid", ThemePreference.System)]
    [InlineData(null, ThemePreference.System)]
    public void ThemePreferencePolicy_NormalizesStrings(string? input, ThemePreference expected)
    {
        Assert.Equal(expected, ThemePreferencePolicy.Normalize(input));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("en-US", "en")]
    [InlineData("de", "de")]
    [InlineData("de-DE", "de")]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("fr", "en")]
    [InlineData("unknown", "en")]
    [InlineData(null, "en")]
    public void LanguagePreferencePolicy_Normalize_NormalizesCorrectly(string? input, string expected)
    {
        Assert.Equal(expected, LanguagePreferencePolicy.Normalize(input));
    }

    [Theory]
    [InlineData("system", "de-DE", "de")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("ru", "de-DE", "ru")]
    [InlineData(null, "ru-RU", "ru")]
    public void LanguagePreferencePolicy_Resolve_HandlesSavedAndDeviceCulture(string? saved, string? device, string expected)
    {
        Assert.Equal(expected, LanguagePreferencePolicy.Resolve(saved, device));
    }

    [Fact]
    public void LocalizationService_ProvidesTranslationsInAllLanguages()
    {
        var service = new LocalizationService();

        service.ApplyLanguagePreference("en");
        Assert.Equal("Correct!", service["Training_Correct"]);
        Assert.Equal("Incorrect", service["Training_IncorrectTitle"]);
        Assert.Equal("Time expired.", service["Training_TimeExpired"]);
        Assert.Equal("Your answer: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Correct answer: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Welcome to MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Operation Bands", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Choose your number keypad", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Phone keypad", service["Keypad_Phone"]);
        Assert.Equal("PC numpad", service["Keypad_Numpad"]);
        Assert.Equal("Backspace", service["Keypad_Backspace"]);
        Assert.Equal("Haptic feedback", service["Settings_HapticFeedbackTitle"]);
        Assert.Equal("Use vibration feedback for keypad taps and answer results.", service["Settings_HapticFeedbackHelp"]);
        Assert.Equal("Haptic feedback On.", service["Settings_HapticFeedbackChangedTo", service["Common_On"]]);
        Assert.Equal("Version 1.0 (Build 1)", service["Settings_VersionBuild", "1.0", 1]);
        Assert.Equal("Build", service["Settings_Build"]);
        Assert.Equal("Source", service["Settings_Source"]);
        Assert.Equal("Copy diagnostic info", service["Settings_CopyDiagnostics"]);
        Assert.Equal("Diagnostic information copied to clipboard.", service["Settings_CopyDiagnostics_Success"]);
        Assert.Equal("Failed to copy diagnostic information to clipboard.", service["Settings_CopyDiagnostics_Failure"]);

        service.ApplyLanguagePreference("de");
        Assert.Equal("Richtig!", service["Training_Correct"]);
        Assert.Equal("Falsch", service["Training_IncorrectTitle"]);
        Assert.Equal("Zeit abgelaufen.", service["Training_TimeExpired"]);
        Assert.Equal("Deine Antwort: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Richtige Antwort: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Willkommen bei MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Operationsbänder", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Zahlentastatur auswählen", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Telefon-Tastatur", service["Keypad_Phone"]);
        Assert.Equal("PC-Ziffernblock", service["Keypad_Numpad"]);
        Assert.Equal("Rücktaste", service["Keypad_Backspace"]);
        Assert.Equal("Haptisches Feedback", service["Settings_HapticFeedbackTitle"]);
        Assert.Equal("Vibrationsfeedback für Tastatureingaben und Antwort-Ergebnisse verwenden.", service["Settings_HapticFeedbackHelp"]);
        Assert.Equal("Haptisches Feedback Ein.", service["Settings_HapticFeedbackChangedTo", service["Common_On"]]);
        Assert.Equal("Version 1.0 (Build 1)", service["Settings_VersionBuild", "1.0", 1]);
        Assert.Equal("Build", service["Settings_Build"]);
        Assert.Equal("Quelle", service["Settings_Source"]);
        Assert.Equal("Diagnose-Informationen kopieren", service["Settings_CopyDiagnostics"]);
        Assert.Equal("Diagnose-Informationen wurden in die Zwischenablage kopiert.", service["Settings_CopyDiagnostics_Success"]);
        Assert.Equal("Diagnose-Informationen konnten nicht kopiert werden.", service["Settings_CopyDiagnostics_Failure"]);

        service.ApplyLanguagePreference("ru");
        Assert.Equal("Правильно!", service["Training_Correct"]);
        Assert.Equal("Неверно", service["Training_IncorrectTitle"]);
        Assert.Equal("Время вышло.", service["Training_TimeExpired"]);
        Assert.Equal("Твой ответ: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Правильный ответ: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Добро пожаловать в MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Прогресс по операциям", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Выберите цифровую клавиатуру", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Телефонная клавиатура", service["Keypad_Phone"]);
        Assert.Equal("Цифровой блок ПК", service["Keypad_Numpad"]);
        Assert.Equal("Удалить символ", service["Keypad_Backspace"]);
        Assert.Equal("Тактильный отклик", service["Settings_HapticFeedbackTitle"]);
        Assert.Equal("Использовать вибрацию при нажатии клавиш и результатах ответов.", service["Settings_HapticFeedbackHelp"]);
        Assert.Equal("Тактильный отклик: Вкл.", service["Settings_HapticFeedbackChangedTo", service["Common_On"]]);
        Assert.Equal("Версия 1.0 (сборка 1)", service["Settings_VersionBuild", "1.0", 1]);
        Assert.Equal("Сборка", service["Settings_Build"]);
        Assert.Equal("Источник", service["Settings_Source"]);
        Assert.Equal("Скопировать данные диагностики", service["Settings_CopyDiagnostics"]);
        Assert.Equal("Данные диагностики скопированы в буфер обмена.", service["Settings_CopyDiagnostics_Success"]);
        Assert.Equal("Не удалось скопировать данные диагностики.", service["Settings_CopyDiagnostics_Failure"]);
    }

    [Fact]
    public void LocalizationService_FormatsArgumentsAccurately()
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference("en");

        var formatted = service["Training_Score", 3, 5];
        Assert.Equal("Correct: 3 / 5", formatted);
    }

    [Theory]
    [InlineData("en", "Addition: progression stage 1", "Addition: progression unavailable")]
    [InlineData("de", "Addition: Fortschrittsstufe 1", "Addition: Fortschritt nicht verfügbar")]
    [InlineData("ru", "Сложение: этап прогресса 1", "Сложение: прогресс недоступен")]
    public void LocalizationService_OperationProgressHudHasAccessibleLanguageParity(
        string language,
        string expectedStage,
        string expectedUnavailable)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expectedStage, service["Training_OperationProgressStage", service["Operation_Addition"], 1]);
        Assert.Equal(expectedUnavailable, service["Training_OperationProgressUnavailable", service["Operation_Addition"]]);
    }

    /// <summary>
    /// Verifies that the static practice gate localization keys remain present and correct in
    /// all languages. These keys serve as the ultimate fallback for the contextual copy system
    /// introduced in MF-UX-002 and must not be removed.
    /// </summary>
    [Theory]
    [InlineData("en", "Ready to practice?", "Start", "Paused", "Resume practice", "Pause")]
    [InlineData("de", "Bereit zum Üben?", "Los geht's", "Pausiert", "Weiterüben", "Pausieren")]
    [InlineData("ru", "Пора заниматься?", "Начать", "Пауза", "Продолжить занятие", "Приостановить")]
    public void LocalizationService_PracticeGatesHaveLanguageParity(
        string language,
        string expectedReady,
        string expectedStart,
        string expectedPaused,
        string expectedResume,
        string expectedPause)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        // Static keys are preserved as ultimate fallback for the contextual copy selector.
        Assert.Equal(expectedReady, service["Training_ReadyTitle"]);
        Assert.Equal(expectedStart, service["Training_Start"]);
        Assert.Equal(expectedPaused, service["Training_PausedTitle"]);
        Assert.Equal(expectedResume, service["Training_ResumePractice"]);
        Assert.Equal(expectedPause, service["Training_Pause"]);
    }

    [Theory]
    [InlineData("en", "Progress could not be saved.", "Retry")]
    [InlineData("de", "Fortschritt konnte nicht gespeichert werden.", "Erneut versuchen")]
    [InlineData("ru", "Не удалось сохранить прогресс.", "Повторить")]
    public void LocalizationService_PersistenceRecoveryHasLanguageParity(
        string language,
        string expectedTitle,
        string expectedRetry)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expectedTitle, service["Training_PersistenceFailureTitle"]);
        Assert.Equal(expectedRetry, service["Training_Retry"]);
    }

    [Theory]
    [InlineData("en", "Get Started")]
    [InlineData("de", "Los geht's")]
    [InlineData("ru", "Начать")]
    public void LocalizationService_GetStartedUsesApprovedLocalizedText(string language, string expected)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expected, service["Common_Start"]);
        Assert.NotEqual("Onboarding_TutorialTitle", service["Onboarding_TutorialTitle"]);
        Assert.NotEqual("Onboarding_TutorialStep1Title", service["Onboarding_TutorialStep1Title"]);
        Assert.NotEqual("Onboarding_TutorialStep2Title", service["Onboarding_TutorialStep2Title"]);
        Assert.NotEqual("Onboarding_TutorialStep3Title", service["Onboarding_TutorialStep3Title"]);
    }

    [Theory]
    [InlineData("en", "independently")]
    [InlineData("de", "unabhängig")]
    [InlineData("ru", "независимо")]
    public void LocalizationService_TutorialExplainsIndependentOperationPractice(
        string language,
        string expectedIndependentPracticePhrase)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Contains(expectedIndependentPracticePhrase, service["Onboarding_TutorialStep3Text"], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "Your arithmetic practice is ready. The answer timer starts only when you select Get Started.")]
    [InlineData("de", "Dein Rechentraining ist bereit. Der Antworttimer startet erst mit „Los geht's“.")]
    [InlineData("ru", "Тренировка по арифметике готова. Таймер ответа запустится только после нажатия «Начать».")]
    public void LocalizationService_OnboardingReadyDescription_IsHistoryNeutral(string language, string expected)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expected, service["Onboarding_StepReady_Desc"]);
    }

    [Fact]
    public void LocalizationService_ApplyLanguagePreference_FiresUiLanguageChangedOnlyWhenChanged()
    {
        var service = new LocalizationService(initialDeviceCulture: "en-US");
        var eventCount = 0;
        service.UiLanguageChanged += (_, _) => eventCount++;

        // Initial preference is "system" (resolves to en)
        // Applying "en" changes persisted preference ("system" -> "en")
        service.ApplyLanguagePreference("en");
        Assert.Equal(1, eventCount);

        // Applying same preference "en" should NOT fire event again
        service.ApplyLanguagePreference("en");
        Assert.Equal(1, eventCount);

        // Applying different preference "de" should fire event
        service.ApplyLanguagePreference("de");
        Assert.Equal(2, eventCount);
        Assert.Equal("de", service.CurrentUiLanguage);
    }

    [Fact]
    public void LocalizationService_ApplyPreviewLanguage_IsIdempotent()
    {
        var service = new LocalizationService(initialDeviceCulture: "en-US");
        var eventCount = 0;
        service.UiLanguageChanged += (_, _) => eventCount++;

        service.ApplyPreviewLanguage("de");
        Assert.Equal(1, eventCount);
        Assert.Equal("de", service.CurrentUiLanguage);

        // Re-applying identical preview language does not trigger event again
        service.ApplyPreviewLanguage("de");
        Assert.Equal(1, eventCount);

        service.ClearPreview();
        Assert.Equal(2, eventCount);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    public void LocalizationService_ResetUiPreferencesDescription_DescribesNumpadLayoutDefault(string language)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        var resetDesc = service["Reset_UiPreferences_Desc"];
        var numpadName = service["Keypad_Numpad"];
        var phoneName = service["Keypad_Phone"];

        Assert.Contains(numpadName, resetDesc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(phoneName, resetDesc, StringComparison.OrdinalIgnoreCase);
        if (language == "ru")
        {
            Assert.DoesNotContain("телефон", resetDesc, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("Common_On", "Вкл.")]
    [InlineData("Common_Off", "Выкл.")]
    public void LocalizationService_RussianHapticFeedbackChangedTo_FormatsWithoutDuplicatePunctuation(string statusKey, string expectedStatusText)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference("ru");

        var statusValue = service[statusKey];
        Assert.Equal(expectedStatusText, statusValue);

        var formatted = service["Settings_HapticFeedbackChangedTo", statusValue];
        Assert.Contains(expectedStatusText, formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("..", formatted, StringComparison.Ordinal);
        Assert.EndsWith(".", formatted, StringComparison.Ordinal);
        Assert.Equal(1, formatted.Count(c => c == '.'));
    }

    [Fact]
    public void LocalizationService_RussianOperationProgressHud_UsesLearnerFacingTerminology()
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference("ru");

        var hudLabel = service["Diagnostics_Group_Learning"];
        Assert.Equal("Прогресс по операциям", hudLabel);
        Assert.DoesNotContain("банды", hudLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("банд", hudLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalizationService_AllLanguagesHaveIdenticalKeys()
    {
        var bindingFlags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var enDict = (Dictionary<string, string>)typeof(LocalizationService).GetField("EnglishStrings", bindingFlags)!.GetValue(null)!;
        var deDict = (Dictionary<string, string>)typeof(LocalizationService).GetField("GermanStrings", bindingFlags)!.GetValue(null)!;
        var ruDict = (Dictionary<string, string>)typeof(LocalizationService).GetField("RussianStrings", bindingFlags)!.GetValue(null)!;

        var enKeys = enDict.Keys.OrderBy(k => k).ToList();
        var deKeys = deDict.Keys.OrderBy(k => k).ToList();
        var ruKeys = ruDict.Keys.OrderBy(k => k).ToList();

        Assert.Equal(enKeys, deKeys);
        Assert.Equal(enKeys, ruKeys);

        foreach (var key in enKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(enDict[key]), $"English string for key '{key}' was empty.");
            Assert.False(string.IsNullOrWhiteSpace(deDict[key]), $"German string for key '{key}' was empty.");
            Assert.False(string.IsNullOrWhiteSpace(ruDict[key]), $"Russian string for key '{key}' was empty.");
        }
    }

    [Fact]
    public void Settings_Contract_ContainsPrivacyNavigationEntry()
    {
        var settingsPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor");
        Assert.True(File.Exists(settingsPath), "Settings.razor was not found.");
        var settings = File.ReadAllText(settingsPath);

        Assert.Contains("href=\"privacy\"", settings, StringComparison.Ordinal);
        Assert.Contains("Localizer[\"Settings_Privacy\"]", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Privacy_PageContract_DeclaresSingleRouteAndOfflineLocalContent()
    {
        var pagesDir = GetRepositoryPath("src", "MathFirst.App", "Components", "Pages");
        var pageFiles = Directory.Exists(pagesDir) ? Directory.GetFiles(pagesDir, "*.razor") : [];
        var matchingFiles = pageFiles
            .Where(path => File.ReadAllText(path).Contains("@page \"/privacy\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(matchingFiles.Count == 1, $"Expected exactly one page declaring @page \"/privacy\", but found {matchingFiles.Count}.");

        var privacyContent = File.ReadAllText(matchingFiles[0]);
        Assert.DoesNotContain("HttpClient", privacyContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", privacyContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", privacyContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<webview", privacyContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", privacyContent, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "Privacy", "Overview", "No Remote Collection or Sharing", "Local Storage and Device Transfer", "Removing Local Data", "Contact")]
    [InlineData("de", "Datenschutz", "Übersicht", "Keine Datenerfassung oder Weitergabe", "Lokale Speicherung und Geräteübertragung", "Daten löschen", "Kontakt")]
    [InlineData("ru", "Конфиденциальность", "Обзор", "Без сбора данных и передачи третьим лицам", "Локальное хранение и перенос между устройствами", "Удаление данных", "Контакты")]
    public void LocalizationService_PrivacyStringsHaveFullLanguageParity(
        string language,
        string expectedTitle,
        string expectedOverviewTitle,
        string expectedNoCollectionTitle,
        string expectedLocalStorageTitle,
        string expectedDeletionTitle,
        string expectedContactTitle)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expectedTitle, service["Privacy_Title"]);
        Assert.Equal(expectedOverviewTitle, service["Privacy_Overview_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Privacy_Overview_Body"]));
        Assert.Equal(expectedNoCollectionTitle, service["Privacy_NoCollection_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Privacy_NoCollection_Body"]));
        Assert.Equal(expectedLocalStorageTitle, service["Privacy_LocalStorage_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Privacy_LocalStorage_Body"]));
        Assert.Equal(expectedDeletionTitle, service["Privacy_Delete_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Privacy_Delete_Body"]));
        Assert.Equal(expectedContactTitle, service["Privacy_Contact_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Privacy_Contact_Body"]));
        Assert.Equal(expectedTitle, service["Settings_Privacy"]);
        Assert.False(string.IsNullOrWhiteSpace(service["Settings_Privacy_Desc"]));
        Assert.False(string.IsNullOrWhiteSpace(service["Settings_Privacy_Action"]));
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
