# ADR-0006: Android Packaging, Signing Protocols, and Manifest Release Security

## Status
Accepted

## Date
2026-09-11

## Context
MathFirst provides focused, offline mental arithmetic practice. Following the implementation of cross-platform topology (ADR-0001), offline persistence (ADR-0002), and adaptive learning models (ADR-0003, ADR-0004, ADR-0005), the Android application requires an explicit release-policy boundary for target SDK selection, network permissions, learner-data migration, packaging, signing, and artifact provenance.

The learner store is `mathfirst_learner.db` under `FileSystem.AppDataDirectory`. The SQLite adapter uses write-ahead logging, so a consistent device transfer must treat `mathfirst_learner.db`, `mathfirst_learner.db-wal`, and `mathfirst_learner.db-shm` as one approved file set. MathFirst does not provide cloud accounts or cloud synchronization, and routine cloud Auto Backup is outside the product boundary.

The consolidated REVIEW_ONLY re-review verified the implementation architecture, resolving initial signature-validation defects (R01–R03) with forward-only remediation commit `0c896e6eac827bf3dddca244bc507306997fc2f9`, satisfying the approved condition for accepting ADR-0006. Acceptance of this ADR records the architecture decision and does not claim that formal FULL_VALIDATION, push, PR reconciliation, or merge has occurred.

## Decision
1. **Explicit Android platform range**:
   - The Android Target Framework Moniker is pinned to `net10.0-android36.0`, which makes API 36 an explicit source-level target.
   - `SupportedOSPlatformVersion` remains `24.0` for Android.
   - The Windows Target Framework Moniker remains unchanged.
2. **Offline-first manifest security**:
   - In accordance with ADR-0002, `AndroidManifest.xml` explicitly omits all network permissions (`android.permission.INTERNET`, `android.permission.ACCESS_NETWORK_STATE`).
   - `BlazorWebView` assets are loaded locally within the application package without network interaction.
3. **Device-to-device-only learner-data migration**:
   - `android:allowBackup="true"` remains enabled because Android's controlled migration infrastructure depends on it.
   - Routine cloud Auto Backup of learner data or other app-private data is forbidden.
   - The manifest references both legacy `android:fullBackupContent` rules and API 31+ `android:dataExtractionRules` rules.
   - API 24–27 use a deny-all fallback because those versions cannot safely distinguish routine cloud backup from device-to-device transfer.
   - API 28–30 include only `mathfirst_learner.db`, `mathfirst_learner.db-wal`, and `mathfirst_learner.db-shm`, each guarded by the `deviceToDeviceTransfer` transport flag through `requireFlags`.
   - API 31+ explicitly exclude all supported app-data domains from `cloud-backup` and include only the three approved learner SQLite files under `device-transfer`.
4. **Externalized release signing**:
   - Cryptographic keystores, private keys, certificates, and passwords remain outside the Git repository.
   - Source-candidate debug signing may support local build and structural validation, but it does not create a distributable release candidate.
   - A later distributable release must use explicitly authorized release signing and independently managed release credentials.
   - Signing automation must fail closed when requested credentials or files are missing or incomplete, and passwords must never be echoed or logged.
5. **AAB packaging and provenance**:
   - `.aab` is the proposed canonical Android distribution format for Release configuration.
   - Packaging automation records the source commit, branch, clean status, timestamp, configuration, target framework, signing state, artifact identity, and artifact SHA-256 hash.
   - Provenance is metadata, not proof by itself. It requires independent validation against the artifact bytes, Git state, build inputs, and signer identity before distribution.
6. **Independent artifact validation**:
   - Local validation should inspect bundle structure, manifest properties, executable content, signing state, and provenance consistency without requiring ADB, an emulator, or a physical device.
   - Structural validation does not replace later installation and device verification.

## Consequences
### Positive
- The Android target SDK is explicit and reviewable rather than inferred from an installed workload default.
- Network access remains unavailable at the Android manifest boundary.
- Learner data can migrate between devices on supported transports without authorizing routine cloud backup.
- API 24–27 fail closed instead of weakening the cloud-versus-device-transfer policy.
- Release credentials remain external to source control, and provenance can support later independent verification.

### Negative and Operational Costs
- Backup rules must be maintained across Android schema generations and kept synchronized with the actual learner-store filenames and storage domain.
- OEM and backup-transport behavior can limit or omit device-to-device migration even when the rules permit it; successful migration is not guaranteed by configuration alone.
- API 24–27 users intentionally receive neither cloud backup nor device-to-device learner-data migration.
- Release operators retain responsibility for the signer lifecycle, including secure key custody, rotation, expiry planning, and recovery.
- Packaging and validation tooling requires ongoing maintenance as .NET Android and Android bundle formats evolve.
- Installation and device verification remain later lifecycle requirements before distribution confidence can be established.

## Alternatives Considered
- **Universal APK generation**: Rejected as Google Play requires AAB format for modern app submissions and dynamic delivery.
- **Retaining `INTERNET` permission in release manifest**: Rejected because it violates ADR-0002 offline privacy invariants and triggers unnecessary Google Play data safety disclosure requirements.
- **Disabling `android:allowBackup`**: Rejected because it also disables the controlled Android backup infrastructure needed for the approved device-to-device migration path.
- **Routine cloud Auto Backup**: Rejected because learner data is local-only and cloud backup would expand the privacy and recovery boundary beyond the approved product policy.
