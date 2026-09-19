namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class TesterApkPackagingContractTests
{
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";
    private const string SampleCertSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ReleaseProfile_ExposesTesterMemberWhilePreservingExistingMembers()
    {
        var names = Enum.GetNames<ReleaseProfile>();

        Assert.Contains(nameof(ReleaseProfile.SourceCandidate), names);
        Assert.Contains(nameof(ReleaseProfile.Distributable), names);
        Assert.Contains(nameof(ReleaseProfile.Tester), names);
        Assert.Equal(3, names.Length);
    }

    [Theory]
    [InlineData("feat/mf-ux-005-tester-apk-workflow")]
    [InlineData("fix/tester-diagnostics")]
    public void RepositoryPolicy_Tester_AcceptsCleanAttachedFeatureBranch(string branch)
    {
        var request = CreateRequest(ReleaseProfile.Tester);
        var snapshot = CreateSnapshot(branch: branch);

        RepositoryPolicy.Validate(request, snapshot);
    }

    [Fact]
    public void RepositoryPolicy_Tester_AcceptsCleanSynchronizedMain()
    {
        var request = CreateRequest(ReleaseProfile.Tester);
        var snapshot = CreateSnapshot(branch: "main", localMainSha: FullSha, originMainSha: FullSha);

        RepositoryPolicy.Validate(request, snapshot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RepositoryPolicy_Tester_RejectsDetachedOrBlankBranch(string branch)
    {
        var request = CreateRequest(ReleaseProfile.Tester);
        var snapshot = CreateSnapshot(branch: branch);

        var exception = Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, snapshot));
        Assert.Contains("attached branch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryPolicy_Tester_RejectsCommitShaMismatch()
    {
        var request = CreateRequest(ReleaseProfile.Tester, expectedSha: new string('e', 40));
        var snapshot = CreateSnapshot();

        Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, snapshot));
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public void RepositoryPolicy_Tester_RejectsDirtyState(bool trackedClean, bool indexClean, int untrackedCount)
    {
        var request = CreateRequest(ReleaseProfile.Tester);
        var snapshot = CreateSnapshot(
            trackedClean: trackedClean,
            indexClean: indexClean,
            untrackedFiles: Enumerable.Range(0, untrackedCount).Select(i => $"file-{i}.tmp").ToArray());

        Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, snapshot));
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", FullSha)]
    [InlineData(FullSha, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RepositoryPolicy_Tester_OnMain_RejectsUnsynchronizedState(string localMainSha, string originMainSha)
    {
        var request = CreateRequest(ReleaseProfile.Tester);
        var snapshot = CreateSnapshot(branch: "main", localMainSha: localMainSha, originMainSha: originMainSha);

        var exception = Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, snapshot));
        Assert.Contains("identical", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SigningPolicy_Tester_RequiresNoProductionSigningInputs()
    {
        var result = SigningPolicy.Validate(
            ReleaseProfile.Tester,
            null,
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android"));

        Assert.Null(result);
    }

    [Fact]
    public void SigningPolicy_Tester_RejectsSuppliedSigningInputs()
    {
        var inputs = CreateExternalSigningInputs();

        var exception = Assert.Throws<ReleaseToolException>(() => SigningPolicy.Validate(
            ReleaseProfile.Tester,
            inputs,
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android")));

        Assert.Contains("does not accept production signing inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishInvocation_Tester_UsesReleaseApkConfigurationAndProjectsTesterIdentity()
    {
        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        Assert.Equal("dotnet", invocation.FileName);
        Assert.Contains("publish", invocation.Arguments);
        Assert.Contains("-f", invocation.Arguments);
        Assert.Contains("net10.0-android36.0", invocation.Arguments);
        Assert.Contains("-c", invocation.Arguments);
        Assert.Contains("Release", invocation.Arguments);
        Assert.Contains("-p:AndroidPackageFormat=apk", invocation.Arguments);
        Assert.Contains("-p:ApplicationId=com.tachiguro.mathfirst.tester", invocation.Arguments);
        Assert.Contains("-p:MathFirstBuildClassification=Tester", invocation.Arguments);
        Assert.Contains($"-p:MathFirstSourceCommit={FullSha}", invocation.Arguments);
        Assert.Contains("-p:AndroidKeyStore=false", invocation.Arguments);

        Assert.DoesNotContain(invocation.Arguments, arg => arg.Contains("AndroidSigningKeyStore", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation.Arguments, arg => arg.Contains("AndroidSigningKeyAlias", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation.Arguments, arg => arg.Contains("AndroidSigningStorePass", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation.Arguments, arg => arg.Contains("AndroidSigningKeyPass", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishInvocation_Tester_SupportsVersionOverrides()
    {
        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides("2.0", "42"),
            null);

        Assert.Contains("-property:ApplicationDisplayVersion=2.0", invocation.Arguments);
        Assert.Contains("-property:ApplicationVersion=42", invocation.Arguments);
    }

    [Fact]
    public void MetadataEvaluation_Tester_ProjectsTesterApplicationId()
    {
        var invocation = AndroidPackageCommand.CreateMetadataEvaluationInvocation(
            GetRepositoryRoot(),
            ReleaseProfile.Tester,
            new VersionOverrides("1.0", "1"));

        Assert.Contains("-property:ApplicationId=com.tachiguro.mathfirst.tester", invocation.Arguments);
        Assert.Contains("-property:Configuration=Release", invocation.Arguments);
        Assert.Contains("-property:TargetFramework=net10.0-android36.0", invocation.Arguments);
    }

    [Fact]
    public async Task FailClosed_Cli_RejectsTesterProfileUntilIntegrated()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var packageExitCode = await ReleaseCli.RunAsync(
            [
                "android-package",
                "--profile",
                "Tester",
                "--expected-commit-sha",
                FullSha
            ]);

            Assert.Equal(1, packageExitCode);
            Assert.Contains("Profile must be exactly 'SourceCandidate' or 'Distributable'", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task FailClosed_CliValidate_RejectsTesterProfileUntilIntegrated()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var validateExitCode = await ReleaseCli.RunAsync(
            [
                "android-validate",
                "--aab-path",
                Path.Combine(GetRepositoryRoot(), "test.aab"),
                "--provenance-path",
                Path.Combine(GetRepositoryRoot(), "test.json"),
                "--expected-commit-sha",
                FullSha,
                "--profile",
                "Tester"
            ]);

            Assert.Equal(1, validateExitCode);
            Assert.Contains("Profile must be exactly 'SourceCandidate' or 'Distributable'", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void FailClosed_AabValidator_RejectsTesterProfile()
    {
        var runner = new FakeRunner();
        var validator = new AndroidAabValidator(runner);
        var request = new ValidationRequest(
            Path.Combine(GetRepositoryRoot(), "dummy.aab"),
            Path.Combine(GetRepositoryRoot(), "dummy.provenance.json"),
            FullSha,
            ReleaseProfile.Tester);

        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("Unsupported release profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailClosed_PackageCommandExecute_RejectsTesterProfileUntilIntegrated()
    {
        var runner = new FakeRunner();
        var command = new AndroidPackageCommand(runner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, GetRepositoryRoot()));
        Assert.Contains("Tester profile is not yet fully integrated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PackageRequest CreateRequest(
        ReleaseProfile profile = ReleaseProfile.Tester,
        string expectedSha = FullSha,
        SigningInputs? signingInputs = null) =>
        new(profile, expectedSha, new VersionOverrides(null, null), signingInputs);

    private static RepositorySnapshot CreateSnapshot(
        string branch = "feat/mf-ux-005-tester-apk-workflow",
        bool trackedClean = true,
        bool indexClean = true,
        IReadOnlyList<string>? untrackedFiles = null,
        string localMainSha = FullSha,
        string originMainSha = FullSha) =>
        new(
            GetRepositoryRoot(),
            FullSha,
            branch,
            trackedClean,
            indexClean,
            untrackedFiles ?? [],
            localMainSha,
            originMainSha);

    private static SigningInputs CreateExternalSigningInputs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mathfirst-inputs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var keystore = Path.Combine(tempDir, "release.keystore");
        var storePass = Path.Combine(tempDir, "store.pass");
        var keyPass = Path.Combine(tempDir, "key.pass");
        File.WriteAllText(keystore, "fake-keystore");
        File.WriteAllText(storePass, "store-pass");
        File.WriteAllText(keyPass, "key-pass");

        return new SigningInputs(
            keystore,
            "release-alias",
            storePass,
            keyPass,
            SampleCertSha256);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class FakeRunner : IProcessRunner
    {
        public ProcessResult Run(ProcessInvocation invocation)
        {
            var cmd = $"{invocation.FileName} {string.Join(" ", invocation.Arguments)}";
            if (cmd.Contains("rev-parse --show-toplevel")) return new(0, GetRepositoryRoot() + "\n", "");
            if (cmd.Contains("rev-parse HEAD")) return new(0, FullSha + "\n", "");
            if (cmd.Contains("branch --show-current")) return new(0, "feat/mf-ux-005-tester-apk-workflow\n", "");
            if (cmd.Contains("rev-parse refs/heads/main")) return new(0, FullSha + "\n", "");
            if (cmd.Contains("rev-parse refs/remotes/origin/main")) return new(0, FullSha + "\n", "");
            if (cmd.Contains("status --porcelain=v2")) return new(0, "", "");
            return new(0, "", "");
        }
    }
}
