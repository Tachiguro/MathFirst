namespace MathFirst.Application;

public static class AppDiagnosticFormatter
{
    public static string Format(
        string applicationTitle,
        string displayVersion,
        int buildNumber,
        string buildClassification,
        string shortSourceCommit,
        string platformDescription,
        string applicationId)
    {
        var title = string.IsNullOrWhiteSpace(applicationTitle) ? "MathFirst" : applicationTitle.Trim();
        var version = string.IsNullOrWhiteSpace(displayVersion) ? "1.0" : displayVersion.Trim();
        var build = buildNumber <= 0 ? 1 : buildNumber;
        var classification = string.IsNullOrWhiteSpace(buildClassification) ? AppBuildInfoMetadataParser.DefaultBuildClassification : buildClassification.Trim();
        var source = string.IsNullOrWhiteSpace(shortSourceCommit) ? AppBuildInfoMetadataParser.DefaultSourceCommit : shortSourceCommit.Trim();
        var platform = string.IsNullOrWhiteSpace(platformDescription) ? "Unknown" : platformDescription.Trim();
        var appId = string.IsNullOrWhiteSpace(applicationId) ? "com.tachiguro.mathfirst" : applicationId.Trim();

        return $"{title} {version} ({build})\nBuild: {classification}\nSource: {source}\nPlatform: {platform}\nApp ID: {appId}";
    }

    public static string Format(AppBuildMetadata metadata, string platformDescription)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return Format(
            metadata.ApplicationTitle,
            metadata.DisplayVersion,
            metadata.BuildNumber,
            metadata.BuildClassification,
            AppBuildInfoMetadataParser.GetShortSourceCommit(metadata.SourceCommit),
            platformDescription,
            metadata.ApplicationId);
    }
}
