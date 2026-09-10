using System.Globalization;
using System.Reflection;

namespace MathFirst.App.Services;

public sealed class AppBuildInfo
{
    private const string ApplicationTitleMetadataKey = "MathFirst.ApplicationTitle";
    private const string ApplicationDisplayVersionMetadataKey = "MathFirst.ApplicationDisplayVersion";
    private const string ApplicationVersionMetadataKey = "MathFirst.ApplicationVersion";

    public AppBuildInfo()
    {
        var metadata = typeof(AppBuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        ApplicationTitle = GetRequiredNonEmptyValue(metadata, ApplicationTitleMetadataKey);
        DisplayVersion = GetRequiredNonEmptyValue(metadata, ApplicationDisplayVersionMetadataKey);
        if (!Version.TryParse(DisplayVersion, out _))
        {
            throw new InvalidOperationException($"Assembly metadata '{ApplicationDisplayVersionMetadataKey}' must contain a valid version.");
        }

        var buildNumber = GetRequiredNonEmptyValue(metadata, ApplicationVersionMetadataKey);
        if (!int.TryParse(buildNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBuildNumber) || parsedBuildNumber <= 0)
        {
            throw new InvalidOperationException($"Assembly metadata '{ApplicationVersionMetadataKey}' must contain a positive integer.");
        }

        BuildNumber = parsedBuildNumber;
    }

    public string ApplicationTitle { get; }

    public string DisplayVersion { get; }

    public int BuildNumber { get; }

    private static string GetRequiredNonEmptyValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required assembly metadata '{key}' is missing or empty.");
        }

        return value;
    }
}
