namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Xml.Linq;

public sealed class ReleaseProfileContractTests
{
    [Fact]
    public void Project_WindowsReleaseProfileRemainsUnpackagedAtApprovedMinimumVersion()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        Assert.Equal("None", GetWindowsProperty(project, "WindowsPackageType"));
        Assert.Equal("10.0.17763.0", GetWindowsProperty(project, "SupportedOSPlatformVersion"));
        Assert.Equal("10.0.17763.0", GetWindowsProperty(project, "TargetPlatformMinVersion"));
    }

    private static string GetWindowsProperty(XDocument project, string name) =>
        project.Descendants(name)
            .Single(element => element.Attribute("Condition")?.Value.Contains("windows", StringComparison.Ordinal) == true)
            .Value;

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
