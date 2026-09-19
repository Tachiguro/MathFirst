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
        ApplicationId = parsedMetadata.ApplicationId;
        DisplayVersion = parsedMetadata.DisplayVersion;
        BuildNumber = parsedMetadata.BuildNumber;
        SourceCommit = parsedMetadata.SourceCommit;
        ShortSourceCommit = AppBuildInfoMetadataParser.GetShortSourceCommit(parsedMetadata.SourceCommit);
        BuildClassification = parsedMetadata.BuildClassification;
    }

    public string ApplicationTitle { get; }

    public string ApplicationId { get; }

    public string DisplayVersion { get; }

    public int BuildNumber { get; }

    public string SourceCommit { get; }

    public string ShortSourceCommit { get; }

    public string BuildClassification { get; }

    public bool IsTesterBuild => string.Equals(BuildClassification, "Tester", StringComparison.Ordinal);
}
