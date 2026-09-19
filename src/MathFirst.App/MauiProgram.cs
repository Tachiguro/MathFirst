using Microsoft.Extensions.Logging;
using MathFirst.Application;
using MathFirst.Application.Copy;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.App.Services;
using MathFirst.Infrastructure.Sqlite;

namespace MathFirst.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if ANDROID
		Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping(
			"StartupSurfaceBackground",
			(handler, _) =>
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#176B4D"));
			});
#endif

		builder.Services.AddSingleton<IPreferenceStore, MauiPreferenceStore>();
		builder.Services.AddSingleton<IThemeService, ThemeService>();
		builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
		builder.Services.AddSingleton<IHapticDriver, MauiHapticDriver>();
		builder.Services.AddSingleton<IHapticFeedbackService, HapticFeedbackService>();
		builder.Services.AddSingleton<IAppBackNavigationCoordinator, AppBackNavigationCoordinator>();
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddSingleton<AppBuildInfo>();
		builder.Services.AddSingleton<IAppPlatformInfo, MauiAppPlatformInfo>();
		builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();

		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mathfirst_learner.db");
		builder.Services.AddSingleton<ILearnerStore>(_ => new SqliteLearnerStore(dbPath));
		builder.Services.AddSingleton<IClock>(_ => MonotonicClock.Instance);
		builder.Services.AddSingleton<AdaptivePracticeSelector>();
		builder.Services.AddSingleton<TrainingSession>();
		builder.Services.AddSingleton<IPracticeCopyLibrary, PracticeCopyLibrary>();
		builder.Services.AddSingleton<PracticeCopySelector>();
		builder.Services.AddSingleton<PracticeGateCopyState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
