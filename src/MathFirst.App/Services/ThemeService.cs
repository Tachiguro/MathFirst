namespace MathFirst.App.Services;

using MathFirst.Application;
using MathFirst.Domain;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.Logging;

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IPreferenceStore? _preferenceStore;
    private readonly ILogger<ThemeService>? _logger;
    private Microsoft.Maui.Controls.Application? _application;
    private bool _initialized;

    public event EventHandler? ThemeChanged;

    public ThemeService(IPreferenceStore? preferenceStore = null, ILogger<ThemeService>? logger = null)
    {
        _preferenceStore = preferenceStore;
        _logger = logger;
    }

    public ThemePreference Preference { get; private set; } = ThemePreference.System;
    public ThemePreference? PreviewPreference { get; private set; }
    public ThemePreference EffectiveTheme { get; private set; } = ThemePreference.Light;

    public string EffectiveThemeCssName => EffectiveTheme == ThemePreference.Dark ? "dark" : "light";

    public void Initialize(Microsoft.Maui.Controls.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_initialized)
        {
            return;
        }

        _application = application;
        Preference = _preferenceStore?.GetThemePreference() ?? ThemePreference.System;
        PreviewPreference = null;
        _application.RequestedThemeChanged += OnRequestedThemeChanged;
        _initialized = true;

        ApplyNativeTheme();
        EffectiveTheme = ResolveEffectiveTheme(_application.RequestedTheme);
        _logger?.LogInformation("Theme initialized. Preference: {Preference}, Effective: {Effective}", Preference, EffectiveTheme);
    }

    public void ApplyPreviewPreference(ThemePreference preference)
    {
        var normalized = ThemePreferencePolicy.Normalize((int)preference);
        PreviewPreference = normalized;
        ApplyNativeTheme();
        UpdateEffectiveTheme(ResolveEffectiveTheme(_application?.RequestedTheme ?? AppTheme.Unspecified), notify: true);
    }

    public void ClearPreview()
    {
        if (PreviewPreference.HasValue)
        {
            PreviewPreference = null;
            ApplyNativeTheme();
            UpdateEffectiveTheme(ResolveEffectiveTheme(_application?.RequestedTheme ?? AppTheme.Unspecified), notify: true);
        }
    }

    public bool SetPreference(ThemePreference preference)
    {
        var normalized = ThemePreferencePolicy.Normalize((int)preference);
        var changed = Preference != normalized || PreviewPreference.HasValue;

        Preference = normalized;
        PreviewPreference = null;
        _preferenceStore?.SetThemePreference(normalized);

        ApplyNativeTheme();
        UpdateEffectiveTheme(ResolveEffectiveTheme(_application?.RequestedTheme ?? AppTheme.Unspecified), notify: true);
        return changed;
    }

    public void ResetPreference()
    {
        Preference = ThemePreference.System;
        PreviewPreference = null;
        _preferenceStore?.SetThemePreference(ThemePreference.System);
        ApplyNativeTheme();
        UpdateEffectiveTheme(ResolveEffectiveTheme(_application?.RequestedTheme ?? AppTheme.Unspecified), notify: true);
    }

    public void Dispose()
    {
        if (_application is not null)
        {
            _application.RequestedThemeChanged -= OnRequestedThemeChanged;
            _application = null;
        }
        _initialized = false;
    }

    private void ApplyNativeTheme()
    {
        if (_application is null)
        {
            return;
        }

        var active = PreviewPreference ?? Preference;
        _application.UserAppTheme = active switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    private ThemePreference ResolveEffectiveTheme(AppTheme requestedTheme)
    {
        var active = PreviewPreference ?? Preference;
        return active switch
        {
            ThemePreference.Light => ThemePreference.Light,
            ThemePreference.Dark => ThemePreference.Dark,
            _ => requestedTheme == AppTheme.Dark ? ThemePreference.Dark : ThemePreference.Light
        };
    }

    private void OnRequestedThemeChanged(object? sender, EventArgs e)
    {
        var active = PreviewPreference ?? Preference;
        if (!_initialized || active != ThemePreference.System)
        {
            return;
        }

        UpdateEffectiveTheme(ResolveEffectiveTheme(_application?.RequestedTheme ?? AppTheme.Unspecified), notify: true);
    }

    private void UpdateEffectiveTheme(ThemePreference newEffectiveTheme, bool notify)
    {
        var changed = EffectiveTheme != newEffectiveTheme;
        EffectiveTheme = newEffectiveTheme;
        if (notify)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
