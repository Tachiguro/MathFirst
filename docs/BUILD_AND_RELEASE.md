# MathFirst Build and Release Governance

This document defines the lifecycle boundaries and invariant rules governing compilation, packaging, artifact signing, distribution, and release for MathFirst.

---

## 1. Lifecycle Boundary Taxonomy

To maintain safety and prevent premature assumptions, MathFirst strictly isolates the following ten distinct lifecycle stages. **No stage may be inferred from another.**

```
[1. Source Commit]
    └── [2. Source Merge]
            └── [3. Build]
                    └── [4. Package]
                            └── [5. Signing]
                                    └── [6. Upload]
                                            └── [7. Distribution]
                                                    └── [8. Installation]
                                                            └── [9. Manual Verification]
                                                                    └── [10. Production Release]
```

### Boundary Definitions
1. **Source Commit**: Recording verified changes on a local or remote Git branch. Does not imply integration into `main`.
2. **Source Merge**: Merging an approved PR into the default branch (`main`). Does not compile binaries or publish artifacts.
3. **Build**: Compiling source code, running type checks, and generating raw build outputs.
   - *Boundary Invariant*: Build does **not** prove behavioral correctness, does **not** imply automated test execution, does **not** assemble packages, and does **not** imply installation or release. Testing and Full Validation are distinct lifecycle gates.
4. **Package**: Assembling compiled outputs and assets into distributable formats (e.g. installers, archives, manifests). Does not imply binary signing or publication.
5. **Signing**: Cryptographically signing binaries or packages using official certificates.
6. **Upload**: Transferring signed artifacts to a staging or distribution repository.
7. **Distribution**: Making uploaded artifacts available across target distribution channels.
8. **Installation**: Deploying or installing artifacts into a target test or staging environment.
9. **Manual Verification**: Human validation of installed software on target hardware or environments.
10. **Production Release**: Declaring a specific package version officially available for general use.

---

## 2. Invariant Rules for Release Operations

1. **Explicit Authorization Required**: Code signing, store uploads, production deployments, and release creation are **non-delegable operations** requiring explicit user confirmation.
2. **Platform-Neutral Governance**: Specific compiler toolchains, packaging scripts, and distribution platforms will be documented once the technology stack and target platforms are chosen in accordance with [docs/ROADMAP.md](ROADMAP.md).
3. **Traceability**: Every distributable artifact must be traceable to an exact immutable Git commit SHA on `main`.

## 3. MF-UX-003 Identity and Version Prerequisites

The canonical semantic release inputs are `ApplicationDisplayVersion` and `ApplicationVersion`, defined in `src/MathFirst.App/MathFirst.App.csproj`. Their current values are `1.0` and `1`. The stable application identifier is `com.tachiguro.mathfirst`.

MF-UX-003 provides the later packaging prerequisites: the MathFirst label, stable ApplicationId, canonical version/build inputs, final native app icon, adaptive icon, and splash identity. The package itself does not package, sign, upload, distribute, install, or publish an artifact.

---

## 4. Android Packaging Automation & Release Governance (MF-REL-001)

[ADR-0006](decisions/ADR-0006-android-packaging-signing-and-manifest-release-security.md) establishes the official packaging, signing, and release automation architecture for Android.

### A. Release Architecture & Tooling

The substantive release, packaging, signing policy, repository inspection, provenance generation, and validation logic is implemented in `tools/MathFirst.ReleaseTool`.

The repository provides thin PowerShell entry point wrappers:
- `scripts/package-android-aab.ps1`: orchestrates MSBuild property evaluation, `dotnet publish`, provenance emission, and atomic artifact promotion.
- `scripts/validate-android-aab.ps1`: invokes the offline validator for structural, manifest, DEX bytecode, cryptographic signature, certificate chain, and provenance inspection.

### B. Release Profiles

MathFirst release tooling strictly isolates two release profiles:

| Profile | Target Branch | Git Baseline Requirement | Signing Mode | Promotion Target | Distribution Status |
|---|---|---|---|---|---|
| **`SourceCandidate`** | `feat/mf-rel-001-android-aab-packaging` | Clean working tree, clean index, zero untracked files, exact full SHA == `HEAD` | Development/debug signed (`-p:AndroidKeyStore=false`) | `artifacts/android/source-candidate/<ArtifactId>/` | **Non-distributable** (development & validation only) |
| **`Distributable`** | `main` | Clean working tree, clean index, zero untracked files, exact full SHA == `HEAD` == `local main` == `origin/main` | Production/release keystore (`-p:AndroidKeyStore=true`) with external secret files | `artifacts/android/distributable/<ArtifactId>/` | **Distributable** (validated release candidate; does not imply upload) |

### C. Android Platform Contract & Manifest Security

- **Target Framework**: `net10.0-android36.0` explicitly pins Android API 36 as the compile and target platform.
- **Minimum SDK**: `SupportedOSPlatformVersion` is pinned to `24.0` (Android 7.0 Nougat).
- **Application ID**: `com.tachiguro.mathfirst`.
- **Offline Invariant**: `src/MathFirst.App/Platforms/Android/AndroidManifest.xml` explicitly omits all network permissions (`android.permission.INTERNET`, `android.permission.ACCESS_NETWORK_STATE`). `BlazorWebView` assets are loaded locally from the packaged application.
- **Multi-Generation Backup Boundary**:
  - `android:allowBackup="true"` is enabled in `AndroidManifest.xml` to allow controlled device-to-device data migration.
  - **API 24–27** (`backup_rules.xml`): Full deny-all fallback (`<exclude domain="..." path="." />` across all 9 supported storage domains) because Android 7.0–8.1 cannot reliably isolate device transfer from cloud Auto Backup.
  - **API 28–30** (`xml-v28/backup_rules.xml`): Device-to-device transfer only (`requireFlags="deviceToDeviceTransfer"`) exclusively for the three approved learner SQLite files (`mathfirst_learner.db`, `mathfirst_learner.db-wal`, `mathfirst_learner.db-shm`).
  - **API 31+** (`xml/data_extraction_rules.xml`): Cloud backup is fully denied (`<cloud-backup>` excludes all domains); device transfer (`<device-transfer>`) allows only the three approved learner SQLite files.
  - *Transport Note*: Device-to-device migration depends on Android OS and OEM transport capabilities; migration cannot be guaranteed by configuration alone.

### D. Signing Secret Handling

- **No Plaintext Passwords**: Passwords and secret values are never passed on the command line or printed to logs.
- **File Indirection**: The canonical `Distributable` packaging path uses .NET Android file indirection:
  ```
  -p:AndroidSigningStorePass=file:<path-to-store-password-file>
  -p:AndroidSigningKeyPass=file:<path-to-key-password-file>
  ```
- **External Secret Isolation**: Keystores and password files must reside outside the repository root and outside `artifacts/`. The tool verifies absolute paths and rejects secret paths that traverse filesystem reparse points.
- **Repository Hygiene**: `.gitignore` strictly ignores keystores (`*.keystore`, `*.jks`, `*.p12`, `*.pfx`), secret files (`signing.properties`, `.env`), and release output directories (`artifacts/`, `*.aab`, `*.apk`).
- *Secret Risk Baseline*: While file indirection eliminates plaintext CLI exposure, operators remain responsible for external key storage and credential lifecycle management.

### E. Mandatory Provenance Schema v1

Every packaged artifact is accompanied by a `<ArtifactId>.provenance.json` metadata record (Schema Version 1) capturing:
- **Artifact**: File name, classification (`source-candidate-debug-signed` or `distributable-release-signed-pending-validation`), size in bytes, and lowercase SHA-256 hash.
- **Application**: Application ID (`com.tachiguro.mathfirst`), display version, and build number.
- **Source**: Expected commit SHA, actual `HEAD` commit SHA, branch ref, source classification, and clean working tree status.
- **Build**: Configuration (`Release`), target framework (`net10.0-android36.0`), minimum SDK (`24.0`), target SDK (`36.0`), requested version overrides, and tool versions (`dotnetSdk`, `msbuild`, `androidNetSdk`).
- **Signing**: Signing state (`development-debug` or `release-expected-pending-validation`), expected certificate fingerprint, and validated certificate fingerprint.
- **Timestamp**: Exact UTC generation timestamp (`generatedAtUtc`).

*Provenance Integrity*: Provenance JSON serves as structured evidence metadata, not cryptographic proof or self-attestation. The offline validator independently recomputes the artifact SHA-256 and verifies all properties against the bundle bytes.

### F. Packaging Automation (`scripts/package-android-aab.ps1`)

**Syntax**:
```powershell
pwsh -File scripts/package-android-aab.ps1 `
    -Profile <SourceCandidate|Distributable> `
    -ExpectedCommitSha <40-character-git-sha> `
    [-DisplayVersion <version-override>] `
    [-BuildNumber <build-number-override>] `
    [-KeystorePath <path-to-external-keystore>] `
    [-KeyAlias <key-alias>] `
    [-StorePasswordFile <path-to-store-pass-file>] `
    [-KeyPasswordFile <path-to-key-pass-file>] `
    [-ExpectedSignerCertificateSha256 <64-hex-fingerprint>]
```

**Example (SourceCandidate)**:
```powershell
pwsh -File scripts/package-android-aab.ps1 `
    -Profile SourceCandidate `
    -ExpectedCommitSha <EXACT_HEAD_SHA>
```

**Deterministic Output Format**:
`MathFirst-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-{Classification}.aab`

### G. Offline Local Artifact Validation (`scripts/validate-android-aab.ps1`)

**Syntax**:
```powershell
pwsh -File scripts/validate-android-aab.ps1 `
    -AabPath <path-to-aab> `
    -ProvenancePath <path-to-provenance-json> `
    -ExpectedCommitSha <40-character-git-sha> `
    -Profile <SourceCandidate|Distributable> `
    [-ExpectedDisplayVersion <version>] `
    [-ExpectedBuildNumber <build>] `
    [-ExpectedSignerCertificateSha256 <64-hex-fingerprint>] `
    [-RepositoryRoot <path-to-repo-root>]
```

**Authoritative Validation Gates**:
1. **Path & Extension**: Asserts `.aab` and `.provenance.json` exist and are non-empty; verifies 40-character hexadecimal commit SHA.
2. **Provenance Conformance**: Verifies Schema Version 1, recomputes artifact SHA-256, verifies exact byte size, and matches application/build/source fields.
3. **Bundletool Structural Validation**: Executes `bundletool validate --bundle=<aab>`.
4. **Binary Manifest Inspection**: Executes `bundletool dump manifest --bundle=<aab>` and asserts:
   - `package == "com.tachiguro.mathfirst"`
   - `versionCode` and `versionName` match provenance
   - `minSdkVersion == "24"` and `targetSdkVersion == "36"`
   - `android:debuggable != "true"` (omitted or false in Release)
   - Zero network permissions (absence of `INTERNET` and `ACCESS_NETWORK_STATE`)
   - Backup wiring: `android:allowBackup="true"`, `android:fullBackupContent="@xml/backup_rules"`, `android:dataExtractionRules="@xml/data_extraction_rules"`
5. **Resource Verification**: Runs `bundletool dump resources` and inspects archive zip entries for `backup_rules.xml`, `xml-v28/backup_rules.xml`, and `data_extraction_rules.xml`.
6. **DEX Bytecode Validation**: Extracts `.dex` entries from `base/dex/` into an isolated temporary directory and executes `dexdump -f` on each DEX payload.
7. **Cryptographic Signature Verification**: Runs `jarsigner -verify <aab>`. Standard Android self-signed certificate, trust-path, and timestamp warnings are accepted as non-fatal warnings; unsigned bundles or cryptographic signature failures fail validation.
8. **Signer Certificate Extraction & Policy**: Invokes `keytool -printcert -jarfile <aab> -rfc` to extract X.509 certificates:
   - Enforces the V1 exactly-one-independent-signer policy.
   - Extracts the leaf certificate SHA-256 fingerprint when certificate chains are present.
   - `SourceCandidate`: Accepts Android Debug certificate (classification: `development-debug`, non-distributable).
   - `Distributable`: Rejects Android Debug signers, verifies the certificate is within its valid date range (`NotBefore`..`NotAfter`), matches the 64-hexadecimal `ExpectedSignerCertificateSha256` fingerprint, and approves classification `release-distributable`.

### H. Artifact Workspace & Promotion

- **Fixed Destination Hierarchy**: `artifacts/android/source-candidate/<ArtifactId>/` and `artifacts/android/distributable/<ArtifactId>/`.
- **Isolated Staging**: Packaging occurs in an invocation-unique directory under `artifacts/android/.staging/<InvocationGuid>/`.
- **Single-AAB Selection**: Selects exactly one `.aab` produced in the publish output; rejects ambiguous multiple outputs.
- **No Overwrite / Force Path**: Aborts if the destination directory already exists.
- **Validation-Gated Promotion**: Artifacts are validated before promotion; `Distributable` promotion strictly requires independent validator approval (`ValidatorApproved`).
- **Atomic Move & Cleanup**: Staged artifacts are promoted via an atomic directory move (`Directory.Move`), followed by invocation staging directory deletion.

### I. Release Boundary Invariants

1. **Packaging is Not Distribution**: Generating a `SourceCandidate` or `Distributable` package does not publish the application.
2. **Synchronized Main for Distributable**: `Distributable` packages cannot be built from feature branches.
3. **Google Play Upload**: Upload to Google Play internal testing or production tracks requires explicit separate user authorization.
4. **Device Testing**: Local offline validation does not replace real-device installation testing.
