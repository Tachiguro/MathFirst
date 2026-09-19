namespace MathFirst.ReleaseTool;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class AndroidApkValidator(
    IProcessRunner processRunner,
    IValidationToolLocator? toolLocator = null)
{
    private static readonly Regex FullShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex DexPattern = new(@"^classes(?:\d+)?\.dex$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IValidationToolLocator tools = toolLocator ?? new DefaultValidationToolLocator();

    public ValidationResult Validate(ApkValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<string>();

        // 1. Validate Input Paths and Request Parameters
        if (request.Profile != ReleaseProfile.Tester)
        {
            throw new ReleaseToolException($"Unsupported release profile '{request.Profile}'. AndroidApkValidator only supports Tester profile.");
        }

        if (string.IsNullOrWhiteSpace(request.ApkPath) || !File.Exists(request.ApkPath))
        {
            throw new ReleaseToolException($"APK file does not exist at '{request.ApkPath}'.");
        }

        var apkFullPath = Path.GetFullPath(request.ApkPath);
        if (!string.Equals(Path.GetExtension(apkFullPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException($"Artifact file '{apkFullPath}' does not have the required .apk extension.");
        }

        var apkFileInfo = new FileInfo(apkFullPath);
        if (apkFileInfo.Length <= 0)
        {
            throw new ReleaseToolException($"APK file '{apkFullPath}' is empty (0 bytes).");
        }

        if (string.IsNullOrWhiteSpace(request.ProvenancePath) || !File.Exists(request.ProvenancePath))
        {
            throw new ReleaseToolException($"Provenance metadata file does not exist at '{request.ProvenancePath}'.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedCommitSha) || !FullShaPattern.IsMatch(request.ExpectedCommitSha))
        {
            throw new ReleaseToolException("ExpectedCommitSha must be a full 40-character hexadecimal Git SHA.");
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
        var provenance = LoadAndValidateProvenance(request, apkFullPath, apkFileInfo.Length);
        diagnostics.Add("Mandatory provenance metadata schema and source invariants verified.");

        // 3. Compute Actual APK SHA-256 and Verify Against Provenance
        var actualApkSha256 = ComputeSha256(apkFullPath);
        if (!string.Equals(actualApkSha256, provenance.Artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                $"APK SHA-256 mismatch! Computed: '{actualApkSha256}', Provenance: '{provenance.Artifact.Sha256}'.");
        }

        diagnostics.Add($"APK byte size ({apkFileInfo.Length}) and SHA-256 ({actualApkSha256}) verified.");

        // 4. Archive Structure and Packaged Backup Resources
        ValidateZipStructureAndBackupResources(apkFullPath);
        diagnostics.Add("APK archive structure and packaged backup resources verified.");

        // 5. Binary Manifest Inspection with aapt2
        ValidateBinaryManifestWithAapt2(apkFullPath, provenance);
        diagnostics.Add("Binary AndroidManifest verified via aapt2 (package, versions, SDKs, non-debuggable, offline permissions, backup wiring).");

        // 6. DEX Bytecode Validation with dexdump
        ValidateDexPayload(apkFullPath);
        diagnostics.Add("Packaged DEX bytecode validated with dexdump.");

        // 7. Cryptographic Signature Verification with apksigner
        var signer = VerifyApkSignatureAndExtractSigner(apkFullPath);
        diagnostics.Add($"APK signature verified with apksigner: {signer.CertificateSha256} (Subject: {signer.Subject})");

        // 8. Signer Classification
        const string signerClassification = "development-debug";
        diagnostics.Add("Tester APK approved with development/debug signing (non-distributable).");

        return new ValidationResult(
            true,
            ArtifactValidationStatus.ValidatorApproved,
            ReleaseProfile.Tester,
            false,
            actualApkSha256,
            signer.CertificateSha256,
            signerClassification,
            diagnostics);
    }

    private static ArtifactProvenance LoadAndValidateProvenance(
        ApkValidationRequest request,
        string apkFullPath,
        long apkLength)
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

        var expectedFileName = Path.GetFileName(apkFullPath);
        if (!string.Equals(provenance.Artifact.FileName, expectedFileName, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance artifact file name mismatch! Expected '{expectedFileName}', got '{provenance.Artifact.FileName}'.");
        }

        if (provenance.Artifact.SizeBytes != apkLength)
        {
            throw new ReleaseToolException(
                $"Provenance artifact size mismatch! Expected {apkLength} bytes, provenance recorded {provenance.Artifact.SizeBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(provenance.Artifact.Sha256) || !Sha256Pattern.IsMatch(provenance.Artifact.Sha256))
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

        if (!string.Equals(provenance.Application.Id, ReleaseConstants.TesterApplicationId, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"Provenance application ID mismatch! Expected '{ReleaseConstants.TesterApplicationId}', got '{provenance.Application.Id}'.");
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

        if (!string.Equals(provenance.Source.Classification, ReleaseConstants.TesterSourceClassification, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Tester provenance must encode '{ReleaseConstants.TesterSourceClassification}' source classification, got '{provenance.Source.Classification}'.");
        }

        if (!string.Equals(provenance.Artifact.Classification, ReleaseConstants.TesterArtifactClassification, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Tester provenance must encode '{ReleaseConstants.TesterArtifactClassification}' artifact classification, got '{provenance.Artifact.Classification}'.");
        }

        if (!string.Equals(provenance.Signing.State, ReleaseConstants.TesterSigningState, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Tester provenance must encode '{ReleaseConstants.TesterSigningState}' signing state, got '{provenance.Signing.State}'.");
        }

        return provenance;
    }

    private static void ValidateZipStructureAndBackupResources(string apkPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(apkPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ReleaseToolException($"Failed to open APK archive at '{apkPath}': {exception.Message}");
        }

        using (archive)
        {
            var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

            var hasManifest = entryNames.Any(e => e.Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase));
            if (!hasManifest)
            {
                throw new ReleaseToolException("Packaged APK is missing root 'AndroidManifest.xml'.");
            }

            var hasDex = entryNames.Any(e => DexPattern.IsMatch(e));
            if (!hasDex)
            {
                throw new ReleaseToolException("Packaged APK contains no root classes*.dex bytecode entries.");
            }

            var hasBaseBackupRules = entryNames.Any(entry =>
                entry.Equals("res/xml/backup_rules.xml", StringComparison.Ordinal) ||
                (entry.StartsWith("res/xml", StringComparison.Ordinal) && entry.EndsWith("/backup_rules.xml", StringComparison.Ordinal) && !entry.Contains("-v28")));
            var hasV28BackupRules = entryNames.Any(entry =>
                entry.Equals("res/xml-v28/backup_rules.xml", StringComparison.Ordinal) ||
                (entry.StartsWith("res/xml-v28", StringComparison.Ordinal) && entry.EndsWith("/backup_rules.xml", StringComparison.Ordinal)));
            var hasDataExtractionRules = entryNames.Any(entry =>
                entry.Equals("res/xml/data_extraction_rules.xml", StringComparison.Ordinal) ||
                (entry.StartsWith("res/xml", StringComparison.Ordinal) && entry.EndsWith("/data_extraction_rules.xml", StringComparison.Ordinal)));

            if (!hasBaseBackupRules)
            {
                throw new ReleaseToolException("Packaged APK is missing base 'backup_rules.xml' resource.");
            }

            if (!hasV28BackupRules)
            {
                throw new ReleaseToolException("Packaged APK is missing API 28 'xml-v28/backup_rules.xml' resource.");
            }

            if (!hasDataExtractionRules)
            {
                throw new ReleaseToolException("Packaged APK is missing API 31+ 'data_extraction_rules.xml' resource.");
            }
        }
    }

    private void ValidateBinaryManifestWithAapt2(string apkPath, ArtifactProvenance provenance)
    {
        var aapt2 = tools.ResolveAapt2();
        var invocation = new ProcessInvocation(
            aapt2,
            ["dump", "xmltree", "--file", "AndroidManifest.xml", apkPath],
            Path.GetDirectoryName(apkPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        result.EnsureSuccess("aapt2 dump xmltree");

        var output = result.StandardOutput;

        // 1. Package ID
        var packageMatch = Regex.Match(output, @"\bpackage=""([^""]+)""", RegexOptions.CultureInvariant);
        if (!packageMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose manifest package ID.");
        }

        var packageId = packageMatch.Groups[1].Value;
        if (!string.Equals(packageId, ReleaseConstants.TesterApplicationId, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Manifest package ID mismatch: expected '{ReleaseConstants.TesterApplicationId}', got '{packageId}'.");
        }

        // 2. Version Code
        var versionCodeMatch = Regex.Match(output, @"\bversionCode(?:\([0-9a-fxA-FX]+\))?=(\d+)", RegexOptions.CultureInvariant);
        if (!versionCodeMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose manifest versionCode.");
        }

        var versionCode = versionCodeMatch.Groups[1].Value;
        if (!string.Equals(versionCode, provenance.Application.BuildNumber.ToString(), StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Manifest versionCode mismatch: expected '{provenance.Application.BuildNumber}', got '{versionCode}'.");
        }

        // 3. Version Name
        var versionNameMatch = Regex.Match(output, @"\bversionName(?:\([0-9a-fxA-FX]+\))?=""([^""]+)""", RegexOptions.CultureInvariant);
        if (!versionNameMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose manifest versionName.");
        }

        var versionName = versionNameMatch.Groups[1].Value;
        if (!string.Equals(versionName, provenance.Application.DisplayVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"Manifest versionName mismatch: expected '{provenance.Application.DisplayVersion}', got '{versionName}'.");
        }

        // 4. uses-sdk
        var minSdkMatch = Regex.Match(output, @"\bminSdkVersion(?:\([0-9a-fxA-FX]+\))?=(\d+)", RegexOptions.CultureInvariant);
        if (!minSdkMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose manifest minSdkVersion.");
        }

        if (minSdkMatch.Groups[1].Value != "24")
        {
            throw new ReleaseToolException($"Manifest minSdkVersion mismatch: expected '24', got '{minSdkMatch.Groups[1].Value}'.");
        }

        var targetSdkMatch = Regex.Match(output, @"\btargetSdkVersion(?:\([0-9a-fxA-FX]+\))?=(\d+)", RegexOptions.CultureInvariant);
        if (!targetSdkMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose manifest targetSdkVersion.");
        }

        if (targetSdkMatch.Groups[1].Value != "36")
        {
            throw new ReleaseToolException($"Manifest targetSdkVersion mismatch: expected '36', got '{targetSdkMatch.Groups[1].Value}'.");
        }

        // 5. Offline-First Network Permissions
        var permissionMatches = Regex.Matches(output, @"E:\s*uses-permission[^\n]*\r?\n(?:\s+A:[^\n]*\r?\n)*", RegexOptions.CultureInvariant);
        var permissions = new List<string>();
        foreach (Match permMatch in permissionMatches)
        {
            var nameMatch = Regex.Match(permMatch.Value, @"\bname(?:\([0-9a-fxA-FX]+\))?=""([^""]+)""", RegexOptions.CultureInvariant);
            if (nameMatch.Success)
            {
                permissions.Add(nameMatch.Groups[1].Value);
            }
        }

        if (permissions.Contains("android.permission.INTERNET", StringComparer.Ordinal) ||
            output.Contains("\"android.permission.INTERNET\"", StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Manifest security violation: forbidden permission 'android.permission.INTERNET' is declared.");
        }

        if (permissions.Contains("android.permission.ACCESS_NETWORK_STATE", StringComparer.Ordinal) ||
            output.Contains("\"android.permission.ACCESS_NETWORK_STATE\"", StringComparison.Ordinal))
        {
            throw new ReleaseToolException("Manifest security violation: forbidden permission 'android.permission.ACCESS_NETWORK_STATE' is declared.");
        }

        // 6. Application Debuggable
        var appMatch = Regex.Match(output, @"E:\s*application[^\n]*\r?\n(?:\s+A:[^\n]*\r?\n)*", RegexOptions.CultureInvariant);
        if (!appMatch.Success)
        {
            throw new ReleaseToolException("aapt2 dump xmltree did not expose <application> element.");
        }

        var appBlock = appMatch.Value;
        var debuggableMatch = Regex.Match(appBlock, @"\bdebuggable(?:\([0-9a-fxA-FX]+\))?=(true|0xffffffff|\(type 0x12\)0xffffffff)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (debuggableMatch.Success)
        {
            throw new ReleaseToolException("Manifest security violation: android:debuggable is 'true' in release package.");
        }

        // 7. Backup Policy Wiring
        var allowBackupMatch = Regex.Match(appBlock, @"\ballowBackup(?:\([0-9a-fxA-FX]+\))?=(true|0xffffffff|\(type 0x12\)0xffffffff)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!allowBackupMatch.Success)
        {
            throw new ReleaseToolException("Manifest backup policy mismatch: android:allowBackup must be 'true'.");
        }

        var fullBackupMatch = Regex.Match(appBlock, @"\bfullBackupContent(?:\([0-9a-fxA-FX]+\))?=(@0x[0-9a-fA-F]+|""@xml/backup_rules"")", RegexOptions.CultureInvariant);
        if (!fullBackupMatch.Success)
        {
            throw new ReleaseToolException("Manifest backup policy mismatch: android:fullBackupContent must reference '@xml/backup_rules'.");
        }

        var dataExtractionMatch = Regex.Match(appBlock, @"\bdataExtractionRules(?:\([0-9a-fxA-FX]+\))?=(@0x[0-9a-fA-F]+|""@xml/data_extraction_rules"")", RegexOptions.CultureInvariant);
        if (!dataExtractionMatch.Success)
        {
            throw new ReleaseToolException("Manifest backup policy mismatch: android:dataExtractionRules must reference '@xml/data_extraction_rules'.");
        }
    }

    private void ValidateDexPayload(string apkPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(apkPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ReleaseToolException($"Failed to open APK archive at '{apkPath}': {exception.Message}");
        }

        using (archive)
        {
            var dexEntries = archive.Entries
                .Where(entry => DexPattern.IsMatch(entry.FullName))
                .ToList();

            if (dexEntries.Count == 0)
            {
                throw new ReleaseToolException("Packaged APK contains no root DEX bytecode files.");
            }

            var tempDexDir = Path.Combine(Path.GetTempPath(), $"mathfirst-apk-val-dex-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDexDir);
            try
            {
                var dexdump = tools.ResolveDexdump();
                foreach (var dexEntry in dexEntries)
                {
                    var fileName = Path.GetFileName(dexEntry.FullName);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || !string.IsNullOrEmpty(Path.GetDirectoryName(dexEntry.FullName)))
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
    }

    private SignerInfo VerifyApkSignatureAndExtractSigner(string apkPath)
    {
        var apksigner = tools.ResolveApksigner();
        var invocation = new ProcessInvocation(
            apksigner,
            ["verify", "--verbose", "--print-certs", apkPath],
            Path.GetDirectoryName(apkPath) ?? Environment.CurrentDirectory);

        var result = processRunner.Run(invocation);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new ReleaseToolException($"apksigner verification failed (exit code {result.ExitCode}): {error.Trim()}");
        }

        var output = result.StandardOutput;
        if (output.Contains("DOES NOT VERIFY", StringComparison.OrdinalIgnoreCase) ||
            !output.Contains("Verifies", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException("apksigner verification failed: APK does not verify.");
        }

        var signersCountMatch = Regex.Match(output, @"Number of signers:\s*(\d+)", RegexOptions.CultureInvariant);
        if (!signersCountMatch.Success)
        {
            throw new ReleaseToolException("apksigner verification failed: could not determine number of signers.");
        }

        var signersCount = int.Parse(signersCountMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (signersCount == 0)
        {
            throw new ReleaseToolException("apksigner verification failed: no signers found.");
        }

        if (signersCount > 1)
        {
            throw new ReleaseToolException($"V1 tester release policy requires exactly one signer certificate, but found {signersCount}.");
        }

        var dnMatch = Regex.Match(output, @"Signer #1 certificate DN:\s*(.+)", RegexOptions.CultureInvariant);
        if (!dnMatch.Success || string.IsNullOrWhiteSpace(dnMatch.Groups[1].Value))
        {
            throw new ReleaseToolException("apksigner verification failed: could not parse signer certificate distinguished name.");
        }
        var dn = dnMatch.Groups[1].Value.Trim();

        var sha256Match = Regex.Match(output, @"Signer #1 certificate SHA-256 digest:\s*([0-9a-fA-F]{64})", RegexOptions.CultureInvariant);
        if (!sha256Match.Success)
        {
            throw new ReleaseToolException("apksigner verification failed: could not parse valid SHA-256 certificate digest.");
        }
        var sha256 = sha256Match.Groups[1].Value.ToLowerInvariant();

        var isDebug = dn.Contains("Android Debug", StringComparison.OrdinalIgnoreCase) ||
                      dn.Contains("CN=Android Debug", StringComparison.OrdinalIgnoreCase) ||
                      dn.Contains("O=Android", StringComparison.OrdinalIgnoreCase);

        if (!isDebug)
        {
            throw new ReleaseToolException("Tester validation failed: artifact is signed with a non-debug certificate.");
        }

        return new SignerInfo(sha256, dn, dn, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, isDebug);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
