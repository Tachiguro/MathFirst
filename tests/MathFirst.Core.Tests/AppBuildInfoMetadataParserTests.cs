namespace MathFirst.Core.Tests;

using System.Globalization;
using MathFirst.Application;

public sealed class AppBuildInfoMetadataParserTests
{
    [Fact]
    public void Parse_ValidMetadata_ReturnsExactAssignments()
    {
        var parsed = AppBuildInfoMetadataParser.Parse(CreateMetadata());

        Assert.Equal("MathFirst", parsed.ApplicationTitle);
        Assert.Equal("1.0", parsed.DisplayVersion);
        Assert.Equal(42, parsed.BuildNumber);
    }

    [Theory]
    [InlineData("MathFirst.ApplicationTitle")]
    [InlineData("MathFirst.ApplicationDisplayVersion")]
    [InlineData("MathFirst.ApplicationVersion")]
    public void Parse_MissingRequiredMetadata_Throws(string missingKey)
    {
        var metadata = CreateMetadata();
        metadata.Remove(missingKey);

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Theory]
    [InlineData("MathFirst.ApplicationTitle", "")]
    [InlineData("MathFirst.ApplicationTitle", "   ")]
    [InlineData("MathFirst.ApplicationDisplayVersion", "")]
    [InlineData("MathFirst.ApplicationDisplayVersion", "   ")]
    [InlineData("MathFirst.ApplicationVersion", "")]
    [InlineData("MathFirst.ApplicationVersion", "   ")]
    public void Parse_EmptyOrWhitespaceRequiredMetadata_Throws(string key, string value)
    {
        var metadata = CreateMetadata();
        metadata[key] = value;

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Theory]
    [InlineData("1.a")]
    [InlineData("version 1.0")]
    public void Parse_MalformedDisplayVersion_Throws(string displayVersion)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.ApplicationDisplayVersion"] = displayVersion;

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Parse_NonPositiveOrNonIntegerBuild_Throws(string buildNumber)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.ApplicationVersion"] = buildNumber;

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Fact]
    public void Parse_BuildNumber_UsesInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var parsed = AppBuildInfoMetadataParser.Parse(CreateMetadata());

            Assert.Equal(42, parsed.BuildNumber);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static Dictionary<string, string?> CreateMetadata() => new(StringComparer.Ordinal)
    {
        ["MathFirst.ApplicationTitle"] = "MathFirst",
        ["MathFirst.ApplicationDisplayVersion"] = "1.0",
        ["MathFirst.ApplicationVersion"] = "42"
    };
}
