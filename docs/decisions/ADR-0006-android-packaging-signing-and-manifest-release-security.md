# ADR-0006: Android Packaging, Signing Protocols, and Manifest Release Security

## Status
Accepted

## Date
2026-09-11

## Context
MathFirst provides focused, offline mental arithmetic practice. Following the implementation of cross-platform topology (ADR-0001), offline persistence (ADR-0002), and adaptive learning models (ADR-0003, ADR-0004, ADR-0005), the Android application required release automation and packaging protocols to support internal distribution and future Google Play testing readiness without compromising data privacy or release governance.

Previous scaffolded project configuration included default framework permissions (`android.permission.INTERNET` and `android.permission.ACCESS_NETWORK_STATE`), lacked dedicated Android App Bundle (.aab) generation automation, lacked deterministic local archive validation, and lacked explicit repository security rules preventing accidental keystore commits.

## Decision
1. **Android App Bundle (.aab) as Canonical Packaging Format**:
   - MathFirst adopts `.aab` as the canonical distribution package format for Android in `Release` configuration.
   - Packaging is automated via repository PowerShell tooling (`scripts/package-android-aab.ps1`), enforcing clean working tree checks and recording immutable Git commit provenance.
2. **Offline-First Manifest Security**:
   - In accordance with ADR-0002, `AndroidManifest.xml` explicitly omits all network permissions (`android.permission.INTERNET`, `android.permission.ACCESS_NETWORK_STATE`).
   - `BlazorWebView` assets are loaded locally within the application package without network interaction.
3. **Data Backup Continuity**:
   - `android:allowBackup="true"` is preserved to permit standard Android Auto Backup of local SQLite learner progress across device upgrades without custom cloud accounts.
4. **Fail-Closed Externalized Signing Architecture**:
   - All cryptographic keystores, private keys, certificates, and passwords remain strictly external to the Git repository.
   - `.gitignore` explicitly blocks all keystores (`*.keystore`, `*.jks`, `*.p12`, `*.pfx`), secret configuration files (`signing.properties`, `.env`), and release artifacts (`artifacts/`, `*.aab`, `*.apk`).
   - Signing automation fails closed if requested signing credentials or files are missing or incomplete. Passwords are never echoed or logged.
5. **Exact Candidate Provenance & Deterministic Naming**:
   - Every generated package produces a companion `{ArtifactName}.provenance.json` containing the Git commit SHA, branch name, clean status, build timestamp (UTC), configuration, target framework, and the SHA-256 hash of the generated bundle.
   - Artifacts follow the deterministic naming schema: `MathFirst-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-{Configuration}.aab`.
6. **Offline Local Artifact Validation**:
   - Standalone offline inspection tooling (`scripts/validate-android-aab.ps1`) validates bundle structure, manifest attributes, DEX bytecode, signature blocks, and provenance SHA-256 checksums without requiring ADB, emulators, or physical devices.

## Consequences
### Positive
- Strict offline privacy is enforced at the Android operating system manifest boundary.
- Artifacts are reproducibly generated and provably tied to exact immutable Git commits.
- Zero risk of accidental secret or keystore commits into repository history.
- Local packaging and validation can be fully executed and verified offline without external network services or devices.

### Neutral
- Keystore generation and credential management remain the responsibility of the release operator outside the repository.
- If network capabilities are ever approved in a future major product milestone, manifest permissions must be deliberately reintroduced.

### Negative
- None identified.

## Alternatives Considered
- **Universal APK generation**: Rejected as Google Play requires AAB format for modern app submissions and dynamic delivery.
- **Retaining `INTERNET` permission in release manifest**: Rejected because it violates ADR-0002 offline privacy invariants and triggers unnecessary Google Play data safety disclosure requirements.
- **Disabling `android:allowBackup`**: Rejected to avoid wiping learner progress when users transfer data to a new Android device.
