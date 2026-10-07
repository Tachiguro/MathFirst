namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MathFirst.ReleaseTool;
using Xunit;

[CollectionDefinition("ReleaseCli process console", DisableParallelization = true)]
public sealed class ReleaseCliProcessConsoleCollection
{
}

[Collection("ReleaseCli process console")]
public sealed class ReleaseCliFailClosedContractTests
{
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";

    [Fact]
    public async Task RunAsync_NoCommand_ReturnsExitCode1AndReportsError()
    {
        var (exitCode, errorOutput) = await RunCliAsync([]);

        Assert.Equal(1, exitCode);
        Assert.Contains("No command specified", errorOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expected 'android-package', 'android-validate', or 'android-validate-apk'", errorOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("definitely-unknown-command")]
    [InlineData("package")]
    [InlineData("validate")]
    public async Task RunAsync_UnknownCommand_ReturnsExitCode1AndReportsError(string command)
    {
        var (exitCode, errorOutput) = await RunCliAsync([command]);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Unknown command '{command}'", errorOutput, StringComparison.Ordinal);
        Assert.Contains("Expected 'android-package', 'android-validate', or 'android-validate-apk'", errorOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("android-package", "--profile")]
    [InlineData("android-package", "--expected-commit-sha")]
    [InlineData("android-validate", "--aab-path")]
    [InlineData("android-validate", "--provenance-path")]
    [InlineData("android-validate-apk", "--apk-path")]
    [InlineData("android-validate-apk", "--expected-commit-sha")]
    public async Task RunAsync_MissingOptionValue_ReturnsExitCode1AndReportsMissingValue(string command, string option)
    {
        var (exitCode, errorOutput) = await RunCliAsync([command, option]);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Missing value for command option '{option}'", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidPackage_DuplicateOption_ReturnsExitCode1AndReportsDuplicate()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-package",
            "--profile", "Tester",
            "--profile", "Tester",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command option '--profile' was specified more than once", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidate_DuplicateOption_ReturnsExitCode1AndReportsDuplicate()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--aab-path", "app.aab",
            "--aab-path", "app.aab",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha,
            "--profile", "SourceCandidate"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command option '--aab-path' was specified more than once", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidateApk_DuplicateOption_ReturnsExitCode1AndReportsDuplicate()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate-apk",
            "--apk-path", "app.apk",
            "--apk-path", "app.apk",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Command option '--apk-path' was specified more than once", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidPackage_UnknownOption_ReturnsExitCode1AndReportsUnknownOption()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-package",
            "--profile", "Tester",
            "--expected-commit-sha", FullSha,
            "--custom-unknown-flag", "value"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command option '--custom-unknown-flag'", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidPackage_MissingRequiredProfile_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-package",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--profile' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidPackage_MissingRequiredExpectedCommitSha_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-package",
            "--profile", "Tester"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--expected-commit-sha' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--keystore-path")]
    [InlineData("--key-alias")]
    [InlineData("--store-password-file")]
    [InlineData("--key-password-file")]
    [InlineData("--expected-signer-certificate-sha256")]
    public async Task RunAsync_AndroidPackage_DistributableMissingRequiredSigningOption_ReturnsExitCode1AndReportsMissing(string missingSigningOption)
    {
        var allSigningOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--keystore-path"] = @"C:\keys\release.keystore",
            ["--key-alias"] = "release-alias",
            ["--store-password-file"] = @"C:\keys\store.pass",
            ["--key-password-file"] = @"C:\keys\key.pass",
            ["--expected-signer-certificate-sha256"] = new string('a', 64)
        };

        var args = new List<string>
        {
            "android-package",
            "--profile", "Distributable",
            "--expected-commit-sha", FullSha
        };

        foreach (var (key, value) in allSigningOptions)
        {
            if (!string.Equals(key, missingSigningOption, StringComparison.Ordinal))
            {
                args.Add(key);
                args.Add(value);
            }
        }

        var (exitCode, errorOutput) = await RunCliAsync(args.ToArray());

        Assert.Equal(1, exitCode);
        Assert.Contains($"Required command option '{missingSigningOption}' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidate_MissingRequiredAabPath_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha,
            "--profile", "SourceCandidate"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--aab-path' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidate_MissingRequiredProvenancePath_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--aab-path", "app.aab",
            "--expected-commit-sha", FullSha,
            "--profile", "SourceCandidate"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--provenance-path' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidate_MissingRequiredExpectedCommitSha_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--aab-path", "app.aab",
            "--provenance-path", "provenance.json",
            "--profile", "SourceCandidate"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--expected-commit-sha' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidate_MissingRequiredProfile_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--aab-path", "app.aab",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--profile' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidateApk_MissingRequiredApkPath_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate-apk",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--apk-path' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidateApk_MissingRequiredProvenancePath_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate-apk",
            "--apk-path", "app.apk",
            "--expected-commit-sha", FullSha
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--provenance-path' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AndroidValidateApk_MissingRequiredExpectedCommitSha_ReturnsExitCode1AndReportsMissing()
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate-apk",
            "--apk-path", "app.apk",
            "--provenance-path", "provenance.json"
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Required command option '--expected-commit-sha' is missing", errorOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-42")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task RunAsync_AndroidValidate_InvalidBuildNumberOverride_ReturnsExitCode1AndReportsInvalidBuildNumber(string invalidBuildNumber)
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate",
            "--aab-path", "app.aab",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha,
            "--profile", "SourceCandidate",
            "--build-number", invalidBuildNumber
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Invalid build number override '{invalidBuildNumber}'", errorOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-42")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task RunAsync_AndroidValidateApk_InvalidBuildNumberOverride_ReturnsExitCode1AndReportsInvalidBuildNumber(string invalidBuildNumber)
    {
        var (exitCode, errorOutput) = await RunCliAsync([
            "android-validate-apk",
            "--apk-path", "app.apk",
            "--provenance-path", "provenance.json",
            "--expected-commit-sha", FullSha,
            "--build-number", invalidBuildNumber
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Invalid build number override '{invalidBuildNumber}'", errorOutput, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string ErrorOutput)> RunCliAsync(string[] args)
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync(args);
            return (exitCode, error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
