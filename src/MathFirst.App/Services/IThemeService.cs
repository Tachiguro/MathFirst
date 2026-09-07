namespace MathFirst.App.Services;

using MathFirst.Domain;

public interface IThemeService
{
    event EventHandler? ThemeChanged;

    ThemePreference Preference { get; }
    ThemePreference? PreviewPreference { get; }
    ThemePreference EffectiveTheme { get; }
    string EffectiveThemeCssName { get; }

    void Initialize(Microsoft.Maui.Controls.Application application);
    bool SetPreference(ThemePreference preference);
    void ApplyPreviewPreference(ThemePreference preference);
    void ClearPreview();
    void ResetPreference();
}
