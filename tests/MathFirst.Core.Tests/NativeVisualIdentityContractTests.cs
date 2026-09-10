namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Xml;
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
        var assets = new[]
        {
            LoadSafeXml("Resources", "AppIcon", "appicon.svg"),
            LoadSafeXml("Resources", "AppIcon", "appiconfg.svg"),
            LoadSafeXml("Resources", "Splash", "splash.svg")
        };

        foreach (var asset in assets)
        {
            Assert.Equal("svg", asset.Root!.Name.LocalName);
            Assert.Equal("http://www.w3.org/2000/svg", asset.Root.Name.NamespaceName);
            Assert.Equal("0 0 256 256", asset.Root.Attribute("viewBox")!.Value);

            Assert.All(asset.Root.DescendantsAndSelf(), element =>
                Assert.Contains(element.Name.LocalName, new[] { "svg", "rect", "g", "path" }));

            var attributeValues = asset.Root
                .DescendantsAndSelf()
                .Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration)
                .Select(attribute => attribute.Value)
                .ToArray();

            Assert.DoesNotContain(attributeValues, value => ContainsExternalResourceReference(value));
            Assert.DoesNotContain(attributeValues, value => value.Contains("dotnet", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(asset.Root.DescendantsAndSelf(), element =>
                element.Name.LocalName is "script" or "text" or "image" or "foreignObject" or "use" or "style" or "linearGradient" or "radialGradient" or "pattern" or "filter");
        }

        foreach (var asset in assets.Skip(1))
        {
            var mark = asset.Root!
                .Descendants("{http://www.w3.org/2000/svg}g")
                .Single(element => string.Equals(element.Attribute("id")?.Value, "mathfirst-mf-mark", StringComparison.Ordinal));

            var paths = mark.Descendants("{http://www.w3.org/2000/svg}path").ToArray();
            Assert.NotEmpty(paths);
            Assert.All(paths, path => Assert.Equal("#FFFFFF", GetEffectiveFill(path)));
        }

        Assert.Equal("#176B4D", assets[0].Root!.Element("{http://www.w3.org/2000/svg}rect")!.Attribute("fill")!.Value);
    }

    [Fact]
    public void AndroidAndNativeHost_UseApprovedThemeAwarePalette()
    {
        var colors = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "Resources", "values", "colors.xml"));
        var values = colors.Root!.Elements("color").ToDictionary(element => element.Attribute("name")!.Value, element => element.Value, StringComparer.Ordinal);
        var app = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "App.xaml"));
        var mainPage = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MainPage.xaml"));

        Assert.Equal("#176B4D", values["colorPrimary"]);
        Assert.Equal("#0F523A", values["colorPrimaryDark"]);
        Assert.Equal("#0F523A", values["colorAccent"]);
        Assert.DoesNotContain("#512BD4", colors.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#2B0B98", colors.ToString(), StringComparison.OrdinalIgnoreCase);

        var nativeBackgrounds = app.Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                element => element.Value,
                StringComparer.Ordinal);
        Assert.Equal("#F4F7F5", nativeBackgrounds["NativeHostBackgroundLight"]);
        Assert.Equal("#121916", nativeBackgrounds["NativeHostBackgroundDark"]);

        Assert.Equal("ContentPage", mainPage.Root!.Name.LocalName);
        var blazorWebView = mainPage.Root.Descendants().Single(element => element.Name.LocalName == "BlazorWebView");
        AssertNativeBackgroundBinding(mainPage.Root);
        AssertNativeBackgroundBinding(blazorWebView);
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

    private static XDocument LoadSafeXml(params string[] assetSegments)
    {
        using var reader = XmlReader.Create(
            Path.Combine([GetRepositoryRoot(), "src", "MathFirst.App", .. assetSegments]),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });

        return XDocument.Load(reader);
    }

    private static bool ContainsExternalResourceReference(string value) =>
        new[] { "http:", "https:", "file:", "data:", "javascript:", "url(" }
            .Any(forbiddenValue => value.Contains(forbiddenValue, StringComparison.OrdinalIgnoreCase));

    private static string? GetEffectiveFill(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            var fill = current.Attribute("fill")?.Value;
            if (fill is not null)
            {
                return fill;
            }
        }

        return null;
    }

    private static void AssertNativeBackgroundBinding(XElement element) =>
        Assert.Equal(
            "{AppThemeBinding Light={StaticResource NativeHostBackgroundLight}, Dark={StaticResource NativeHostBackgroundDark}}",
            element.Attribute("BackgroundColor")!.Value);
}
