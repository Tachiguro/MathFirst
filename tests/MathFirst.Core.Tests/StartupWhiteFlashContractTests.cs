namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MathFirst.Application;
using MathFirst.Domain;

public sealed class StartupWhiteFlashContractTests
{
    private const string ExpectedBrandColor = "#176B4D";

    [Fact]
    public void IndexHtml_DefinesBrandedStartupSurfaceStylesBeforeExternalStylesheets()
    {
        var indexPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "index.html");
        var html = File.ReadAllText(indexPath);

        Assert.DoesNotContain("<div id=\"app\">MathFirst</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("MathFirst</div>", html, StringComparison.Ordinal);
        Assert.Contains("<div id=\"app\"></div>", html, StringComparison.Ordinal);

        var headMatch = Regex.Match(html, "<head>(?<headContent>.*?)</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(headMatch.Success, "index.html must contain a <head> block.");

        var headContent = headMatch.Groups["headContent"].Value;
        var styleMatch = Regex.Match(headContent, "<style>(?<styleContent>.*?)</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(styleMatch.Success, "index.html <head> must contain a synchronous inline <style> block.");

        var styleContent = styleMatch.Groups["styleContent"].Value;
        Assert.Contains("#app", styleContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background-color", styleContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ExpectedBrandColor, styleContent, StringComparison.OrdinalIgnoreCase);

        var bootstrapIndex = headContent.IndexOf("bootstrap", StringComparison.OrdinalIgnoreCase);
        var styleIndex = headContent.IndexOf("<style>", StringComparison.OrdinalIgnoreCase);
        Assert.True(styleIndex >= 0, "Inline <style> must exist in <head>.");
        Assert.True(styleIndex < bootstrapIndex, "Synchronous startup <style> must precede external bootstrap link to guarantee first-paint handoff.");
    }

    [Fact]
    public void MauiProgram_ConfiguresAndroidBlazorWebViewStartupBackground()
    {
        var mauiProgramPath = GetRepositoryPath("src", "MathFirst.App", "MauiProgram.cs");
        var source = File.ReadAllText(mauiProgramPath);

        Assert.Contains("#if ANDROID", source, StringComparison.Ordinal);
        Assert.Contains("BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping", source, StringComparison.Ordinal);
        Assert.Contains("SetBackgroundColor", source, StringComparison.Ordinal);
        Assert.Contains(ExpectedBrandColor, source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_MaintainsMinimalPermissionsWithoutNetworkAccess()
    {
        var manifestPath = GetRepositoryPath("src", "MathFirst.App", "Platforms", "Android", "AndroidManifest.xml");
        var manifest = XDocument.Load(manifestPath);
        var androidNs = (XNamespace)"http://schemas.android.com/apk/res/android";

        var permissions = manifest.Descendants("uses-permission")
            .Select(element => element.Attribute(androidNs + "name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        Assert.DoesNotContain("android.permission.INTERNET", permissions);
        Assert.DoesNotContain("android.permission.ACCESS_NETWORK_STATE", permissions);
        Assert.Equal(["android.permission.VIBRATE"], permissions);
    }

    [Fact]
    public void MauiSplashScreen_PreservesAuthoritativeBrandColor()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));
        var splash = project.Descendants("MauiSplashScreen").Single();

        Assert.Equal("Resources\\Splash\\splash.svg", splash.Attribute("Include")!.Value);
        Assert.Equal(ExpectedBrandColor, splash.Attribute("Color")!.Value);
    }

    [Fact]
    public void ThemePreference_ContractPreservesLightDarkSystemSemanticsWithoutLocalStorageDuplication()
    {
        // Domain Policy verification
        Assert.Equal(ThemePreference.System, ThemePreferencePolicy.Normalize((int)ThemePreference.System));
        Assert.Equal(ThemePreference.Light, ThemePreferencePolicy.Normalize((int)ThemePreference.Light));
        Assert.Equal(ThemePreference.Dark, ThemePreferencePolicy.Normalize((int)ThemePreference.Dark));
        Assert.Equal(ThemePreference.System, ThemePreferencePolicy.Normalize(999));

        // Source contract verification on ThemeService
        var themeServicePath = GetRepositoryPath("src", "MathFirst.App", "Services", "ThemeService.cs");
        var themeServiceSource = File.ReadAllText(themeServicePath);

        Assert.Contains("IPreferenceStore", themeServiceSource, StringComparison.Ordinal);
        Assert.Contains("EffectiveThemeCssName => EffectiveTheme == ThemePreference.Dark ? \"dark\" : \"light\"", themeServiceSource, StringComparison.Ordinal);
        Assert.Contains("UserAppTheme", themeServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", themeServiceSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IJSRuntime", themeServiceSource, StringComparison.Ordinal);

        // Source contract verification on Routes.razor
        var routesPath = GetRepositoryPath("src", "MathFirst.App", "Components", "Routes.razor");
        var routesSource = File.ReadAllText(routesPath);
        Assert.Contains("data-theme=\"@ThemeService.EffectiveThemeCssName\"", routesSource, StringComparison.Ordinal);

        // Source contract verification on mathfirst-ui.js
        var jsPath = GetRepositoryPath("src", "MathFirst.App", "wwwroot", "mathfirst-ui.js");
        var jsSource = File.ReadAllText(jsPath);
        Assert.DoesNotContain("localStorage", jsSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("theme", jsSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
