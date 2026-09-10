using System.Reflection;
using MathFirst.Application;

namespace MathFirst.App.Services;

public sealed class AppBuildInfo
{
    public AppBuildInfo()
    {
        var metadata = typeof(AppBuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        var parsedMetadata = AppBuildInfoMetadataParser.Parse(metadata);
        ApplicationTitle = parsedMetadata.ApplicationTitle;
        DisplayVersion = parsedMetadata.DisplayVersion;
        BuildNumber = parsedMetadata.BuildNumber;
    }

    public string ApplicationTitle { get; }

    public string DisplayVersion { get; }

    public int BuildNumber { get; }
}
