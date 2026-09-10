namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MathFirst.Application;

public sealed class NativeVisualIdentityContractTests
{
    [Fact]
    public void Project_UsesApprovedMauiIconAndSplashConfiguration()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var icon = project.Descendants("MauiIcon").Single();
        var splash = project.Descendants("MauiSplashScreen").Single();

        Assert.Equal("Resources\\AppIcon\\appicon.svg", icon.Attribute("Include")!.Value);
        Assert.Equal("Resources\\AppIcon\\appiconfg.svg", icon.Attribute("ForegroundFile")!.Value);
        Assert.Equal("#176B4D", icon.Attribute("Color")!.Value);
        Assert.Equal("0.65", icon.Attribute("ForegroundScale")!.Value);
        Assert.Equal("Resources\\Splash\\splash.svg", splash.Attribute("Include")!.Value);
        Assert.Equal("#176B4D", splash.Attribute("Color")!.Value);
    }

    [Fact]
    public void ActiveIconAndSplashSources_UseTheCleanGeometricMathFirstMark()
    {
        var sources = new[]
        {
            File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Resources", "AppIcon", "appicon.svg")),
            File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Resources", "AppIcon", "appiconfg.svg")),
            File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Resources", "Splash", "splash.svg"))
        };

        foreach (var source in sources)
        {
            Assert.Contains("viewBox=\"0 0 256 256\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("<!DOCTYPE", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("serif", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dotnet", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<text", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("font", source, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var source in sources.Skip(1))
        {
            Assert.Contains("id=\"mathfirst-mf-mark\"", source, StringComparison.Ordinal);
            Assert.Contains("fill=\"#FFFFFF\"", source, StringComparison.Ordinal);
            Assert.Contains("<path", source, StringComparison.Ordinal);
        }

        Assert.Contains("fill=\"#176B4D\"", sources[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidAndNativeHost_UseApprovedThemeAwarePalette()
    {
        var colors = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "Resources", "values", "colors.xml"));
        var values = colors.Root!.Elements("color").ToDictionary(element => element.Attribute("name")!.Value, element => element.Value, StringComparer.Ordinal);
        var app = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "App.xaml"));
        var mainPage = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "MainPage.xaml"));

        Assert.Equal("#176B4D", values["colorPrimary"]);
        Assert.Equal("#0F523A", values["colorPrimaryDark"]);
        Assert.Equal("#0F523A", values["colorAccent"]);
        Assert.DoesNotContain("#512BD4", colors.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#2B0B98", colors.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("NativeHostBackgroundLight", app, StringComparison.Ordinal);
        Assert.Contains("#F4F7F5", app, StringComparison.Ordinal);
        Assert.Contains("NativeHostBackgroundDark", app, StringComparison.Ordinal);
        Assert.Contains("#121916", app, StringComparison.Ordinal);
        Assert.Contains("AppThemeBinding", mainPage, StringComparison.Ordinal);
        Assert.Contains("BackgroundColor", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatePayloads_AreRemovedAndNoLongerPackaged()
    {
        Assert.False(File.Exists(GetRepositoryPath("src", "MathFirst.App", "Resources", "Images", "dotnet_bot.svg")));
        Assert.False(File.Exists(GetRepositoryPath("src", "MathFirst.App", "Resources", "Raw", "AboutAssets.txt")));

        var project = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        Assert.DoesNotContain("dotnet_bot", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resources\\Images\\*", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Resources\\Raw\\**", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseVisibleFallbackCopy_UsesLocalizationAndMathFirstStartupIdentity()
    {
        var notFound = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "NotFound.razor"));
        var index = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "index.html"));
        var service = new LocalizationService();

        service.ApplyLanguagePreference("en");
        Assert.Equal("Page not found", service["NotFound_Title"]);
        Assert.Equal("The requested page could not be found.", service["NotFound_Description"]);
        service.ApplyLanguagePreference("de");
        Assert.Equal("Seite nicht gefunden", service["NotFound_Title"]);
        Assert.Equal("Die angeforderte Seite wurde nicht gefunden.", service["NotFound_Description"]);
        service.ApplyLanguagePreference("ru");
        Assert.Equal("Страница не найдена", service["NotFound_Title"]);
        Assert.Equal("Запрошенная страница не найдена.", service["NotFound_Description"]);

        Assert.Contains("@Localizer[\"NotFound_Title\"]", notFound, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"NotFound_Description\"]", notFound, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading...", index, StringComparison.Ordinal);
        Assert.Contains("<div id=\"app\">MathFirst</div>", index, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
