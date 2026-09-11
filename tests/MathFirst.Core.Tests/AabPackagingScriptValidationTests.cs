namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MathFirst.ReleaseTool;

public sealed class AabPackagingScriptValidationTests
{
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";

    [Fact]
    public void PublicRequest_HasNoConfigurationOrDirtyEscapeHatches()
    {
        var properties = typeof(PackageRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Configuration", properties);
        Assert.DoesNotContain("AllowDirty", properties);
        Assert.Equal("Release", ReleaseConstants.Configuration);
        Assert.Equal("net10.0-android36.0", ReleaseConstants.TargetFramework);
    }

    [Fact]
    public async Task PublicCli_RejectsDebugConfigurationOption()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync(
            [
                "android-package",
                "--profile",
                "SourceCandidate",
                "--expected-commit-sha",
                FullSha,
                "--configuration",
                "Debug"
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown command option '--configuration'", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 1)]
    public void RepositoryPolicy_RejectsEveryDirtyState(bool trackedClean, bool indexClean, int untrackedCount)
    {
        var snapshot = CreateSnapshot(
            trackedClean: trackedClean,
            indexClean: indexClean,
            untrackedFiles: Enumerable.Range(0, untrackedCount).Select(index => $"file-{index}").ToArray());

        Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(CreateRequest(), snapshot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("199dbd7")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void RepositoryPolicy_RejectsAbsentMalformedOrAbbreviatedExpectedSha(string? expectedSha)
    {
        Assert.Throws<ReleaseToolException>(() =>
            RepositoryPolicy.Validate(CreateRequest(expectedSha: expectedSha!), CreateSnapshot()));
    }

    [Fact]
    public void RepositoryPolicy_RejectsExpectedShaThatDoesNotEqualHead()
    {
        var request = CreateRequest(expectedSha: new string('a', 40));

        Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, CreateSnapshot()));
    }

    [Fact]
    public void RepositoryPolicy_SourceCandidateRequiresExactFeatureBranch()
    {
        Assert.Throws<ReleaseToolException>(() =>
            RepositoryPolicy.Validate(CreateRequest(), CreateSnapshot(branch: "some-other-branch")));
    }

    [Fact]
    public void RepositoryPolicy_SourceCandidateAcceptsCleanExactSourceState()
    {
        RepositoryPolicy.Validate(CreateRequest(), CreateSnapshot());
    }

    [Theory]
    [InlineData("feature", FullSha, FullSha)]
    [InlineData("main", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", FullSha)]
    [InlineData("main", FullSha, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RepositoryPolicy_DistributableRequiresSynchronizedMain(
        string branch,
        string localMainSha,
        string originMainSha)
    {
        var request = CreateRequest(ReleaseProfile.Distributable, signingInputs: CreateExternalSigningInputs());
        var snapshot = CreateSnapshot(branch: branch, localMainSha: localMainSha, originMainSha: originMainSha);

        Assert.Throws<ReleaseToolException>(() => RepositoryPolicy.Validate(request, snapshot));
    }

    [Fact]
    public void RepositoryPolicy_DistributableAcceptsExpectedSynchronizedMain()
    {
        var request = CreateRequest(ReleaseProfile.Distributable, signingInputs: CreateExternalSigningInputs());

        RepositoryPolicy.Validate(request, CreateSnapshot(branch: "main"));
    }

    [Fact]
    public void VersionPolicy_UsesEvaluatedProjectDefaultsThroughSharedParserContract()
    {
        var result = VersionPolicy.Validate(CreateMetadata(), new VersionOverrides(null, null));

        Assert.Equal("MathFirst", result.ApplicationTitle);
        Assert.Equal("com.tachiguro.mathfirst", result.ApplicationId);
        Assert.Equal("1.0", result.DisplayVersion);
        Assert.Equal(1, result.BuildNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("version 1.0")]
    [InlineData("1.2.3.4.5")]
    public void VersionPolicy_RejectsExplicitInvalidDisplayVersion(string value)
    {
        Assert.Throws<ReleaseToolException>(() =>
            VersionPolicy.Validate(CreateMetadata(), new VersionOverrides(value, null)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("not-a-number")]
    public void VersionPolicy_RejectsExplicitInvalidBuildNumber(string value)
    {
        Assert.Throws<ReleaseToolException>(() =>
            VersionPolicy.Validate(CreateMetadata(), new VersionOverrides(null, value)));
    }

    [Fact]
    public void VersionPolicy_AcceptsAndReturnsExplicitOverrides()
    {
        var result = VersionPolicy.Validate(CreateMetadata("2.3", "42"), new VersionOverrides("2.3", "42"));

        Assert.Equal("2.3", result.DisplayVersion);
        Assert.Equal(42, result.BuildNumber);
    }

    [Fact]
    public void MetadataEvaluation_RequestsEffectiveReleaseAndroidProperties()
    {
        var invocation = AndroidPackageCommand.CreateMetadataEvaluationInvocation(
            GetRepositoryRoot(),
            new VersionOverrides("2.3", "42"));

        Assert.Equal("dotnet", invocation.FileName);
        Assert.Contains("msbuild", invocation.Arguments);
        Assert.Contains("-property:Configuration=Release", invocation.Arguments);
        Assert.Contains("-property:TargetFramework=net10.0-android36.0", invocation.Arguments);
        Assert.Contains("-property:ApplicationDisplayVersion=2.3", invocation.Arguments);
        Assert.Contains("-property:ApplicationVersion=42", invocation.Arguments);
        Assert.Contains(invocation.Arguments, argument => argument.Contains("ApplicationTitle", StringComparison.Ordinal));
        Assert.Contains(invocation.Arguments, argument => argument.Contains("ApplicationId", StringComparison.Ordinal));
    }

    [Fact]
    public void MetadataEvaluation_ParsesActualMsBuildJsonShape()
    {
        const string json = """
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

        Assert.Equal(CreateMetadata(), AndroidPackageCommand.ParseEvaluatedMetadata(json));
    }

    [Theory]
    [InlineData("git")]
    [InlineData("dotnet")]
    [InlineData("keytool")]
    public void RequiredExternalCommandFailure_IsNeverIgnored(string commandName)
    {
        var result = new ProcessResult(23, "partial output", "synthetic failure");

        var exception = Assert.Throws<ReleaseToolException>(() => result.EnsureSuccess(commandName));
        Assert.Contains(commandName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("23", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SigningPolicy_SourceCandidateRequiresNoProductionSecrets()
    {
        var result = SigningPolicy.Validate(
            ReleaseProfile.SourceCandidate,
            null,
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android"));

        Assert.Null(result);
    }

    [Fact]
    public void SigningPolicy_DistributableRequiresAllSigningInputs()
    {
        Assert.Throws<ReleaseToolException>(() => SigningPolicy.Validate(
            ReleaseProfile.Distributable,
            null,
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android")));
    }

    [Fact]
    public void SigningPolicy_RejectsEmptyAlias()
    {
        using var fixture = new ExternalSigningFixture();

        Assert.Throws<ReleaseToolException>(() => ValidateSigning(fixture.CreateInputs() with { KeyAlias = "   " }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aa:bb")]
    public void SigningPolicy_RejectsMalformedCertificateFingerprint(string fingerprint)
    {
        using var fixture = new ExternalSigningFixture();

        Assert.Throws<ReleaseToolException>(() => ValidateSigning(
            fixture.CreateInputs() with { ExpectedSignerCertificateSha256 = fingerprint }));
    }

    [Fact]
    public void SigningPolicy_RejectsRelativeSecretPaths()
    {
        using var fixture = new ExternalSigningFixture();

        Assert.Throws<ReleaseToolException>(() => ValidateSigning(
            fixture.CreateInputs() with { StorePasswordFile = "store-password.txt" }));
    }

    [Fact]
    public void SigningPolicy_RejectsMissingSecretFiles()
    {
        using var fixture = new ExternalSigningFixture();

        Assert.Throws<ReleaseToolException>(() => ValidateSigning(
            fixture.CreateInputs() with { KeystorePath = Path.Combine(fixture.Root, "missing.jks") }));
    }

    [Fact]
    public void SigningPolicy_RejectsRepositoryContainedSecretFiles()
    {
        var repositoryRoot = GetRepositoryRoot();
        var inputs = new SigningInputs(
            Path.Combine(repositoryRoot, "repo-secret.jks"),
            "release",
            Path.Combine(repositoryRoot, "store.pass"),
            Path.Combine(repositoryRoot, "key.pass"),
            new string('a', 64));

        Assert.Throws<ReleaseToolException>(() => SigningPolicy.Validate(
            ReleaseProfile.Distributable,
            inputs,
            repositoryRoot,
            Path.Combine(repositoryRoot, "artifacts", "android")));
    }

    [Fact]
    public void SigningPolicy_RejectsArtifactContainedSecretFiles()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"mathfirst-repo-{Guid.NewGuid():N}");
        var artifactsRoot = Path.Combine(repositoryRoot, "artifacts", "android");
        var inputs = new SigningInputs(
            Path.Combine(artifactsRoot, "release.jks"),
            "release",
            Path.Combine(artifactsRoot, "store.pass"),
            Path.Combine(artifactsRoot, "key.pass"),
            new string('a', 64));

        Assert.Throws<ReleaseToolException>(() => SigningPolicy.Validate(
            ReleaseProfile.Distributable,
            inputs,
            repositoryRoot,
            artifactsRoot));
    }

    [Fact]
    public void SigningPolicy_AcceptsExternalFilesAndNormalizesFingerprint()
    {
        using var fixture = new ExternalSigningFixture();
        var inputs = fixture.CreateInputs() with
        {
            ExpectedSignerCertificateSha256 = string.Join(':', Enumerable.Repeat("ab", 32))
        };

        Assert.Equal(string.Concat(Enumerable.Repeat("ab", 32)), ValidateSigning(inputs));
    }

    [Fact]
    public void SigningPolicy_RejectsSecretPathsThroughReparsePointWhenSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var target = new TemporaryDirectory("mathfirst-signing-target");
        using var links = new TemporaryDirectory("mathfirst-signing-links");
        var targetFixture = new ExternalSigningFixture(target.Path);
        var link = Path.Combine(links.Path, "linked-secrets");
        try
        {
            Directory.CreateSymbolicLink(link, target.Path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var inputs = targetFixture.CreateInputs() with
        {
            KeystorePath = Path.Combine(link, "release.jks"),
            StorePasswordFile = Path.Combine(link, "store.pass"),
            KeyPasswordFile = Path.Combine(link, "key.pass")
        };

        Assert.Throws<ReleaseToolException>(() => ValidateSigning(inputs));
    }

    [Fact]
    public void SigningInputs_ExposeNoPlaintextPasswordProperties()
    {
        var names = typeof(SigningInputs).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("StorePassword", names);
        Assert.DoesNotContain("KeyPassword", names);
        Assert.Contains("StorePasswordFile", names);
        Assert.Contains("KeyPasswordFile", names);
    }

    [Fact]
    public void PublishInvocation_UsesPasswordFilesWithoutReadingTheirContents()
    {
        using var fixture = new ExternalSigningFixture();
        var inputs = fixture.CreateInputs();

        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.Distributable,
            new VersionOverrides(null, null),
            inputs);

        Assert.Contains($"-p:AndroidSigningStorePass=file:{inputs.StorePasswordFile}", invocation.Arguments);
        Assert.Contains($"-p:AndroidSigningKeyPass=file:{inputs.KeyPasswordFile}", invocation.Arguments);
        Assert.DoesNotContain(invocation.Arguments, argument => argument.Contains(ExternalSigningFixture.SecretContents, StringComparison.Ordinal));
        Assert.Contains("-p:AndroidKeyStore=true", invocation.Arguments);
        Assert.Contains("-p:AndroidPackageFormat=aab", invocation.Arguments);
        Assert.Contains("-f", invocation.Arguments);
        Assert.Contains("net10.0-android36.0", invocation.Arguments);
        Assert.Contains("-c", invocation.Arguments);
        Assert.Contains("Release", invocation.Arguments);
    }

    [Fact]
    public void PublishInvocation_SourceCandidateUsesDevelopmentDebugSigning()
    {
        var invocation = AndroidPackageCommand.CreatePublishInvocation(
            GetRepositoryRoot(),
            Path.Combine(GetRepositoryRoot(), "artifacts", "android", ".staging", "test", "publish"),
            ReleaseProfile.SourceCandidate,
            new VersionOverrides(null, null),
            null);

        Assert.Contains("-p:AndroidKeyStore=false", invocation.Arguments);
        Assert.DoesNotContain(invocation.Arguments, argument => argument.Contains("AndroidSigning", StringComparison.Ordinal));
    }

    [Fact]
    public void ArtifactWorkspace_UsesFixedFreshInvocationRoot()
    {
        using var repository = new TemporaryDirectory("mathfirst-workspace");
        var workspace = new ArtifactWorkspace(repository.Path, "invocation-123");

        Assert.Equal(Path.Combine(repository.Path, "artifacts", "android"), workspace.ArtifactsRoot);
        Assert.Equal(Path.Combine(workspace.ArtifactsRoot, ".staging", "invocation-123"), workspace.InvocationRoot);
        Assert.Equal(Path.Combine(workspace.InvocationRoot, "publish"), workspace.PublishRoot);
        Assert.Equal(Path.Combine(workspace.InvocationRoot, "ready"), workspace.ReadyRoot);
        Assert.True(Directory.Exists(workspace.PublishRoot));
        Assert.True(Directory.Exists(workspace.ReadyRoot));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsNormalizedPathEscape()
    {
        using var root = new TemporaryDirectory("mathfirst-containment");

        Assert.Throws<ReleaseToolException>(() =>
            ArtifactWorkspace.EnsureContained(root.Path, Path.Combine(root.Path, "..", "escaped")));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsReparsePointEscapeWhenSupported()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var repository = new TemporaryDirectory("mathfirst-reparse-repo");
        using var external = new TemporaryDirectory("mathfirst-reparse-external");
        var artifactsLink = Path.Combine(repository.Path, "artifacts");
        try
        {
            Directory.CreateSymbolicLink(artifactsLink, external.Path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<ReleaseToolException>(() => new ArtifactWorkspace(repository.Path, "reparse"));
    }

    [Fact]
    public void ArtifactWorkspace_RequiresExactlyOneTopLevelAab()
    {
        using var repository = new TemporaryDirectory("mathfirst-single-aab");
        var workspace = new ArtifactWorkspace(repository.Path, "single");
        var expected = Path.Combine(workspace.PublishRoot, "app.aab");
        File.WriteAllText(expected, "synthetic-aab");
        var nested = Directory.CreateDirectory(Path.Combine(workspace.PublishRoot, "old"));
        File.WriteAllText(Path.Combine(nested.FullName, "old.aab"), "ignored");

        Assert.Equal(expected, workspace.FindSingleAab());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArtifactWorkspace_RejectsZeroOrMultipleTopLevelAabs(int count)
    {
        using var repository = new TemporaryDirectory("mathfirst-aab-count");
        var workspace = new ArtifactWorkspace(repository.Path, "count");
        for (var index = 0; index < count; index++)
        {
            File.WriteAllText(Path.Combine(workspace.PublishRoot, $"app-{index}.aab"), "synthetic");
        }

        Assert.Throws<ReleaseToolException>(() => workspace.FindSingleAab());
    }

    [Fact]
    public void ArtifactWorkspace_RejectsExistingFinalDestinationWithoutOverwrite()
    {
        using var repository = new TemporaryDirectory("mathfirst-collision");
        var workspace = new ArtifactWorkspace(repository.Path, "collision");
        Directory.CreateDirectory(workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, "artifact-id"));

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(
                ReleaseProfile.SourceCandidate,
                "artifact-id",
                CreateProvenance(),
                ArtifactValidationStatus.NotValidated));
    }

    [Fact]
    public void ArtifactWorkspace_DistributableCannotPromoteWithoutValidatorApproval()
    {
        using var repository = new TemporaryDirectory("mathfirst-no-approval");
        var workspace = new ArtifactWorkspace(repository.Path, "no-approval");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(
                ReleaseProfile.Distributable,
                "artifact-id",
                CreateProvenance(),
                ArtifactValidationStatus.NotValidated));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsPromotionWithoutCompleteProvenance()
    {
        using var repository = new TemporaryDirectory("mathfirst-no-provenance");
        var workspace = new ArtifactWorkspace(repository.Path, "no-provenance");

        Assert.Throws<ReleaseToolException>(() =>
            workspace.Promote(
                ReleaseProfile.SourceCandidate,
                "artifact-id",
                null,
                ArtifactValidationStatus.NotValidated));
    }

    [Fact]
    public void ArtifactWorkspace_SourceCandidatePromotesReadyDirectoryAtomically()
    {
        using var repository = new TemporaryDirectory("mathfirst-promote");
        var workspace = new ArtifactWorkspace(repository.Path, "promote");
        File.WriteAllText(Path.Combine(workspace.ReadyRoot, "MathFirst.aab"), "synthetic-aab");

        workspace.Promote(
            ReleaseProfile.SourceCandidate,
            "artifact-id",
            CreateWorkspaceProvenance("synthetic-aab"),
            ArtifactValidationStatus.NotValidated);

        var final = workspace.GetFinalDirectory(ReleaseProfile.SourceCandidate, "artifact-id");
        Assert.True(File.Exists(Path.Combine(final, "MathFirst.aab")));
        Assert.True(File.Exists(Path.Combine(final, "artifact-id.provenance.json")));
        Assert.False(Directory.Exists(workspace.ReadyRoot));
    }

    [Fact]
    public void ArtifactWorkspace_RejectsArtifactHashThatDoesNotMatchProvenance()
    {
        using var repository = new TemporaryDirectory("mathfirst-hash-mismatch");
        var workspace = new ArtifactWorkspace(repository.Path, "hash-mismatch");
        File.WriteAllText(Path.Combine(workspace.ReadyRoot, "MathFirst.aab"), "synthetic-aab");

        Assert.Throws<ReleaseToolException>(() => workspace.Promote(
            ReleaseProfile.SourceCandidate,
            "artifact-id",
            CreateProvenance(),
            ArtifactValidationStatus.NotValidated));
    }

    [Fact]
    public void ArtifactWorkspace_CleanupRemovesOnlyOwnedInvocationDirectory()
    {
        using var repository = new TemporaryDirectory("mathfirst-cleanup");
        var workspace = new ArtifactWorkspace(repository.Path, "owned");
        var sibling = Directory.CreateDirectory(Path.Combine(workspace.ArtifactsRoot, ".staging", "sibling"));

        workspace.Cleanup();

        Assert.False(Directory.Exists(workspace.InvocationRoot));
        Assert.True(Directory.Exists(sibling.FullName));
    }

    [Theory]
    [InlineData(ReleaseProfile.SourceCandidate, "source-candidate-debug-signed")]
    [InlineData(ReleaseProfile.Distributable, "distributable-release-signed-pending-validation")]
    public void ArtifactNaming_EncodesProfileVersionBuildAndCommit(
        ReleaseProfile profile,
        string classification)
    {
        var artifactId = AndroidPackageCommand.CreateArtifactId(profile, CreateValidatedMetadata(), FullSha);

        Assert.Equal($"MathFirst-v1.0-b1-{FullSha[..12]}-{classification}", artifactId);
    }

    [Fact]
    public void ProvenanceSchemaV1_ContainsAllMandatoryTypedFields()
    {
        var provenance = CreateProvenance();
        var json = JsonSerializer.Serialize(provenance, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("MathFirst.aab", root.GetProperty("artifact").GetProperty("fileName").GetString());
        Assert.Equal("source-candidate-debug-signed", root.GetProperty("artifact").GetProperty("classification").GetString());
        Assert.Equal(13, root.GetProperty("artifact").GetProperty("sizeBytes").GetInt64());
        Assert.Equal(new string('b', 64), root.GetProperty("artifact").GetProperty("sha256").GetString());
        Assert.Equal("com.tachiguro.mathfirst", root.GetProperty("application").GetProperty("id").GetString());
        Assert.Equal("1.0", root.GetProperty("application").GetProperty("displayVersion").GetString());
        Assert.Equal(1, root.GetProperty("application").GetProperty("buildNumber").GetInt32());
        Assert.Equal(FullSha, root.GetProperty("source").GetProperty("expectedCommitSha").GetString());
        Assert.Equal(FullSha, root.GetProperty("source").GetProperty("commitSha").GetString());
        Assert.Equal(ReleaseConstants.SourceCandidateBranch, root.GetProperty("source").GetProperty("ref").GetString());
        Assert.Equal("source-candidate", root.GetProperty("source").GetProperty("classification").GetString());
        Assert.True(root.GetProperty("source").GetProperty("workingTreeClean").GetBoolean());
        Assert.Equal("Release", root.GetProperty("build").GetProperty("configuration").GetString());
        Assert.Equal("net10.0-android36.0", root.GetProperty("build").GetProperty("targetFramework").GetString());
        Assert.Equal("24.0", root.GetProperty("build").GetProperty("minSdk").GetString());
        Assert.Equal("36.0", root.GetProperty("build").GetProperty("targetSdk").GetString());
        Assert.Equal("10.0.401", root.GetProperty("build").GetProperty("toolVersions").GetProperty("dotnetSdk").GetString());
        Assert.Equal("development-debug", root.GetProperty("signing").GetProperty("state").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-09-11T10:00:00Z"), root.GetProperty("generatedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public void PackageScript_IsThinAndExposesOnlySafeInputs()
    {
        var script = File.ReadAllText(GetRepositoryPath("scripts", "package-android-aab.ps1"));

        Assert.Contains("MathFirst.ReleaseTool", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommitSha", script, StringComparison.Ordinal);
        Assert.Contains("StorePasswordFile", script, StringComparison.Ordinal);
        Assert.Contains("KeyPasswordFile", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDirty", script, StringComparison.Ordinal);
        Assert.DoesNotContain("OutputDir", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StorePassword =", script, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyPassword =", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-ChildItem", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateScript_IsThinAndDelegatesToReleaseTool()
    {
        var script = File.ReadAllText(GetRepositoryPath("scripts", "validate-android-aab.ps1"));

        Assert.Contains("MathFirst.ReleaseTool", script, StringComparison.Ordinal);
        Assert.Contains("android-validate", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommitSha", script, StringComparison.Ordinal);
        Assert.Contains("Profile", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedSignerCertificateSha256", script, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireSigned", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-ChildItem", script, StringComparison.Ordinal);
        Assert.DoesNotContain("System.IO.Compression", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicCli_ValidateCommand_RejectsUnknownOption()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync(
            [
                "android-validate",
                "--profile",
                "SourceCandidate",
                "--unknown-option",
                "value"
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown command option '--unknown-option'", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private static PackageRequest CreateRequest(
        ReleaseProfile profile = ReleaseProfile.SourceCandidate,
        string expectedSha = FullSha,
        SigningInputs? signingInputs = null) =>
        new(profile, expectedSha, new VersionOverrides(null, null), signingInputs);

    private static RepositorySnapshot CreateSnapshot(
        string branch = ReleaseConstants.SourceCandidateBranch,
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

    private static EvaluatedProjectMetadata CreateMetadata(
        string displayVersion = "1.0",
        string buildNumber = "1") =>
        new(
            "MathFirst",
            "com.tachiguro.mathfirst",
            displayVersion,
            buildNumber,
            "net10.0-android36.0",
            "24.0",
            "36.0",
            "36.1.69");

    private static ValidatedBuildMetadata CreateValidatedMetadata() =>
        new("MathFirst", "com.tachiguro.mathfirst", "1.0", 1, "net10.0-android36.0", "24.0", "36.0");

    private static ArtifactProvenance CreateProvenance() => AndroidPackageCommand.CreateProvenance(
        CreateRequest(),
        CreateSnapshot(),
        CreateValidatedMetadata(),
        "MathFirst.aab",
        13,
        new string('b', 64),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnetSdk"] = "10.0.401" },
        null,
        DateTimeOffset.Parse("2026-09-11T10:00:00Z"));

    private static ArtifactProvenance CreateWorkspaceProvenance(string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents);
        return AndroidPackageCommand.CreateProvenance(
            CreateRequest(),
            CreateSnapshot(),
            CreateValidatedMetadata(),
            "MathFirst.aab",
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["dotnetSdk"] = "10.0.401" },
            null,
            DateTimeOffset.Parse("2026-09-11T10:00:00Z"));
    }

    private static SigningInputs CreateExternalSigningInputs() =>
        new("C:\\external\\release.jks", "release", "C:\\external\\store.pass", "C:\\external\\key.pass", new string('a', 64));

    private static string? ValidateSigning(SigningInputs inputs) => SigningPolicy.Validate(
        ReleaseProfile.Distributable,
        inputs,
        GetRepositoryRoot(),
        Path.Combine(GetRepositoryRoot(), "artifacts", "android"));

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine(new[] { GetRepositoryRoot() }.Concat(segments).ToArray());

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class ExternalSigningFixture : IDisposable
    {
        public const string SecretContents = "synthetic-secret-never-forward";
        private readonly TemporaryDirectory? directory;

        public ExternalSigningFixture()
        {
            directory = new TemporaryDirectory("mathfirst-signing");
            Root = directory.Path;
        }

        public ExternalSigningFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public SigningInputs CreateInputs()
        {
            var keystore = CreateFile("release.jks", "synthetic-keystore");
            var storePassword = CreateFile("store.pass", SecretContents);
            var keyPassword = CreateFile("key.pass", SecretContents);
            return new SigningInputs(keystore, "release", storePassword, keyPassword, new string('a', 64));
        }

        public void Dispose() => directory?.Dispose();

        private string CreateFile(string name, string contents)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, contents);
            return path;
        }
    }

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
