namespace MathFirst.ReleaseTool;

using System.Text.RegularExpressions;
using MathFirst.Application;

public static class RepositoryPolicy
{
    private static readonly Regex FullShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

    public static void Validate(PackageRequest request, RepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!FullShaPattern.IsMatch(request.ExpectedCommitSha ?? string.Empty))
        {
            throw new ReleaseToolException("ExpectedCommitSha must be a full 40-character hexadecimal Git SHA.");
        }

        if (!string.Equals(request.ExpectedCommitSha, snapshot.HeadSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("ExpectedCommitSha does not equal the repository HEAD.");
        }

        if (!snapshot.TrackedWorkingTreeClean || !snapshot.IndexClean || snapshot.UntrackedFiles.Count != 0)
        {
            throw new ReleaseToolException("Packaging requires a clean working tree, clean index, and zero untracked files.");
        }

        switch (request.Profile)
        {
            case ReleaseProfile.SourceCandidate:
                if (!string.Equals(snapshot.Branch, ReleaseConstants.SourceCandidateBranch, StringComparison.Ordinal))
                {
                    throw new ReleaseToolException(
                        $"SourceCandidate requires branch '{ReleaseConstants.SourceCandidateBranch}'.");
                }

                break;

            case ReleaseProfile.Distributable:
                if (!string.Equals(snapshot.Branch, "main", StringComparison.Ordinal))
                {
                    throw new ReleaseToolException("Distributable requires branch 'main'.");
                }

                if (!string.Equals(snapshot.HeadSha, snapshot.LocalMainSha, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(snapshot.LocalMainSha, snapshot.OriginMainSha, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ReleaseToolException(
                        "Distributable requires HEAD, local main, and origin/main to be identical.");
                }

                break;

            default:
                throw new ReleaseToolException($"Unsupported release profile '{request.Profile}'.");
        }
    }
}

public sealed class RepositoryInspector(IProcessRunner processRunner)
{
    public string DiscoverRepositoryRoot(string workingDirectory)
    {
        var result = RunGit(workingDirectory, "rev-parse", "--show-toplevel");
        var root = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ReleaseToolException("Git did not return an absolute repository root.");
        }

        return Path.GetFullPath(root);
    }

    public RepositorySnapshot Read(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var head = ReadSingleLine(root, "rev-parse", "HEAD");
        var branch = ReadSingleLine(root, "branch", "--show-current");
        var localMain = ReadSingleLine(root, "rev-parse", "refs/heads/main");
        var originMain = ReadSingleLine(root, "rev-parse", "refs/remotes/origin/main");
        var status = RunGit(root, "status", "--porcelain=v2", "--untracked-files=all").StandardOutput;

        var trackedClean = true;
        var indexClean = true;
        var untracked = new List<string>();
        foreach (var line in status.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked.Add(line[2..]);
                continue;
            }

            if ((line.StartsWith("1 ", StringComparison.Ordinal) ||
                 line.StartsWith("2 ", StringComparison.Ordinal)) && line.Length >= 4)
            {
                indexClean &= line[2] == '.';
                trackedClean &= line[3] == '.';
            }
        }

        return new RepositorySnapshot(
            root,
            head,
            branch,
            trackedClean,
            indexClean,
            untracked,
            localMain,
            originMain);
    }

    private string ReadSingleLine(string root, params string[] arguments)
    {
        var value = RunGit(root, arguments).StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\n') || value.Contains('\r'))
        {
            throw new ReleaseToolException($"Git returned an invalid value for '{string.Join(' ', arguments)}'.");
        }

        return value;
    }

    private ProcessResult RunGit(string root, params string[] arguments) =>
        processRunner.Run(new ProcessInvocation("git", arguments, root)).EnsureSuccess("git");
}

public static class VersionPolicy
{
    public static ValidatedBuildMetadata Validate(
        EvaluatedProjectMetadata evaluated,
        VersionOverrides requestedOverrides)
    {
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentNullException.ThrowIfNull(requestedOverrides);

        AppBuildMetadata parsed;
        try
        {
            parsed = AppBuildInfoMetadataParser.Parse(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MathFirst.ApplicationTitle"] = evaluated.ApplicationTitle,
                ["MathFirst.ApplicationDisplayVersion"] = evaluated.ApplicationDisplayVersion,
                ["MathFirst.ApplicationVersion"] = evaluated.ApplicationVersion
            });
        }
        catch (InvalidOperationException exception)
        {
            throw new ReleaseToolException(exception.Message);
        }

        if (requestedOverrides.DisplayVersion is not null &&
            !string.Equals(requestedOverrides.DisplayVersion, parsed.DisplayVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Evaluated ApplicationDisplayVersion does not equal the requested override.");
        }

        if (requestedOverrides.BuildNumber is not null &&
            !string.Equals(requestedOverrides.BuildNumber, evaluated.ApplicationVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Evaluated ApplicationVersion does not equal the requested override.");
        }

        if (string.IsNullOrWhiteSpace(evaluated.ApplicationId))
        {
            throw new ReleaseToolException("Evaluated ApplicationId is missing or empty.");
        }

        if (!string.Equals(evaluated.TargetFramework, ReleaseConstants.TargetFramework, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Evaluated TargetFramework must be '{ReleaseConstants.TargetFramework}'.");
        }

        if (string.IsNullOrWhiteSpace(evaluated.MinimumSdk) || string.IsNullOrWhiteSpace(evaluated.TargetSdk))
        {
            throw new ReleaseToolException("Evaluated Android SDK properties are incomplete.");
        }

        return new ValidatedBuildMetadata(
            parsed.ApplicationTitle,
            evaluated.ApplicationId,
            parsed.DisplayVersion,
            parsed.BuildNumber,
            evaluated.TargetFramework,
            evaluated.MinimumSdk,
            evaluated.TargetSdk);
    }

    public static void ValidateRequestedOverrides(
        EvaluatedProjectMetadata projectDefaults,
        VersionOverrides requestedOverrides)
    {
        var projected = projectDefaults with
        {
            ApplicationDisplayVersion = requestedOverrides.DisplayVersion ?? projectDefaults.ApplicationDisplayVersion,
            ApplicationVersion = requestedOverrides.BuildNumber ?? projectDefaults.ApplicationVersion
        };
        _ = Validate(projected, requestedOverrides);
    }
}

public static class SigningPolicy
{
    public static string? Validate(
        ReleaseProfile profile,
        SigningInputs? signingInputs,
        string repositoryRoot,
        string artifactsRoot)
    {
        if (profile == ReleaseProfile.SourceCandidate)
        {
            if (signingInputs is not null)
            {
                throw new ReleaseToolException("SourceCandidate does not accept production signing inputs.");
            }

            return null;
        }

        if (profile != ReleaseProfile.Distributable)
        {
            throw new ReleaseToolException($"Unsupported release profile '{profile}'.");
        }

        if (signingInputs is null)
        {
            throw new ReleaseToolException("Distributable requires external signing inputs.");
        }

        if (string.IsNullOrWhiteSpace(signingInputs.KeyAlias))
        {
            throw new ReleaseToolException("Distributable requires a non-empty signing key alias.");
        }

        ValidateExternalFile(signingInputs.KeystorePath, "keystore", repositoryRoot, artifactsRoot);
        ValidateExternalFile(signingInputs.StorePasswordFile, "store password file", repositoryRoot, artifactsRoot);
        ValidateExternalFile(signingInputs.KeyPasswordFile, "key password file", repositoryRoot, artifactsRoot);

        var normalizedFingerprint = (signingInputs.ExpectedSignerCertificateSha256 ?? string.Empty)
            .Replace(":", string.Empty, StringComparison.Ordinal);
        if (!Regex.IsMatch(normalizedFingerprint, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
        {
            throw new ReleaseToolException("Expected signer certificate SHA-256 must contain exactly 64 hexadecimal digits.");
        }

        return normalizedFingerprint.ToLowerInvariant();
    }

    private static void ValidateExternalFile(
        string path,
        string description,
        string repositoryRoot,
        string artifactsRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ReleaseToolException($"The {description} path must be absolute.");
        }

        var fullPath = Path.GetFullPath(path);
        if (IsWithin(repositoryRoot, fullPath) || IsWithin(artifactsRoot, fullPath))
        {
            throw new ReleaseToolException($"The {description} must remain outside the repository and artifacts roots.");
        }

        if (!File.Exists(fullPath))
        {
            throw new ReleaseToolException($"The required external {description} does not exist.");
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0 || HasReparsePointParent(fullPath))
        {
            throw new ReleaseToolException($"The external {description} path must not cross a reparse point.");
        }
    }

    private static bool HasReparsePointParent(string filePath)
    {
        var directory = Directory.GetParent(filePath);
        while (directory is not null)
        {
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative == "." ||
               (!Path.IsPathFullyQualified(relative) &&
                !relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
