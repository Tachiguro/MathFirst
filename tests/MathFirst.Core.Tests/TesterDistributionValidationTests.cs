namespace MathFirst.Core.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MathFirst.ReleaseTool;

public sealed class TesterDistributionValidationTests
{
    private const string ArtifactId = "MathFirst-v1.0-b1-199dbd7cd38f-source-candidate-debug-signed";
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";
    private const string AabHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ProvenanceHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ReceiptHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string ReadmeHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private const string SignerHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    [Fact]
    public void ReleaseEvidenceGenerator_ExposesApprovedEvidenceContracts()
    {
        var assembly = typeof(ReleaseProfile).Assembly;

        Assert.NotNull(assembly.GetType("MathFirst.ReleaseTool.ValidationReceipt"));
        Assert.NotNull(assembly.GetType("MathFirst.ReleaseTool.ValidationReceiptArtifact"));
        Assert.NotNull(assembly.GetType("MathFirst.ReleaseTool.ValidationReceiptProvenance"));
        Assert.NotNull(assembly.GetType("MathFirst.ReleaseTool.ValidationReceiptSigner"));

        var generator = assembly.GetType("MathFirst.ReleaseTool.ReleaseEvidenceGenerator");
        Assert.NotNull(generator);
        Assert.NotNull(generator.GetMethod("CreateValidationReceipt"));
        Assert.NotNull(generator.GetMethod("SerializeValidationReceipt"));
        Assert.NotNull(generator.GetMethod("CreateTesterReadme"));
        Assert.NotNull(generator.GetMethod("CreateSha256Sums"));
    }

    [Fact]
    public void ArtifactWorkspace_ExposesEvidenceOnlyPromotionBoundary()
    {
        Assert.NotNull(typeof(ArtifactWorkspace).GetMethod(
            "Promote",
            [typeof(ReleaseProfile), typeof(string)]));
    }

    [Fact]
    public void ValidationReceipt_BindsSuppliedValidatorEvidenceAndUtcTimestamp()
    {
        var generatedAt = DateTimeOffset.Parse("2026-09-12T10:15:30+02:00");

        var receipt = ReleaseEvidenceGenerator.CreateValidationReceipt(
            generatedAt,
            ReleaseProfile.SourceCandidate,
            CreateValidationResult(),
            $"{ArtifactId}.aab",
            AabHash,
            $"{ArtifactId}.provenance.json",
            ProvenanceHash);

        Assert.Equal(1, receipt.SchemaVersion);
        Assert.Equal(DateTimeOffset.Parse("2026-09-12T08:15:30Z"), receipt.GeneratedAtUtc);
        Assert.Equal(ReleaseProfile.SourceCandidate, receipt.Profile);
        Assert.Equal(ArtifactValidationStatus.ValidatorApproved, receipt.Status);
        Assert.False(receipt.IsDistributable);
        Assert.Equal($"{ArtifactId}.aab", receipt.Artifact.FileName);
        Assert.Equal(AabHash, receipt.Artifact.Sha256);
        Assert.Equal($"{ArtifactId}.provenance.json", receipt.Provenance.FileName);
        Assert.Equal(ProvenanceHash, receipt.Provenance.Sha256);
        Assert.Equal(SignerHash, receipt.Signer.CertificateSha256);
        Assert.Equal("development-debug", receipt.Signer.Classification);
    }

    [Fact]
    public void ValidationReceipt_SerializationIsStableAndUsesCanonicalEnumStrings()
    {
        var receipt = ReleaseEvidenceGenerator.CreateValidationReceipt(
            DateTimeOffset.Parse("2026-09-12T08:15:30Z"),
            ReleaseProfile.SourceCandidate,
            CreateValidationResult(),
            $"{ArtifactId}.aab",
            AabHash,
            $"{ArtifactId}.provenance.json",
            ProvenanceHash);

        var first = ReleaseEvidenceGenerator.SerializeValidationReceipt(receipt);
        var second = ReleaseEvidenceGenerator.SerializeValidationReceipt(receipt);

        Assert.Equal(first, second);
        Assert.DoesNotContain('\r', first);
        Assert.EndsWith("\n", first, StringComparison.Ordinal);
        Assert.False(first.EndsWith("\n\n", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        Assert.Equal("SourceCandidate", root.GetProperty("profile").GetString());
        Assert.Equal("ValidatorApproved", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("checks", out _));
        Assert.Equal(
            "{\n" +
            "  \"schemaVersion\": 1,\n" +
            "  \"generatedAtUtc\": \"2026-09-12T08:15:30+00:00\",\n" +
            "  \"profile\": \"SourceCandidate\",\n" +
            "  \"status\": \"ValidatorApproved\",\n" +
            "  \"isDistributable\": false,\n" +
            "  \"artifact\": {\n" +
            $"    \"fileName\": \"{ArtifactId}.aab\",\n" +
            $"    \"sha256\": \"{AabHash}\"\n" +
            "  },\n" +
            "  \"provenance\": {\n" +
            $"    \"fileName\": \"{ArtifactId}.provenance.json\",\n" +
            $"    \"sha256\": \"{ProvenanceHash}\"\n" +
            "  },\n" +
            "  \"signer\": {\n" +
            $"    \"certificateSha256\": \"{SignerHash}\",\n" +
            "    \"classification\": \"development-debug\"\n" +
            "  }\n" +
            "}\n",
            first);
    }

    [Fact]
    public void TesterReadme_CommunicatesAuthoritativeEvidenceAndSecurityBoundaries()
    {
        var readme = ReleaseEvidenceGenerator.CreateTesterReadme(
            ArtifactId,
            CreateProvenance(),
            CreateReceipt());

        Assert.Contains(ArtifactId, readme, StringComparison.Ordinal);
        Assert.Contains("com.tachiguro.mathfirst", readme, StringComparison.Ordinal);
        Assert.Contains("1.0", readme, StringComparison.Ordinal);
        Assert.Contains("1", readme, StringComparison.Ordinal);
        Assert.Contains(FullSha, readme, StringComparison.Ordinal);
        Assert.Contains("SourceCandidate", readme, StringComparison.Ordinal);
        Assert.Contains("source-candidate-debug-signed", readme, StringComparison.Ordinal);
        Assert.Contains($"{ArtifactId}.aab", readme, StringComparison.Ordinal);
        Assert.Contains($"{ArtifactId}.provenance.json", readme, StringComparison.Ordinal);
        Assert.Contains($"{ArtifactId}.validation.json", readme, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", readme, StringComparison.Ordinal);
        Assert.Contains("ValidatorApproved", readme, StringComparison.Ordinal);
        Assert.Contains("development-debug", readme, StringComparison.Ordinal);
        Assert.Contains("Distributable: no", readme, StringComparison.Ordinal);
        Assert.Contains("AAB is not directly installable as an APK", readme, StringComparison.Ordinal);
        Assert.Contains("conversion, installation, and manual-device verification are separate", readme, StringComparison.Ordinal);
        Assert.Contains("Production signing credentials are never distributed to testers", readme, StringComparison.Ordinal);
        Assert.Contains("Google Play testing or upload requires separate authorization", readme, StringComparison.Ordinal);
        Assert.Contains("does not prove installation or real-device verification", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("keystore", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb ", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bundletool", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upload --", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', readme);
        Assert.EndsWith("\n", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Sha256Sums_ContainsExactlyFourOrdinallySortedPayloadEntries()
    {
        var sums = ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            [
                new("TESTER_README.md", ReadmeHash),
                new($"{ArtifactId}.validation.json", ReceiptHash),
                new($"{ArtifactId}.aab", AabHash),
                new($"{ArtifactId}.provenance.json", ProvenanceHash)
            ]);

        Assert.Equal(
            $"{AabHash}  {ArtifactId}.aab\n" +
            $"{ProvenanceHash}  {ArtifactId}.provenance.json\n" +
            $"{ReceiptHash}  {ArtifactId}.validation.json\n" +
            $"{ReadmeHash}  TESTER_README.md\n",
            sums);
        Assert.DoesNotContain("SHA256SUMS", sums, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', sums);
        Assert.Equal(4, sums.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.EndsWith("\n", sums, StringComparison.Ordinal);
        Assert.False(sums.EndsWith("\n\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Sha256Sums_RejectsUnsafeDuplicateUnknownOrNonCanonicalEntries()
    {
        Assert.Throws<ReleaseToolException>(() => ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            CreatePayloadHashes(("../payload.aab", AabHash))));
        Assert.Throws<ReleaseToolException>(() => ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            [
                new($"{ArtifactId}.aab", AabHash),
                new($"{ArtifactId}.aab", AabHash),
                new($"{ArtifactId}.provenance.json", ProvenanceHash),
                new($"{ArtifactId}.validation.json", ReceiptHash),
                new("TESTER_README.md", ReadmeHash)
            ]));
        Assert.Throws<ReleaseToolException>(() => ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            CreatePayloadHashes(("NOTES.md", ReadmeHash))));
        Assert.Throws<ReleaseToolException>(() => ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            CreatePayloadHashes(("SHA256SUMS", ReadmeHash))));
        Assert.Throws<ReleaseToolException>(() => ReleaseEvidenceGenerator.CreateSha256Sums(
            ArtifactId,
            CreatePayloadHashes(($"{ArtifactId}.aab", AabHash.ToUpperInvariant()))));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsMissingEvidenceFile()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-missing");
        var workspace = new ArtifactWorkspace(repository.Path, "missing");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        File.Delete(Path.Combine(workspace.ReadyRoot, "TESTER_README.md"));

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsExtraEvidenceFile()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-extra");
        var workspace = new ArtifactWorkspace(repository.Path, "extra");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        File.WriteAllText(Path.Combine(workspace.ReadyRoot, "NOTES.md"), "unexpected");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsAabBytesThatNoLongerMatchEvidence()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-aab-hash");
        var workspace = new ArtifactWorkspace(repository.Path, "aab-hash");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        File.AppendAllText(Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.aab"), "corrupt");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsProvenanceBytesThatNoLongerMatchReceipt()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-provenance-hash");
        var workspace = new ArtifactWorkspace(repository.Path, "provenance-hash");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        File.AppendAllText(Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.provenance.json"), " ");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsReceiptProfileMismatch()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-profile");
        var workspace = new ArtifactWorkspace(repository.Path, "profile");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.Distributable, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsSourceCandidateClaimingDistributable()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-distributable");
        var workspace = new ArtifactWorkspace(repository.Path, "distributable");
        var receipt = WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        RewriteReceiptAndChecksums(workspace, receipt with { IsDistributable = true });

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsIncorrectSignerClassification()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-signer");
        var workspace = new ArtifactWorkspace(repository.Path, "signer");
        var receipt = WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        RewriteReceiptAndChecksums(
            workspace,
            receipt with { Signer = receipt.Signer with { Classification = "release-distributable" } });

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsMalformedSha256Sums()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-sums");
        var workspace = new ArtifactWorkspace(repository.Path, "sums");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        var sumsPath = Path.Combine(workspace.ReadyRoot, "SHA256SUMS");
        var sums = File.ReadAllText(sumsPath);
        File.WriteAllText(sumsPath, $"g{sums[1..]}");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));
    }

    [Theory]
    [InlineData("missing-generatedAtUtc")]
    [InlineData("missing-profile")]
    [InlineData("missing-status")]
    [InlineData("missing-isDistributable")]
    [InlineData("numeric-profile")]
    [InlineData("numeric-status")]
    [InlineData("default-generatedAtUtc")]
    [InlineData("missing-artifact")]
    [InlineData("missing-provenance")]
    [InlineData("missing-signer")]
    [InlineData("missing-artifact-fileName")]
    [InlineData("empty-artifact-fileName")]
    [InlineData("missing-artifact-sha256")]
    [InlineData("empty-artifact-sha256")]
    [InlineData("missing-provenance-fileName")]
    [InlineData("empty-provenance-fileName")]
    [InlineData("missing-provenance-sha256")]
    [InlineData("empty-provenance-sha256")]
    [InlineData("missing-signer-certificateSha256")]
    [InlineData("empty-signer-certificateSha256")]
    [InlineData("missing-signer-classification")]
    [InlineData("empty-signer-classification")]
    public void ArtifactWorkspace_RejectsNonCanonicalSchemaV1Receipt(string mutation)
    {
        using var repository = new TemporaryDirectory($"mathfirst-receipt-{mutation}");
        var workspace = new ArtifactWorkspace(repository.Path, mutation);
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        MutateReceipt(workspace, mutation);

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId));

        Assert.True(Directory.Exists(workspace.ReadyRoot));
        Assert.False(Directory.Exists(workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, ArtifactId)));
    }

    [Fact]
    public void ArtifactWorkspace_PreservesExactProvenanceBytesThroughPromotion()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-immutable");
        var workspace = new ArtifactWorkspace(repository.Path, "immutable");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);
        var provenanceName = $"{ArtifactId}.provenance.json";
        var before = File.ReadAllBytes(Path.Combine(workspace.ReadyRoot, provenanceName));

        workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId);

        var finalDirectory = workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, ArtifactId);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(finalDirectory, provenanceName)));
    }

    [Fact]
    public void ArtifactWorkspace_AtomicallyPromotesValidFiveFileEvidenceDirectory()
    {
        using var repository = new TemporaryDirectory("mathfirst-evidence-promote");
        var workspace = new ArtifactWorkspace(repository.Path, "promote");
        WriteValidEvidence(workspace, ReleaseProfile.SourceCandidate);

        workspace.Promote(ReleaseProfile.SourceCandidate, ArtifactId);

        var finalDirectory = workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, ArtifactId);
        Assert.False(Directory.Exists(workspace.ReadyRoot));
        Assert.Equal(
            [
                $"{ArtifactId}.aab",
                $"{ArtifactId}.provenance.json",
                $"{ArtifactId}.validation.json",
                "SHA256SUMS",
                "TESTER_README.md"
            ],
            Directory.GetFiles(finalDirectory).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<KeyValuePair<string, string>> CreatePayloadHashes(
        params (string FileName, string Sha256)[] replacements)
    {
        var entries = new List<KeyValuePair<string, string>>
        {
            new($"{ArtifactId}.aab", AabHash),
            new($"{ArtifactId}.provenance.json", ProvenanceHash),
            new($"{ArtifactId}.validation.json", ReceiptHash),
            new("TESTER_README.md", ReadmeHash)
        };
        foreach (var replacement in replacements)
        {
            var index = entries.FindIndex(entry => string.Equals(entry.Key, replacement.FileName, StringComparison.Ordinal));
            if (index >= 0)
            {
                entries[index] = new(replacement.FileName, replacement.Sha256);
            }
            else
            {
                entries.Add(new(replacement.FileName, replacement.Sha256));
            }
        }

        return entries;
    }

    private static ValidationResult CreateValidationResult() =>
        new(
            true,
            ArtifactValidationStatus.ValidatorApproved,
            ReleaseProfile.SourceCandidate,
            false,
            AabHash,
            SignerHash,
            "development-debug",
            []);

    private static ValidationReceipt CreateReceipt() =>
        new(
            1,
            DateTimeOffset.Parse("2026-09-12T08:15:30Z"),
            ReleaseProfile.SourceCandidate,
            ArtifactValidationStatus.ValidatorApproved,
            false,
            new($"{ArtifactId}.aab", AabHash),
            new($"{ArtifactId}.provenance.json", ProvenanceHash),
            new(SignerHash, "development-debug"));

    private static ArtifactProvenance CreateProvenance() =>
        new(
            1,
            new($"{ArtifactId}.aab", "source-candidate-debug-signed", 13, AabHash),
            new("com.tachiguro.mathfirst", "1.0", 1),
            new(FullSha, FullSha, "feat/mf-rel-002-tester-distribution-release-hardening", "source-candidate", true),
            new(
                "Release",
                "net10.0-android36.0",
                "24.0",
                "36.0",
                new(null, null),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnetSdk"] = "10.0.401" }),
            new("development-debug", null, null),
            DateTimeOffset.Parse("2026-09-12T08:00:00Z"));

    private static ValidationReceipt WriteValidEvidence(
        ArtifactWorkspace workspace,
        ReleaseProfile profile)
    {
        var artifactName = $"{ArtifactId}.aab";
        var provenanceName = $"{ArtifactId}.provenance.json";
        var receiptName = $"{ArtifactId}.validation.json";
        var artifactPath = Path.Combine(workspace.ReadyRoot, artifactName);
        File.WriteAllBytes(artifactPath, Encoding.UTF8.GetBytes("synthetic-aab"));
        var artifactHash = ComputeSha256(artifactPath);

        var isDistributable = profile == ReleaseProfile.Distributable;
        var artifactClassification = isDistributable
            ? "distributable-release-signed-pending-validation"
            : "source-candidate-debug-signed";
        var signerClassification = isDistributable ? "release-distributable" : "development-debug";
        var provenance = CreateProvenance() with
        {
            Artifact = new(artifactName, artifactClassification, new FileInfo(artifactPath).Length, artifactHash),
            Source = CreateProvenance().Source with
            {
                Ref = isDistributable ? "main" : "feat/mf-rel-002-tester-distribution-release-hardening",
                Classification = isDistributable ? "distributable" : "source-candidate"
            },
            Signing = new(
                isDistributable ? "release-expected-pending-validation" : "development-debug",
                isDistributable ? SignerHash : null,
                null)
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
            profile,
            isDistributable,
            artifactHash,
            SignerHash,
            signerClassification,
            []);
        var receipt = ReleaseEvidenceGenerator.CreateValidationReceipt(
            DateTimeOffset.Parse("2026-09-12T08:15:30Z"),
            profile,
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

    private static void MutateReceipt(ArtifactWorkspace workspace, string mutation)
    {
        var receiptPath = Path.Combine(workspace.ReadyRoot, $"{ArtifactId}.validation.json");
        var root = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        switch (mutation)
        {
            case "missing-generatedAtUtc": root.Remove("generatedAtUtc"); break;
            case "missing-profile": root.Remove("profile"); break;
            case "missing-status": root.Remove("status"); break;
            case "missing-isDistributable": root.Remove("isDistributable"); break;
            case "numeric-profile": root["profile"] = (int)ReleaseProfile.SourceCandidate; break;
            case "numeric-status": root["status"] = (int)ArtifactValidationStatus.ValidatorApproved; break;
            case "default-generatedAtUtc": root["generatedAtUtc"] = "0001-01-01T00:00:00+00:00"; break;
            case "missing-artifact": root.Remove("artifact"); break;
            case "missing-provenance": root.Remove("provenance"); break;
            case "missing-signer": root.Remove("signer"); break;
            case "missing-artifact-fileName": root["artifact"]!.AsObject().Remove("fileName"); break;
            case "empty-artifact-fileName": root["artifact"]!["fileName"] = ""; break;
            case "missing-artifact-sha256": root["artifact"]!.AsObject().Remove("sha256"); break;
            case "empty-artifact-sha256": root["artifact"]!["sha256"] = ""; break;
            case "missing-provenance-fileName": root["provenance"]!.AsObject().Remove("fileName"); break;
            case "empty-provenance-fileName": root["provenance"]!["fileName"] = ""; break;
            case "missing-provenance-sha256": root["provenance"]!.AsObject().Remove("sha256"); break;
            case "empty-provenance-sha256": root["provenance"]!["sha256"] = ""; break;
            case "missing-signer-certificateSha256": root["signer"]!.AsObject().Remove("certificateSha256"); break;
            case "empty-signer-certificateSha256": root["signer"]!["certificateSha256"] = ""; break;
            case "missing-signer-classification": root["signer"]!.AsObject().Remove("classification"); break;
            case "empty-signer-classification": root["signer"]!["classification"] = ""; break;
            default: throw new InvalidOperationException($"Unknown receipt mutation '{mutation}'.");
        }

        File.WriteAllText(
            receiptPath,
            root.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) +
            Environment.NewLine);
        WriteChecksums(workspace);
    }

    private static void WriteChecksums(ArtifactWorkspace workspace)
    {
        var payloadNames = new[]
        {
            $"{ArtifactId}.aab",
            $"{ArtifactId}.provenance.json",
            $"{ArtifactId}.validation.json",
            "TESTER_README.md"
        };
        var hashes = payloadNames.Select(name => new KeyValuePair<string, string>(
            name,
            ComputeSha256(Path.Combine(workspace.ReadyRoot, name))));
        File.WriteAllText(
            Path.Combine(workspace.ReadyRoot, "SHA256SUMS"),
            ReleaseEvidenceGenerator.CreateSha256Sums(ArtifactId, hashes));
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
