namespace MathFirst.ReleaseTool;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class AndroidPackageCommand(IProcessRunner processRunner)
{
    private const string ProjectRelativePath = "src/MathFirst.App/MathFirst.App.csproj";
    private static readonly Regex FullShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

    public string Execute(PackageRequest request, string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var repositoryInspector = new RepositoryInspector(processRunner);
        var repository = repositoryInspector.Read(root);
        RepositoryPolicy.Validate(request, repository);

        var artifactsRoot = Path.Combine(root, "artifacts", "android");
        var expectedCertificateSha256 = SigningPolicy.Validate(
            request.Profile,
            request.SigningInputs,
            root,
            artifactsRoot);

        var defaults = EvaluateMetadata(root, new VersionOverrides(null, null));
        VersionPolicy.ValidateRequestedOverrides(defaults, request.VersionOverrides);
        var effective = request.VersionOverrides is { DisplayVersion: null, BuildNumber: null }
            ? defaults
            : EvaluateMetadata(root, request.VersionOverrides);
        var validatedMetadata = VersionPolicy.Validate(effective, request.VersionOverrides);

        repository = repositoryInspector.Read(root);
        RepositoryPolicy.Validate(request, repository);

        var workspace = new ArtifactWorkspace(root, Guid.NewGuid().ToString("N"));
        try
        {
            var publishInvocation = CreatePublishInvocation(
                root,
                workspace.PublishRoot,
                request.Profile,
                request.VersionOverrides,
                request.SigningInputs);
            processRunner.Run(publishInvocation).EnsureSuccess("dotnet publish");

            repository = repositoryInspector.Read(root);
            RepositoryPolicy.Validate(request, repository);

            var publishedAab = workspace.FindSingleAab();
            var fileInfo = new FileInfo(publishedAab);
            if (fileInfo.Length <= 0)
            {
                throw new ReleaseToolException("The generated AAB is empty.");
            }

            var artifactId = CreateArtifactId(request.Profile, validatedMetadata, repository.HeadSha);
            var artifactFileName = $"{artifactId}.aab";
            var stagedAab = Path.Combine(workspace.ReadyRoot, artifactFileName);
            File.Move(publishedAab, stagedAab);

            var artifactSha256 = ComputeSha256(stagedAab);
            var toolVersions = ReadToolVersions(root, effective.AndroidNetSdkVersion);
            var provenance = CreateProvenance(
                request,
                repository,
                validatedMetadata,
                artifactFileName,
                fileInfo.Length,
                artifactSha256,
                toolVersions,
                expectedCertificateSha256,
                DateTimeOffset.UtcNow);

            var stagedProvenancePath = Path.Combine(workspace.ReadyRoot, $"{artifactId}.provenance.json");
            File.WriteAllText(
                stagedProvenancePath,
                JsonSerializer.Serialize(provenance, ArtifactWorkspace.ProvenanceJsonOptions) + Environment.NewLine);

            var validator = new AndroidAabValidator(processRunner);
            var validationRequest = new ValidationRequest(
                stagedAab,
                stagedProvenancePath,
                repository.HeadSha,
                request.Profile,
                validatedMetadata.DisplayVersion,
                validatedMetadata.BuildNumber,
                expectedCertificateSha256,
                root);

            var validationResult = validator.Validate(validationRequest);
            if (!validationResult.IsValid || validationResult.Status != ArtifactValidationStatus.ValidatorApproved)
            {
                throw new ReleaseToolException("Authoritative validation failed for packaged AAB.");
            }

            workspace.Promote(
                request.Profile,
                artifactId,
                provenance,
                ArtifactValidationStatus.ValidatorApproved);
            return workspace.GetFinalDirectory(request.Profile, artifactId);
        }
        finally
        {
            workspace.Cleanup();
        }
    }

    public static ProcessInvocation CreateMetadataEvaluationInvocation(
        string repositoryRoot,
        VersionOverrides requestedOverrides)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var arguments = new List<string>
        {
            "msbuild",
            Path.Combine(root, ProjectRelativePath),
            "-nologo",
            "-getProperty:ApplicationTitle,ApplicationId,ApplicationDisplayVersion,ApplicationVersion,TargetFramework,SupportedOSPlatformVersion,TargetPlatformVersion,AndroidNETSdkVersion",
            $"-property:Configuration={ReleaseConstants.Configuration}",
            $"-property:TargetFramework={ReleaseConstants.TargetFramework}"
        };

        AddVersionOverrides(arguments, requestedOverrides);
        return new ProcessInvocation("dotnet", arguments, root);
    }

    public static EvaluatedProjectMetadata ParseEvaluatedMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ReleaseToolException("MSBuild returned no evaluated project metadata.");
        }

        try
        {
            var firstBrace = json.IndexOf('{');
            var lastBrace = json.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace < firstBrace)
            {
                throw new JsonException("No JSON object was found.");
            }

            using var document = JsonDocument.Parse(json[firstBrace..(lastBrace + 1)]);
            var properties = document.RootElement.GetProperty("Properties");
            return new EvaluatedProjectMetadata(
                GetRequiredProperty(properties, "ApplicationTitle"),
                GetRequiredProperty(properties, "ApplicationId"),
                GetRequiredProperty(properties, "ApplicationDisplayVersion"),
                GetRequiredProperty(properties, "ApplicationVersion"),
                GetRequiredProperty(properties, "TargetFramework"),
                GetRequiredProperty(properties, "SupportedOSPlatformVersion"),
                GetRequiredProperty(properties, "TargetPlatformVersion"),
                GetRequiredProperty(properties, "AndroidNETSdkVersion"));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ReleaseToolException($"MSBuild returned invalid evaluated project metadata: {exception.Message}");
        }
    }

    public static ProcessInvocation CreatePublishInvocation(
        string repositoryRoot,
        string publishRoot,
        ReleaseProfile profile,
        VersionOverrides requestedOverrides,
        SigningInputs? signingInputs)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var output = ArtifactWorkspace.EnsureContained(
            Path.Combine(root, "artifacts", "android", ".staging"),
            publishRoot);
        var arguments = new List<string>
        {
            "publish",
            Path.Combine(root, ProjectRelativePath),
            "-f",
            ReleaseConstants.TargetFramework,
            "-c",
            ReleaseConstants.Configuration,
            "--output",
            output,
            "-p:AndroidPackageFormat=aab"
        };

        AddVersionOverrides(arguments, requestedOverrides);
        switch (profile)
        {
            case ReleaseProfile.SourceCandidate:
                if (signingInputs is not null)
                {
                    throw new ReleaseToolException("SourceCandidate does not accept production signing inputs.");
                }

                arguments.Add("-p:AndroidKeyStore=false");
                break;

            case ReleaseProfile.Distributable:
                if (signingInputs is null)
                {
                    throw new ReleaseToolException("Distributable requires external signing inputs.");
                }

                arguments.Add("-p:AndroidKeyStore=true");
                arguments.Add($"-p:AndroidSigningKeyStore={signingInputs.KeystorePath}");
                arguments.Add($"-p:AndroidSigningKeyAlias={signingInputs.KeyAlias}");
                arguments.Add($"-p:AndroidSigningStorePass=file:{signingInputs.StorePasswordFile}");
                arguments.Add($"-p:AndroidSigningKeyPass=file:{signingInputs.KeyPasswordFile}");
                break;

            default:
                throw new ReleaseToolException($"Unsupported release profile '{profile}'.");
        }

        return new ProcessInvocation("dotnet", arguments, root);
    }

    public static string CreateArtifactId(
        ReleaseProfile profile,
        ValidatedBuildMetadata metadata,
        string commitSha)
    {
        if (commitSha is null || !FullShaPattern.IsMatch(commitSha))
        {
            throw new ReleaseToolException("Artifact identity requires a full 40-character commit SHA.");
        }

        var classification = GetArtifactClassification(profile);
        return $"MathFirst-v{metadata.DisplayVersion}-b{metadata.BuildNumber}-{commitSha[..12].ToLowerInvariant()}-{classification}";
    }

    public static ArtifactProvenance CreateProvenance(
        PackageRequest request,
        RepositorySnapshot repository,
        ValidatedBuildMetadata metadata,
        string artifactFileName,
        long artifactSize,
        string artifactSha256,
        IReadOnlyDictionary<string, string> toolVersions,
        string? expectedCertificateSha256,
        DateTimeOffset generatedAtUtc)
    {
        var clean = repository.TrackedWorkingTreeClean &&
                    repository.IndexClean &&
                    repository.UntrackedFiles.Count == 0;
        var sourceClassification = request.Profile == ReleaseProfile.SourceCandidate
            ? "source-candidate"
            : "distributable";
        var signingState = request.Profile == ReleaseProfile.SourceCandidate
            ? "development-debug"
            : "release-expected-pending-validation";

        return new ArtifactProvenance(
            1,
            new ProvenanceArtifact(
                artifactFileName,
                GetArtifactClassification(request.Profile),
                artifactSize,
                artifactSha256.ToLowerInvariant()),
            new ProvenanceApplication(metadata.ApplicationId, metadata.DisplayVersion, metadata.BuildNumber),
            new ProvenanceSource(
                request.ExpectedCommitSha.ToLowerInvariant(),
                repository.HeadSha.ToLowerInvariant(),
                repository.Branch,
                sourceClassification,
                clean),
            new ProvenanceBuild(
                ReleaseConstants.Configuration,
                metadata.TargetFramework,
                metadata.MinimumSdk,
                metadata.TargetSdk,
                request.VersionOverrides,
                toolVersions),
            new ProvenanceSigning(signingState, expectedCertificateSha256, null),
            generatedAtUtc.ToUniversalTime());
    }

    private EvaluatedProjectMetadata EvaluateMetadata(string root, VersionOverrides requestedOverrides)
    {
        var invocation = CreateMetadataEvaluationInvocation(root, requestedOverrides);
        var result = processRunner.Run(invocation).EnsureSuccess("dotnet msbuild metadata evaluation");
        return ParseEvaluatedMetadata(result.StandardOutput);
    }

    private IReadOnlyDictionary<string, string> ReadToolVersions(string root, string androidNetSdkVersion)
    {
        var dotnet = processRunner.Run(new ProcessInvocation("dotnet", ["--version"], root))
            .EnsureSuccess("dotnet --version")
            .StandardOutput.Trim();
        var msbuild = processRunner.Run(new ProcessInvocation("dotnet", ["msbuild", "-version", "-nologo"], root))
            .EnsureSuccess("dotnet msbuild -version")
            .StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(dotnet) || string.IsNullOrWhiteSpace(msbuild) ||
            string.IsNullOrWhiteSpace(androidNetSdkVersion))
        {
            throw new ReleaseToolException("Required build tool versions could not be established.");
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["androidNetSdk"] = androidNetSdkVersion,
            ["dotnetSdk"] = dotnet,
            ["msbuild"] = msbuild
        };
    }

    private static void AddVersionOverrides(List<string> arguments, VersionOverrides requestedOverrides)
    {
        if (requestedOverrides.DisplayVersion is not null)
        {
            arguments.Add($"-property:ApplicationDisplayVersion={requestedOverrides.DisplayVersion}");
        }

        if (requestedOverrides.BuildNumber is not null)
        {
            arguments.Add($"-property:ApplicationVersion={requestedOverrides.BuildNumber}");
        }
    }

    private static string GetRequiredProperty(JsonElement properties, string name)
    {
        var value = properties.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Evaluated property '{name}' is missing or empty.");
        }

        return value;
    }

    private static string GetArtifactClassification(ReleaseProfile profile) => profile switch
    {
        ReleaseProfile.SourceCandidate => "source-candidate-debug-signed",
        ReleaseProfile.Distributable => "distributable-release-signed-pending-validation",
        _ => throw new ReleaseToolException($"Unsupported release profile '{profile}'.")
    };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public static class ReleaseCli
{
    public static Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                throw new ReleaseToolException("No command specified. Expected 'android-package' or 'android-validate'.");
            }

            var runner = new ProcessRunner();
            if (string.Equals(args[0], "android-package", StringComparison.Ordinal))
            {
                var request = ParsePackageRequest(args);
                var inspector = new RepositoryInspector(runner);
                var repositoryRoot = inspector.DiscoverRepositoryRoot(Environment.CurrentDirectory);
                var finalDirectory = new AndroidPackageCommand(runner).Execute(request, repositoryRoot);
                Console.WriteLine($"Android package promoted to: {finalDirectory}");
                return Task.FromResult(0);
            }

            if (string.Equals(args[0], "android-validate", StringComparison.Ordinal))
            {
                var validationRequest = ParseValidationRequest(args);
                var validator = new AndroidAabValidator(runner);
                var result = validator.Validate(validationRequest);
                Console.WriteLine($"Validation PASSED for: {validationRequest.AabPath}");
                Console.WriteLine($"  Classification: {result.SignerClassification}");
                Console.WriteLine($"  Signer SHA-256: {result.SignerCertificateSha256}");
                Console.WriteLine($"  Distributable: {result.IsDistributable}");
                return Task.FromResult(0);
            }

            throw new ReleaseToolException($"Unknown command '{args[0]}'. Expected 'android-package' or 'android-validate'.");
        }
        catch (Exception exception) when (exception is ReleaseToolException or ArgumentException)
        {
            Console.Error.WriteLine($"Operation failed: {exception.Message}");
            return Task.FromResult(1);
        }
    }

    private static PackageRequest ParsePackageRequest(string[] args)
    {
        var values = ParseOptions(args, [
            "--profile",
            "--expected-commit-sha",
            "--display-version",
            "--build-number",
            "--keystore-path",
            "--key-alias",
            "--store-password-file",
            "--key-password-file",
            "--expected-signer-certificate-sha256"
        ]);

        var profileText = GetRequired(values, "--profile");
        if (!Enum.TryParse<ReleaseProfile>(profileText, ignoreCase: false, out var profile) ||
            !Enum.IsDefined(profile))
        {
            throw new ReleaseToolException("Profile must be exactly 'SourceCandidate' or 'Distributable'.");
        }

        var expectedSha = GetRequired(values, "--expected-commit-sha");
        var versionOverrides = new VersionOverrides(
            values.GetValueOrDefault("--display-version"),
            values.GetValueOrDefault("--build-number"));

        SigningInputs? signingInputs = null;
        var signingOptionPresent = values.Keys.Any(key => key is
            "--keystore-path" or
            "--key-alias" or
            "--store-password-file" or
            "--key-password-file" or
            "--expected-signer-certificate-sha256");
        if (profile == ReleaseProfile.Distributable || signingOptionPresent)
        {
            signingInputs = new SigningInputs(
                GetRequired(values, "--keystore-path"),
                GetRequired(values, "--key-alias"),
                GetRequired(values, "--store-password-file"),
                GetRequired(values, "--key-password-file"),
                GetRequired(values, "--expected-signer-certificate-sha256"));
        }

        return new PackageRequest(profile, expectedSha, versionOverrides, signingInputs);
    }

    private static ValidationRequest ParseValidationRequest(string[] args)
    {
        var values = ParseOptions(args, [
            "--aab-path",
            "--provenance-path",
            "--expected-commit-sha",
            "--profile",
            "--display-version",
            "--build-number",
            "--expected-signer-certificate-sha256",
            "--repository-root"
        ]);

        var aabPath = GetRequired(values, "--aab-path");
        var provenancePath = GetRequired(values, "--provenance-path");
        var expectedSha = GetRequired(values, "--expected-commit-sha");

        var profileText = GetRequired(values, "--profile");
        if (!Enum.TryParse<ReleaseProfile>(profileText, ignoreCase: false, out var profile) ||
            !Enum.IsDefined(profile))
        {
            throw new ReleaseToolException("Profile must be exactly 'SourceCandidate' or 'Distributable'.");
        }

        int? buildNumber = null;
        if (values.TryGetValue("--build-number", out var buildNumberText))
        {
            if (!int.TryParse(buildNumberText, out var parsedBuildNumber) || parsedBuildNumber <= 0)
            {
                throw new ReleaseToolException($"Invalid build number override '{buildNumberText}'.");
            }

            buildNumber = parsedBuildNumber;
        }

        return new ValidationRequest(
            aabPath,
            provenancePath,
            expectedSha,
            profile,
            values.GetValueOrDefault("--display-version"),
            buildNumber,
            values.GetValueOrDefault("--expected-signer-certificate-sha256"),
            values.GetValueOrDefault("--repository-root"));
    }

    private static Dictionary<string, string> ParseOptions(string[] args, HashSet<string> allowedOptions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ReleaseToolException($"Missing value for command option '{args[index]}'.");
            }

            if (!values.TryAdd(args[index], args[index + 1]))
            {
                throw new ReleaseToolException($"Command option '{args[index]}' was specified more than once.");
            }
        }

        var unknown = values.Keys.FirstOrDefault(key => !allowedOptions.Contains(key));
        if (unknown is not null)
        {
            throw new ReleaseToolException($"Unknown command option '{unknown}'.");
        }

        return values;
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            throw new ReleaseToolException($"Required command option '{name}' is missing.");
        }

        return value;
    }
}
