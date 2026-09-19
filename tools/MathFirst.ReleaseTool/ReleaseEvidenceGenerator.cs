namespace MathFirst.ReleaseTool;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public static class ReleaseEvidenceGenerator
{
    private static readonly Regex LowercaseSha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ReceiptJsonOptions = CreateReceiptJsonOptions();

    public static ValidationReceipt CreateValidationReceipt(
        DateTimeOffset generatedAtUtc,
        ReleaseProfile profile,
        ValidationResult validationResult,
        string artifactFileName,
        string artifactSha256,
        string provenanceFileName,
        string provenanceSha256)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        if (!validationResult.IsValid ||
            validationResult.Status != ArtifactValidationStatus.ValidatorApproved ||
            validationResult.Profile != profile)
        {
            throw new ReleaseToolException("Validation receipt requires matching ValidatorApproved evidence.");
        }

        ValidateSafeFileName(artifactFileName, "artifact");
        ValidateSafeFileName(provenanceFileName, "provenance");
        ValidateSha256(artifactSha256, "artifact");
        ValidateSha256(provenanceSha256, "provenance");
        ValidateSha256(validationResult.ArtifactSha256, "validator artifact");
        ValidateSha256(validationResult.SignerCertificateSha256, "signer certificate");
        if (!string.Equals(artifactSha256, validationResult.ArtifactSha256, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Validation receipt artifact SHA-256 must match validator evidence.");
        }

        if (string.IsNullOrWhiteSpace(validationResult.SignerClassification))
        {
            throw new ReleaseToolException("Validation receipt requires a signer classification.");
        }

        return new ValidationReceipt(
            1,
            generatedAtUtc.ToUniversalTime(),
            profile,
            validationResult.Status,
            validationResult.IsDistributable,
            new ValidationReceiptArtifact(artifactFileName, artifactSha256),
            new ValidationReceiptProvenance(provenanceFileName, provenanceSha256),
            new ValidationReceiptSigner(
                validationResult.SignerCertificateSha256,
                validationResult.SignerClassification));
    }

    public static string SerializeValidationReceipt(ValidationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return NormalizeLf(JsonSerializer.Serialize(receipt, ReceiptJsonOptions)) + "\n";
    }

    public static string CreateTesterReadme(
        string artifactId,
        ArtifactProvenance provenance,
        ValidationReceipt receipt)
    {
        ValidateSafeFileName(artifactId, "artifact ID");
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(receipt);

        var distributable = receipt.IsDistributable ? "yes" : "no";
        var builder = new StringBuilder();
        builder.AppendLine("# MathFirst Tester Evidence");
        builder.AppendLine();
        builder.AppendLine($"Artifact ID: {artifactId}");
        builder.AppendLine($"Application ID: {provenance.Application.Id}");
        builder.AppendLine($"Display version: {provenance.Application.DisplayVersion}");
        builder.AppendLine($"Build number: {provenance.Application.BuildNumber.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Source commit: {provenance.Source.CommitSha}");
        builder.AppendLine($"Profile: {receipt.Profile}");
        builder.AppendLine($"Artifact classification: {provenance.Artifact.Classification}");
        builder.AppendLine($"Validation status: {receipt.Status}");
        builder.AppendLine($"Signer classification: {receipt.Signer.Classification}");
        builder.AppendLine($"Distributable: {distributable}");
        builder.AppendLine();
        builder.AppendLine("## Evidence files");
        builder.AppendLine();
        builder.AppendLine($"- {receipt.Artifact.FileName}");
        builder.AppendLine($"- {receipt.Provenance.FileName}");
        builder.AppendLine($"- {artifactId}.validation.json");
        builder.AppendLine("- TESTER_README.md");
        builder.AppendLine("- SHA256SUMS");
        builder.AppendLine();
        builder.AppendLine("## Boundaries");
        builder.AppendLine();
        if (receipt.Profile == ReleaseProfile.Tester)
        {
            builder.AppendLine("This artifact is a non-production MathFirst Tester APK directly installable on compatible Android devices.");
            builder.AppendLine("The APK is not a Google Play release artifact and is not distributable.");
            builder.AppendLine("Production signing credentials are never used or distributed for tester builds.");
            builder.AppendLine($"The application package ID is '{ReleaseConstants.TesterApplicationId}', distinct from production '{ReleaseConstants.ProductionApplicationId}'.");
            builder.AppendLine($"Tester installation can coexist with production '{ReleaseConstants.ProductionApplicationId}' because Android maintains a separate application sandbox and app data.");
            builder.AppendLine("Installation is a separate lifecycle activity.");
            builder.AppendLine("Physical-device verification is a separate developer activity.");
            builder.AppendLine("This evidence does not prove installation or device verification.");
            builder.AppendLine("In-place updates on Android require subsequent APKs to be signed with the same signing identity as the installed Tester APK.");
        }
        else
        {
            builder.AppendLine("The AAB is not directly installable as an APK.");
            builder.AppendLine("APK conversion, installation, and manual-device verification are separate developer activities.");
            builder.AppendLine("Production signing credentials are never distributed to testers.");
            builder.AppendLine("Google Play testing or upload requires separate authorization.");
            builder.AppendLine("This evidence bundle does not prove installation or real-device verification.");
        }

        return NormalizeLf(builder.ToString());
    }

    public static string CreateSha256Sums(
        string artifactId,
        ReleaseProfile profile,
        IEnumerable<KeyValuePair<string, string>> payloadHashes)
    {
        ValidateSafeFileName(artifactId, "artifact ID");
        ArgumentNullException.ThrowIfNull(payloadHashes);

        var artifactExtension = profile == ReleaseProfile.Tester ? ".apk" : ".aab";
        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{artifactId}{artifactExtension}",
            $"{artifactId}.provenance.json",
            $"{artifactId}.validation.json",
            "TESTER_README.md"
        };
        var entries = payloadHashes.ToList();
        if (entries.Count != expectedNames.Count)
        {
            throw new ReleaseToolException("SHA256SUMS requires exactly four payload entries.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ValidateSafeFileName(entry.Key, "checksum payload");
            if (!expectedNames.Contains(entry.Key))
            {
                throw new ReleaseToolException($"SHA256SUMS contains unknown payload '{entry.Key}'.");
            }

            if (!names.Add(entry.Key))
            {
                throw new ReleaseToolException($"SHA256SUMS contains duplicate payload '{entry.Key}'.");
            }

            ValidateSha256(entry.Value, $"checksum payload '{entry.Key}'");
        }

        if (!names.SetEquals(expectedNames))
        {
            throw new ReleaseToolException("SHA256SUMS payload set is incomplete.");
        }

        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(entry.Value).Append("  ").Append(entry.Key).Append('\n');
        }

        return builder.ToString();
    }

    public static string CreateSha256Sums(
        string artifactId,
        IEnumerable<KeyValuePair<string, string>> payloadHashes) =>
        CreateSha256Sums(artifactId, ReleaseProfile.SourceCandidate, payloadHashes);

    private static JsonSerializerOptions CreateReceiptJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void ValidateSafeFileName(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ReleaseToolException($"The {description} must be a safe single file name.");
        }
    }

    private static void ValidateSha256(string value, string description)
    {
        if (value is null || !LowercaseSha256Pattern.IsMatch(value))
        {
            throw new ReleaseToolException($"The {description} SHA-256 must contain 64 lowercase hexadecimal digits.");
        }
    }

    private static string NormalizeLf(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
