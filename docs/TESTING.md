# MathFirst Testing Standards & Evidence Principles

This document defines the authoritative testing taxonomy, evidence boundaries, validation protocols, and privacy safeguards for MathFirst.

---

## 1. Test Scope Taxonomy

All automated testing in MathFirst is categorized into distinct scope layers:

| Scope Layer | Focus | Typical Boundaries |
|---|---|---|
| **Unit Tests** | Isolated pure functions, domain logic, and mathematical algorithms. | In-memory, zero I/O, zero network, deterministic. |
| **Integration Tests** | Interactions between multiple internal modules or persistence adapters. | Temporary local directories, synthetic fixtures, mock services. |
| **Contract / API Tests** | Protocol and data serialization conformance. | Schema validation, contract invariants, offline fixtures. |
| **Adapter Conformance Tests** | Conformance of platform persistence adapters against the shared persistence contract. | Unified reusable test contract verifying atomicity, revision conflicts, versioning, corruption handling, and reopen persistence. |
| **Build Checks** | Compilation, type checking, and raw artifact generation. | Build tool verification, clean compilation, zero warnings. |
| **GUI / UI Tests** | User interface interactions and rendering logic. | Headless test harnesses, component snapshots, synthetic events. |
| **Package Validation** | Distribution package integrity and file layout. | Archive inspection, manifest checks. |

---

## 2. Evidence Boundaries & Non-Extrapolation

- **Strict Evidence Truth**: Agent reports must claim only what was explicitly executed and proven.
- A passing unit test suite does **not** prove end-to-end integration.
- A successful build does **not** prove runtime behavioral correctness.
- A clean linter check does **not** prove absence of functional bugs.

---

## 3. Real Private User Data Protection

Automated test suites and test runners must **NEVER**:
- Open or read real private user documents or directories.
- Copy, migrate, alter, or delete real user data.
- Run tests against production user environments without explicit authorization.

### Permitted Test Fixtures
All automated testing must use:
- Synthetic in-memory fixtures.
- Isolated temporary filesystem directories (e.g. wiped after test run).
- Mock HTTP and network interceptors.
- Deterministic clocks and fixed timestamps.

---

## 4. Deterministic & Offline Defaults

- **Offline by Default**: All automated unit and integration tests must run fully offline without dependencies on external network services.
- **Deterministic**: Tests must not rely on non-deterministic seeds, wall-clock timing, or race conditions.
- Tests requiring live external network APIs must be explicitly tagged and isolated into optional suites requiring affirmative opt-in.

---

## 5. Fail-Closed Validation Policy

- Testing tools, validation scripts, and test runners must operate on a **fail-closed** model: any error, timeout, or ambiguity is treated as a test failure.
- `TEST_ONLY` and `FULL_VALIDATION` modes must **never** perform automatic code repairs or silent retry loops. Failures must be accurately reported in the technical output.

---

## 6. Pre-Technology-Stack Full Validation Concept

Before a language runtime or application technology stack is selected, `FULL_VALIDATION` evaluates repository and documentation integrity via:
1. **Governance & File Presence**: Verify all required repository files and paths exist.
2. **Markdown Link Integrity**: Verify that all repository-relative Markdown links resolve to existing files and valid anchors.
3. **Candidate Diff & Whitespace Audit**: Run `git diff --check <verified-base>...HEAD` against the dynamically verified base branch/commit to ensure no trailing whitespace or corrupt line endings exist in the candidate commit.
4. **Git Hygiene Audit**: Verify `git status` is clean on candidate HEAD and no unexpected untracked artifacts or temporary files are present.

*Note: Canonical stack-specific validation commands will be introduced when the product technology stack is formally established.*

---

## 7. MF-UX-002 Contextual-Copy Contracts

Contextual practice-gate tests must use deterministic clocks and synthetic learner state. They verify deterministic stable-hash selection with ordered per-trigger recency rollover and an effective exclusion window of `min(pool.Count - 1, 5)`; the selector contract includes its deterministic recency state and must not be represented as a raw-argument-only pure function.

The corpus contract verifies English, German, and Russian physical-ID and placeholder parity (55 IDs per locale, 165 localized strings total), unique visible text within each locale and trigger pool, normalized locale behavior, and static Ready/Paused fallback. Gate-presentation tests verify no selection rotation on ordinary rerender, navigation, or route recreation; same-ID re-localization on language change; and invalidation after learner-state reset or authoritative reload.

Persistence/lifecycle coverage verifies the authoritative latest-accepted-practice read model across cold startup, successful persistence, reset, and recovery, plus contextual Background Resume selection for immediate and deferred final gate transitions. These tests also preserve learning-state isolation: contextual copy must not alter curriculum, progression, learning evidence, FSRS, answer semantics, or Schema V5. The final implementation review recorded 497 Core tests passed, 0 failed, 0 skipped, a Windows build with 0 warnings and 0 errors, and passing `git diff --check`; this evidence does not claim the separate `FULL_VALIDATION` lifecycle step.

## 8. MF-UX-003 Identity, Version, and Native-Visual Contracts

MF-UX-003 contract coverage verifies canonical identity project properties, `ApplicationTitle` projection into Product and AssemblyTitle metadata, and canonical `ApplicationDisplayVersion`/`ApplicationVersion` inputs. `AppBuildInfoMetadataParser` coverage verifies complete valid metadata and fail-closed behavior for missing, blank, invalid display-version, and non-positive or non-invariant build values; `AppBuildInfo` must delegate to that shared parser.

Presentation and asset contracts verify localization parity; localized Settings version/build display; structural SVG safety; native-host XAML structure and light/dark backgrounds; Android palette, application identity, adaptive icon, and splash identity; Windows unpackaged identity; absence of removed template payloads; localized Not Found content; and the language-neutral startup placeholder. These checks are source/contract verification and do not replace device validation.

Focused implementation evidence recorded 50 passed tests after slice 1 and 64 passed, 0 failed, and 0 skipped after slice 2. Targeted Windows builds recorded 0 warnings and 0 errors for both slices; the second slice also recorded an Android build with 0 warnings and 0 errors. Review remediation began with 2 failing and 8 passing tests, then finished with 26 passed, 0 failed, and 0 skipped, plus targeted Windows and Android builds with 0 warnings and 0 errors. The documentation-inclusive candidate has not undergone formal `FULL_VALIDATION`; no release build, AAB packaging, or real-device validation is claimed.
