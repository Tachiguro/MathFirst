using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.App.Components.Shared;
using MathFirst.App.Services;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

// =============================================================================
// TEST-ONLY SUPPORT TYPES FOR SETTINGS COMPILATION & TESTING
// =============================================================================

namespace MathFirst.Core.Tests
{
    public sealed class TesterDiagnosticsSection : ComponentBase
    {
    }
}

namespace MathFirst.App.Components.Shared
{
    public sealed class NumericKeypadPreview : ComponentBase
    {
        [Parameter, EditorRequired]
        public NumericKeypadLayout Layout { get; set; }

        [Parameter, EditorRequired]
        public string DecimalSeparator { get; set; } = ".";
    }
}

namespace MathFirst.App.Services
{
    public interface IThemeService
    {
        event EventHandler? ThemeChanged;
        ThemePreference Preference { get; }
        ThemePreference? PreviewPreference { get; }
        ThemePreference EffectiveTheme { get; }
        string EffectiveThemeCssName { get; }
        bool SetPreference(ThemePreference preference);
        void ApplyPreviewPreference(ThemePreference preference);
        void ClearPreview();
        void ResetPreference();
    }

    public sealed class AppBuildInfo : IAppBuildInfo
    {
        public string ApplicationTitle { get; init; } = "MathFirst";
        public string ApplicationId { get; init; } = "com.mathfirst";
        public string DisplayVersion { get; init; } = "1.0.0";
        public int BuildNumber { get; init; } = 1;
        public string SourceCommit { get; init; } = "test";
        public string ShortSourceCommit { get; init; } = "test";
        public string BuildClassification { get; init; } = "Test";
        public bool IsTesterBuild => false;
    }

    public sealed class TestThemeService : IThemeService
    {
        public event EventHandler? ThemeChanged;
        public ThemePreference Preference { get; set; } = ThemePreference.System;
        public ThemePreference? PreviewPreference { get; set; }
        public ThemePreference EffectiveTheme => Preference;
        public string EffectiveThemeCssName => "theme-system";

        public bool SetPreference(ThemePreference preference)
        {
            Preference = preference;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void ApplyPreviewPreference(ThemePreference preference) => PreviewPreference = preference;
        public void ClearPreview() => PreviewPreference = null;
        public void ResetPreference()
        {
            Preference = ThemePreference.System;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

namespace MathFirst.Core.Tests
{
    /// <summary>
    /// Deterministic behavioral tests exercising the actual Settings.razor component's
    /// ReplayTutorial action handler, typed request interface, request-before-navigation
    /// ordering, and Calm Mode / combat isolation boundaries (Gate C / MF-CYBER-009 Slice 3B).
    /// </summary>
    public sealed class SettingsReplayHandlerTests : IDisposable
    {
        private readonly List<string> _tempDirectories = new();

        public void Dispose()
        {
            foreach (var dir in _tempDirectories)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup of temporary test assets.
                }
            }
        }

        private string CreateTempDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "MathFirstSettingsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _tempDirectories.Add(dir);
            return dir;
        }

        // =====================================================================
        // REQUIREMENT A & F: BUTTON VISIBILITY IN CYBER DEFENSE VS CALM MODE
        // =====================================================================

        [Fact]
        public async Task Settings_WhenCyberDefenseEnabled_RendersReplayTutorialButtonWithLocalization()
        {
            await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: true);

            var html = await fixture.GetHtmlAsync();
            var expectedLabel = fixture.Localizer["Settings_CyberDefenseReplayTutorial"];

            Assert.Contains("replay-tutorial-btn", html);
            Assert.Contains("cyber-replay-action-row", html);
            Assert.Contains(expectedLabel, html);
        }

        [Fact]
        public async Task Settings_CalmMode_DoesNotExposeReplayControl()
        {
            await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: false);

            var html = await fixture.GetHtmlAsync();
            var expectedLabel = fixture.Localizer["Settings_CyberDefenseReplayTutorial"];

            Assert.DoesNotContain("replay-tutorial-btn", html);
            Assert.DoesNotContain("cyber-replay-action-row", html);
            Assert.DoesNotContain(expectedLabel, html);
        }

        // =====================================================================
        // REQUIREMENT B, C, D: REAL HANDLER EXECUTION, DI MUTATION & NAVIGATION
        // =====================================================================

        [Fact]
        public async Task Settings_ReplayTutorialHandler_InvokesTypedInterface_MutatesSharedSingleton_AndNavigatesToHome()
        {
            await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: true);

            // Pre-condition: pending replay is false and no navigation has occurred
            Assert.False(fixture.CyberDefenseSessionState.IsTutorialReplayRequested);
            Assert.Empty(fixture.Navigation.Navigations);

            // Execute the actual Settings.razor private ReplayTutorial() handler
            await fixture.InvokeReplayTutorialAsync();

            // Evidence B & D: RequestTutorialReplay was invoked on the typed interface,
            // mutating the shared CyberDefenseSessionState DI singleton instance
            Assert.True(fixture.CyberDefenseSessionState.IsTutorialReplayRequested);

            // Evidence C: The handler navigates to Home ("/")
            Assert.Single(fixture.Navigation.Navigations);
            Assert.Equal("/", fixture.Navigation.LastNavigatedUri);
        }

        // =====================================================================
        // REQUIREMENT 4 & 5: REQUEST-BEFORE-NAVIGATION ORDERING
        // =====================================================================

        [Fact]
        public async Task Settings_ReplayTutorialHandler_StrictlyExecutesRequestBeforeNavigation()
        {
            var callLog = new List<string>();

            await using var fixture = await SettingsFixture.CreateAsync(
                this,
                cyberDefense: true,
                onRequestReplay: () => callLog.Add("RequestTutorialReplay"),
                onNavigate: (uri, _) => callLog.Add($"NavigateTo:{uri}"));

            await fixture.InvokeReplayTutorialAsync();

            Assert.Equal(2, callLog.Count);
            Assert.Equal("RequestTutorialReplay", callLog[0]);
            Assert.Equal("NavigateTo:/", callLog[1]);
        }

        // =====================================================================
        // REQUIREMENT E: FAILING SERVICE PREVENTS NAVIGATION
        // =====================================================================

        [Fact]
        public async Task Settings_ReplayTutorialHandler_FailingRequestService_PreventsNavigation()
        {
            await using var fixture = await SettingsFixture.CreateAsync(
                this,
                cyberDefense: true,
                throwOnRequest: true);

            var ex = await Assert.ThrowsAsync<TargetInvocationException>(() => fixture.InvokeReplayTutorialAsync());
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("Simulated request failure", ex.InnerException.Message);

            // Navigation must never occur if the request service fails
            Assert.Empty(fixture.Navigation.Navigations);
            Assert.Null(fixture.Navigation.LastNavigatedUri);
        }

        // =====================================================================
        // REQUIREMENT G: COMBAT STATE & PRACTICE GATE ISOLATION
        // =====================================================================

        [Fact]
        public async Task Settings_CombatStateIsolation_DoesNotPolluteOrMutateSessionProgress()
        {
            await using var fixture = await SettingsFixture.CreateAsync(this, cyberDefense: true);

            // Item timing is paused while inside Settings
            Assert.False(fixture.Session.IsTimingActive);
            Assert.Equal(0, fixture.Session.SessionTotalCount);

            // Activating replay tutorial does not advance items or complete rounds
            await fixture.InvokeReplayTutorialAsync();

            Assert.Equal(0, fixture.Session.SessionTotalCount);
            Assert.Null(fixture.Session.LastEvaluation);
            Assert.False(fixture.Session.IsTimingActive);
            Assert.True(fixture.CyberDefenseSessionState.IsTutorialReplayRequested);
        }

        // =====================================================================
        // TEST HARNESS & NAVIGATION DOUBLE
        // =====================================================================

        private sealed class SettingsFixture : IAsyncDisposable
        {
            private readonly ServiceProvider _services;
            private readonly HtmlRenderer _renderer;
            private readonly HtmlRootComponent _root;
            private readonly Settings _settings;

            public TestPreferenceStore Preferences { get; }
            public CyberDefenseSessionState CyberDefenseSessionState { get; }
            public TrainingSession Session { get; }
            public TestNavigationManager Navigation { get; }
            public LocalizationService Localizer { get; }

            private SettingsFixture(
                ServiceProvider services,
                HtmlRenderer renderer,
                HtmlRootComponent root,
                Settings settings,
                TestPreferenceStore preferences,
                CyberDefenseSessionState cyberDefenseSessionState,
                TrainingSession session,
                TestNavigationManager navigation,
                LocalizationService localizer)
            {
                _services = services;
                _renderer = renderer;
                _root = root;
                _settings = settings;
                Preferences = preferences;
                CyberDefenseSessionState = cyberDefenseSessionState;
                Session = session;
                Navigation = navigation;
                Localizer = localizer;
            }

            public static async Task<SettingsFixture> CreateAsync(
                SettingsReplayHandlerTests suite,
                bool cyberDefense = true,
                Action? onRequestReplay = null,
                Action<string, NavigationOptions>? onNavigate = null,
                bool throwOnRequest = false)
            {
                var dir = suite.CreateTempDirectory();
                var dbPath = Path.Combine(dir, "learner.db");
                var learner = new SqliteLearnerStore(dbPath);

                var preferences = new TestPreferenceStore();
                preferences.SetCyberDefenseEnabled(cyberDefense);

                var clock = new FakeClock();
                var session = new TrainingSession(learner, clock, preferenceStore: preferences);
                var sessionState = new CyberDefenseSessionState(preferences);
                var localizer = new LocalizationService();
                var themeService = new TestThemeService();
                var buildInfo = new AppBuildInfo();
                var backCoordinator = new AppBackNavigationCoordinator();
                var hapticService = new TestHapticFeedbackService();
                var resetCoordinator = new NoResetCoordinator();
                var jsRuntime = new TestJsRuntime();
                var navigation = new TestNavigationManager(onNavigate: onNavigate);

                var services = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<ILocalizationService>(localizer)
                    .AddSingleton<IThemeService>(themeService)
                    .AddSingleton(buildInfo)
                    .AddSingleton<IPreferenceStore>(preferences)
                    .AddSingleton<ICyberDefenseModePreferences>(preferences)
                    .AddSingleton(sessionState);

                if (onRequestReplay is not null || throwOnRequest)
                {
                    services.AddSingleton<ICyberDefenseTutorialReplayRequests>(new TrackingReplayRequests(
                        sessionState,
                        onRequestReplay,
                        throwOnRequest));
                }
                else
                {
                    // Production registration pattern from MauiProgram.cs
                    services.AddSingleton<ICyberDefenseTutorialReplayRequests>(sp =>
                        sp.GetRequiredService<CyberDefenseSessionState>());
                }

                services.AddSingleton(session)
                    .AddSingleton<NavigationManager>(navigation)
                    .AddSingleton<IJSRuntime>(jsRuntime)
                    .AddSingleton<IAppBackNavigationCoordinator>(backCoordinator)
                    .AddSingleton<IHapticFeedbackService>(hapticService)
                    .AddSingleton<IAppResetCoordinator>(resetCoordinator);

                var sp = services.BuildServiceProvider();
                var renderer = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());

                Settings capturedSettings = null!;
                var root = await renderer.Dispatcher.InvokeAsync(async () =>
                {
                    return await renderer.RenderComponentAsync<SettingsHost>(ParameterView.FromDictionary(new Dictionary<string, object?>
                    {
                        ["Capture"] = (Action<Settings>)(s => capturedSettings = s)
                    }));
                });

                return new SettingsFixture(
                    sp,
                    renderer,
                    root,
                    capturedSettings,
                    preferences,
                    sessionState,
                    session,
                    navigation,
                    localizer);
            }

            public Task<string> GetHtmlAsync() =>
                _renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString());

            public Task InvokeReplayTutorialAsync() =>
                _renderer.Dispatcher.InvokeAsync(() =>
                {
                    var method = typeof(Settings).GetMethod("ReplayTutorial", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(method);
                    method.Invoke(_settings, null);
                });

            public Task InvokeNavigateBackAsync() =>
                _renderer.Dispatcher.InvokeAsync(() =>
                {
                    var method = typeof(Settings).GetMethod("NavigateBack", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(method);
                    method.Invoke(_settings, null);
                });

            public async ValueTask DisposeAsync()
            {
                await _renderer.Dispatcher.InvokeAsync(() => _settings.Dispose());
                await _renderer.DisposeAsync();
                await _services.DisposeAsync();
            }
        }

        private sealed class SettingsHost : ComponentBase
        {
            [Parameter]
            public Action<Settings> Capture { get; set; } = null!;

            protected override void BuildRenderTree(RenderTreeBuilder builder)
            {
                builder.OpenComponent<Settings>(0);
                builder.AddComponentReferenceCapture(1, instance => Capture((Settings)instance));
                builder.CloseComponent();
            }
        }

        public sealed class TestNavigationManager : NavigationManager
        {
            private readonly Action<string, NavigationOptions>? _onNavigate;

            public TestNavigationManager(string baseUri = "http://localhost/", Action<string, NavigationOptions>? onNavigate = null)
            {
                _onNavigate = onNavigate;
                Initialize(baseUri, baseUri);
            }

            public List<(string Uri, NavigationOptions Options)> Navigations { get; } = new();

            public string? LastNavigatedUri => Navigations.Count > 0 ? Navigations[^1].Uri : null;

            protected override void NavigateToCore(string uri, NavigationOptions options)
            {
                Navigations.Add((uri, options));
                _onNavigate?.Invoke(uri, options);
                Uri = ToAbsoluteUri(uri).ToString();
            }
        }

        private sealed class TrackingReplayRequests : ICyberDefenseTutorialReplayRequests
        {
            private readonly ICyberDefenseTutorialReplayRequests _inner;
            private readonly Action? _onRequest;
            private readonly bool _shouldThrow;

            public TrackingReplayRequests(
                ICyberDefenseTutorialReplayRequests inner,
                Action? onRequest = null,
                bool shouldThrow = false)
            {
                _inner = inner;
                _onRequest = onRequest;
                _shouldThrow = shouldThrow;
            }

            public void RequestTutorialReplay()
            {
                _onRequest?.Invoke();
                if (_shouldThrow)
                {
                    throw new InvalidOperationException("Simulated request failure on ICyberDefenseTutorialReplayRequests.");
                }

                _inner.RequestTutorialReplay();
            }
        }

        private sealed class TestPreferenceStore : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
        {
            private bool _cyberDefense = true;
            private bool _tutorialCompleted;
            private string _language = "system";
            private ThemePreference _theme = ThemePreference.System;
            private NumericKeypadLayout _keypad = NumericKeypadLayout.Numpad;
            private bool _haptic = true;
            private PracticeTimeSetting _timeSetting = PracticeTimeSetting.Standard;
            private readonly Dictionary<ArithmeticOperation, bool> _ops = new();

            public bool GetCyberDefenseEnabled() => _cyberDefense;
            public void SetCyberDefenseEnabled(bool enabled) => _cyberDefense = enabled;

            public bool GetTutorialCompleted() => _tutorialCompleted;
            public void SetTutorialCompleted(bool completed) => _tutorialCompleted = completed;

            public ThemePreference GetThemePreference() => _theme;
            public void SetThemePreference(ThemePreference preference) => _theme = preference;

            public NumericKeypadLayout GetNumericKeypadLayout() => _keypad;
            public void SetNumericKeypadLayout(NumericKeypadLayout layout) => _keypad = layout;

            public string GetLanguagePreference() => _language;
            public void SetLanguagePreference(string languageCode) => _language = languageCode;

            public bool GetHapticFeedbackEnabled() => _haptic;
            public void SetHapticFeedbackEnabled(bool enabled) => _haptic = enabled;

            public bool GetOperationEnabled(ArithmeticOperation operation) => _ops.GetValueOrDefault(operation, true);
            public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) => _ops[operation] = enabled;

            public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
                PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                    PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));

            public PracticeTimeSetting GetPracticeTimeSetting() => _timeSetting;
            public void SetPracticeTimeSetting(PracticeTimeSetting setting) => _timeSetting = setting;

            public void ResetPracticePreferences() => _ops.Clear();
            public void ResetAllPreferences()
            {
                _ops.Clear();
                _cyberDefense = true;
                _tutorialCompleted = false;
                _language = "system";
                _theme = ThemePreference.System;
                _keypad = NumericKeypadLayout.Numpad;
                _haptic = true;
            }
        }

        private sealed class FakeClock : IClock
        {
            public long CurrentTimestamp { get; set; } = 1_000_000;
            public long AutoAdvanceMs { get; set; } = 0;

            public long GetTimestamp()
            {
                var ts = CurrentTimestamp;
                CurrentTimestamp += AutoAdvanceMs;
                return ts;
            }

            public TimeSpan GetElapsedTime(long startTimestamp)
            {
                var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, CurrentTimestamp - startTimestamp));
                CurrentTimestamp += AutoAdvanceMs;
                return elapsed;
            }
        }

        private sealed class TestHapticFeedbackService : IHapticFeedbackService
        {
            public bool IsSupported => true;
            public void PerformCue(HapticFeedbackCue cue) { }
        }

        private sealed class NoResetCoordinator : IAppResetCoordinator
        {
            public Task<bool> ReconcileStartupResetStateAsync(CancellationToken ct = default) => Task.FromResult(false);
            public Task ExecuteFullResetAsync(CancellationToken ct = default) => Task.CompletedTask;
        }

        private sealed class TestJsRuntime : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
                ValueTask.FromResult(default(TValue)!);

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
                ValueTask.FromResult(default(TValue)!);
        }
    }
}
