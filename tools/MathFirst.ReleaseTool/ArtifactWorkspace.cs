namespace MathFirst.ReleaseTool;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class ArtifactWorkspace
{
    internal static readonly JsonSerializerOptions ProvenanceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ValidationReceiptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public ArtifactWorkspace(string repositoryRoot, string invocationId)
    {
        if (!Path.IsPathFullyQualified(repositoryRoot))
        {
            throw new ReleaseToolException("Repository root must be an absolute path.");
        }

        if (string.IsNullOrWhiteSpace(invocationId) ||
            invocationId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            invocationId.Contains(Path.DirectorySeparatorChar) ||
            invocationId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ReleaseToolException("Invocation ID must be a safe single path segment.");
        }

        var normalizedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        ArtifactsRoot = Path.Combine(normalizedRepositoryRoot, "artifacts", "android");
        InvocationRoot = EnsureContained(
            Path.Combine(ArtifactsRoot, ".staging"),
            Path.Combine(ArtifactsRoot, ".staging", invocationId));
        PublishRoot = EnsureContained(InvocationRoot, Path.Combine(InvocationRoot, "publish"));
        ReadyRoot = EnsureContained(InvocationRoot, Path.Combine(InvocationRoot, "ready"));

        RejectExistingReparsePoints(normalizedRepositoryRoot, ArtifactsRoot);
        if (Directory.Exists(InvocationRoot) || File.Exists(InvocationRoot))
        {
            throw new ReleaseToolException("The invocation-owned staging directory already exists.");
        }

        Directory.CreateDirectory(PublishRoot);
        Directory.CreateDirectory(ReadyRoot);
        RejectExistingReparsePoints(ArtifactsRoot, ReadyRoot);
    }

    public string ArtifactsRoot { get; }

    public string InvocationRoot { get; }

    public string PublishRoot { get; }

    public string ReadyRoot { get; }

    public static string EnsureContained(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
        {
            throw new ReleaseToolException("Normalized artifact path escapes its required root.");
        }

        return normalizedCandidate;
    }

    private const string SignedAabSuffix = "-Signed.aab";

    public string FindSingleAab()
    {
        RejectExistingReparsePoints(ArtifactsRoot, PublishRoot);
        var aabs = Directory.GetFiles(PublishRoot, "*.aab", SearchOption.TopDirectoryOnly);
        if (aabs.Length == 0)
        {
            throw new ReleaseToolException("No AAB files found in the invocation publish output.");
        }

        var signedCandidates = new List<string>();
        var unsignedCandidates = new List<string>();

        foreach (var aab in aabs)
        {
            var normalized = EnsureContained(PublishRoot, aab);
            var fileName = Path.GetFileName(normalized);
            if (fileName.EndsWith(SignedAabSuffix, StringComparison.OrdinalIgnoreCase) &&
                fileName.Length > SignedAabSuffix.Length)
            {
                signedCandidates.Add(normalized);
            }
            else
            {
                unsignedCandidates.Add(normalized);
            }
        }

        if (signedCandidates.Count == 0)
        {
            throw new ReleaseToolException(
                $"Expected exactly one signed AAB candidate in the invocation publish output, but found 0 (found {aabs.Length} total AAB files).");
        }

        if (signedCandidates.Count > 1)
        {
            throw new ReleaseToolException(
                $"Expected exactly one signed AAB candidate in the invocation publish output, but found {signedCandidates.Count}.");
        }

        var selectedSignedAab = signedCandidates[0];
        var selectedFileName = Path.GetFileName(selectedSignedAab);
        var expectedIntermediateFileName = selectedFileName[..^SignedAabSuffix.Length] + ".aab";

        if (unsignedCandidates.Count > 1)
        {
            throw new ReleaseToolException(
                $"Found multiple unsigned AAB files in the invocation publish output ({unsignedCandidates.Count}). Expected at most one matching intermediate.");
        }

        if (unsignedCandidates.Count == 1)
        {
            var intermediateFileName = Path.GetFileName(unsignedCandidates[0]);
            if (!string.Equals(intermediateFileName, expectedIntermediateFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReleaseToolException(
                    $"Found unexpected or ambiguous AAB file '{intermediateFileName}' alongside signed candidate '{selectedFileName}'. Expected '{expectedIntermediateFileName}'.");
            }
        }

        return EnsureContained(PublishRoot, selectedSignedAab);
    }

    public string GetFinalDirectory(ReleaseProfile profile, string artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId) ||
            artifactId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            artifactId.Contains(Path.DirectorySeparatorChar) ||
            artifactId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ReleaseToolException("Artifact ID must be a safe single path segment.");
        }

        var category = profile switch
        {
            ReleaseProfile.SourceCandidate => "source-candidate",
            ReleaseProfile.Distributable => "distributable",
            _ => throw new ReleaseToolException($"Unsupported release profile '{profile}'.")
        };

        return EnsureContained(ArtifactsRoot, Path.Combine(ArtifactsRoot, category, artifactId));
    }

    public void Promote(
        ReleaseProfile profile,
        string artifactId)
    {
        var finalDirectory = GetFinalDirectory(profile, artifactId);
        if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
        {
            throw new ReleaseToolException("Final artifact destination already exists; overwrite is forbidden.");
        }

        RejectExistingReparsePoints(ArtifactsRoot, ReadyRoot);
        var artifactFileName = $"{artifactId}.aab";
        var provenanceFileName = $"{artifactId}.provenance.json";
        var receiptFileName = $"{artifactId}.validation.json";
        var expectedFileNames = new HashSet<string>(StringComparer.Ordinal)
        {
            artifactFileName,
            provenanceFileName,
            receiptFileName,
            "TESTER_README.md",
            "SHA256SUMS"
        };
        var entries = Directory.GetFileSystemEntries(ReadyRoot, "*", SearchOption.TopDirectoryOnly);
        var actualFileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var fileName = Path.GetFileName(entry);
            if (!File.Exists(entry) ||
                (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0 ||
                !actualFileNames.Add(fileName))
            {
                throw new ReleaseToolException("Ready evidence must contain only distinct regular files.");
            }
        }

        if (entries.Length != expectedFileNames.Count || !actualFileNames.SetEquals(expectedFileNames))
        {
            throw new ReleaseToolException("Ready evidence must contain exactly the five expected files.");
        }

        var provenancePath = EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, provenanceFileName));
        var provenance = ReadJson<ArtifactProvenance>(provenancePath, ProvenanceJsonOptions, "provenance");
        ValidateCompleteProvenance(provenance);
        if (!string.Equals(provenance.Artifact.FileName, artifactFileName, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Provenance artifact file name does not match the expected AAB.");
        }

        ValidateStagedArtifact(provenance);
        var artifactPath = EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, artifactFileName));
        var actualArtifactSha256 = ComputeSha256(artifactPath);
        var actualProvenanceSha256 = ComputeSha256(provenancePath);

        var receiptPath = EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, receiptFileName));
        var receipt = ReadValidationReceipt(receiptPath);
        ValidateReceipt(
            receipt,
            profile,
            artifactFileName,
            actualArtifactSha256,
            provenanceFileName,
            actualProvenanceSha256);

        ValidateSha256Sums(
            EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, "SHA256SUMS")),
            [artifactFileName, provenanceFileName, receiptFileName, "TESTER_README.md"]);

        var categoryDirectory = Path.GetDirectoryName(finalDirectory)!;
        Directory.CreateDirectory(categoryDirectory);
        RejectExistingReparsePoints(ArtifactsRoot, categoryDirectory);
        Directory.Move(ReadyRoot, finalDirectory);
    }

    public void Promote(
        ReleaseProfile profile,
        string artifactId,
        ArtifactProvenance? provenance,
        ArtifactValidationStatus validationStatus)
    {
        ValidateCompleteProvenance(provenance);
        if (validationStatus != ArtifactValidationStatus.ValidatorApproved)
        {
            throw new ReleaseToolException(
                "Artifact promotion requires independent validator approval.");
        }

        Promote(profile, artifactId);
    }

    public void Cleanup()
    {
        var stagingRoot = Path.Combine(ArtifactsRoot, ".staging");
        _ = EnsureContained(stagingRoot, InvocationRoot);
        if (Directory.Exists(InvocationRoot))
        {
            Directory.Delete(InvocationRoot, recursive: true);
        }
    }

    private static void ValidateCompleteProvenance(ArtifactProvenance? provenance)
    {
        if (provenance is null ||
            provenance.Artifact is null ||
            provenance.Application is null ||
            provenance.Source is null ||
            provenance.Build is null ||
            provenance.Signing is null ||
            provenance.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(provenance.Artifact.FileName) ||
            string.IsNullOrWhiteSpace(provenance.Artifact.Classification) ||
            provenance.Artifact.SizeBytes <= 0 ||
            provenance.Artifact.Sha256.Length != 64 ||
            provenance.Artifact.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
            string.IsNullOrWhiteSpace(provenance.Application.Id) ||
            string.IsNullOrWhiteSpace(provenance.Source.CommitSha) ||
            !provenance.Source.WorkingTreeClean ||
            string.IsNullOrWhiteSpace(provenance.Build.Configuration) ||
            provenance.Build.ToolVersions is null ||
            provenance.Build.ToolVersions.Count == 0 ||
            string.IsNullOrWhiteSpace(provenance.Signing.State))
        {
            throw new ReleaseToolException("Artifact promotion requires complete schema-v1 provenance.");
        }
    }

    private static T ReadJson<T>(string path, JsonSerializerOptions options, string description)
        where T : class
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), options);
            return value ?? throw new ReleaseToolException($"The staged {description} is empty.");
        }
        catch (ReleaseToolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            throw new ReleaseToolException($"The staged {description} is invalid: {exception.Message}");
        }
    }

    private static ValidationReceipt ReadValidationReceipt(string path)
    {
        try
        {
            var contents = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(contents);
            ValidateCanonicalValidationReceiptJson(document.RootElement);
            return JsonSerializer.Deserialize<ValidationReceipt>(contents, ValidationReceiptJsonOptions) ??
                throw new ReleaseToolException("The staged validation receipt is empty.");
        }
        catch (ReleaseToolException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            throw new ReleaseToolException($"The staged validation receipt is invalid: {exception.Message}");
        }
    }

    private static void ValidateCanonicalValidationReceiptJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ReleaseToolException("Validation receipt must be a JSON object.");
        }

        var schemaVersion = RequireProperty(root, "schemaVersion", JsonValueKind.Number);
        if (!schemaVersion.TryGetInt32(out var schemaVersionValue) || schemaVersionValue != 1)
        {
            throw new ReleaseToolException("Validation receipt schemaVersion must be the integer 1.");
        }

        _ = RequireProperty(root, "generatedAtUtc", JsonValueKind.String);
        var profile = RequireProperty(root, "profile", JsonValueKind.String).GetString();
        if (profile is not (nameof(ReleaseProfile.SourceCandidate) or nameof(ReleaseProfile.Distributable)))
        {
            throw new ReleaseToolException("Validation receipt profile must be a canonical supported enum name.");
        }

        var status = RequireProperty(root, "status", JsonValueKind.String).GetString();
        if (status is not (nameof(ArtifactValidationStatus.NotValidated) or nameof(ArtifactValidationStatus.ValidatorApproved)))
        {
            throw new ReleaseToolException("Validation receipt status must be a canonical enum name.");
        }

        _ = RequireProperty(root, "isDistributable", JsonValueKind.True, JsonValueKind.False);
        var artifact = RequireProperty(root, "artifact", JsonValueKind.Object);
        var provenance = RequireProperty(root, "provenance", JsonValueKind.Object);
        var signer = RequireProperty(root, "signer", JsonValueKind.Object);
        _ = RequireProperty(artifact, "fileName", JsonValueKind.String);
        _ = RequireProperty(artifact, "sha256", JsonValueKind.String);
        _ = RequireProperty(provenance, "fileName", JsonValueKind.String);
        _ = RequireProperty(provenance, "sha256", JsonValueKind.String);
        _ = RequireProperty(signer, "certificateSha256", JsonValueKind.String);
        _ = RequireProperty(signer, "classification", JsonValueKind.String);
    }

    private static JsonElement RequireProperty(
        JsonElement owner,
        string propertyName,
        params JsonValueKind[] allowedKinds)
    {
        if (!owner.TryGetProperty(propertyName, out var value) || !allowedKinds.Contains(value.ValueKind))
        {
            throw new ReleaseToolException(
                $"Validation receipt property '{propertyName}' is missing or has an invalid JSON type.");
        }

        return value;
    }

    private static void ValidateReceipt(
        ValidationReceipt receipt,
        ReleaseProfile profile,
        string artifactFileName,
        string artifactSha256,
        string provenanceFileName,
        string provenanceSha256)
    {
        if (receipt.Artifact is null || receipt.Provenance is null || receipt.Signer is null)
        {
            throw new ReleaseToolException("Validation receipt is incomplete.");
        }

        if (receipt.SchemaVersion != 1 ||
            receipt.GeneratedAtUtc == default ||
            receipt.Status != ArtifactValidationStatus.ValidatorApproved ||
            receipt.Profile != profile)
        {
            throw new ReleaseToolException("Validation receipt schema, status, or profile is invalid.");
        }

        var expectedDistributable = profile == ReleaseProfile.Distributable;
        var expectedSignerClassification = profile switch
        {
            ReleaseProfile.SourceCandidate => "development-debug",
            ReleaseProfile.Distributable => "release-distributable",
            _ => throw new ReleaseToolException($"Unsupported release profile '{profile}'.")
        };
        if (receipt.IsDistributable != expectedDistributable ||
            !string.Equals(receipt.Signer.Classification, expectedSignerClassification, StringComparison.Ordinal) ||
            !IsLowercaseSha256(receipt.Signer.CertificateSha256))
        {
            throw new ReleaseToolException("Validation receipt distributable or signer classification is invalid.");
        }

        if (!string.Equals(receipt.Artifact.FileName, artifactFileName, StringComparison.Ordinal) ||
            !string.Equals(receipt.Artifact.Sha256, artifactSha256, StringComparison.Ordinal) ||
            !string.Equals(receipt.Provenance.FileName, provenanceFileName, StringComparison.Ordinal) ||
            !string.Equals(receipt.Provenance.Sha256, provenanceSha256, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Validation receipt does not bind the exact staged AAB and provenance bytes.");
        }
    }

    private void ValidateSha256Sums(string sumsPath, IReadOnlyCollection<string> payloadFileNames)
    {
        string contents;
        try
        {
            contents = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(File.ReadAllBytes(sumsPath));
        }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException)
        {
            throw new ReleaseToolException($"SHA256SUMS is not valid UTF-8: {exception.Message}");
        }

        if (contents.Contains('\r') ||
            !contents.EndsWith('\n') ||
            contents.EndsWith("\n\n", StringComparison.Ordinal))
        {
            throw new ReleaseToolException("SHA256SUMS must use LF only and exactly one final LF.");
        }

        var lines = contents[..^1].Split('\n');
        var expectedNames = payloadFileNames.Order(StringComparer.Ordinal).ToArray();
        if (lines.Length != expectedNames.Length)
        {
            throw new ReleaseToolException("SHA256SUMS must contain exactly four payload entries.");
        }

        var observedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length <= 66 ||
                line[64] != ' ' ||
                line[65] != ' ' ||
                line[66] == ' ')
            {
                throw new ReleaseToolException("SHA256SUMS entries require a lowercase hash and two-space delimiter.");
            }

            var sha256 = line[..64];
            var fileName = line[66..];
            if (!IsLowercaseSha256(sha256) ||
                !observedNames.Add(fileName) ||
                !string.Equals(fileName, expectedNames[index], StringComparison.Ordinal))
            {
                throw new ReleaseToolException("SHA256SUMS contains malformed, duplicate, unknown, or unsorted entries.");
            }

            var payloadPath = EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, fileName));
            if (!string.Equals(sha256, ComputeSha256(payloadPath), StringComparison.Ordinal))
            {
                throw new ReleaseToolException($"SHA256SUMS hash does not match staged file '{fileName}'.");
            }
        }
    }

    private static bool IsLowercaseSha256(string value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private void ValidateStagedArtifact(ArtifactProvenance provenance)
    {
        if (!string.Equals(Path.GetFileName(provenance.Artifact.FileName), provenance.Artifact.FileName, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Provenance artifact file name must be a safe file name.");
        }

        var artifactPath = EnsureContained(ReadyRoot, Path.Combine(ReadyRoot, provenance.Artifact.FileName));
        if (!File.Exists(artifactPath))
        {
            throw new ReleaseToolException("The provenance artifact does not exist in the ready directory.");
        }

        var fileInfo = new FileInfo(artifactPath);
        if (fileInfo.Length != provenance.Artifact.SizeBytes)
        {
            throw new ReleaseToolException("Staged artifact size does not match provenance.");
        }

        using var stream = File.OpenRead(artifactPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(sha256, provenance.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("Staged artifact SHA-256 does not match provenance.");
        }
    }

    private static void RejectExistingReparsePoints(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedCandidate = EnsureContained(normalizedRoot, candidate);
        var current = new DirectoryInfo(normalizedCandidate);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ReleaseToolException(
                    $"Artifact workspace path crosses reparse point '{current.FullName}'.");
            }

            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(current.FullName),
                    Path.TrimEndingDirectorySeparator(normalizedRoot),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                break;
            }

            current = current.Parent;
        }
    }
}
