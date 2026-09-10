using MathFirst.Application;
using MathFirst.App.Services;

namespace MathFirst.App;

public partial class App : Microsoft.Maui.Controls.Application
{
	private readonly TrainingSession _session;
	private readonly AppBuildInfo _buildInfo;

	public App(IThemeService themeService, TrainingSession session, AppBuildInfo buildInfo)
	{
		InitializeComponent();
		_session = session;
		_buildInfo = buildInfo;
		themeService.Initialize(this);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = _buildInfo.ApplicationTitle };
		window.Deactivated += (_, _) => _session.SetAppForeground(false);
		window.Stopped += (_, _) => _session.SetAppForeground(false);
		window.Resumed += (_, _) => _session.SetAppForeground(true);
		window.Activated += (_, _) => _session.SetAppForeground(true);
		return window;
	}

	protected override void OnSleep()
	{
		base.OnSleep();
		_session.SetAppForeground(false);
	}

	protected override void OnResume()
	{
		base.OnResume();
		_session.SetAppForeground(true);
	}
}
