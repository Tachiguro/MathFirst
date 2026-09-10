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

MF-REL-001 may provide explicit build-time overrides of the canonical MSBuild properties if that remains consistent with repository design; it must not introduce a competing semantic-version source. Exact-candidate provenance and fail-closed release behavior remain required.

Android release-readiness remains unresolved for `INTERNET`, `ACCESS_NETWORK_STATE`, `android:allowBackup`, and target SDK policy. AAB generation remains distinct from upload or publication, signing material remains externalized, and no automatic Play Store upload is authorized.
