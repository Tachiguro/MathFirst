namespace MathFirst.Core.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class AndroidApkValidationTests
{
    private const string FullSha = "179273ea46caea767912e1ec3984a5162310749a";
    private const string SampleCertSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TestCandidateBranch = "feat/mf-ux-005-tester-apk-workflow";

    [Fact]
    public void Validator_RejectsNullRequest()
    {
        using var fixture = new ApkValidationTestFixture();
        var validator = fixture.CreateValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    [Theory]
    [InlineData(ReleaseProfile.SourceCandidate)]
    [InlineData(ReleaseProfile.Distributable)]
    public void Validator_RejectsNonTesterProfiles(ReleaseProfile profile)
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest() with { Profile = profile };

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("only supports Tester profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsMissingApkFile()
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest(apkPath: Path.Combine(fixture.Root, "nonexistent.apk"));

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("artifact.aab")]
    [InlineData("artifact.zip")]
    [InlineData("artifact.txt")]
    public void Validator_RejectsWrongFileExtension(string fileName)
    {
        using var fixture = new ApkValidationTestFixture();
        var wrongExtPath = Path.Combine(fixture.Root, fileName);
        File.Copy(fixture.ApkPath, wrongExtPath);
        var request = fixture.CreateRequest(apkPath: wrongExtPath);

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains(".apk extension", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsEmptyApkFile()
    {
        using var fixture = new ApkValidationTestFixture();
        var emptyApk = Path.Combine(fixture.Root, "empty.apk");
        File.WriteAllBytes(emptyApk, []);
        var request = fixture.CreateRequest(apkPath: emptyApk);

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsMissingProvenanceFile()
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest(provenancePath: Path.Combine(fixture.Root, "nonexistent.json"));

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-sha")]
    [InlineData("179273e")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void Validator_RejectsMalformedOrAbbreviatedExpectedSha(string expectedSha)
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest(expectedSha: expectedSha);

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("40-character hexadecimal Git SHA", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsMalformedProvenanceJson()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.WriteProvenanceRaw("{ invalid json content }");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsUnsupportedProvenanceSchemaVersion()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.WriteProvenance(fixture.CreateDefaultProvenance() with { SchemaVersion = 2 });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactFileNameMismatch()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { FileName = "different.apk" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("file name mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactSizeMismatch()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { SizeBytes = 999999 }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("size mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactSha256Mismatch()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { Sha256 = new string('f', 64) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("SHA-256 mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsProvenanceExpectedShaMismatch()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { ExpectedCommitSha = new string('e', 40) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("expected commit SHA", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsProvenanceCommitShaMismatch()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { CommitSha = new string('c', 40) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("commit SHA", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsDirtyProvenanceSourceState()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { WorkingTreeClean = false }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("dirty working tree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsWrongApplicationIdInProvenance()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Application = provenance.Application with { Id = "com.tachiguro.mathfirst" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("application ID mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsDisplayVersionMismatchWithRequestedOverride()
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest(expectedDisplayVersion: "2.0");

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("display version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsBuildNumberMismatchWithRequestedOverride()
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest(expectedBuildNumber: 42);

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("build number", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("ReleaseWithDebug")]
    public void Validator_RejectsInvalidBuildConfigurationInProvenance(string config)
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { Configuration = config }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("build configuration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsInvalidTargetFrameworkInProvenance()
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { TargetFramework = "net9.0-android35.0" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("target framework", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("21", "36")]
    [InlineData("24", "35")]
    public void Validator_RejectsInvalidSdkVersionsInProvenance(string minSdk, string targetSdk)
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { MinSdk = minSdk, TargetSdk = targetSdk }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Theory]
    [InlineData("distributable", "tester-debug-signed", "development-debug")]
    [InlineData("source-candidate", "source-candidate-debug-signed", "development-debug")]
    [InlineData("tester", "distributable-release-signed-pending-validation", "development-debug")]
    [InlineData("tester", "tester-debug-signed", "release-distributable")]
    public void Validator_RejectsInvalidTesterClassificationsInProvenance(string sourceClass, string artifactClass, string signingState)
    {
        using var fixture = new ApkValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { Classification = sourceClass },
            Artifact = provenance.Artifact with { Classification = artifactClass },
            Signing = provenance.Signing with { State = signingState }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsCorruptZipArchive()
    {
        using var fixture = new ApkValidationTestFixture();
        File.WriteAllText(fixture.ApkPath, "corrupt zip bytes that are not a zip");
        fixture.UpdateApkProvenance();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsApkMissingManifest()
    {
        using var fixture = new ApkValidationTestFixture(includeManifest: false);
        fixture.UpdateApkProvenance();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("AndroidManifest.xml", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApkMissingDexPayload()
    {
        using var fixture = new ApkValidationTestFixture(includeDex: false);
        fixture.UpdateApkProvenance();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("DEX", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsAapt2ProcessFailure()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ProcessRunner.SetPrefixResult("aapt2 dump", new ProcessResult(1, "", "aapt2 internal error"));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("aapt2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsPackageIdMismatchInManifest()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(packageId: "com.tachiguro.mathfirst");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("package ID mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsVersionCodeMismatchInManifest()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(versionCode: "99");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("versionCode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsVersionNameMismatchInManifest()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(versionName: "99.0");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("versionName", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("21", "36")]
    [InlineData("24", "35")]
    public void Validator_RejectsSdkMismatchInManifest(string minSdk, string targetSdk)
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(minSdk: minSdk, targetSdk: targetSdk);
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Theory]
    [InlineData("android.permission.INTERNET", null)]
    [InlineData(null, "android.permission.ACCESS_NETWORK_STATE")]
    [InlineData("android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE")]
    public void Validator_RejectsForbiddenNetworkPermissionsInManifest(string? perm1, string? perm2)
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(permission1: perm1, permission2: perm2);
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("security violation", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsDebuggableTrueInManifest()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(debuggable: "true");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("debuggable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("false", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("true", "wrong", "@xml/data_extraction_rules")]
    [InlineData("true", "@xml/backup_rules", "wrong")]
    public void Validator_RejectsInvalidBackupWiringInManifest(string allowBackup, string fullBackup, string extractionRules)
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureAapt2XmlTree(allowBackup: allowBackup, fullBackupContent: fullBackup, dataExtractionRules: extractionRules);
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("backup policy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsMissingBackupResourceFilesInApk()
    {
        using var fixture = new ApkValidationTestFixture(includeBackupResources: false);
        fixture.UpdateApkProvenance();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("backup_rules", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsDexdumpFailure()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ProcessRunner.DefaultDexdumpResult = new ProcessResult(1, "", "dexdump verification failure");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("dexdump", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApksignerProcessFailure()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(1, "", "apksigner process error"));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("apksigner", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApksignerVerificationFailure()
    {
        using var fixture = new ApkValidationTestFixture();
        const string failOutput = """
            DOES NOT VERIFY
            ERROR: Signature verification failed
            """;
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, failOutput, ""));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("does not verify", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApksignerNoSigners()
    {
        using var fixture = new ApkValidationTestFixture();
        const string noSignerOutput = """
            Verifies
            Number of signers: 0
            """;
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, noSignerOutput, ""));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("no signers", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApksignerMultipleSigners()
    {
        using var fixture = new ApkValidationTestFixture();
        const string multiSignerOutput = """
            Verifies
            Number of signers: 2
            Signer #1 certificate DN: CN=Android Debug, O=Android, C=US
            Signer #1 certificate SHA-256 digest: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            Signer #2 certificate DN: CN=Android Debug, O=Android, C=US
            Signer #2 certificate SHA-256 digest: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            """;
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, multiSignerOutput, ""));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("exactly one signer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsApksignerMalformedCertificateSha256()
    {
        using var fixture = new ApkValidationTestFixture();
        const string malformedCertOutput = """
            Verifies
            Number of signers: 1
            Signer #1 certificate DN: CN=Android Debug, O=Android, C=US
            Signer #1 certificate SHA-256 digest: not-a-valid-sha
            """;
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, malformedCertOutput, ""));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_RejectsNonDebugSignerInTesterProfile()
    {
        using var fixture = new ApkValidationTestFixture();
        const string prodSignerOutput = """
            Verifies
            Verified using v2 scheme (APK Signature Scheme v2): true
            Verified using v3 scheme (APK Signature Scheme v3): true
            Number of signers: 1
            Signer #1 certificate DN: CN=MathFirst Release, O=Tachiguro, C=DE
            Signer #1 certificate SHA-256 digest: 4615d53aa96d9aec71ece1892982ad406a3bffce979f631246b22be8679c5b1a
            """;
        fixture.ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, prodSignerOutput, ""));
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var exception = Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
        Assert.Contains("non-debug certificate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_PassesOnValidTesterApk()
    {
        using var fixture = new ApkValidationTestFixture();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
        Assert.Equal(ReleaseProfile.Tester, result.Profile);
        Assert.False(result.IsDistributable);
        Assert.Equal("development-debug", result.SignerClassification);
        Assert.Equal(SampleCertSha256, result.SignerCertificateSha256);
        Assert.Equal(fixture.GetApkSha256(), result.ArtifactSha256);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Validator_PassesWithMultipleDexPayloads()
    {
        using var fixture = new ApkValidationTestFixture(multiDex: true);
        fixture.UpdateApkProvenance();
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
        Assert.Equal(ReleaseProfile.Tester, result.Profile);
    }

    [Fact]
    public void Validator_WithRepositoryState_AcceptsCleanTesterBranch()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureGitRepository(branch: TestCandidateBranch);
        var request = fixture.CreateRequest(repositoryRoot: fixture.Root);

        var validator = fixture.CreateValidator();
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, d => d.Contains("Repository state verified"));
    }

    [Fact]
    public void Validator_WithRepositoryState_AcceptsCleanSynchronizedMain()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureGitRepository(branch: "main", localMainSha: FullSha, originMainSha: FullSha);
        var request = fixture.CreateRequest(repositoryRoot: fixture.Root);

        var validator = fixture.CreateValidator();
        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_WithRepositoryState_RejectsDirtyState()
    {
        using var fixture = new ApkValidationTestFixture();
        fixture.ConfigureGitRepository(branch: TestCandidateBranch, statusOutput: "? dirty-file.tmp\n");
        var request = fixture.CreateRequest(repositoryRoot: fixture.Root);

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    private sealed class ApkValidationTestFixture : IDisposable
    {
        public string Root { get; }
        public string ApkPath { get; }
        public string ProvenancePath { get; }
        public FakeRunner ProcessRunner { get; } = new();
        public FakeToolLocator Tools { get; } = new();

        public ApkValidationTestFixture(
            bool includeManifest = true,
            bool includeDex = true,
            bool includeBackupResources = true,
            bool multiDex = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"mathfirst-apk-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            ApkPath = Path.Combine(Root, "com.tachiguro.mathfirst.tester.apk");
            ProvenancePath = Path.Combine(Root, "com.tachiguro.mathfirst.tester.provenance.json");

            CreateApkArchive(includeManifest, includeDex, includeBackupResources, multiDex);
            WriteProvenance(CreateDefaultProvenance());
            ConfigureDefaultProcesses();
        }

        public AndroidApkValidator CreateValidator() => new(ProcessRunner, Tools);

        public ApkValidationRequest CreateRequest(
            string? apkPath = null,
            string? provenancePath = null,
            string? expectedSha = null,
            string? expectedDisplayVersion = null,
            int? expectedBuildNumber = null,
            string? repositoryRoot = null) =>
            new(
                apkPath ?? ApkPath,
                provenancePath ?? ProvenancePath,
                expectedSha ?? FullSha,
                ReleaseProfile.Tester,
                expectedDisplayVersion,
                expectedBuildNumber,
                repositoryRoot);

        public ArtifactProvenance CreateDefaultProvenance()
        {
            var apkSha = GetApkSha256();
            var apkLength = new FileInfo(ApkPath).Length;
            return new ArtifactProvenance(
                1,
                new ProvenanceArtifact(Path.GetFileName(ApkPath), "tester-debug-signed", apkLength, apkSha),
                new ProvenanceApplication(ReleaseConstants.TesterApplicationId, "1.0", 1),
                new ProvenanceSource(FullSha, FullSha, TestCandidateBranch, "tester", true),
                new ProvenanceBuild(
                    ReleaseConstants.Configuration,
                    ReleaseConstants.TargetFramework,
                    "24",
                    "36",
                    new VersionOverrides(null, null),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["dotnetSdk"] = "10.0.401",
                        ["msbuild"] = "17.12.0",
                        ["androidNetSdk"] = "36.1.69"
                    }),
                new ProvenanceSigning("development-debug", null, null),
                DateTimeOffset.UtcNow);
        }

        public void WriteProvenance(ArtifactProvenance provenance)
        {
            var json = JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProvenancePath, json);
        }

        public void WriteProvenanceRaw(string rawJson) => File.WriteAllText(ProvenancePath, rawJson);

        public void UpdateApkProvenance() => WriteProvenance(CreateDefaultProvenance());

        public string GetApkSha256()
        {
            using var stream = File.OpenRead(ApkPath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public void ConfigureAapt2XmlTree(
            string packageId = "com.tachiguro.mathfirst.tester",
            string versionCode = "1",
            string versionName = "1.0",
            string minSdk = "24",
            string targetSdk = "36",
            string debuggable = "false",
            string? permission1 = null,
            string? permission2 = null,
            string allowBackup = "true",
            string fullBackupContent = "@0x7f120000",
            string dataExtractionRules = "@0x7f120001")
        {
            var sb = new StringBuilder();
            sb.AppendLine("N: android=http://schemas.android.com/apk/res/android (line=8)");
            sb.AppendLine("  E: manifest (line=8)");
            sb.AppendLine($"    A: http://schemas.android.com/apk/res/android:versionCode(0x0101021b)={versionCode}");
            sb.AppendLine($"    A: http://schemas.android.com/apk/res/android:versionName(0x0101021c)=\"{versionName}\" (Raw: \"{versionName}\")");
            sb.AppendLine($"    A: package=\"{packageId}\" (Raw: \"{packageId}\")");
            sb.AppendLine("      E: uses-sdk (line=9)");
            sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:minSdkVersion(0x0101020c)={minSdk}");
            sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:targetSdkVersion(0x01010270)={targetSdk}");
            if (!string.IsNullOrWhiteSpace(permission1))
            {
                sb.AppendLine("      E: uses-permission (line=10)");
                sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:name(0x01010003)=\"{permission1}\" (Raw: \"{permission1}\")");
            }
            if (!string.IsNullOrWhiteSpace(permission2))
            {
                sb.AppendLine("      E: uses-permission (line=11)");
                sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:name(0x01010003)=\"{permission2}\" (Raw: \"{permission2}\")");
            }
            sb.AppendLine("      E: application (line=12)");
            if (debuggable != "false")
            {
                sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:debuggable(0x0101000f)={debuggable}");
            }
            sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:allowBackup(0x01010280)={allowBackup}");
            sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:fullBackupContent(0x010104eb)={fullBackupContent}");
            sb.AppendLine($"        A: http://schemas.android.com/apk/res/android:dataExtractionRules(0x0101063e)={dataExtractionRules}");

            ProcessRunner.SetPrefixResult("aapt2 dump xmltree", new ProcessResult(0, sb.ToString(), ""));
        }

        public void ConfigureGitRepository(
            string branch = TestCandidateBranch,
            string headSha = FullSha,
            string localMainSha = FullSha,
            string originMainSha = FullSha,
            string statusOutput = "")
        {
            ProcessRunner.SetPrefixResult("git rev-parse --show-toplevel", new ProcessResult(0, Root + "\n", ""));
            ProcessRunner.SetPrefixResult("git rev-parse HEAD", new ProcessResult(0, headSha + "\n", ""));
            ProcessRunner.SetPrefixResult("git branch --show-current", new ProcessResult(0, branch + "\n", ""));
            ProcessRunner.SetPrefixResult("git rev-parse refs/heads/main", new ProcessResult(0, localMainSha + "\n", ""));
            ProcessRunner.SetPrefixResult("git rev-parse refs/remotes/origin/main", new ProcessResult(0, originMainSha + "\n", ""));
            ProcessRunner.SetPrefixResult("git status --porcelain=v2", new ProcessResult(0, statusOutput, ""));
        }

        private void ConfigureDefaultProcesses()
        {
            ConfigureAapt2XmlTree();

            // apksigner default
            const string apksignerOutput = $"""
                Verifies
                Verified using v1 scheme (JAR signing): false
                Verified using v2 scheme (APK Signature Scheme v2): true
                Verified using v3 scheme (APK Signature Scheme v3): true
                Verified using v3.1 scheme (APK Signature Scheme v3.1): false
                Verified using v4 scheme (APK Signature Scheme v4): false
                Number of signers: 1
                Signer #1 certificate DN: CN=Android Debug, O=Android, C=US
                Signer #1 certificate SHA-256 digest: {SampleCertSha256}
                """;
            ProcessRunner.SetPrefixResult("apksigner verify", new ProcessResult(0, apksignerOutput, ""));

            // dexdump default
            ProcessRunner.DefaultDexdumpResult = new ProcessResult(0, "DEX file header:\nchecksum: 1234\n", "");
        }

        private void CreateApkArchive(bool includeManifest, bool includeDex, bool includeBackupResources, bool multiDex)
        {
            using var archive = ZipFile.Open(ApkPath, ZipArchiveMode.Create);
            if (includeManifest)
            {
                var manifestEntry = archive.CreateEntry("AndroidManifest.xml");
                using var writer = new StreamWriter(manifestEntry.Open());
                writer.Write("synthetic-binary-manifest");
            }

            if (includeDex)
            {
                var dexEntry = archive.CreateEntry("classes.dex");
                using (var writer = new StreamWriter(dexEntry.Open()))
                {
                    writer.Write("dex\n035\0synthetic-dex-bytes");
                }

                if (multiDex)
                {
                    var dexEntry2 = archive.CreateEntry("classes2.dex");
                    using (var writer2 = new StreamWriter(dexEntry2.Open()))
                    {
                        writer2.Write("dex\n035\0synthetic-dex-bytes-2");
                    }
                }
            }

            if (includeBackupResources)
            {
                var r1 = archive.CreateEntry("res/xml/backup_rules.xml");
                using (var writer = new StreamWriter(r1.Open())) { writer.Write("<rules></rules>"); }
                var r2 = archive.CreateEntry("res/xml-v28/backup_rules.xml");
                using (var writer = new StreamWriter(r2.Open())) { writer.Write("<rules></rules>"); }
                var r3 = archive.CreateEntry("res/xml/data_extraction_rules.xml");
                using (var writer = new StreamWriter(r3.Open())) { writer.Write("<rules></rules>"); }
                var arsc = archive.CreateEntry("resources.arsc");
                using (var writer = new StreamWriter(arsc.Open())) { writer.Write("synthetic-resources-arsc"); }
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private sealed class FakeToolLocator : IValidationToolLocator
    {
        public string ResolveJava() => "java";
        public string ResolveBundletoolJar() => "C:\\tools\\bundletool.jar";
        public string ResolveJarsigner() => "jarsigner";
        public string ResolveKeytool() => "keytool";
        public string ResolveDexdump() => "dexdump";
        public string ResolveAapt2() => "aapt2";
        public string ResolveApksigner() => "apksigner";
    }

    private sealed class FakeRunner : IProcessRunner
    {
        private readonly List<(string Prefix, ProcessResult Result)> prefixResults = [];
        public ProcessResult DefaultDexdumpResult { get; set; } = new(0, "DEX header", "");

        public void SetPrefixResult(string prefix, ProcessResult result)
        {
            prefixResults.Add((prefix, result));
        }

        public ProcessResult Run(ProcessInvocation invocation)
        {
            var fullCmd = $"{invocation.FileName} {string.Join(" ", invocation.Arguments)}";

            for (var i = prefixResults.Count - 1; i >= 0; i--)
            {
                var (prefix, res) = prefixResults[i];
                if (fullCmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return res;
                }
            }

            if (invocation.FileName.Contains("dexdump", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultDexdumpResult;
            }

            throw new InvalidOperationException($"Unexpected process invocation in FakeRunner: {fullCmd}");
        }
    }
}
