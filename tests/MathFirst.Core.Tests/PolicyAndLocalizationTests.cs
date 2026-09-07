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
        Assert.Equal("Checkpoint Status", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("FSRS-6 Task Scheduler", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Timing & Storage", service["Diagnostics_Group_Storage"]);

        service.ApplyLanguagePreference("de");
        Assert.Equal("Richtig!", service["Training_Correct"]);
        Assert.Equal("Falsch", service["Training_IncorrectTitle"]);
        Assert.Equal("Zeit abgelaufen.", service["Training_TimeExpired"]);
        Assert.Equal("Deine Antwort: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Richtige Antwort: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Willkommen bei MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Lernfortschritt", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Checkpoint-Status", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("FSRS-6 Task-Planer", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Zeit & Speicher", service["Diagnostics_Group_Storage"]);

        service.ApplyLanguagePreference("ru");
        Assert.Equal("Правильно!", service["Training_Correct"]);
        Assert.Equal("Неверно", service["Training_IncorrectTitle"]);
        Assert.Equal("Время вышло.", service["Training_TimeExpired"]);
        Assert.Equal("Твой ответ: 5", service["Training_YourAnswer", 5]);
        Assert.Equal("Правильный ответ: 6", service["Training_CorrectAnswer", 6]);
        Assert.Equal("Добро пожаловать в MathFirst", service["Onboarding_WelcomeTitle"]);
        Assert.Equal("Прогресс обучения", service["Diagnostics_Group_Learning"]);
        Assert.Equal("Контрольная проверка", service["Diagnostics_Group_Checkpoint"]);
        Assert.Equal("Планировщик FSRS-6", service["Diagnostics_Group_Scheduler"]);
        Assert.Equal("Время и хранилище", service["Diagnostics_Group_Storage"]);
    }

    [Fact]
    public void LocalizationService_FormatsArgumentsAccurately()
    {
        var service = new LocalizationService();
        service.ApplyLanguagePreference("en");

        var formatted = service["Training_Score", 3, 5];
        Assert.Equal("Correct: 3 / 5", formatted);
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
