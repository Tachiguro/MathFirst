namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public sealed class AppConstructorDependencyContractTests
{
    [Fact]
    public void AppConstructor_DoesNotDependDirectlyOnMainPageAndAcceptsServiceProvider()
    {
        var appPath = GetRepositoryPath("src", "MathFirst.App", "App.xaml.cs");
        var appSource = File.ReadAllText(appPath);

        // App constructor must not depend on MainPage directly to prevent early instantiation before App.InitializeComponent()
        Assert.DoesNotContain("MainPage mainPage", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly MainPage", appSource, StringComparison.Ordinal);

        // App constructor must accept IServiceProvider
        Assert.Contains("IServiceProvider services", appSource, StringComparison.Ordinal);
        Assert.Contains("_services = services ?? throw new ArgumentNullException(nameof(services));", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppConstructor_ExecutesInitializeComponentBeforeThemeInitialization()
    {
        var appPath = GetRepositoryPath("src", "MathFirst.App", "App.xaml.cs");
        var appSource = File.ReadAllText(appPath);

        var initComponentIndex = appSource.IndexOf("InitializeComponent();", StringComparison.Ordinal);
        var themeInitIndex = appSource.IndexOf("themeService.Initialize(this);", StringComparison.Ordinal);

        Assert.True(initComponentIndex >= 0, "App constructor must call InitializeComponent().");
        Assert.True(themeInitIndex >= 0, "App constructor must call themeService.Initialize(this).");
        Assert.True(initComponentIndex < themeInitIndex, "App.InitializeComponent() must execute before themeService.Initialize(this) to guarantee Application.Resources exist.");
    }

    [Fact]
    public void CreateWindow_ResolvesMainPageViaServiceProvider()
    {
        var appPath = GetRepositoryPath("src", "MathFirst.App", "App.xaml.cs");
        var appSource = File.ReadAllText(appPath);

        Assert.Contains("protected override Window CreateWindow(IActivationState? activationState)", appSource, StringComparison.Ordinal);
        Assert.Contains("var mainPage = _services.GetRequiredService<MainPage>();", appSource, StringComparison.Ordinal);
        Assert.Contains("var window = new Window(mainPage)", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiProgram_RegistersMainPageAsTransient()
    {
        var mauiProgramPath = GetRepositoryPath("src", "MathFirst.App", "MauiProgram.cs");
        var mauiProgramSource = File.ReadAllText(mauiProgramPath);

        Assert.Contains("builder.Services.AddTransient<MainPage>();", mauiProgramSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<MainPage>", mauiProgramSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppXaml_DefinesNativeHostBackgroundResourcesRequiredByMainPage()
    {
        var appXamlPath = GetRepositoryPath("src", "MathFirst.App", "App.xaml");
        var appXaml = XDocument.Load(appXamlPath);

        var colors = appXaml.Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                element => element.Value,
                StringComparer.Ordinal);

        Assert.Equal("#F4F7F5", colors["NativeHostBackgroundLight"]);
        Assert.Equal("#121916", colors["NativeHostBackgroundDark"]);
    }

    [Fact]
    public void MainPageXaml_ReferencesNativeHostBackgroundStaticResources()
    {
        var mainPageXamlPath = GetRepositoryPath("src", "MathFirst.App", "MainPage.xaml");
        var mainPageXaml = File.ReadAllText(mainPageXamlPath);

        Assert.Contains("StaticResource NativeHostBackgroundLight", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("StaticResource NativeHostBackgroundDark", mainPageXaml, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
