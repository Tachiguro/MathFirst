namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class ReleaseProfileContractTests
{
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";
    private const string SampleCertSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Project_WindowsReleaseProfileRemainsUnpackagedAtApprovedMinimumVersion()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        Assert.Equal("None", GetWindowsProperty(project, "WindowsPackageType"));
        Assert.Equal("10.0.17763.0", GetWindowsProperty(project, "SupportedOSPlatformVersion"));
        Assert.Equal("10.0.17763.0", GetWindowsProperty(project, "TargetPlatformMinVersion"));
    }

    [Fact]
    public void Project_MathFirstApp_DeclaresTesterDiagnosticsPropertyDefaultsAndCompileConstant()
    {
        var project = XDocument.Load(GetRepositoryPath("src", "MathFirst.App", "MathFirst.App.csproj"));

        var debugDefault = project.Descendants("MathFirstEnableTesterDiagnostics")
            .Single(element => element.Attribute("Condition")?.Value.Contains("Debug", StringComparison.Ordinal) == true);
        Assert.Equal("true", debugDefault.Value.Trim());
        Assert.Contains("'$(Configuration)' == 'Debug'", debugDefault.Attribute("Condition")!.Value);

        var releaseDefault = project.Descendants("MathFirstEnableTesterDiagnostics")
            .Single(element => element.Attribute("Condition")?.Value == "'$(MathFirstEnableTesterDiagnostics)' == ''");
        Assert.Equal("false", releaseDefault.Value.Trim());

        var defineConstantsGroup = project.Descendants("PropertyGroup")
            .Single(element => element.Attribute("Condition")?.Value == "'$(MathFirstEnableTesterDiagnostics)' == 'true'");
        var defineConstants = defineConstantsGroup.Element("DefineConstants");
        Assert.NotNull(defineConstants);
        Assert.Equal("$(DefineConstants);MATHFIRST_TESTER_DIAGNOSTICS", defineConstants.Value.Trim());
    }

    [Fact]
    public void PublishInvocation_Tester_PropagatesTesterDiagnosticsAndProfileMetadata()
    {
        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        Assert.Contains("-p:MathFirstEnableTesterDiagnostics=true", invocation.Arguments);
        Assert.Contains("-p:MathFirstBuildClassification=Tester", invocation.Arguments);
        Assert.Contains($"-p:MathFirstSourceCommit={FullSha}", invocation.Arguments);
        Assert.Contains("-p:AndroidPackageFormat=apk", invocation.Arguments);
        Assert.Contains("-p:ApplicationId=com.tachiguro.mathfirst.tester", invocation.Arguments);
        Assert.Contains("-p:AndroidKeyStore=false", invocation.Arguments);
    }

    [Fact]
    public void PublishInvocation_SourceCandidate_PropagatesCandidateDiagnosticsAndProfileMetadata()
    {
        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.SourceCandidate,
            FullSha,
            new VersionOverrides(null, null),
            null);

        Assert.Contains("-p:MathFirstEnableTesterDiagnostics=false", invocation.Arguments);
        Assert.Contains("-p:MathFirstBuildClassification=SourceCandidate", invocation.Arguments);
        Assert.Contains($"-p:MathFirstSourceCommit={FullSha}", invocation.Arguments);
        Assert.Contains("-p:AndroidPackageFormat=aab", invocation.Arguments);
        Assert.Contains("-p:AndroidKeyStore=false", invocation.Arguments);
        Assert.DoesNotContain(invocation.Arguments, arg => arg.Contains("AndroidSigningKeyStore", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishInvocation_Distributable_PropagatesProductionDiagnosticsAndProfileMetadata()
    {
        using var fixture = new ExternalSigningFixture();
        var inputs = fixture.CreateInputs();

        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.Distributable,
            FullSha,
            new VersionOverrides(null, null),
            inputs);

        Assert.Contains("-p:MathFirstEnableTesterDiagnostics=false", invocation.Arguments);
        Assert.Contains("-p:MathFirstBuildClassification=Production", invocation.Arguments);
        Assert.Contains($"-p:MathFirstSourceCommit={FullSha}", invocation.Arguments);
        Assert.Contains("-p:AndroidPackageFormat=aab", invocation.Arguments);
        Assert.Contains("-p:AndroidKeyStore=true", invocation.Arguments);
        Assert.Contains($"-p:AndroidSigningKeyStore={inputs.KeystorePath}", invocation.Arguments);
        Assert.Contains($"-p:AndroidSigningKeyAlias={inputs.KeyAlias}", invocation.Arguments);
        Assert.Contains($"-p:AndroidSigningStorePass=file:{inputs.StorePasswordFile}", invocation.Arguments);
        Assert.Contains($"-p:AndroidSigningKeyPass=file:{inputs.KeyPasswordFile}", invocation.Arguments);
    }

    private static string GetWindowsProperty(XDocument project, string name) =>
        project.Descendants(name)
            .Single(element => element.Attribute("Condition")?.Value.Contains("windows", StringComparison.Ordinal) == true)
            .Value;

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class ExternalSigningFixture : IDisposable
    {
        private readonly string _tempDirectory;

        public ExternalSigningFixture()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"mathfirst-signing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);
        }

        public SigningInputs CreateInputs()
        {
            var keystore = Path.Combine(_tempDirectory, "test.keystore");
            var storePass = Path.Combine(_tempDirectory, "store.pass");
            var keyPass = Path.Combine(_tempDirectory, "key.pass");
            File.WriteAllText(keystore, "fake-keystore");
            File.WriteAllText(storePass, "fake-store-pass");
            File.WriteAllText(keyPass, "fake-key-pass");

            return new SigningInputs(
                keystore,
                "test-alias",
                storePass,
                keyPass,
                SampleCertSha256);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
