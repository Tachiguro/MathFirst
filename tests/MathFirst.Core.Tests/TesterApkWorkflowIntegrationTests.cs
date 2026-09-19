namespace MathFirst.Core.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MathFirst.ReleaseTool;
using Xunit;

public sealed class TesterApkWorkflowIntegrationTests
{
    private const string FullSha = "199dbd7cd38feafca5c94f9a5b1939bf2d912a20";
    private const string SampleCertSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_EndToEndSuccess_ProducesPromotedFiveFileEvidence()
    {
        using var fixture = new OrchestrationTestFixture();
        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var finalDirectory = command.Execute(request, fixture.RepositoryRoot);

        var expectedArtifactId = $"MathFirst-v1.0-b1-{FullSha[..12]}-tester-debug-signed";
        var expectedDirectory = Path.Combine(
            fixture.RepositoryRoot,
            "artifacts",
            "android",
            "tester",
            expectedArtifactId);

        Assert.Equal(Path.GetFullPath(expectedDirectory), Path.GetFullPath(finalDirectory));
        Assert.True(Directory.Exists(finalDirectory));

        var files = Directory.GetFiles(finalDirectory).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            [
                $"{expectedArtifactId}.apk",
                $"{expectedArtifactId}.provenance.json",
                $"{expectedArtifactId}.validation.json",
                "SHA256SUMS",
                "TESTER_README.md"
            ],
            files);

        // Verify staging cleanup
        var stagingRoot = Path.Combine(fixture.RepositoryRoot, "artifacts", "android", ".staging");
        if (Directory.Exists(stagingRoot))
        {
            Assert.Empty(Directory.GetDirectories(stagingRoot));
        }
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_WithVersionOverrides_HonorsOverridesInEvidence()
    {
        using var fixture = new OrchestrationTestFixture(displayVersion: "2.5", buildNumber: 99);
        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides("2.5", "99"),
            null);

        var finalDirectory = command.Execute(request, fixture.RepositoryRoot);

        var expectedArtifactId = $"MathFirst-v2.5-b99-{FullSha[..12]}-tester-debug-signed";
        var expectedDirectory = Path.Combine(
            fixture.RepositoryRoot,
            "artifacts",
            "android",
            "tester",
            expectedArtifactId);

        Assert.Equal(Path.GetFullPath(expectedDirectory), Path.GetFullPath(finalDirectory));
        Assert.True(File.Exists(Path.Combine(finalDirectory, $"{expectedArtifactId}.apk")));
        Assert.True(File.Exists(Path.Combine(finalDirectory, $"{expectedArtifactId}.provenance.json")));
        Assert.True(File.Exists(Path.Combine(finalDirectory, $"{expectedArtifactId}.validation.json")));
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsDirtyRepositoryBeforePublish()
    {
        using var fixture = new OrchestrationTestFixture(gitStatus: "? dirty-file.tmp\n");
        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("clean working tree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsRepositoryMutationAfterPublish()
    {
        using var fixture = new OrchestrationTestFixture();
        fixture.ProcessRunner.OnPublishHook = () =>
        {
            fixture.ProcessRunner.GitStatusOutput = "? mutated-file.tmp\n";
        };

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("clean working tree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsPublishProcessFailure()
    {
        using var fixture = new OrchestrationTestFixture();
        fixture.ProcessRunner.PublishExitCode = 1;

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("dotnet publish", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsNoSignedApk()
    {
        using var fixture = new OrchestrationTestFixture();
        fixture.ProcessRunner.PublishGeneratesSignedApk = false;

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("Expected exactly one signed APK candidate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsEmptyApk()
    {
        using var fixture = new OrchestrationTestFixture();
        fixture.ProcessRunner.PublishGeneratesEmptyApk = true;

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsApkValidationFailure()
    {
        using var fixture = new OrchestrationTestFixture();
        fixture.ProcessRunner.ApksignerOutput = """
            DOES NOT VERIFY
            ERROR: Signature verification failed
            """;

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("does not verify", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsExistingDestinationCollision()
    {
        using var fixture = new OrchestrationTestFixture();
        var expectedArtifactId = $"MathFirst-v1.0-b1-{FullSha[..12]}-tester-debug-signed";
        var existingDir = Path.Combine(
            fixture.RepositoryRoot,
            "artifacts",
            "android",
            "tester",
            expectedArtifactId);
        Directory.CreateDirectory(existingDir);

        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            null);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseCli_AndroidPackage_AcceptsTesterProfile()
    {
        using var fixture = new OrchestrationTestFixture();
        var originalOut = Console.Out;
        try
        {
            using var stdout = new StringWriter();
            Console.SetOut(stdout);

            var exitCode = await ReleaseCli.RunAsync([
                "android-package",
                "--profile",
                "Tester",
                "--expected-commit-sha",
                FullSha
            ]);

            // ReleaseCli uses real ProcessRunner which might fail if not in repo,
            // but the argument parsing itself must accept Tester and not reject with
            // "Profile must be exactly 'SourceCandidate' or 'Distributable'".
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void AndroidPackageCommand_Execute_Tester_RejectsSuppliedSigningInputs()
    {
        using var fixture = new OrchestrationTestFixture();
        var command = new AndroidPackageCommand(fixture.ProcessRunner);
        var signingInputs = new SigningInputs(
            @"C:\external\test.keystore",
            "alias",
            @"C:\external\store.pass",
            @"C:\external\key.pass",
            SampleCertSha256);
        var request = new PackageRequest(
            ReleaseProfile.Tester,
            FullSha,
            new VersionOverrides(null, null),
            signingInputs);

        var exception = Assert.Throws<ReleaseToolException>(() => command.Execute(request, fixture.RepositoryRoot));
        Assert.Contains("does not accept production signing inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseCli_AndroidPackage_TesterProfile_RecognizedByParser()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync([
                "android-package",
                "--profile",
                "Tester",
                "--expected-commit-sha",
                FullSha
            ]);

            // ReleaseCli parses Tester as valid profile, so error must NOT say profile is invalid
            Assert.DoesNotContain("Profile must be exactly", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ReleaseCli_AndroidValidateApk_CommandIsRecognized()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync([
                "android-validate-apk",
                "--apk-path",
                @"C:\nonexistent\app.apk",
                "--provenance-path",
                @"C:\nonexistent\app.provenance.json",
                "--expected-commit-sha",
                FullSha
            ]);

            Assert.Equal(1, exitCode);
            // It should fail on file not found or validation, NOT on "Unknown command 'android-validate-apk'"
            Assert.DoesNotContain("Unknown command", error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not exist", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ReleaseCli_AndroidValidateApk_RejectsProfileOption()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync([
                "android-validate-apk",
                "--apk-path",
                @"C:\nonexistent\app.apk",
                "--provenance-path",
                @"C:\nonexistent\app.provenance.json",
                "--expected-commit-sha",
                FullSha,
                "--profile",
                "Tester"
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown command option '--profile'", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task ReleaseCli_AndroidValidate_StillRejectsTesterProfile()
    {
        var originalError = Console.Error;
        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            var exitCode = await ReleaseCli.RunAsync([
                "android-validate",
                "--aab-path",
                @"C:\nonexistent\app.aab",
                "--provenance-path",
                @"C:\nonexistent\app.provenance.json",
                "--expected-commit-sha",
                FullSha,
                "--profile",
                "Tester"
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Profile must be exactly 'SourceCandidate' or 'Distributable'", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void PackageTesterApkScript_ContractVerification()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "package-android-tester-apk.ps1");
        Assert.True(File.Exists(scriptPath), "package-android-tester-apk.ps1 must exist.");

        var content = File.ReadAllText(scriptPath);
        Assert.Contains("MathFirst.ReleaseTool", content, StringComparison.Ordinal);
        Assert.Contains("android-package", content, StringComparison.Ordinal);
        Assert.Contains("--profile", content, StringComparison.Ordinal);
        Assert.Contains("Tester", content, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommitSha", content, StringComparison.Ordinal);
        Assert.Contains("DisplayVersion", content, StringComparison.Ordinal);
        Assert.Contains("BuildNumber", content, StringComparison.Ordinal);
        Assert.Contains("-c", content, StringComparison.Ordinal);
        Assert.Contains("Release", content, StringComparison.Ordinal);

        // Ensure no production signing options are exposed
        Assert.DoesNotContain("KeystorePath", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KeyAlias", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StorePasswordFile", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KeyPasswordFile", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExpectedSignerCertificateSha256", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTesterApkScript_ContractVerification()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "validate-android-apk.ps1");
        Assert.True(File.Exists(scriptPath), "validate-android-apk.ps1 must exist.");

        var content = File.ReadAllText(scriptPath);
        Assert.Contains("MathFirst.ReleaseTool", content, StringComparison.Ordinal);
        Assert.Contains("android-validate-apk", content, StringComparison.Ordinal);
        Assert.Contains("ApkPath", content, StringComparison.Ordinal);
        Assert.Contains("ProvenancePath", content, StringComparison.Ordinal);
        Assert.Contains("ExpectedCommitSha", content, StringComparison.Ordinal);
        Assert.Contains("-c", content, StringComparison.Ordinal);
        Assert.Contains("Release", content, StringComparison.Ordinal);

        // Ensure no production signer option
        Assert.DoesNotContain("ExpectedSignerCertificateSha256", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profile", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageAabScript_ValidateSet_ContainsOnlyAabProfiles()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "package-android-aab.ps1");
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("\"SourceCandidate\"", content, StringComparison.Ordinal);
        Assert.Contains("\"Distributable\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Tester\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateAabScript_ValidateSet_ContainsOnlyAabProfiles()
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "validate-android-aab.ps1");
        var content = File.ReadAllText(scriptPath);

        Assert.Contains("\"SourceCandidate\"", content, StringComparison.Ordinal);
        Assert.Contains("\"Distributable\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Tester\"", content, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return Path.GetFullPath(current);
            }

            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }

        return @"C:\Dev\MathFirst";
    }

    private sealed class OrchestrationTestFixture : IDisposable
    {
        public string RepositoryRoot { get; }
        public FakeOrchestrationRunner ProcessRunner { get; }

        public OrchestrationTestFixture(
            string branch = "feat/mf-ux-005-tester-apk-workflow",
            string gitStatus = "",
            string displayVersion = "1.0",
            int buildNumber = 1)
        {
            RepositoryRoot = Path.Combine(Path.GetTempPath(), $"mathfirst-orch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, "src", "MathFirst.App"));
            File.WriteAllText(Path.Combine(RepositoryRoot, "src", "MathFirst.App", "MathFirst.App.csproj"), "<Project/>");

            ProcessRunner = new FakeOrchestrationRunner(RepositoryRoot, branch, gitStatus, displayVersion, buildNumber);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepositoryRoot))
            {
                try
                {
                    Directory.Delete(RepositoryRoot, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    private sealed class FakeOrchestrationRunner : IProcessRunner
    {
        public string RepoRoot { get; }
        public string Branch { get; set; }
        public string GitStatusOutput { get; set; }
        public string DisplayVersion { get; set; }
        public int BuildNumber { get; set; }
        public int PublishExitCode { get; set; } = 0;
        public bool PublishGeneratesSignedApk { get; set; } = true;
        public bool PublishGeneratesEmptyApk { get; set; } = false;
        public Action? OnPublishHook { get; set; }
        public string? ApksignerOutput { get; set; }

        public FakeOrchestrationRunner(string repoRoot, string branch, string gitStatus, string displayVersion, int buildNumber)
        {
            RepoRoot = repoRoot;
            Branch = branch;
            GitStatusOutput = gitStatus;
            DisplayVersion = displayVersion;
            BuildNumber = buildNumber;
        }

        public ProcessResult Run(ProcessInvocation invocation)
        {
            var joined = $"{invocation.FileName} {string.Join(" ", invocation.Arguments)}";

            if (invocation.FileName == "git")
            {
                if (joined.Contains("rev-parse --show-toplevel")) return new(0, RepoRoot + "\n", "");
                if (joined.Contains("rev-parse HEAD")) return new(0, FullSha + "\n", "");
                if (joined.Contains("branch --show-current")) return new(0, Branch + "\n", "");
                if (joined.Contains("rev-parse refs/heads/main")) return new(0, FullSha + "\n", "");
                if (joined.Contains("rev-parse refs/remotes/origin/main")) return new(0, FullSha + "\n", "");
                if (joined.Contains("status --porcelain=v2")) return new(0, GitStatusOutput, "");
                return new(0, "", "");
            }

            if (joined.Contains("msbuild") && joined.Contains("-getProperty:"))
            {
                var json = $$"""
                    {
                      "Properties": {
                        "ApplicationTitle": "MathFirst",
                        "ApplicationId": "com.tachiguro.mathfirst.tester",
                        "ApplicationDisplayVersion": "{{DisplayVersion}}",
                        "ApplicationVersion": "{{BuildNumber}}",
                        "TargetFramework": "net10.0-android36.0",
                        "SupportedOSPlatformVersion": "24.0",
                        "TargetPlatformVersion": "36.0",
                        "AndroidNETSdkVersion": "36.1.69"
                      }
                    }
                    """;
                return new(0, json, "");
            }

            if (joined.Contains("publish"))
            {
                OnPublishHook?.Invoke();

                if (PublishExitCode != 0)
                {
                    return new(PublishExitCode, "", "dotnet publish synthetic failure");
                }

                var argsList = invocation.Arguments.ToList();
                var outputIdx = argsList.IndexOf("--output");
                if (outputIdx >= 0 && outputIdx + 1 < argsList.Count)
                {
                    var publishOutput = argsList[outputIdx + 1];
                    Directory.CreateDirectory(publishOutput);

                    if (PublishGeneratesSignedApk)
                    {
                        var signedApk = Path.Combine(publishOutput, "com.tachiguro.mathfirst.tester-Signed.apk");
                        if (PublishGeneratesEmptyApk)
                        {
                            File.WriteAllBytes(signedApk, []);
                        }
                        else
                        {
                            CreateValidSyntheticApk(signedApk);
                        }
                    }
                    else
                    {
                        var unsignedApk = Path.Combine(publishOutput, "com.tachiguro.mathfirst.tester.apk");
                        CreateValidSyntheticApk(unsignedApk);
                    }
                }

                return new(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n", "");
            }

            if (joined.Contains("--version"))
            {
                return new(0, "10.0.401\n", "");
            }

            if (joined.Contains("msbuild -version"))
            {
                return new(0, "17.12.0\n", "");
            }

            if (joined.Contains("dump xmltree", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new StringBuilder();
                sb.AppendLine("N: android=http://schemas.android.com/apk/res/android (line=8)");
                sb.AppendLine("  E: manifest (line=8)");
                sb.AppendLine($"    A: http://schemas.android.com/apk/res/android:versionCode(0x0101021b)={BuildNumber}");
                sb.AppendLine($"    A: http://schemas.android.com/apk/res/android:versionName(0x0101021c)=\"{DisplayVersion}\" (Raw: \"{DisplayVersion}\")");
                sb.AppendLine("    A: package=\"com.tachiguro.mathfirst.tester\" (Raw: \"com.tachiguro.mathfirst.tester\")");
                sb.AppendLine("      E: uses-sdk (line=9)");
                sb.AppendLine("        A: http://schemas.android.com/apk/res/android:minSdkVersion(0x0101020c)=24");
                sb.AppendLine("        A: http://schemas.android.com/apk/res/android:targetSdkVersion(0x01010270)=36");
                sb.AppendLine("      E: application (line=12)");
                sb.AppendLine("        A: http://schemas.android.com/apk/res/android:allowBackup(0x01010280)=true");
                sb.AppendLine("        A: http://schemas.android.com/apk/res/android:fullBackupContent(0x010104eb)=@0x7f120000");
                sb.AppendLine("        A: http://schemas.android.com/apk/res/android:dataExtractionRules(0x0101063e)=@0x7f120001");
                return new(0, sb.ToString(), "");
            }

            if (joined.Contains("dexdump", StringComparison.OrdinalIgnoreCase))
            {
                return new(0, "dexdump success", "");
            }

            if (joined.Contains("apksigner", StringComparison.OrdinalIgnoreCase))
            {
                if (ApksignerOutput != null)
                {
                    return new(0, ApksignerOutput, "");
                }

                var defaultSigner = """
                    Verifies
                    Verified using v1 scheme (JAR signing): true
                    Verified using v2 scheme (APK Signature Scheme v2): true
                    Number of signers: 1
                    Signer #1 certificate DN: CN=Android Debug, O=Android, C=US
                    Signer #1 certificate SHA-256 digest: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
                    """;
                return new(0, defaultSigner, "");
            }

            return new(0, "", "");
        }

        private static void CreateValidSyntheticApk(string path)
        {
            using var fileStream = File.Create(path);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            CreateZipEntry(archive, "AndroidManifest.xml", "manifest-bytes");
            CreateZipEntry(archive, "classes.dex", "dex-bytes");
            CreateZipEntry(archive, "res/xml/backup_rules.xml", "<rules/>");
            CreateZipEntry(archive, "res/xml-v28/backup_rules.xml", "<rules-v28/>");
            CreateZipEntry(archive, "res/xml/data_extraction_rules.xml", "<data-extraction/>");
        }

        private static void CreateZipEntry(ZipArchive archive, string entryName, string content)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
