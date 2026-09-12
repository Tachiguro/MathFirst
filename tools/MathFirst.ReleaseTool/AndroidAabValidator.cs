namespace MathFirst.ReleaseTool;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public interface IValidationToolLocator
{
    string ResolveJava();
    string ResolveBundletoolJar();
    string ResolveJarsigner();
    string ResolveKeytool();
    string ResolveDexdump();
}

public sealed class DefaultValidationToolLocator : IValidationToolLocator
{
    public string ResolveJava() =>
        FindExecutable("java.exe", "JAVA_HOME", [
            @"C:\Program Files\Android\openjdk",
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Eclipse Adoptium"
        ]) ?? "java";

    public string ResolveBundletoolJar()
    {
        var env = Environment.GetEnvironmentVariable("BUNDLETOOL_JAR");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        var dotnetPacks = @"C:\Program Files\dotnet\packs\Microsoft.Android.Sdk.Windows";
        if (Directory.Exists(dotnetPacks))
        {
            var jars = Directory.GetFiles(dotnetPacks, "bundletool.jar", SearchOption.AllDirectories);
            if (jars.Length > 0)
            {
                Array.Sort(jars, StringComparer.OrdinalIgnoreCase);
                return jars[^1];
            }
        }

        return "bundletool.jar";
    }

    public string ResolveJarsigner() =>
        FindExecutable("jarsigner.exe", "JAVA_HOME", [
            @"C:\Program Files\Android\openjdk",
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Eclipse Adoptium"
        ]) ?? "jarsigner";

    public string ResolveKeytool() =>
        FindExecutable("keytool.exe", "JAVA_HOME", [
            @"C:\Program Files\Android\openjdk",
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Eclipse Adoptium"
        ]) ?? "keytool";

    public string ResolveDexdump() =>
        FindExecutable("dexdump.exe", "ANDROID_HOME", [
            @"C:\Program Files (x86)\Android\android-sdk\build-tools",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\build-tools")
        ]) ?? "dexdump";

    private static string? FindExecutable(string exeName, string envVar, string[] candidateRoots)
    {
        var envPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var direct = Path.Combine(envPath, "bin", exeName);
            if (File.Exists(direct)) return direct;
            var subDirect = Path.Combine(envPath, exeName);
            if (File.Exists(subDirect)) return subDirect;
        }

        foreach (var root in candidateRoots)
        {
            if (Directory.Exists(root))
            {
                var files = Directory.GetFiles(root, exeName, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    return files[^1];
                }
            }
        }

        return null;
    }
}

public sealed record ValidationRequest(
    string AabPath,
    string ProvenancePath,
    string ExpectedCommitSha,
    ReleaseProfile Profile,
    string? ExpectedDisplayVersion = null,
    int? ExpectedBuildNumber = null,
    string? ExpectedSignerCertificateSha256 = null,
    string? RepositoryRoot = null);

public sealed record ValidationResult(
    bool IsValid,
    ArtifactValidationStatus Status,
    ReleaseProfile Profile,
    bool IsDistributable,
    string ArtifactSha256,
    string SignerCertificateSha256,
    string SignerClassification,
    IReadOnlyList<string> Diagnostics);

public sealed class AndroidAabValidator(
    IProcessRunner processRunner,
    IValidationToolLocator? toolLocator = null)
{
    private static readonly Regex FullShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex FingerprintPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    private readonly IValidationToolLocator tools = toolLocator ?? new DefaultValidationToolLocator();

    public ValidationResult Validate(ValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<string>();

        // 1. Validate Input Paths and Request Parameters
        if (string.IsNullOrWhiteSpace(request.AabPath) || !File.Exists(request.AabPath))
        {
            throw new ReleaseToolException($"AAB file does not exist at '{request.AabPath}'.");
        }

        var aabFullPath = Path.GetFullPath(request.AabPath);
        if (!string.Equals(Path.GetExtension(aabFullPath), ".aab", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException($"Artifact file '{aabFullPath}' does not have the required .aab extension.");
        }

        var aabFileInfo = new FileInfo(aabFullPath);
        if (aabFileInfo.Length <= 0)
        {
            throw new ReleaseToolException($"AAB file '{aabFullPath}' is empty (0 bytes).");
        }

        if (string.IsNullOrWhiteSpace(request.ProvenancePath) || !File.Exists(request.ProvenancePath))
        {
            throw new ReleaseToolException($"Provenance metadata file does not exist at '{request.ProvenancePath}'.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedCommitSha) || !FullShaPattern.IsMatch(request.ExpectedCommitSha))
        {
            throw new ReleaseToolException("ExpectedCommitSha must be a full 40-character hexadecimal Git SHA.");
        }

        if (!Enum.IsDefined(request.Profile))
        {
            throw new ReleaseToolException($"Unsupported release profile '{request.Profile}'.");
        }

        string? normalizedExpectedSignerSha = null;
        if (request.Profile == ReleaseProfile.Distributable)
        {
            if (string.IsNullOrWhiteSpace(request.ExpectedSignerCertificateSha256))
            {
                throw new ReleaseToolException("Distributable validation requires an expected signer certificate SHA-256 fingerprint.");
            }

            var cleanFingerprint = request.ExpectedSignerCertificateSha256.Replace(":", string.Empty, StringComparison.Ordinal);
            if (!FingerprintPattern.IsMatch(cleanFingerprint))
            {
                throw new ReleaseToolException("Expected signer certificate SHA-256 must be exactly 64 hexadecimal characters.");
            }

            normalizedExpectedSignerSha = cleanFingerprint.ToLowerInvariant();
        }

        // Optional Git Repository State Validation
        if (!string.IsNullOrWhiteSpace(request.RepositoryRoot) && Directory.Exists(request.RepositoryRoot))
        {
            var inspector = new RepositoryInspector(processRunner);
            var snapshot = inspector.Read(request.RepositoryRoot);
            var packageRequest = new PackageRequest(
                request.Profile,
                request.ExpectedCommitSha,
                new VersionOverrides(request.ExpectedDisplayVersion, request.ExpectedBuildNumber?.ToString()),
                null);
            RepositoryPolicy.Validate(packageRequest, snapshot);
            diagnostics.Add("Repository state verified clean and synchronized.");
        }

        // 2. Load and Validate Mandatory Provenance Metadata
        var provenance = LoadAndValidateProvenance(request, aabFullPath, aabFileInfo.Length, normalizedExpectedSignerSha);
        diagnostics.Add("Mandatory provenance metadata schema and source invariants verified.");

        // 3. Compute Actual AAB SHA-256 and Verify Against Provenance
        var actualAabSha256 = ComputeSha256(aabFullPath);
        if (!string.Equals(actualAabSha256, provenance.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                $"AAB SHA-256 mismatch! Computed: '{actualAabSha256}', Provenance: '{provenance.Artifact.Sha256}'.");
        }

        diagnostics.Add($"AAB byte size ({aabFileInfo.Length}) and SHA-256 ({actualAabSha256}) verified.");

        // 4. bundletool Structural Validation
        ValidateWithBundletool(aabFullPath);
        diagnostics.Add("bundletool validate passed.");

        // 5. Binary Manifest Inspection
        ValidateBinaryManifest(aabFullPath, provenance);
        diagnostics.Add("Binary AndroidManifest verified (package, versions, SDKs, non-debuggable, offline permissions, backup wiring).");

        // 6. Resource and Backup-Rule Verification
        ValidateResources(aabFullPath);
        diagnostics.Add("Packaged backup policy resources verified in resource table and archive.");

        // 7. DEX Bytecode Validation with dexdump
        ValidateDexPayload(aabFullPath);
        diagnostics.Add("Packaged DEX bytecode validated with dexdump.");

        // 8. Cryptographic JAR Signature Verification
        var signatureInspector = new JarSignatureInspector(processRunner, tools);
        signatureInspector.VerifySignature(aabFullPath);
        diagnostics.Add("Cryptographic JAR/AAB signature verified.");

        // 9. Signer Certificate Extraction and Classification
        var signers = signatureInspector.ExtractSigners(aabFullPath);
        if (signers.Count == 0)
        {
            throw new ReleaseToolException("Cryptographic verification failed: no signer certificates found.");
        }

        if (signers.Count > 1)
        {
            throw new ReleaseToolException($"V1 release policy requires exactly one signer certificate, but found {signers.Count}.");
        }

        var signer = signers[0];
        diagnostics.Add($"Extracted signer certificate SHA-256: {signer.CertificateSha256} (Subject: {signer.Subject})");

        // 10. Enforce Profile-Specific Signer Policy
        string signerClassification;
        bool isDistributable;
        if (request.Profile == ReleaseProfile.SourceCandidate)
        {
            signerClassification = "development-debug";
            isDistributable = false;
            diagnostics.Add("SourceCandidate approved with development/debug signing (non-distributable).");
        }
        else if (request.Profile == ReleaseProfile.Distributable)
        {
            if (signer.IsAndroidDebug)
            {
                throw new ReleaseToolException("Distributable validation failed: artifact is signed with an Android Debug certificate.");
            }

            if (!string.Equals(signer.CertificateSha256, normalizedExpectedSignerSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new ReleaseToolException(
                    $"Distributable signer certificate mismatch! Expected: '{normalizedExpectedSignerSha}', Observed: '{signer.CertificateSha256}'.");
            }

            var now = DateTimeOffset.UtcNow;
            if (now < signer.NotBefore || now > signer.NotAfter)
            {
                throw new ReleaseToolException(
                    $"Signer certificate is not currently valid (valid from {signer.NotBefore:u} to {signer.NotAfter:u}).");
            }

            signerClassification = "release-distributable";
            isDistributable = true;
            diagnostics.Add("Distributable approved with authoritative release certificate matching expected fingerprint.");
        }
        else
        {
            throw new ReleaseToolException($"Unsupported release profile '{request.Profile}'.");
        }

        return new ValidationResult(
            true,
            ArtifactValidationStatus.ValidatorApproved,
            request.Profile,
            isDistributable,
            actualAabSha256,
            signer.CertificateSha256,
            signerClassification,
            diagnostics);
    }

    private static ArtifactProvenance LoadAndValidateProvenance(
        ValidationRequest request,
        string aabFullPath,
        long aabLength,
        string? expectedSignerSha)
    {
        string rawJson;
        try
        {
            rawJson = File.ReadAllText(request.ProvenancePath);
        }
        catch (Exception exception)
        {
            throw new ReleaseToolException($"Could not read provenance file at '{request.ProvenancePath}': {exception.Message}");
        }

        ArtifactProvenance provenance;
        try
        {
            provenance = JsonSerializer.Deserialize<ArtifactProvenance>(
                rawJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new JsonException("Deserialized provenance object is null.");
        }
        catch (JsonException exception)
        {
            throw new ReleaseToolException($"Provenance JSON is malformed or invalid: {exception.Message}");
        }

        if (provenance.SchemaVersion != 1)
        {
            throw new ReleaseToolException($"Unsupported provenance schema version '{provenance.SchemaVersion}'. Expected 1.");
        }

        if (provenance.Artifact is null ||
            provenance.Application is null ||
            provenance.Source is null ||
            provenance.Build is null ||
            provenance.Signing is null)
        {
            throw new ReleaseToolException("Provenance is missing one or more required sections.");
        }

        var expectedFileName = Path.GetFileName(aabFullPath);
        if (!string.Equals(provenance.Artifact.FileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance artifact file name mismatch! Expected '{expectedFileName}', got '{provenance.Artifact.FileName}'.");
        }

        if (provenance.Artifact.SizeBytes != aabLength)
        {
            throw new ReleaseToolException(
                $"Provenance artifact size mismatch! Expected {aabLength} bytes, provenance recorded {provenance.Artifact.SizeBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(provenance.Artifact.Sha256) || provenance.Artifact.Sha256.Length != 64)
        {
            throw new ReleaseToolException("Provenance artifact SHA-256 must be a 64-character hex string.");
        }

        if (!string.Equals(provenance.Source.ExpectedCommitSha, request.ExpectedCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                $"Provenance expected commit SHA '{provenance.Source.ExpectedCommitSha}' does not match requested SHA '{request.ExpectedCommitSha}'.");
        }

        if (!string.Equals(provenance.Source.CommitSha, request.ExpectedCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                $"Provenance commit SHA '{provenance.Source.CommitSha}' does not match requested SHA '{request.ExpectedCommitSha}'.");
        }

        if (!provenance.Source.WorkingTreeClean)
        {
            throw new ReleaseToolException("Provenance records a dirty working tree, violating release invariants.");
        }

        if (!string.Equals(provenance.Build.Configuration, ReleaseConstants.Configuration, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance build configuration must be '{ReleaseConstants.Configuration}', got '{provenance.Build.Configuration}'.");
        }

        if (!string.Equals(provenance.Build.TargetFramework, ReleaseConstants.TargetFramework, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance target framework must be '{ReleaseConstants.TargetFramework}', got '{provenance.Build.TargetFramework}'.");
        }

        var minSdk = provenance.Build.MinSdk?.Trim();
        var targetSdk = provenance.Build.TargetSdk?.Trim();
        if (minSdk != "24.0" && minSdk != "24")
        {
            throw new ReleaseToolException($"Provenance minSdk must be 24.0 or 24, got '{minSdk}'.");
        }

        if (targetSdk != "36.0" && targetSdk != "36")
        {
            throw new ReleaseToolException($"Provenance targetSdk must be 36.0 or 36, got '{targetSdk}'.");
        }

        if (!string.Equals(provenance.Application.Id, "com.tachiguro.mathfirst", StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance application ID mismatch! Expected 'com.tachiguro.mathfirst', got '{provenance.Application.Id}'.");
        }

        if (request.ExpectedDisplayVersion is not null &&
            !string.Equals(provenance.Application.DisplayVersion, request.ExpectedDisplayVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance display version '{provenance.Application.DisplayVersion}' does not match expected '{request.ExpectedDisplayVersion}'.");
        }

        if (request.ExpectedBuildNumber.HasValue && request.ExpectedBuildNumber.Value > 0 &&
            provenance.Application.BuildNumber != request.ExpectedBuildNumber.Value)
        {
            throw new ReleaseToolException(
                $"Provenance build number '{provenance.Application.BuildNumber}' does not match expected '{request.ExpectedBuildNumber}'.");
        }

        if (request.Profile == ReleaseProfile.SourceCandidate)
        {
            if (!string.Equals(provenance.Source.Classification, "source-candidate", StringComparison.Ordinal) ||
                !string.Equals(provenance.Artifact.Classification, "source-candidate-debug-signed", StringComparison.Ordinal))
            {
                throw new ReleaseToolException("SourceCandidate provenance must encode source-candidate classification.");
            }
        }
        else if (request.Profile == ReleaseProfile.Distributable)
        {
            if (!string.Equals(provenance.Source.Classification, "distributable", StringComparison.Ordinal) ||
                !string.Equals(provenance.Artifact.Classification, "distributable-release-signed-pending-validation", StringComparison.Ordinal))
            {
                throw new ReleaseToolException("Distributable provenance must encode distributable classification.");
            }

            if (!string.Equals(provenance.Source.Ref, "main", StringComparison.Ordinal))
            {
                throw new ReleaseToolException("Distributable provenance source ref must be 'main'.");
            }
        }

        return provenance;
    }

    private void ValidateWithBundletool(string aabPath)
    {
        var bundletoolJar = tools.ResolveBundletoolJar();
        var java = tools.ResolveJava();
        var invocation = new ProcessInvocation(
            java,
            ["-jar", bundletoolJar, "validate", $"--bundle={aabPath}"],
            Path.GetDirectoryName(aabPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        result.EnsureSuccess("bundletool validate");
    }

    private void ValidateBinaryManifest(string aabPath, ArtifactProvenance provenance)
    {
        var bundletoolJar = tools.ResolveBundletoolJar();
        var java = tools.ResolveJava();
        var invocation = new ProcessInvocation(
            java,
            ["-jar", bundletoolJar, "dump", "manifest", $"--bundle={aabPath}"],
            Path.GetDirectoryName(aabPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        result.EnsureSuccess("bundletool dump manifest");

        XDocument manifest;
        try
        {
            manifest = XDocument.Parse(result.StandardOutput);
        }
        catch (Exception exception)
        {
            throw new ReleaseToolException($"Failed to parse dumped AndroidManifest XML: {exception.Message}");
        }

        var root = manifest.Root ?? throw new ReleaseToolException("Dumped AndroidManifest XML has no root element.");
        var packageId = root.Attribute("package")?.Value;
        if (!string.Equals(packageId, "com.tachiguro.mathfirst", StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Manifest package ID mismatch: expected 'com.tachiguro.mathfirst', got '{packageId}'.");
        }

        var versionCode = root.Attribute(AndroidNs + "versionCode")?.Value ?? root.Attribute("versionCode")?.Value;
        if (!string.Equals(versionCode, provenance.Application.BuildNumber.ToString(), StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Manifest versionCode mismatch: expected '{provenance.Application.BuildNumber}', got '{versionCode}'.");
        }

        var versionName = root.Attribute(AndroidNs + "versionName")?.Value ?? root.Attribute("versionName")?.Value;
        if (!string.Equals(versionName, provenance.Application.DisplayVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Manifest versionName mismatch: expected '{provenance.Application.DisplayVersion}', got '{versionName}'.");
        }

        var usesSdk = root.Element("uses-sdk") ?? throw new ReleaseToolException("Manifest is missing required <uses-sdk> element.");
        var minSdkVersion = usesSdk.Attribute(AndroidNs + "minSdkVersion")?.Value ?? usesSdk.Attribute("minSdkVersion")?.Value;
        if (minSdkVersion != "24")
        {
            throw new ReleaseToolException($"Manifest minSdkVersion mismatch: expected '24', got '{minSdkVersion}'.");
        }

        var targetSdkVersion = usesSdk.Attribute(AndroidNs + "targetSdkVersion")?.Value ?? usesSdk.Attribute("targetSdkVersion")?.Value;
        if (targetSdkVersion != "36")
        {
            throw new ReleaseToolException($"Manifest targetSdkVersion mismatch: expected '36', got '{targetSdkVersion}'.");
        }

        // Offline-First Network Invariants (ADR-0002)
        var permissions = root.Elements("uses-permission")
            .Select(element => element.Attribute(AndroidNs + "name")?.Value ?? element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (permissions.Contains("android.permission.INTERNET", StringComparer.Ordinal))
        {
            throw new ReleaseToolException("Manifest security violation: forbidden permission 'android.permission.INTERNET' is declared.");
        }

        if (permissions.Contains("android.permission.ACCESS_NETWORK_STATE", StringComparer.Ordinal))
        {
            throw new ReleaseToolException("Manifest security violation: forbidden permission 'android.permission.ACCESS_NETWORK_STATE' is declared.");
        }

        // Application Element Invariants
        var applicationElements = root.Elements("application").ToList();
        if (applicationElements.Count != 1)
        {
            throw new ReleaseToolException($"Manifest must contain exactly one <application> element, but found {applicationElements.Count}.");
        }

        var app = applicationElements[0];
        var debuggable = app.Attribute(AndroidNs + "debuggable")?.Value ?? app.Attribute("debuggable")?.Value;
        if (string.Equals(debuggable, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("Manifest security violation: android:debuggable is 'true' in release package.");
        }

        var allowBackup = app.Attribute(AndroidNs + "allowBackup")?.Value ?? app.Attribute("allowBackup")?.Value;
        if (!string.Equals(allowBackup, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException($"Manifest backup policy mismatch: android:allowBackup must be 'true', got '{allowBackup}'.");
        }

        var fullBackupContent = app.Attribute(AndroidNs + "fullBackupContent")?.Value ?? app.Attribute("fullBackupContent")?.Value;
        if (!string.Equals(fullBackupContent, "@xml/backup_rules", StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Manifest backup policy mismatch: android:fullBackupContent must be '@xml/backup_rules', got '{fullBackupContent}'.");
        }

        var dataExtractionRules = app.Attribute(AndroidNs + "dataExtractionRules")?.Value ?? app.Attribute("dataExtractionRules")?.Value;
        if (!string.Equals(dataExtractionRules, "@xml/data_extraction_rules", StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Manifest backup policy mismatch: android:dataExtractionRules must be '@xml/data_extraction_rules', got '{dataExtractionRules}'.");
        }
    }

    private void ValidateResources(string aabPath)
    {
        var bundletoolJar = tools.ResolveBundletoolJar();
        var java = tools.ResolveJava();
        var invocation = new ProcessInvocation(
            java,
            ["-jar", bundletoolJar, "dump", "resources", $"--bundle={aabPath}"],
            Path.GetDirectoryName(aabPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        result.EnsureSuccess("bundletool dump resources");

        var output = result.StandardOutput;
        if (!output.Contains("backup_rules", StringComparison.Ordinal) ||
            !output.Contains("data_extraction_rules", StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Resource dump did not contain required backup rule resources.");
        }

        // In addition, verify physical entries in the AAB archive
        using var archive = ZipFile.OpenRead(aabPath);
        var entryNames = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToList();

        var hasBaseBackupRules = entryNames.Any(entry =>
            entry.Equals("base/res/xml/backup_rules.xml", StringComparison.Ordinal) ||
            (entry.StartsWith("base/res/xml", StringComparison.Ordinal) && entry.EndsWith("/backup_rules.xml", StringComparison.Ordinal) && !entry.Contains("-v28")));
        var hasV28BackupRules = entryNames.Any(entry =>
            entry.Equals("base/res/xml-v28/backup_rules.xml", StringComparison.Ordinal) ||
            (entry.StartsWith("base/res/xml-v28", StringComparison.Ordinal) && entry.EndsWith("/backup_rules.xml", StringComparison.Ordinal)));
        var hasDataExtractionRules = entryNames.Any(entry =>
            entry.Equals("base/res/xml/data_extraction_rules.xml", StringComparison.Ordinal) ||
            (entry.StartsWith("base/res/xml", StringComparison.Ordinal) && entry.EndsWith("/data_extraction_rules.xml", StringComparison.Ordinal)));

        if (!hasBaseBackupRules)
        {
            throw new ReleaseToolException("Packaged AAB is missing base 'backup_rules.xml' resource.");
        }

        if (!hasV28BackupRules)
        {
            throw new ReleaseToolException("Packaged AAB is missing API 28 'xml-v28/backup_rules.xml' resource.");
        }

        if (!hasDataExtractionRules)
        {
            throw new ReleaseToolException("Packaged AAB is missing API 31+ 'data_extraction_rules.xml' resource.");
        }
    }

    private void ValidateDexPayload(string aabPath)
    {
        using var archive = ZipFile.OpenRead(aabPath);
        var dexEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("base/dex/", StringComparison.Ordinal) &&
                            entry.FullName.EndsWith(".dex", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (dexEntries.Count == 0)
        {
            throw new ReleaseToolException("Packaged AAB contains no DEX bytecode in 'base/dex/'.");
        }

        var tempDexDir = Path.Combine(Path.GetTempPath(), $"mathfirst-val-dex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDexDir);
        try
        {
            var dexdump = tools.ResolveDexdump();
            foreach (var dexEntry in dexEntries)
            {
                var fileName = Path.GetFileName(dexEntry.FullName);
                if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
                {
                    throw new ReleaseToolException($"Invalid DEX entry path in archive: '{dexEntry.FullName}'.");
                }

                var targetPath = Path.Combine(tempDexDir, fileName);
                dexEntry.ExtractToFile(targetPath, overwrite: true);

                var invocation = new ProcessInvocation(
                    dexdump,
                    ["-f", targetPath],
                    tempDexDir);

                var result = processRunner.Run(invocation);
                if (result.ExitCode != 0)
                {
                    var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                    throw new ReleaseToolException($"dexdump failed for DEX entry '{fileName}' with exit code {result.ExitCode}: {error.Trim()}");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDexDir))
            {
                Directory.Delete(tempDexDir, recursive: true);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
