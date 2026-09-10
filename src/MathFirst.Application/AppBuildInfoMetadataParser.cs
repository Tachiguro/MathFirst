using System.Globalization;

namespace MathFirst.Application;

public sealed record AppBuildMetadata(string ApplicationTitle, string DisplayVersion, int BuildNumber);

public static class AppBuildInfoMetadataParser
{
    private const string ApplicationTitleMetadataKey = "MathFirst.ApplicationTitle";
    private const string ApplicationDisplayVersionMetadataKey = "MathFirst.ApplicationDisplayVersion";
    private const string ApplicationVersionMetadataKey = "MathFirst.ApplicationVersion";

    public static AppBuildMetadata Parse(IReadOnlyDictionary<string, string?> metadata)
    {
        var applicationTitle = GetRequiredNonEmptyValue(metadata, ApplicationTitleMetadataKey);
        var displayVersion = GetRequiredNonEmptyValue(metadata, ApplicationDisplayVersionMetadataKey);
        if (!Version.TryParse(displayVersion, out _))
        {
            throw new InvalidOperationException($"Assembly metadata '{ApplicationDisplayVersionMetadataKey}' must contain a valid version.");
        }

        var buildNumber = GetRequiredNonEmptyValue(metadata, ApplicationVersionMetadataKey);
        if (!int.TryParse(buildNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBuildNumber) || parsedBuildNumber <= 0)
        {
            throw new InvalidOperationException($"Assembly metadata '{ApplicationVersionMetadataKey}' must contain a positive integer.");
        }

        return new AppBuildMetadata(applicationTitle, displayVersion, parsedBuildNumber);
    }

    private static string GetRequiredNonEmptyValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required assembly metadata '{key}' is missing or empty.");
        }

        return value;
    }
}
