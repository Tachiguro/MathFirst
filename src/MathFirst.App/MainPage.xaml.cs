using MathFirst.Application.Navigation;

namespace MathFirst.App;

public partial class MainPage : ContentPage
{
	private readonly IAppBackNavigationCoordinator _backCoordinator;

	public MainPage(IAppBackNavigationCoordinator backCoordinator)
	{
		InitializeComponent();
		_backCoordinator = backCoordinator ?? throw new ArgumentNullException(nameof(backCoordinator));
	}

	protected override bool OnBackButtonPressed()
	{
		if (_backCoordinator.TryHandleBack())
		{
			return true;
		}

		return base.OnBackButtonPressed();
	}
}
