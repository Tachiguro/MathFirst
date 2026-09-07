namespace MathFirst.Application;

public interface ILocalizationService
{
    string CurrentUiLanguage { get; }
    string SelectedLanguagePreference { get; }
    event EventHandler? UiLanguageChanged;

    string GetString(string key, params object[] args);
    string this[string key] { get; }
    string this[string key, params object[] args] { get; }

    void ApplyLanguagePreference(string languagePreference, string? deviceCulture = null);
    void ApplyPreviewLanguage(string languageCode);
    void ClearPreview();
}
