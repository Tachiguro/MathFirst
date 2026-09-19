using System.Globalization;
using System.Text.RegularExpressions;

namespace MathFirst.Application;

public sealed record AppBuildMetadata(
    string ApplicationTitle,
    string ApplicationId,
    string DisplayVersion,
    int BuildNumber,
    string SourceCommit,
    string BuildClassification);

public static class AppBuildInfoMetadataParser
{
    private const string ApplicationTitleMetadataKey = "MathFirst.ApplicationTitle";
    private const string ApplicationIdMetadataKey = "MathFirst.ApplicationId";
    private const string ApplicationDisplayVersionMetadataKey = "MathFirst.ApplicationDisplayVersion";
    private const string ApplicationVersionMetadataKey = "MathFirst.ApplicationVersion";
    private const string SourceCommitMetadataKey = "MathFirst.SourceCommit";
    private const string BuildClassificationMetadataKey = "MathFirst.BuildClassification";

    public const string DefaultSourceCommit = "local";
    public const string DefaultBuildClassification = "Local";

    private static readonly Regex GitShaRegex = new("^[0-9a-fA-F]{7,40}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static AppBuildMetadata Parse(IReadOnlyDictionary<string, string?> metadata)
    {
        var applicationTitle = GetRequiredNonEmptyValue(metadata, ApplicationTitleMetadataKey);
        var applicationId = GetRequiredNonEmptyValue(metadata, ApplicationIdMetadataKey);
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

        var sourceCommit = ParseSourceCommit(metadata);
        var buildClassification = ParseBuildClassification(metadata);

        return new AppBuildMetadata(
            applicationTitle,
            applicationId,
            displayVersion,
            parsedBuildNumber,
            sourceCommit,
            buildClassification);
    }

    public static string GetShortSourceCommit(string sourceCommit)
    {
        if (string.IsNullOrWhiteSpace(sourceCommit) || string.Equals(sourceCommit, DefaultSourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultSourceCommit;
        }

        return sourceCommit.Length <= 8 ? sourceCommit : sourceCommit[..8];
    }

    private static string ParseSourceCommit(IReadOnlyDictionary<string, string?> metadata)
    {
        if (!metadata.TryGetValue(SourceCommitMetadataKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return DefaultSourceCommit;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, DefaultSourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultSourceCommit;
        }

        if (!GitShaRegex.IsMatch(trimmed))
        {
            throw new InvalidOperationException($"Assembly metadata '{SourceCommitMetadataKey}' must contain a valid commit SHA or '{DefaultSourceCommit}'.");
        }

        return trimmed.ToLowerInvariant();
    }

    private static string ParseBuildClassification(IReadOnlyDictionary<string, string?> metadata)
    {
        if (!metadata.TryGetValue(BuildClassificationMetadataKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return DefaultBuildClassification;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return "Local";
        }

        if (string.Equals(trimmed, "Tester", StringComparison.OrdinalIgnoreCase))
        {
            return "Tester";
        }

        if (string.Equals(trimmed, "SourceCandidate", StringComparison.OrdinalIgnoreCase))
        {
            return "SourceCandidate";
        }

        if (string.Equals(trimmed, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return "Production";
        }

        throw new InvalidOperationException($"Assembly metadata '{BuildClassificationMetadataKey}' contains an unrecognized classification '{trimmed}'.");
    }

    private static string GetRequiredNonEmptyValue(IReadOnlyDictionary<string, string?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required assembly metadata '{key}' is missing or empty.");
        }

        return value.Trim();
    }
}
