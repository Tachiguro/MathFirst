namespace MathFirst.Core.Tests;

using System.Globalization;
using MathFirst.Application;

public sealed class AppBuildInfoMetadataParserTests
{
    [Fact]
    public void Parse_CompleteValidProductionMetadata_ReturnsExactAssignments()
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = "cf1d2a3f4779c16f1c6104fe8736f405eb14d94e";
        metadata["MathFirst.BuildClassification"] = "Production";

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("MathFirst", parsed.ApplicationTitle);
        Assert.Equal("com.tachiguro.mathfirst", parsed.ApplicationId);
        Assert.Equal("1.0", parsed.DisplayVersion);
        Assert.Equal(42, parsed.BuildNumber);
        Assert.Equal("cf1d2a3f4779c16f1c6104fe8736f405eb14d94e", parsed.SourceCommit);
        Assert.Equal("Production", parsed.BuildClassification);
    }

    [Fact]
    public void Parse_CompleteValidTesterMetadata_ReturnsExactAssignments()
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = "cf1d2a3f4779c16f1c6104fe8736f405eb14d94e";
        metadata["MathFirst.BuildClassification"] = "Tester";

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("Tester", parsed.BuildClassification);
        Assert.Equal("cf1d2a3f4779c16f1c6104fe8736f405eb14d94e", parsed.SourceCommit);
    }

    [Fact]
    public void Parse_CompleteValidSourceCandidateMetadata_ReturnsExactAssignments()
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = "cf1d2a3f4779c16f1c6104fe8736f405eb14d94e";
        metadata["MathFirst.BuildClassification"] = "SourceCandidate";

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("SourceCandidate", parsed.BuildClassification);
    }

    [Fact]
    public void Parse_LocalBuildMetadata_ReturnsLocalDefaults()
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = "local";
        metadata["MathFirst.BuildClassification"] = "Local";

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("local", parsed.SourceCommit);
        Assert.Equal("Local", parsed.BuildClassification);
    }

    [Theory]
    [InlineData("MathFirst.ApplicationTitle")]
    [InlineData("MathFirst.ApplicationId")]
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
    [InlineData("MathFirst.ApplicationId", "")]
    [InlineData("MathFirst.ApplicationId", "   ")]
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

    [Fact]
    public void Parse_MissingClassification_DefaultsToLocalAndDoesNotReportProduction()
    {
        var metadata = CreateMetadata();
        metadata.Remove("MathFirst.BuildClassification");

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("Local", parsed.BuildClassification);
        Assert.NotEqual("Production", parsed.BuildClassification);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankClassification_DefaultsToLocalAndDoesNotReportProduction(string value)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.BuildClassification"] = value;

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("Local", parsed.BuildClassification);
        Assert.NotEqual("Production", parsed.BuildClassification);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Staging")]
    [InlineData("Prod")]
    [InlineData("Debug")]
    [InlineData("TesterBuild")]
    [InlineData("Release")]
    public void Parse_UnknownClassification_Throws(string unknownClassification)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.BuildClassification"] = unknownClassification;

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Fact]
    public void Parse_MissingSourceCommit_DefaultsToLocal()
    {
        var metadata = CreateMetadata();
        metadata.Remove("MathFirst.SourceCommit");

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("local", parsed.SourceCommit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankSourceCommit_DefaultsToLocal(string value)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = value;

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal("local", parsed.SourceCommit);
    }

    [Theory]
    [InlineData("cf1d2a3f4779c16f1c6104fe8736f405eb14d94e", "cf1d2a3f4779c16f1c6104fe8736f405eb14d94e")]
    [InlineData("CF1D2A3F4779C16F1C6104FE8736F405EB14D94E", "cf1d2a3f4779c16f1c6104fe8736f405eb14d94e")]
    [InlineData("cf1d2a3", "cf1d2a3")]
    [InlineData("CF1D2A3", "cf1d2a3")]
    [InlineData("199dbd7cd38feafc", "199dbd7cd38feafc")]
    public void Parse_ValidGitShaSourceCommit_PreservesNormalizedSha(string inputSha, string expectedSha)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = inputSha;

        var parsed = AppBuildInfoMetadataParser.Parse(metadata);

        Assert.Equal(expectedSha, parsed.SourceCommit);
    }

    [Theory]
    [InlineData("not-a-sha")]
    [InlineData("123456")]
    [InlineData("xyz1234567")]
    [InlineData("199dbd7cd38feafca5c94f9a5b1939bf2d912a20z")]
    [InlineData("cf1d2a3 with space")]
    public void Parse_MalformedSourceCommit_Throws(string invalidCommit)
    {
        var metadata = CreateMetadata();
        metadata["MathFirst.SourceCommit"] = invalidCommit;

        Assert.Throws<InvalidOperationException>(() => AppBuildInfoMetadataParser.Parse(metadata));
    }

    [Theory]
    [InlineData("local", "local")]
    [InlineData("LOCAL", "local")]
    [InlineData(null, "local")]
    [InlineData("", "local")]
    [InlineData("   ", "local")]
    [InlineData("cf1d2a3f4779c16f1c6104fe8736f405eb14d94e", "cf1d2a3f")]
    [InlineData("cf1d2a3", "cf1d2a3")]
    [InlineData("12345678", "12345678")]
    [InlineData("123456789", "12345678")]
    public void GetShortSourceCommit_ReturnsDeterministicShortIdentity(string? input, string expected)
    {
        var actual = AppBuildInfoMetadataParser.GetShortSourceCommit(input!);

        Assert.Equal(expected, actual);
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
        ["MathFirst.ApplicationId"] = "com.tachiguro.mathfirst",
        ["MathFirst.ApplicationDisplayVersion"] = "1.0",
        ["MathFirst.ApplicationVersion"] = "42",
        ["MathFirst.SourceCommit"] = "local",
        ["MathFirst.BuildClassification"] = "Local"
    };
}
