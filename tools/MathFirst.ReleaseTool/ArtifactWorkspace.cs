namespace MathFirst.ReleaseTool;

using System.Security.Cryptography;
using System.Text.Json;

public sealed class ArtifactWorkspace
{
    private static readonly JsonSerializerOptions ProvenanceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
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

    public string FindSingleAab()
    {
        RejectExistingReparsePoints(ArtifactsRoot, PublishRoot);
        var aabs = Directory.GetFiles(PublishRoot, "*.aab", SearchOption.TopDirectoryOnly);
        if (aabs.Length != 1)
        {
            throw new ReleaseToolException(
                $"Expected exactly one AAB in the invocation publish output, but found {aabs.Length}.");
        }

        return aabs[0];
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
        string artifactId,
        ArtifactProvenance? provenance,
        ArtifactValidationStatus validationStatus)
    {
        ValidateCompleteProvenance(provenance);
        if (profile == ReleaseProfile.Distributable && validationStatus != ArtifactValidationStatus.ValidatorApproved)
        {
            throw new ReleaseToolException(
                "Distributable promotion requires independent Slice 3 validator approval.");
        }

        var finalDirectory = GetFinalDirectory(profile, artifactId);
        if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory))
        {
            throw new ReleaseToolException("Final artifact destination already exists; overwrite is forbidden.");
        }

        RejectExistingReparsePoints(ArtifactsRoot, ReadyRoot);
        ValidateStagedArtifact(provenance!);
        var categoryDirectory = Path.GetDirectoryName(finalDirectory)!;
        Directory.CreateDirectory(categoryDirectory);
        RejectExistingReparsePoints(ArtifactsRoot, categoryDirectory);

        var provenancePath = Path.Combine(ReadyRoot, $"{artifactId}.provenance.json");
        File.WriteAllText(
            provenancePath,
            JsonSerializer.Serialize(provenance, ProvenanceJsonOptions) + Environment.NewLine);

        Directory.Move(ReadyRoot, finalDirectory);
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
            provenance.Build.ToolVersions.Count == 0 ||
            string.IsNullOrWhiteSpace(provenance.Signing.State))
        {
            throw new ReleaseToolException("Artifact promotion requires complete schema-v1 provenance.");
        }
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
