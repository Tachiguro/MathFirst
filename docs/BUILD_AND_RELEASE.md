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

[ADR-0006](decisions/ADR-0006-android-packaging-signing-and-manifest-release-security.md) establishes the official packaging and release automation architecture for Android:

### A. Manifest Hardening & Offline Security
- **Offline Invariant**: `src/MathFirst.App/Platforms/Android/AndroidManifest.xml` explicitly omits `android.permission.INTERNET` and `android.permission.ACCESS_NETWORK_STATE`. `BlazorWebView` assets are loaded locally from the package.
- **Backup Policy**: `android:allowBackup="true"` is preserved to allow seamless Android Auto Backup of local SQLite learner progress across device transfers.
- **Target SDK**: .NET 10 implicitly targets Android API 36 with minimum supported SDK 24.0.

### B. Repeatable AAB Packaging (`scripts/package-android-aab.ps1`)
- **Canonical Command**:
  ```powershell
  pwsh -File scripts/package-android-aab.ps1 -Configuration Release
  ```
- **Supported Parameters**:
  - `-Configuration <String>`: Defaults to `Release`.
  - `-DisplayVersion <String>`: Optional semantic display version override (validated `^\d+(\.\d+)+$`).
  - `-BuildNumber <Int32>`: Optional positive integer build number override ($> 0$).
  - `-Sign`: Enables cryptographic keystore signing.
  - `-KeystorePath <String>`: Path to external keystore file (or `$env:MATHFIRST_ANDROID_KEYSTORE_PATH`).
  - `-KeyAlias <String>`: Signing key alias (or `$env:MATHFIRST_ANDROID_KEY_ALIAS`).
  - `-StorePassword <String>`: Keystore password (or `$env:MATHFIRST_ANDROID_STORE_PASS`).
  - `-KeyPassword <String>`: Key password (or `$env:MATHFIRST_ANDROID_KEY_PASS`).
  - `-OutputDir <String>`: Destination folder (defaults to `artifacts/android`).
  - `-AllowDirty`: Allows packaging on a dirty working tree for local development testing only.
- **Fail-Closed Working Tree Policy**: Aborts immediately if uncommitted Git changes exist unless `-AllowDirty` is explicitly passed.
- **Deterministic Output Naming**: `MathFirst-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-{Configuration}.aab`
- **Companion Provenance**: Generates `<ArtifactName>.provenance.json` with commit SHA, branch, timestamp (UTC), configuration, target framework, signing state, and SHA-256 hash.

### C. Fail-Closed Signing Architecture
- Keystores, certificates, and passwords remain strictly externalized and excluded from Git via `.gitignore`.
- Passwords and secret values are never printed, echoed, or committed to logs.
- If `-Sign` is requested, the script verifies all credentials exist; if any input is missing, packaging halts immediately.

### D. Offline Local Validation (`scripts/validate-android-aab.ps1`)
- **Canonical Command**:
  ```powershell
  pwsh -File scripts/validate-android-aab.ps1 -AabPath <path-to-aab> [-ExpectedDisplayVersion <version>] [-ExpectedBuildNumber <build>] [-RequireSigned]
  ```
- **Validation Gates**:
  1. Archive integrity: opens via `System.IO.Compression.ZipArchive`.
  2. Required bundle modules: asserts `BundleConfig.pb`, `base/manifest/AndroidManifest.xml`, `base/dex/*.dex`, and base resources exist.
  3. Signature detection: inspects `META-INF/` for signature blocks.
  4. Provenance match: validates SHA-256 hash against companion `.provenance.json` and asserts `ApplicationId == com.tachiguro.mathfirst`.
