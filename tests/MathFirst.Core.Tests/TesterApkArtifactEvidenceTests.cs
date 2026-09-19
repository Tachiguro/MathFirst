namespace MathFirst.Core.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class TesterApkArtifactEvidenceTests
{
    private const string ArtifactId = "MathFirst-v1.0-b1-199dbd7cd38f-tester-debug-signed";
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";
    private const string ApkHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string ProvenanceHash = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string ReceiptHash = "3333333333333333333333333333333333333333333333333333333333333333";
    private const string ReadmeHash = "4444444444444444444444444444444444444444444444444444444444444444";
    private const string SignerHash = "5555555555555555555555555555555555555555555555555555555555555555";

    [Fact]
    public void CreateArtifactId_Tester_ProducesDeterministicTesterIdentifier()
    {
        var metadata = new ValidatedBuildMetadata(
            "MathFirst",
            ReleaseConstants.TesterApplicationId,
            "1.0",
            1,
            ReleaseConstants.TargetFramework,
            "24",
            "36");

        var artifactId = AndroidPackageCommand.CreateArtifactId(ReleaseProfile.Tester, metadata, FullSha);

        Assert.Equal("MathFirst-v1.0-b1-199dbd7cd38f-tester-debug-signed", artifactId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("199dbd7")]
    [InlineData("not-a-valid-sha-at-all-000000000000000000")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggg")]
    public void CreateArtifactId_Tester_RejectsMalformedOrAbbreviatedCommitSha(string invalidSha)
    {
        var metadata = new ValidatedBuildMetadata(
            "MathFirst",
            ReleaseConstants.TesterApplicationId,
            "1.0",
            1,
            ReleaseConstants.TargetFramework,
            "24",
            "36");

        Assert.Throws<ReleaseToolException>(() =>
            AndroidPackageCommand.CreateArtifactId(ReleaseProfile.Tester, metadata, invalidSha));
    }

    [Fact]
    public void CreateArtifactId_AabProfilesRemainUnchanged()
    {
        var metadata = new ValidatedBuildMetadata(
            "MathFirst",
            ReleaseConstants.ProductionApplicationId,
            "1.0",
            1,
            ReleaseConstants.TargetFramework,
            "24",
            "36");

        var sourceCandidateId = AndroidPackageCommand.CreateArtifactId(ReleaseProfile.SourceCandidate, metadata, FullSha);
        var distributableId = AndroidPackageCommand.CreateArtifactId(ReleaseProfile.Distributable, metadata, FullSha);

        Assert.Equal("MathFirst-v1.0-b1-199dbd7cd38f-source-candidate-debug-signed", sourceCandidateId);
        Assert.Equal("MathFirst-v1.0-b1-199dbd7cd38f-distributable-release-signed-pending-validation", distributableId);
    }

    [Fact]
    public void FindSingleApk_FindsSingleSignedApkCandidate()
    {
        using var repository = new TemporaryDirectory("mathfirst-find-single-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "find-single-apk");

        var signedApk = Path.Combine(workspace.PublishRoot, "com.tachiguro.mathfirst.tester-Signed.apk");
        File.WriteAllText(signedApk, "signed-apk");

        var found = workspace.FindSingleApk();
        Assert.Equal(Path.GetFullPath(signedApk), Path.GetFullPath(found));
    }

    [Fact]
    public void FindSingleApk_AcceptsMatchingUnsignedIntermediateAlongsideSignedCandidate()
    {
        using var repository = new TemporaryDirectory("mathfirst-find-intermediate-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "find-intermediate-apk");

        var signedApk = Path.Combine(workspace.PublishRoot, "com.tachiguro.mathfirst.tester-Signed.apk");
        var unsignedApk = Path.Combine(workspace.PublishRoot, "com.tachiguro.mathfirst.tester.apk");
        File.WriteAllText(signedApk, "signed-apk");
        File.WriteAllText(unsignedApk, "unsigned-intermediate");

        var found = workspace.FindSingleApk();
        Assert.Equal(Path.GetFullPath(signedApk), Path.GetFullPath(found));
    }

    [Fact]
    public void FindSingleApk_RejectsZeroSignedCandidates()
    {
        using var repository = new TemporaryDirectory("mathfirst-no-signed-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "no-signed-apk");

        var unsignedApk = Path.Combine(workspace.PublishRoot, "com.tachiguro.mathfirst.tester.apk");
        File.WriteAllText(unsignedApk, "unsigned-only");

        var exception = Assert.Throws<ReleaseToolException>(() => workspace.FindSingleApk());
        Assert.Contains("Expected exactly one signed APK candidate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSingleApk_RejectsEmptyPublishRoot()
    {
        using var repository = new TemporaryDirectory("mathfirst-empty-publish-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "empty-publish-apk");

        var exception = Assert.Throws<ReleaseToolException>(() => workspace.FindSingleApk());
        Assert.Contains("No APK files found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSingleApk_RejectsMultipleSignedCandidates()
    {
        using var repository = new TemporaryDirectory("mathfirst-multi-signed-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "multi-signed-apk");

        File.WriteAllText(Path.Combine(workspace.PublishRoot, "first-Signed.apk"), "signed-1");
        File.WriteAllText(Path.Combine(workspace.PublishRoot, "second-Signed.apk"), "signed-2");

        var exception = Assert.Throws<ReleaseToolException>(() => workspace.FindSingleApk());
        Assert.Contains("Expected exactly one signed APK candidate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSingleApk_RejectsMultipleUnsignedCandidates()
    {
        using var repository = new TemporaryDirectory("mathfirst-multi-unsigned-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "multi-unsigned-apk");

        File.WriteAllText(Path.Combine(workspace.PublishRoot, "app-Signed.apk"), "signed");
        File.WriteAllText(Path.Combine(workspace.PublishRoot, "app.apk"), "unsigned-1");
        File.WriteAllText(Path.Combine(workspace.PublishRoot, "other.apk"), "unsigned-2");

        var exception = Assert.Throws<ReleaseToolException>(() => workspace.FindSingleApk());
        Assert.Contains("Found multiple unsigned APK files", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindSingleApk_RejectsMismatchedUnsignedIntermediate()
    {
        using var repository = new TemporaryDirectory("mathfirst-mismatched-intermediate-apk");
        var workspace = new ArtifactWorkspace(repository.Path, "mismatched-intermediate-apk");

        File.WriteAllText(Path.Combine(workspace.PublishRoot, "app-Signed.apk"), "signed");
        File.WriteAllText(Path.Combine(workspace.PublishRoot, "other.apk"), "mismatched-intermediate");

        var exception = Assert.Throws<ReleaseToolException>(() => workspace.FindSingleApk());
        Assert.Contains("unexpected or ambiguous APK file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFinalDirectory_Tester_MapsToTesterHierarchy()
    {
        using var repository = new TemporaryDirectory("mathfirst-final-dir-tester");
        var workspace = new ArtifactWorkspace(repository.Path, "final-dir-tester");

        var finalDir = workspace.GetFinalDirectory(ReleaseProfile.Tester, ArtifactId);
        var expected = Path.Combine(repository.Path, "artifacts", "android", "tester", ArtifactId);

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(finalDir));
    }

    [Fact]
    public void GetFinalDirectory_AabProfilesRemainUnchanged()
    {
        using var repository = new TemporaryDirectory("mathfirst-final-dir-aab");
        var workspace = new ArtifactWorkspace(repository.Path, "final-dir-aab");

        var scDir = workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, "MathFirst-sc");
        var distDir = workspace.GetFinalDirectory(ReleaseProfile.Distributable, "MathFirst-dist");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(repository.Path, "artifacts", "android", "source-candidate", "MathFirst-sc")),
            Path.GetFullPath(scDir));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(repository.Path, "artifacts", "android", "distributable", "MathFirst-dist")),
            Path.GetFullPath(distDir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("invalid*char")]
    public void GetFinalDirectory_RejectsUnsafeArtifactId(string invalidArtifactId)
    {
        using var repository = new TemporaryDirectory("mathfirst-final-dir-unsafe");
        var workspace = new ArtifactWorkspace(repository.Path, "final-dir-unsafe");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.GetFinalDirectory(ReleaseProfile.Tester, invalidArtifactId));
    }

    [Fact]
    public void CreateProvenance_Tester_PopulatesExpectedApkArchitecture()
    {
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var repository = new RepositorySnapshot(
            @"C:\Dev\MathFirst",
            FullSha,
            "feat/mf-ux-005-tester-apk-workflow",
            true,
            true,
            [],
            FullSha,
            FullSha);

        var metadata = new ValidatedBuildMetadata(
            "MathFirst",
            ReleaseConstants.TesterApplicationId,
            "1.0",
            1,
            ReleaseConstants.TargetFramework,
            "24",
            "36");

        var toolVersions = new Dictionary<string, string>
        {
            ["dotnetSdk"] = "10.0.401",
            ["msbuild"] = "17.12.0",
            ["androidNetSdk"] = "36.0.0"
        };

        var generatedAt = DateTimeOffset.Parse("2026-09-19T04:00:00Z");

        var provenance = AndroidPackageCommand.CreateProvenance(
            request,
            repository,
            metadata,
            $"{ArtifactId}.apk",
            1234567,
            ApkHash,
            toolVersions,
            null,
            generatedAt);

        Assert.Equal(1, provenance.SchemaVersion);
        Assert.Equal($"{ArtifactId}.apk", provenance.Artifact.FileName);
        Assert.Equal(ReleaseConstants.TesterArtifactClassification, provenance.Artifact.Classification);
        Assert.Equal(1234567, provenance.Artifact.SizeBytes);
        Assert.Equal(ApkHash, provenance.Artifact.Sha256);

        Assert.Equal(ReleaseConstants.TesterApplicationId, provenance.Application.Id);
        Assert.Equal("1.0", provenance.Application.DisplayVersion);
        Assert.Equal(1, provenance.Application.BuildNumber);

        Assert.Equal(FullSha, provenance.Source.ExpectedCommitSha);
        Assert.Equal(FullSha, provenance.Source.CommitSha);
        Assert.Equal("feat/mf-ux-005-tester-apk-workflow", provenance.Source.Ref);
        Assert.Equal(ReleaseConstants.TesterSourceClassification, provenance.Source.Classification);
        Assert.True(provenance.Source.WorkingTreeClean);

        Assert.Equal(ReleaseConstants.Configuration, provenance.Build.Configuration);
        Assert.Equal(ReleaseConstants.TargetFramework, provenance.Build.TargetFramework);
        Assert.Equal("24", provenance.Build.MinSdk);
        Assert.Equal("36", provenance.Build.TargetSdk);

        Assert.Equal(ReleaseConstants.TesterSigningState, provenance.Signing.State);
        Assert.Null(provenance.Signing.ExpectedCertificateSha256);
        Assert.Null(provenance.Signing.CertificateSha256);

        Assert.Equal(generatedAt, provenance.GeneratedAtUtc);
    }

    [Fact]
    public void ValidationReceipt_Tester_BindsExactApkAndProvenanceEvidence()
    {
        var validationResult = new ValidationResult(
            true,
            ArtifactValidationStatus.ValidatorApproved,
            ReleaseProfile.Tester,
            false,
            ApkHash,
            SignerHash,
            "development-debug",
            ["Validation passed."]);

        var generatedAt = DateTimeOffset.Parse("2026-09-19T04:15:30Z");

        var receipt = ReleaseEvidenceGenerator.CreateValidationReceipt(
            generatedAt,
            ReleaseProfile.Tester,
            validationResult,
            $"{ArtifactId}.apk",
            ApkHash,
            $"{ArtifactId}.provenance.json",
            ProvenanceHash);

        Assert.Equal(1, receipt.SchemaVersion);
        Assert.Equal(ReleaseProfile.Tester, receipt.Profile);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, receipt.Status);
        Assert.False(receipt.IsDistributable);
        Assert.Equal($"{ArtifactId}.apk", receipt.Artifact.FileName);
        Assert.Equal(ApkHash, receipt.Artifact.Sha256);
        Assert.Equal($"{ArtifactId}.provenance.json", receipt.Provenance.FileName);
        Assert.Equal(ProvenanceHash, receipt.Provenance.Sha256);
        Assert.Equal(SignerHash, receipt.Signer.CertificateSha256);
        Assert.Equal("development-debug", receipt.Signer.Classification);

        var serialized = ReleaseEvidenceGenerator.SerializeValidationReceipt(receipt);
        Assert.Contains("\"profile\": \"Tester\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"ValidatorApproved\"", serialized, StringComparison.Ordinal);
        Assert.Contains("\"isDistributable\": false", serialized, StringComparison.Ordinal);
        Assert.Contains($"\"fileName\": \"{ArtifactId}.apk\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', serialized);
    }

    [Fact]
    public void TesterReadme_TesterApk_CommunicatesTruthfulApkSpecificBoundaries()
    {
        var provenance = CreateTesterProvenance();
        var receipt = CreateTesterReceipt();

        var readme = ReleaseEvidenceGenerator.CreateTesterReadme(
            ArtifactId,
            provenance,
            receipt);

        // Core header metadata
        Assert.Contains(ArtifactId, readme, StringComparison.Ordinal);
        Assert.Contains(ReleaseConstants.TesterApplicationId, readme, StringComparison.Ordinal);
        Assert.Contains("1.0", readme, StringComparison.Ordinal);
        Assert.Contains("1", readme, StringComparison.Ordinal);
        Assert.Contains(FullSha, readme, StringComparison.Ordinal);
        Assert.Contains("Profile: Tester", readme, StringComparison.Ordinal);
        Assert.Contains(ReleaseConstants.TesterArtifactClassification, readme, StringComparison.Ordinal);
        Assert.Contains("ValidatorApproved", readme, StringComparison.Ordinal);
        Assert.Contains("development-debug", readme, StringComparison.Ordinal);
        Assert.Contains("Distributable: no", readme, StringComparison.Ordinal);

        // Five evidence files listed
        Assert.Contains($"{ArtifactId}.apk", readme, StringComparison.Ordinal);
        Assert.Contains($"{ArtifactId}.provenance.json", readme, StringComparison.Ordinal);
        Assert.Contains($"{ArtifactId}.validation.json", readme, StringComparison.Ordinal);
        Assert.Contains("TESTER_README.md", readme, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", readme, StringComparison.Ordinal);

        // APK-specific boundary assertions
        Assert.Contains("directly installable", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-production", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Google Play", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production signing credentials", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("com.tachiguro.mathfirst", readme, StringComparison.Ordinal);
        Assert.Contains("separate application sandbox", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installation is a separate", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("device verification", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same signing identity", readme, StringComparison.OrdinalIgnoreCase);

        // Disclaimers: no false claims, no secrets
        Assert.DoesNotContain("keystore", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', readme);
        Assert.EndsWith("\n", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Sha256Sums_Tester_ContainsExactlyFourTesterPayloads()
    {
        var sums = ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            ReleaseProfile.Tester,
            [
                new("TESTER_README.md", ReadmeHash),
                new($"{ArtifactId}.validation.json", ReceiptHash),
                new($"{ArtifactId}.apk", ApkHash),
                new($"{ArtifactId}.provenance.json", ProvenanceHash)
            ]);

        Assert.Equal(
            $"{ApkHash}  {ArtifactId}.apk\n" +
            $"{ProvenanceHash}  {ArtifactId}.provenance.json\n" +
            $"{ReceiptHash}  {ArtifactId}.validation.json\n" +
            $"{ReadmeHash}  TESTER_README.md\n",
            sums);
        Assert.DoesNotContain("SHA256SUMS", sums, StringComparison.Ordinal);
        Assert.DoesNotContain(".aab", sums, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', sums);
        Assert.Equal(4, sums.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.EndsWith("\n", sums, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactWorkspace_AtomicallyPromotesValidTesterEvidenceDirectory()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-promote");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-promote");
        WriteValidTesterEvidence(workspace);

        workspace.Promote(ReleaseProfile.Tester, ArtifactId);

        var finalDirectory = workspace.GetFinalDirectory(ReleaseProfile.Tester, ArtifactId);
        Assert.False(Directory.Exists(workspace.ReadyRoot));
        Assert.True(Directory.Exists(finalDirectory));
        Assert.Equal(
            [
                $"{ArtifactId}.apk",
                $"{ArtifactId}.provenance.json",
                $"{ArtifactId}.validation.json",
                "SHA256SUMS",
                "TESTER_README.md"
            ],
            Directory.GetFiles(finalDirectory).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsAabInsteadOfApk()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-reject-aab");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-reject-aab");
        WriteValidTesterEvidence(workspace);

        // Replace .apk with .aab
        File.Move(
            Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.apk"),
            Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.aab"));

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsDistributableClaim()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-reject-distributable");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-reject-distributable");
        var receipt = WriteValidTesterEvidence(workspace);

        RewriteReceiptAndChecksums(workspace, receipt with { IsDistributable = true });

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsReleaseDistributableSignerClassification()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-reject-release-signer");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-reject-release-signer");
        var receipt = WriteValidTesterEvidence(workspace);

        RewriteReceiptAndChecksums(
            workspace,
            receipt with { Signer = receipt.Signer with { Classification = "release-distributable" } });

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsExtraFiles()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-reject-extra");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-reject-extra");
        WriteValidTesterEvidence(workspace);

        File.WriteAllText(Path.Combine(workspace.ReadyRoot, "extra-artifact.apk"), "extra");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsMissingFiles()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-reject-missing");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-reject-missing");
        WriteValidTesterEvidence(workspace);

        File.Delete(Path.Combine(workspace.ReadyRoot, "TESTER_README.md"));

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_Tester_RejectsOverwritingExistingFinalDirectory()
    {
        using var repository = new TemporaryDirectory("mathfirst-tester-overwrite");
        var workspace = new ArtifactWorkspace(repository.Path, "tester-overwrite");
        WriteValidTesterEvidence(workspace);

        var finalDir = workspace.GetFinalDirectory(ReleaseProfile.Tester, ArtifactId);
        Directory.CreateDirectory(finalDir);

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Tester, ArtifactId));
    }

    private static ArtifactProvenance CreateTesterProvenance() =>
        new(
            1,
            new($"{ArtifactId}.apk", ReleaseConstants.TesterArtifactClassification, 13, ApkHash),
            new(ReleaseConstants.TesterApplicationId, "1.0", 1),
            new(FullSha, FullSha, "feat/mf-ux-005-tester-apk-workflow", ReleaseConstants.TesterSourceClassification, true),
            new(
                ReleaseConstants.Configuration,
                ReleaseConstants.TargetFramework,
                "24",
                "36",
                new(null, null),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnetSdk"] = "10.0.401" }),
            new(ReleaseConstants.TesterSigningState, null, null),
            DateTimeOffset.Parse("2026-09-19T04:00:00Z"));

    private static ValidationReceipt CreateTesterReceipt() =>
        new(
            1,
            DateTimeOffset.Parse("2026-09-19T04:15:30Z"),
            ReleaseProfile.Tester,
            ArtifactValidationStatus.ValidatorApproved,
            false,
            new($"{ArtifactId}.apk", ApkHash),
            new($"{ArtifactId}.provenance.json", ProvenanceHash),
            new(SignerHash, "development-debug"));

    private static ValidationReceipt WriteValidTesterEvidence(ArtifactWorkspace workspace)
    {
        var artifactName = $"{ArtifactId}.apk";
        var provenanceName = $"{ArtifactId}.provenance.json";
        var receiptName = $"{ArtifactId}.validation.json";

        var artifactPath = Path.Combine(workspace.ReadyRoot, artifactName);
        File.WriteAllBytes(artifactPath, Encoding.UTF8.GetBytes("synthetic-apk-bytes"));
        var artifactHash = ComputeSha256(artifactPath);

        var provenance = CreateTesterProvenance() with
        {
            Artifact = new(artifactName, ReleaseConstants.TesterArtifactClassification, new FileInfo(artifactPath).Length, artifactHash)
        };
        var provenancePath = Path.Combine(workspace.ReadyRoot, provenanceName);
        File.WriteAllText(
            provenancePath,
            JsonSerializer.Serialize(
                provenance,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        var provenanceHash = ComputeSha256(provenancePath);

        var validation = new ValidationResult(
            true,
            ArtifactValidationStatus.ValidatorApproved,
            ReleaseProfile.Tester,
            false,
            artifactHash,
            SignerHash,
            "development-debug",
            []);

        var receipt = ReleaseEvidenceGenerator.CreateValidationReceipt(
            DateTimeOffset.Parse("2026-09-19T04:15:30Z"),
            ReleaseProfile.Tester,
            validation,
            artifactName,
            artifactHash,
            provenanceName,
            provenanceHash);

        File.WriteAllText(
            Path.Combine(workspace.ReadyRoot, receiptName),
            ReleaseEvidenceGenerator.SerializeValidationReceipt(receipt));

        File.WriteAllText(
            Path.Combine(workspace.ReadyRoot, "TESTER_README.md"),
            ReleaseEvidenceGenerator.CreateTesterReadme(ArtifactId, provenance, receipt));

        WriteChecksums(workspace);
        return receipt;
    }

    private static void RewriteReceiptAndChecksums(ArtifactWorkspace workspace, ValidationReceipt receipt)
    {
        File.WriteAllText(
            Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.validation.json"),
            ReleaseEvidenceGenerator.SerializeValidationReceipt(receipt));
        WriteChecksums(workspace);
    }

    private static void WriteChecksums(ArtifactWorkspace workspace)
    {
        var payloadNames = new[]
        {
            $"{ArtifactId}.apk",
            $"{ArtifactId}.provenance.json",
            $"{ArtifactId}.validation.json",
            "TESTER_README.md"
        };
        var hashes = payloadNames.Select(name => new KeyValuePair<string, string>(
            name,
            ComputeSha256(Path.Combine(workspace.ReadyRoot, name))));
        File.WriteAllText(
            Path.Combine(workspace.ReadyRoot, "SHA256SUMS"),
            ReleaseEvidenceGenerator.CreateSha256Sums(ArtifactId, ReleaseProfile.Tester, hashes));
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
