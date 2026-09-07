namespace MathFirst.App;

public partial class App : Microsoft.Maui.Controls.Application
{
	public App(MathFirst.App.Services.IThemeService themeService)
	{
		InitializeComponent();
		themeService.Initialize(this);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage()) { Title = "MathFirst" };
	}
}
