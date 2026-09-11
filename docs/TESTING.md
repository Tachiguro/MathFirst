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

---

## 8. MF-UX-003 Identity, Version, and Native-Visual Contracts

MF-UX-003 contract coverage verifies canonical identity project properties, `ApplicationTitle` projection into Product and AssemblyTitle metadata, and canonical `ApplicationDisplayVersion`/`ApplicationVersion` inputs. `AppBuildInfoMetadataParser` coverage verifies complete valid metadata and fail-closed behavior for missing, blank, invalid display-version, and non-positive or non-invariant build values; `AppBuildInfo` must delegate to that shared parser.

Presentation and asset contracts verify localization parity; localized Settings version/build display; structural SVG safety; native-host XAML structure and light/dark backgrounds; Android palette, application identity, adaptive icon, and splash identity; Windows unpackaged identity; absence of removed template payloads; localized Not Found content; and the language-neutral startup placeholder. These checks are source/contract verification and do not replace device validation.

Focused implementation evidence recorded 50 passed tests after slice 1 and 64 passed, 0 failed, and 0 skipped after slice 2. Targeted Windows builds recorded 0 warnings and 0 errors for both slices; the second slice also recorded an Android build with 0 warnings and 0 errors. Review remediation began with 2 failing and 8 passing tests, then finished with 26 passed, 0 failed, and 0 skipped, plus targeted Windows and Android builds with 0 warnings and 0 errors. The documentation-inclusive candidate has not undergone formal `FULL_VALIDATION`; no release build, AAB packaging, or real-device validation is claimed.

---

## 9. MF-STAB-001 Practice Progression and HUD Stabilization Contracts

MF-STAB-001 regression coverage verifies the sole Multiplication BandIndex-0 (`MUL-D01`) bootstrap: 12 qualifying accepted attempts after `BandStartedPracticePosition`, at least 11 correct and fluent, 8 owned-frontier attempts, four distinct owned facts, and complete dense coverage. It also verifies that the standard 40/38/34/20/`min(16, owned-frontier-size)` profile remains unchanged for Addition, Subtraction, Division, and Multiplication BandIndex 1 and later; retained existing learner evidence is rehydrated compatibly without migration or intentional backward movement.

Selector contracts preserve ten-slot operation/role scheduling, deterministic ranking, remediation priority, exact FactId cooldown distance 3, Addition/Multiplication mirror cooldown distance 3, maximum preferred same-operation streak 2, FactIds, FSRS architecture, and Schema V5. Timing coverage verifies that every new fact has a fixed 30,000 ms deadline independent of streak and that latency/FSRS classification remains separate: Correct at <= 1000 ms is Easy, 1001–2500 ms is Good, above 2500 ms is Hard, and Incorrect/Timeout is Again.

Presentation coverage verifies red danger Pause semantics; hidden pre-Start score; exactly one transient score after Start; four independent HUD stages in Addition/Subtraction/Multiplication/Division order; fail-closed unavailable stages; stable curriculum and cached diagnostics on timer-only render refreshes; responsive four-column/two-column HUD structure; accessible labels; and English, German, and Russian localization parity. It does not imply a broad keypad or training-card redesign.

---

## 10. MF-LEARN-002 Adaptive Pace, Fast Acquisition, and Practice Interventions Contracts

MF-LEARN-002 contract and regression coverage validates the complete adaptive learning loop, Schema V6 persistence, and practice interaction architecture across 708 automated tests in `MathFirst.Core.Tests`:

1. **Adaptive Pace & Deadlines**:
   - Multi-level hierarchical shrinkage pace estimation ($P_0=4500\text{ ms}$; learner $W=12, N=30$; operation $W=8, N=20$; band $W=6, N=15$; fact $W=4, N=5$) clamped to $[600, 12000]\text{ ms}$.
   - Exact-fact instability allowance: $+1000\text{ ms}$ for Incorrect, $+1500\text{ ms}$ for Timeout across the last 5 attempts, clamped to $[0, 3000]\text{ ms}$.
   - Answer deadline: $\lceil (2 \cdot P_{\text{fact}} + \text{Allowance}) / 100 \rceil \cdot 100\text{ ms}$, clamped to $[3000, 30000]\text{ ms}$, with cold baseline $9000\text{ ms}$.
2. **Adaptive Fluency & Rating Mapping**:
   - FSRS ratings adapt dynamically to fact pace: Easy $\le \text{clamp}(\lfloor 0.85 \cdot P_{\text{fact}} \rceil, 600, 2000)\text{ ms}$, Good $\le \text{clamp}(\lfloor 1.25 \cdot P_{\text{fact}} \rceil, 1500, 4000)\text{ ms}$, Hard $> \text{FluencyThreshold}$, and Again on error or timeout.
   - Schema V6 stores persisted `attempt_history.is_fluent` (`CHECK (is_fluent IN (0, 1)) CHECK (is_fluent = 0 OR (is_correct = 1 AND outcome = 'Correct'))`).
   - Lossless migration backfills valid attempts with `ResponseLatencyMs <= 2500` and `Outcome == 'Correct'` as `is_fluent = 1`, preserving `NULL` PracticePosition for migrated V4 attempts.
3. **Fast Acquisition for Dense Bands**:
   - Dense bands with owned frontier size $N \in [1, 12]$ advance immediately upon 100% Correct and raw response latency $\le 2000\text{ ms}$ on the first positioned encounter of all $N$ owned facts.
   - Clean qualifying prefix rule requires no intervening errors or timeouts in the same-operation qualifying prefix.
   - Horizon rule completes within the phase-aware $N$-th requested-New role horizon derived from operation/role scheduling.
4. **Role-Specific Selector Chains**:
   - Explicit chains for `Requested New`, `Requested Due`, `Requested Maintenance`, and `Requested Frontier` eliminate `AnyMaterialized`.
   - `Early Review` ($\text{DuePracticePosition} > \text{prospectivePosition}$, not in remediation) acts as a strictly bounded liveness bridge.
   - Cooldown relaxation (mirror then exact) occurs strictly within the selected semantic pool.
   - Remediation priority override triggers for scheduled operations with `NeedsRemediation == true` when $\text{prospectivePosition} \ge \text{LastReviewPracticePosition} + 4$.
5. **Repeated-Error Teaching Overlay**:
   - Session-local 2nd consecutive error on the same exact `FactId` displays canonical equation teaching overlay.
   - Deliberate acknowledgement ("I understand") resumes practice without generating attempt records, incrementing Practice Position, or mutating FSRS card state.
6. **Session Check-ins & Zero-Timing Pause**:
   - Checkpoint triggers every 20 accepted attempts, presenting correct count and median latency of correct attempts only.
   - "Keep Going" resumes immediately; "Take a Break" engages the Pause state with guaranteed zero-timing measurement on resume.
7. **Clean Practice HUD**:
   - Practice screen removes transient session score and progress indicators for distraction-free practice.
   - Pause button is styled in danger red.

### Verification Evidence
- **Automated Test Suite**: 708 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Independent Review**: Package-wide post-correction independent review approved (`REVIEW_PASS`).
- **Lifecycle Status**: The documentation-inclusive candidate has not yet undergone the separate `FULL_VALIDATION` lifecycle step. Windows and Android Release build checks, release packaging, and device validation are not claimed as current evidence and remain scheduled for `FULL_VALIDATION` following `COMMIT_ONLY`.
