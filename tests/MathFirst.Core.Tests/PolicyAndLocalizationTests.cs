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
        Assert.Equal("Learning Progression", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Mixed round status", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("Mixed round", service["Training_MixedRoundLabel"]);
        Assert.Equal("Mixed round · 3/12", service["Training_CheckpointBadge", 3, 12]);
        Assert.Equal("FSRS-6 Task Scheduler", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Timing & Storage", service["Diagnostics_Group_Storage"]);
        Assert.Equal("Choose your number keypad", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Phone keypad", service["Keypad_Phone"]);
        Assert.Equal("PC numpad", service["Keypad_Numpad"]);
        Assert.Equal("Backspace", service["Keypad_Backspace"]);

        service.ApplyLanguagePreference("de");
        Assert.Equal("Richtig!", service["Training_Correct"]);
        Assert.Equal("Falsch", service["Training_IncorrectTitle"]);
        Assert.Equal("Zeit abgelaufen.", service["Training_TimeExpired"]);
        Assert.Equal("Deine Antwort: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Richtige Antwort: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Willkommen bei MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Lernfortschritt", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Mischrunden-Status", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("Mischrunde", service["Training_MixedRoundLabel"]);
        Assert.Equal("Mischrunde · 3/12", service["Training_CheckpointBadge", 3, 12]);
        Assert.Equal("FSRS-6-Aufgabenplaner", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Zeit & Speicher", service["Diagnostics_Group_Storage"]);
        Assert.Equal("Zahlentastatur auswählen", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Telefon-Tastatur", service["Keypad_Phone"]);
        Assert.Equal("PC-Ziffernblock", service["Keypad_Numpad"]);
        Assert.Equal("Rücktaste", service["Keypad_Backspace"]);

        service.ApplyLanguagePreference("ru");
        Assert.Equal("Правильно!", service["Training_Correct"]);
        Assert.Equal("Неверно", service["Training_IncorrectTitle"]);
        Assert.Equal("Время вышло.", service["Training_TimeExpired"]);
        Assert.Equal("Твой ответ: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Правильный ответ: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Добро пожаловать в MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Прогресс обучения", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Статус смешанного раунда", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("Смешанный раунд", service["Training_MixedRoundLabel"]);
        Assert.Equal("Смешанный раунд · 3/12", service["Training_CheckpointBadge", 3, 12]);
        Assert.Equal("Планировщик FSRS-6", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Время и хранилище", service["Diagnostics_Group_Storage"]);
        Assert.Equal("Выберите цифровую клавиатуру", service["Onboarding_KeypadTitle"]);
        Assert.Equal("Телефонная клавиатура", service["Keypad_Phone"]);
        Assert.Equal("Цифровой блок ПК", service["Keypad_Numpad"]);
        Assert.Equal("Удалить символ", service["Keypad_Backspace"]);
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
    [InlineData("en", "Ready to practice?", "Start", "Paused", "Resume practice", "Pause")]
    [InlineData("de", "Bereit zum Üben?", "Los geht's", "Pausiert", "Weiterüben", "Pausieren")]
    [InlineData("ru", "Готовы заниматься?", "Начать", "Пауза", "Продолжить занятие", "Приостановить")]
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
    [InlineData("en", "Mixed round", "12-question mixed round", "not a pass/fail test")]
    [InlineData("de", "Mischrunde", "Mischrunde mit 12 Aufgaben", "keine Prüfung")]
    [InlineData("ru", "Смешанный раунд", "12 задач", "не экзамен")]
    public void LocalizationService_MixedRoundAndTutorialAreExplained(
        string language,
        string expectedLabel,
        string expectedTutorialPhrase,
        string expectedNotTestPhrase)
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference(language);

        Assert.Equal(expectedLabel, service["Training_MixedRoundLabel"]);
        Assert.Contains(expectedTutorialPhrase, service["Onboarding_TutorialStep3Text"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedNotTestPhrase, service["Onboarding_TutorialStep3Text"], StringComparison.OrdinalIgnoreCase);
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
}
