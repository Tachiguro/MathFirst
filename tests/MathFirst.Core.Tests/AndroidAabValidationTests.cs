namespace MathFirst.Core.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class AndroidAabValidationTests
{
    private const string FullSha = "179273ea46caea767912e1ec3984a5162310749a";
    private const string SampleCertSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Validator_RejectsMissingAabFile()
    {
        using var fixture = new ValidationTestFixture();
        var request = fixture.CreateRequest(aabPath: Path.Combine(fixture.Root, "nonexistent.aab"));

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsMissingProvenanceFile()
    {
        using var fixture = new ValidationTestFixture();
        var request = fixture.CreateRequest(provenancePath: Path.Combine(fixture.Root, "nonexistent.json"));

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-sha")]
    [InlineData("179273e")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void Validator_RejectsMalformedOrAbbreviatedExpectedSha(string expectedSha)
    {
        using var fixture = new ValidationTestFixture();
        var request = fixture.CreateRequest(expectedSha: expectedSha);

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsMalformedProvenanceJson()
    {
        using var fixture = new ValidationTestFixture();
        fixture.WriteProvenanceRaw("{ invalid json content }");
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsUnsupportedProvenanceSchemaVersion()
    {
        using var fixture = new ValidationTestFixture();
        fixture.WriteProvenance(fixture.CreateDefaultProvenance() with { SchemaVersion = 2 });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactFileNameMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { FileName = "different.aab" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactSizeMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { SizeBytes = 999999 }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceArtifactSha256Mismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Artifact = provenance.Artifact with { Sha256 = new string('f', 64) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceExpectedShaMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { ExpectedCommitSha = new string('e', 40) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceCommitShaMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { CommitSha = new string('c', 40) }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsDirtyProvenanceSourceState()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Source = provenance.Source with { WorkingTreeClean = false }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsNonReleaseProvenanceConfiguration()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { Configuration = "Debug" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsInvalidProvenanceTargetFramework()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { TargetFramework = "net9.0-android" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Theory]
    [InlineData("21.0", "36.0")]
    [InlineData("24.0", "35.0")]
    public void Validator_RejectsInvalidProvenanceSdkValues(string minSdk, string targetSdk)
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Build = provenance.Build with { MinSdk = minSdk, TargetSdk = targetSdk }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsProvenanceApplicationIdMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var provenance = fixture.CreateDefaultProvenance();
        fixture.WriteProvenance(provenance with
        {
            Application = provenance.Application with { Id = "com.other.app" }
        });
        var request = fixture.CreateRequest();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsBundletoolValidateFailure()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess("java", ["-jar", fixture.Tools.ResolveBundletoolJar(), "validate", $"--bundle={fixture.AabPath}"],
            exitCode: 1, standardError: "Bundletool validation error: invalid zip structure");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Theory]
    [InlineData("com.other.app", "1.0", "1", "24", "36", "false", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "2.0", "1", "24", "36", "false", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "2", "24", "36", "false", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "21", "36", "false", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "35", "false", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "true", null, null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "false", "android.permission.INTERNET", null, "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "false", null, "android.permission.ACCESS_NETWORK_STATE", "true", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "false", null, null, "false", "@xml/backup_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "false", null, null, "true", "@xml/wrong_rules", "@xml/data_extraction_rules")]
    [InlineData("com.tachiguro.mathfirst", "1.0", "1", "24", "36", "false", null, null, "true", "@xml/backup_rules", "@xml/wrong_rules")]
    public void Validator_RejectsInvalidBinaryManifestProperties(
        string packageId,
        string versionName,
        string versionCode,
        string minSdk,
        string targetSdk,
        string debuggable,
        string? permission1,
        string? permission2,
        string allowBackup,
        string fullBackupContent,
        string dataExtractionRules)
    {
        using var fixture = new ValidationTestFixture();
        var manifestXml = fixture.CreateManifestXml(
            packageId, versionName, versionCode, minSdk, targetSdk, debuggable,
            permission1, permission2, allowBackup, fullBackupContent, dataExtractionRules);
        fixture.ConfigureProcess("java", ["-jar", fixture.Tools.ResolveBundletoolJar(), "dump", "manifest", $"--bundle={fixture.AabPath}"],
            exitCode: 0, standardOutput: manifestXml);

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsMissingBackupXmlResourceEntries()
    {
        using var fixture = new ValidationTestFixture(includeBackupResources: false);
        var validator = fixture.CreateValidator();

        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsMissingDexEntries()
    {
        using var fixture = new ValidationTestFixture(includeDex: false);
        var validator = fixture.CreateValidator();

        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsDexdumpFailure()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureDexdumpFailure();

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_AcceptsAndroidSelfSignedSignatureWithTrustAndTimestampWarnings()
    {
        using var fixture = new ValidationTestFixture();
        const string realisticOutput = """
            jar verified.

            Warning:
            This jar contains entries whose certificate chain is invalid. Reason: PKIX path building failed: sun.security.provider.certpath.SunCertPathBuilderException: unable to find valid certification path to requested target
            This jar contains entries whose signer certificate is self-signed.
            This jar contains signatures that do not include a timestamp. Without a timestamp, users may not be able to validate this jar after any of the signer certificates expire (as early as 2056-07-07).

            Re-run with the -verbose and -certs options for more details.
            """;
        fixture.ConfigureProcess(fixture.Tools.ResolveJarsigner(), ["-verify", fixture.AabPath],
            exitCode: 0, standardOutput: realisticOutput);

        var validator = fixture.CreateValidator();
        var result = validator.Validate(fixture.CreateRequest(ReleaseProfile.SourceCandidate));

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
    }

    [Fact]
    public void Validator_RejectsJarsignerFailure()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess(fixture.Tools.ResolveJarsigner(), ["-verify", fixture.AabPath],
            exitCode: 1, standardError: "jarsigner error: certificate has expired");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsUnsignedJarOutput()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess(fixture.Tools.ResolveJarsigner(), ["-verify", fixture.AabPath],
            exitCode: 0, standardOutput: "jar is unsigned.\n");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsOutputWithoutJarVerifiedConfirmation()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess(fixture.Tools.ResolveJarsigner(), ["-verify", fixture.AabPath],
            exitCode: 0, standardOutput: "completed without verification confirmation");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsZeroSignerCertificates()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess(fixture.Tools.ResolveKeytool(), ["-printcert", "-jarfile", fixture.AabPath, "-rfc"],
            exitCode: 0, standardOutput: "");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_AcceptsSingleSignerWithCertificateChain()
    {
        using var fixture = new ValidationTestFixture();
        var leafCertPem = fixture.GenerateSelfSignedCertPem("CN=Signer Leaf");
        var intermediateCertPem = fixture.GenerateSelfSignedCertPem("CN=Signer Intermediate");
        var rootCertPem = fixture.GenerateSelfSignedCertPem("CN=Signer Root");

        var (leafSha, _) = fixture.ConfigureKeytoolCertChain(leafCertPem, intermediateCertPem, rootCertPem);

        var validator = fixture.CreateValidator();
        var result = validator.Validate(fixture.CreateRequest(ReleaseProfile.SourceCandidate));

        Assert.True(result.IsValid);
        Assert.Equal(leafSha, result.SignerCertificateSha256);
    }

    [Fact]
    public void Validator_Distributable_AcceptsSignerWithCertificateChainAndMatchesLeafFingerprint()
    {
        using var fixture = new ValidationTestFixture();
        var leafCertPem = fixture.GenerateSelfSignedCertPem("CN=MathFirst Release, O=Tachiguro");
        var intermediateCertPem = fixture.GenerateSelfSignedCertPem("CN=Intermediate CA, O=Tachiguro");
        var rootCertPem = fixture.GenerateSelfSignedCertPem("CN=Root CA, O=Tachiguro");

        var (leafSha, _) = fixture.ConfigureKeytoolCertChain(leafCertPem, intermediateCertPem, rootCertPem);

        var provenance = fixture.CreateDefaultProvenance() with
        {
            Artifact = fixture.CreateDefaultProvenance().Artifact with
            {
                Classification = "distributable-release-signed-pending-validation"
            },
            Source = fixture.CreateDefaultProvenance().Source with
            {
                Classification = "distributable",
                Ref = "main"
            },
            Signing = new ProvenanceSigning("release-expected-pending-validation", leafSha, null)
        };
        fixture.WriteProvenance(provenance);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: leafSha);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
        Assert.True(result.IsDistributable);
        Assert.Equal(leafSha, result.SignerCertificateSha256);
        Assert.Equal("release-distributable", result.SignerClassification);
    }

    [Fact]
    public void Validator_Distributable_RejectsWhenExpectedFingerprintMatchesIntermediateInsteadOfLeaf()
    {
        using var fixture = new ValidationTestFixture();
        var leafCertPem = fixture.GenerateSelfSignedCertPem("CN=MathFirst Release, O=Tachiguro");
        var intermediateCertPem = fixture.GenerateSelfSignedCertPem("CN=Intermediate CA, O=Tachiguro");

        fixture.ConfigureKeytoolCertChain(leafCertPem, intermediateCertPem);

        using var intermediateCert = X509Certificate2.CreateFromPem(intermediateCertPem);
        var intermediateSha = Convert.ToHexString(SHA256.HashData(intermediateCert.RawData)).ToLowerInvariant();

        var provenance = fixture.CreateDefaultProvenance() with
        {
            Artifact = fixture.CreateDefaultProvenance().Artifact with
            {
                Classification = "distributable-release-signed-pending-validation"
            },
            Source = fixture.CreateDefaultProvenance().Source with
            {
                Classification = "distributable",
                Ref = "main"
            },
            Signing = new ProvenanceSigning("release-expected-pending-validation", intermediateSha, null)
        };
        fixture.WriteProvenance(provenance);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: intermediateSha);

        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_RejectsMultipleSignerCertificates()
    {
        using var fixture = new ValidationTestFixture();
        var cert1 = fixture.GenerateSelfSignedCertPem("CN=Signer 1");
        var cert2 = fixture.GenerateSelfSignedCertPem("CN=Signer 2");
        fixture.ConfigureKeytoolMultipleSigners(cert1, cert2);

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_RejectsMalformedSignerStructureWithoutCertificates()
    {
        using var fixture = new ValidationTestFixture();
        fixture.ConfigureProcess(fixture.Tools.ResolveKeytool(), ["-printcert", "-jarfile", fixture.AabPath, "-rfc"],
            exitCode: 0, standardOutput: "Signer #1:\n\nNo certificates found here\n");

        var validator = fixture.CreateValidator();
        Assert.Throws<ReleaseToolException>(() => validator.Validate(fixture.CreateRequest()));
    }

    [Fact]
    public void Validator_SourceCandidate_AcceptsAndroidDebugSigner()
    {
        using var fixture = new ValidationTestFixture();
        var debugCertPem = fixture.GenerateSelfSignedCertPem("CN=Android Debug, O=Android, C=US");
        fixture.ConfigureKeytoolCert(debugCertPem);

        var validator = fixture.CreateValidator();
        var result = validator.Validate(fixture.CreateRequest(ReleaseProfile.SourceCandidate));

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
        Assert.False(result.IsDistributable);
        Assert.Equal("development-debug", result.SignerClassification);
    }

    [Fact]
    public void Validator_Distributable_RejectsAndroidDebugSigner()
    {
        using var fixture = new ValidationTestFixture();
        var debugCertPem = fixture.GenerateSelfSignedCertPem("CN=Android Debug, O=Android, C=US");
        var (certSha, _) = fixture.ConfigureKeytoolCert(debugCertPem);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: certSha);

        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_Distributable_RejectsSignerFingerprintMismatch()
    {
        using var fixture = new ValidationTestFixture();
        var releaseCertPem = fixture.GenerateSelfSignedCertPem("CN=MathFirst Release, O=Tachiguro");
        fixture.ConfigureKeytoolCert(releaseCertPem);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: new string('b', 64));

        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void Validator_Distributable_AcceptsValidReleaseSignerAndFingerprint()
    {
        using var fixture = new ValidationTestFixture();
        var releaseCertPem = fixture.GenerateSelfSignedCertPem("CN=MathFirst Release, O=Tachiguro");
        var (certSha, _) = fixture.ConfigureKeytoolCert(releaseCertPem);
        var provenance = fixture.CreateDefaultProvenance() with
        {
            Artifact = fixture.CreateDefaultProvenance().Artifact with
            {
                Classification = "distributable-release-signed-pending-validation"
            },
            Source = fixture.CreateDefaultProvenance().Source with
            {
                Classification = "distributable",
                Ref = "main"
            },
            Signing = new ProvenanceSigning("release-expected-pending-validation", certSha, null)
        };
        fixture.WriteProvenance(provenance);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: certSha);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, result.Status);
        Assert.True(result.IsDistributable);
        Assert.Equal(certSha, result.SignerCertificateSha256);
        Assert.Equal("release-distributable", result.SignerClassification);
    }

    [Fact]
    public void Validator_Distributable_RejectsExpiredCertificate()
    {
        using var fixture = new ValidationTestFixture();
        var expiredCertPem = fixture.GenerateSelfSignedCertPem("CN=MathFirst Release, O=Tachiguro", notBefore: DateTimeOffset.UtcNow.AddDays(-60), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        var (certSha, _) = fixture.ConfigureKeytoolCert(expiredCertPem);

        var validator = fixture.CreateValidator();
        var request = fixture.CreateRequest(
            ReleaseProfile.Distributable,
            expectedSignerSha: certSha);

        Assert.Throws<ReleaseToolException>(() => validator.Validate(request));
    }

    [Fact]
    public void PackagingIntegration_FailsWhenValidationFails()
    {
        using var fixture = new ValidationTestFixture();
        var packageRequest = new PackageRequest(
            ReleaseProfile.SourceCandidate,
            FullSha,
            new VersionOverrides(null, null),
            null);

        // Configure git inspection
        fixture.ConfigureProcess("git", ["rev-parse", "--show-toplevel"], exitCode: 0, standardOutput: fixture.Root + "\n");
        fixture.ConfigureProcess("git", ["rev-parse", "HEAD"], exitCode: 0, standardOutput: FullSha + "\n");
        fixture.ConfigureProcess("git", ["branch", "--show-current"], exitCode: 0, standardOutput: ReleaseConstants.SourceCandidateBranch + "\n");
        fixture.ConfigureProcess("git", ["rev-parse", "refs/heads/main"], exitCode: 0, standardOutput: FullSha + "\n");
        fixture.ConfigureProcess("git", ["rev-parse", "refs/remotes/origin/main"], exitCode: 0, standardOutput: FullSha + "\n");
        fixture.ConfigureProcess("git", ["status", "--porcelain=v2", "--untracked-files=all"], exitCode: 0, standardOutput: "");

        // Configure msbuild metadata evaluation
        const string metadataJson = """
            {
              "Properties": {
                "ApplicationTitle": "MathFirst",
                "ApplicationId": "com.tachiguro.mathfirst",
                "ApplicationDisplayVersion": "1.0",
                "ApplicationVersion": "1",
                "TargetFramework": "net10.0-android36.0",
                "SupportedOSPlatformVersion": "24.0",
                "TargetPlatformVersion": "36.0",
                "AndroidNETSdkVersion": "36.1.69"
              }
            }
            """;
        fixture.ProcessRunner.SetPrefixResult("dotnet msbuild", new ProcessResult(0, metadataJson, ""));
        fixture.ConfigureProcess("dotnet", ["--version"], exitCode: 0, standardOutput: "10.0.401\n");
        fixture.ConfigureProcess("dotnet", ["msbuild", "-version", "-nologo"], exitCode: 0, standardOutput: "17.12.0\n");

        // Custom publish handler that creates published AAB
        fixture.ProcessRunner.OnPublish = (publishInvocation) =>
        {
            var outputArg = publishInvocation.Arguments.SkipWhile(arg => arg != "--output").Skip(1).FirstOrDefault();
            if (outputArg is not null && Directory.Exists(outputArg))
            {
                var targetAab = Path.Combine(outputArg, "app.aab");
                File.Copy(fixture.AabPath, targetAab);
            }
        };

        // But bundletool validate fails for any AAB in staging
        fixture.ProcessRunner.DefaultBundletoolValidateResult = new ProcessResult(1, "", "bundletool validate: structural failure");

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        Assert.Throws<ReleaseToolException>(() => command.Execute(packageRequest, fixture.Root));

        // Ensure no promoted artifact directory exists
        var finalDir = Path.Combine(fixture.Root, "artifacts", "android", "source-candidate");
        Assert.False(Directory.Exists(finalDir));
    }

    private sealed class ValidationTestFixture : IDisposable
    {
        public string Root { get; }
        public string AabPath { get; }
        public string ProvenancePath { get; }
        public FakeProcessRunner ProcessRunner { get; }
        public FakeToolLocator Tools { get; }

        public ValidationTestFixture(bool includeBackupResources = true, bool includeDex = true)
        {
            Root = Path.Combine(Path.GetTempPath(), $"mathfirst-val-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);

            AabPath = Path.Combine(Root, "MathFirst-v1.0-b1-179273ea46ca-source-candidate-debug-signed.aab");
            ProvenancePath = Path.Combine(Root, "MathFirst-v1.0-b1-179273ea46ca-source-candidate-debug-signed.provenance.json");

            ProcessRunner = new FakeProcessRunner();
            Tools = new FakeToolLocator();

            CreateAabFile(includeBackupResources, includeDex);
            WriteProvenance(CreateDefaultProvenance());

            // Default process expectations
            ConfigureDefaultProcesses();
        }

        public AndroidAabValidator CreateValidator() => new(ProcessRunner, Tools);

        public ValidationRequest CreateRequest(
            ReleaseProfile profile = ReleaseProfile.SourceCandidate,
            string? aabPath = null,
            string? provenancePath = null,
            string expectedSha = FullSha,
            string? expectedSignerSha = null) =>
            new(
                aabPath ?? AabPath,
                provenancePath ?? ProvenancePath,
                expectedSha,
                profile,
                ExpectedDisplayVersion: "1.0",
                ExpectedBuildNumber: 1,
                ExpectedSignerCertificateSha256: expectedSignerSha);

        public ArtifactProvenance CreateDefaultProvenance()
        {
            var aabBytes = File.ReadAllBytes(AabPath);
            var aabSha = Convert.ToHexString(SHA256.HashData(aabBytes)).ToLowerInvariant();

            return new ArtifactProvenance(
                1,
                new ProvenanceArtifact(Path.GetFileName(AabPath), "source-candidate-debug-signed", aabBytes.Length, aabSha),
                new ProvenanceApplication("com.tachiguro.mathfirst", "1.0", 1),
                new ProvenanceSource(FullSha, FullSha, ReleaseConstants.SourceCandidateBranch, "source-candidate", true),
                new ProvenanceBuild("Release", "net10.0-android36.0", "24.0", "36.0", new VersionOverrides(null, null),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnetSdk"] = "10.0.401" }),
                new ProvenanceSigning("development-debug", null, null),
                DateTimeOffset.UtcNow);
        }

        public void WriteProvenance(ArtifactProvenance provenance)
        {
            var json = JsonSerializer.Serialize(provenance, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            File.WriteAllText(ProvenancePath, json + Environment.NewLine);
        }

        public void WriteProvenanceRaw(string content)
        {
            File.WriteAllText(ProvenancePath, content);
        }

        public void ConfigureProcess(string fileName, IReadOnlyList<string> arguments, int exitCode, string standardOutput = "", string standardError = "")
        {
            ProcessRunner.SetResult(fileName, arguments, new ProcessResult(exitCode, standardOutput, standardError));
        }

        public (string sha256, string pem) ConfigureKeytoolCert(string pem)
        {
            var cert = X509Certificate2.CreateFromPem(pem);
            var sha256 = Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();

            var output = $"Signer #1:\n\nCertificate #1:\nCertificate owner: {cert.Subject}\n\n{pem}\n";
            ConfigureProcess(Tools.ResolveKeytool(), ["-printcert", "-jarfile", AabPath, "-rfc"],
                exitCode: 0, standardOutput: output);
            return (sha256, pem);
        }

        public (string leafSha256, string output) ConfigureKeytoolCertChain(string leafPem, params string[] chainPems)
        {
            var leafCert = X509Certificate2.CreateFromPem(leafPem);
            var leafSha = Convert.ToHexString(SHA256.HashData(leafCert.RawData)).ToLowerInvariant();

            var sb = new StringBuilder();
            sb.AppendLine("Signer #1:");
            sb.AppendLine();
            sb.AppendLine($"Certificate #1:\nCertificate owner: {leafCert.Subject}\n\n{leafPem}\n");
            for (var i = 0; i < chainPems.Length; i++)
            {
                var chainCert = X509Certificate2.CreateFromPem(chainPems[i]);
                sb.AppendLine($"Certificate #{i + 2}:\nCertificate owner: {chainCert.Subject}\n\n{chainPems[i]}\n");
            }

            var output = sb.ToString();
            ConfigureProcess(Tools.ResolveKeytool(), ["-printcert", "-jarfile", AabPath, "-rfc"],
                exitCode: 0, standardOutput: output);
            return (leafSha, output);
        }

        public void ConfigureKeytoolMultipleSigners(string signer1Pem, string signer2Pem)
        {
            var cert1 = X509Certificate2.CreateFromPem(signer1Pem);
            var cert2 = X509Certificate2.CreateFromPem(signer2Pem);

            var output = $"Signer #1:\n\nCertificate #1:\nCertificate owner: {cert1.Subject}\n\n{signer1Pem}\n\nSigner #2:\n\nCertificate #1:\nCertificate owner: {cert2.Subject}\n\n{signer2Pem}\n";
            ConfigureProcess(Tools.ResolveKeytool(), ["-printcert", "-jarfile", AabPath, "-rfc"],
                exitCode: 0, standardOutput: output);
        }

        public void ConfigureDexdumpFailure()
        {
            ProcessRunner.DefaultDexdumpResult = new ProcessResult(1, "", "dexdump failure: bad dex file");
        }

        public string GenerateSelfSignedCertPem(
            string distinguishedName,
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var start = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
            var end = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
            using var cert = req.CreateSelfSigned(start, end);
            return cert.ExportCertificatePem();
        }

        public string CreateManifestXml(
            string packageId = "com.tachiguro.mathfirst",
            string versionName = "1.0",
            string versionCode = "1",
            string minSdk = "24",
            string targetSdk = "36",
            string debuggable = "false",
            string? permission1 = null,
            string? permission2 = null,
            string allowBackup = "true",
            string fullBackupContent = "@xml/backup_rules",
            string dataExtractionRules = "@xml/data_extraction_rules")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"{packageId}\" android:versionCode=\"{versionCode}\" android:versionName=\"{versionName}\">");
            sb.AppendLine($"  <uses-sdk android:minSdkVersion=\"{minSdk}\" android:targetSdkVersion=\"{targetSdk}\" />");
            if (!string.IsNullOrWhiteSpace(permission1))
            {
                sb.AppendLine($"  <uses-permission android:name=\"{permission1}\" />");
            }
            if (!string.IsNullOrWhiteSpace(permission2))
            {
                sb.AppendLine($"  <uses-permission android:name=\"{permission2}\" />");
            }
            sb.AppendLine($"  <application android:allowBackup=\"{allowBackup}\" android:fullBackupContent=\"{fullBackupContent}\" android:dataExtractionRules=\"{dataExtractionRules}\" android:debuggable=\"{debuggable}\">");
            sb.AppendLine("  </application>");
            sb.AppendLine("</manifest>");
            return sb.ToString();
        }

        private void ConfigureDefaultProcesses()
        {
            // bundletool validate
            ConfigureProcess("java", ["-jar", Tools.ResolveBundletoolJar(), "validate", $"--bundle={AabPath}"],
                exitCode: 0, standardOutput: "Bundle is valid.\n");

            // bundletool dump manifest
            ConfigureProcess("java", ["-jar", Tools.ResolveBundletoolJar(), "dump", "manifest", $"--bundle={AabPath}"],
                exitCode: 0, standardOutput: CreateManifestXml());

            // bundletool dump resources
            ConfigureProcess("java", ["-jar", Tools.ResolveBundletoolJar(), "dump", "resources", $"--bundle={AabPath}"],
                exitCode: 0, standardOutput: "Package 'com.tachiguro.mathfirst':\n  Type 'xml':\n    Resource 'backup_rules':\n      (default) - res/xml/backup_rules.xml\n      v28 - res/xml-v28/backup_rules.xml\n    Resource 'data_extraction_rules':\n      (default) - res/xml/data_extraction_rules.xml\n");

            // jarsigner verify
            const string realisticJarsignerOutput = """
                jar verified.

                Warning:
                This jar contains entries whose certificate chain is invalid. Reason: PKIX path building failed: sun.security.provider.certpath.SunCertPathBuilderException: unable to find valid certification path to requested target
                This jar contains entries whose signer certificate is self-signed.
                This jar contains signatures that do not include a timestamp. Without a timestamp, users may not be able to validate this jar after any of the signer certificates expire (as early as 2056-07-07).

                Re-run with the -verbose and -certs options for more details.
                """;
            ConfigureProcess(Tools.ResolveJarsigner(), ["-verify", AabPath],
                exitCode: 0, standardOutput: realisticJarsignerOutput);

            // keytool printcert
            var debugCert = GenerateSelfSignedCertPem("CN=Android Debug, O=Android, C=US");
            ConfigureKeytoolCert(debugCert);

            // dexdump default
            ProcessRunner.DefaultDexdumpResult = new ProcessResult(0, "DEX file header:\nchecksum: 1234\n", "");
        }

        private void CreateAabFile(bool includeBackupResources, bool includeDex)
        {
            using var archive = ZipFile.Open(AabPath, ZipArchiveMode.Create);
            var configEntry = archive.CreateEntry("BundleConfig.pb");
            using (var writer = new StreamWriter(configEntry.Open()))
            {
                writer.Write("synthetic-bundle-config");
            }

            var manifestEntry = archive.CreateEntry("base/manifest/AndroidManifest.xml");
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write("<manifest></manifest>");
            }

            if (includeDex)
            {
                var dexEntry = archive.CreateEntry("base/dex/classes.dex");
                using var writer = new StreamWriter(dexEntry.Open());
                writer.Write("dex\n035\0synthetic-dex-bytes");
            }

            if (includeBackupResources)
            {
                var r1 = archive.CreateEntry("base/res/xml/backup_rules.xml");
                using (var writer = new StreamWriter(r1.Open())) { writer.Write("<rules></rules>"); }
                var r2 = archive.CreateEntry("base/res/xml-v28/backup_rules.xml");
                using (var writer = new StreamWriter(r2.Open())) { writer.Write("<rules></rules>"); }
                var r3 = archive.CreateEntry("base/res/xml/data_extraction_rules.xml");
                using (var writer = new StreamWriter(r3.Open())) { writer.Write("<rules></rules>"); }
                var resPb = archive.CreateEntry("base/resources.pb");
                using (var writer = new StreamWriter(resPb.Open())) { writer.Write("synthetic-resources-pb"); }
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
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
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, ProcessResult> results = new(StringComparer.Ordinal);
        private readonly List<(string Prefix, ProcessResult Result)> prefixResults = [];
        public ProcessResult DefaultDexdumpResult { get; set; } = new(0, "DEX header", "");
        public ProcessResult? DefaultBundletoolValidateResult { get; set; }
        public Action<ProcessInvocation>? OnPublish { get; set; }

        public void SetResult(string fileName, IReadOnlyList<string> arguments, ProcessResult result)
        {
            var key = MakeKey(fileName, arguments);
            results[key] = result;
        }

        public void SetPrefixResult(string prefix, ProcessResult result)
        {
            prefixResults.Add((prefix, result));
        }

        public ProcessResult Run(ProcessInvocation invocation)
        {
            var fullCmd = MakeKey(invocation.FileName, invocation.Arguments);

            if (invocation.Arguments.Contains("publish"))
            {
                OnPublish?.Invoke(invocation);
                return new ProcessResult(0, "Publish success\n", "");
            }

            if (invocation.FileName.Contains("dexdump", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultDexdumpResult;
            }

            if (DefaultBundletoolValidateResult is not null && invocation.Arguments.Contains("validate"))
            {
                return DefaultBundletoolValidateResult;
            }

            if (results.TryGetValue(fullCmd, out var result))
            {
                return result;
            }

            foreach (var (prefix, res) in prefixResults)
            {
                if (fullCmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return res;
                }
            }

            throw new InvalidOperationException($"Unexpected process invocation: {fullCmd}");
        }

        private static string MakeKey(string fileName, IReadOnlyList<string> arguments) =>
            $"{fileName} {string.Join(" ", arguments)}";
    }
}
