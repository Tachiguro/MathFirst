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

### Verification Evidence (Historical Package Delivery)
- **Automated Test Suite**: 708 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Independent Review**: Package-wide post-correction independent review approved (`REVIEW_PASS`).
- **Delivery Status**: Merged into `main` via PR #15 at commit `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1`.

---

## 11. MF-LEARN-003 Acclimation Timing, Rapid Dense Expansion, and Keypad Defaults Contracts

MF-LEARN-003 contract, regression, and simulation coverage validates the acclimation timing model, keypad preferences, Coverage-First selection, bounded latest-per-frontier persistence, correctness-driven Dense progression, and editable multi-digit input handling across 790 automated tests in `MathFirst.Core.Tests`:

1. **Answer-Length Acclimation Deadlines & Durable Proof**:
   - Durable fact proof: Fact is proven iff `ItemLearningState.CorrectAttempts > 0`.
   - Digit-aware novelty floors for unproven facts: 1 digit = $15000\text{ ms}$, 2 digits = $20000\text{ ms}$, 3 digits = $25000\text{ ms}$, 4+ digits = $30000\text{ ms}$ derived from canonical non-negative correct result using integer arithmetic ($0$ is 1 digit).
   - Multi-digit entry allowance: $+1000\text{ ms} \cdot \max(0, \text{DigitCount} - 1)$.
   - Adaptive deadline: $\lceil (2 \cdot P_{\text{fact}} + \text{InstabilityAllowanceMs} + \text{EntryAllowanceMs}) / 100 \rceil \cdot 100\text{ ms}$, clamped $[3000, 30000]\text{ ms}$.
   - Effective deadline: Proven $\implies \text{AdaptiveDeadline}$; Unproven $\implies \min(30000, \max(\text{AdaptiveDeadline}, \text{NoveltyFloor}))$.
   - Strict non-interference: Latency measurement, adaptive fluency thresholds, `IsFluent`, and FSRS ratings remain unaffected by novelty deadline floors.
2. **Keypad Defaults & Ordering**:
   - Default/fallback is `Numpad` across missing, null, or invalid stored preference values.
   - Visual option order in Onboarding and Settings presents Numpad first (left) and Phone second (right).
   - Storage enum values remain `Phone = 0`, `Numpad = 1`. Explicitly stored preferences remain preserved.
   - Initial Home backing state and Full Local Reset default to Numpad.
3. **Coverage-First Dense Selection & Materialization Safety**:
   - Selector precedence: 1. eligible Remediation ($\ge 4$ distance); 2. Dense Coverage-First New; 3. ordinary requested-role chains.
   - Coverage-First Dense New selects unmaterialized owned-frontier facts across nominal Due/Maintenance/Frontier turns until first-pass coverage is complete.
   - Materialization invariant: Unmaterialized facts enter practice only via explicit `Requested New` or `Coverage-First Dense New`. Non-New resolved roles strictly cannot materialize facts.
   - Structured bands are isolated from Coverage-First selection.
4. **Authoritative Latest-per-Frontier Persistence & Schema V6 Index**:
   - Store contract `ILearnerStore.LoadLatestFrontierAttemptsAsync` queries at most one latest positioned attempt per requested frontier fact for `PracticePosition > BandStartedPracticePosition`.
   - Bounded query scope: Frontier is bounded by current Dense band ($N \le 25$).
   - Schema V6 partial index `ix_attempt_history_operation_fact_position` on `attempt_history(operation, fact_id, practice_position DESC) WHERE practice_position IS NOT NULL` is initialized/repaired idempotently across fresh V6, existing V6, and migrations.
   - Verified >40-attempt evidence case ensuring progression is evaluated over all attempts since band start, not truncated by the recent 40 window.
5. **Correctness-Driven Dense Progression ($C \cdot 10 \ge N \cdot 9$)**:
   - Complete frontier coverage required before progression evaluation.
   - In-memory candidate overlay pre-evaluates progression before atomic persistence commit.
   - Error and timeout recovery: Latest positioned outcome is authoritative (an error followed by a correct answer votes Correct).
   - Slow / non-fluent Correct answers count as Correct for Dense progression.
   - Threshold conformance: $N=2..9 \implies 100\%$ Correct required; $N=10 \implies 9$ Correct; $N=11 \implies 10$; $N=12 \implies 11$; $N=20 \implies 18$; $N=25 \implies 23$.
   - Structured band isolation: Structured bands strictly retain the rolling 40-attempt gate ($\ge 38$ Correct, $\ge 34$ Fluent, $\ge 20$ frontier, $\ge \min(16, N)$ distinct, 16 introductions).
   - Fast Acquisition and `FastAcquisitionEvaluator` are retired.
   - `MUL-D01` 12-attempt special bootstrap is retired; `MUL-D01` follows the universal Dense progression rule ($N=4 \implies 4$ Correct).
6. **Weak-Fact Continuity, Atomicity & Restart Equivalence**:
   - Weak facts in $\ge 90\%$ advanced bands remain in `ItemLearningState`, FSRS card state, and remediation queues.
   - Persistence failure does not publish or mutate learner state; recovery reloads durable state.
   - Progression is restart-stable without ephemeral session markers.
   - Terminal safety: Terminal bands (`ADD-P8-D0`, `SUB-P8-D0`, `MUL-P7-SCALED`, `DIV-P7-SCALED`) do not synthesize unsafe successors.
7. **Deterministic Progression Simulations**:
   - Ideal all-Correct simulation validates early expansion: DIV-D01 $\to$ D02 at position 8, SUB-D01 $\to$ D02 at position 10, ADD-D01 $\to$ D02 at position 13, MUL-D01 $\to$ D02 at position 15 (all 4 initial bands advanced by position 15).
   - Validated positions: Position 20 (all D02), Position 50 (ADD-D03, SUB-D04, MUL-D03, DIV-D04), Position 100 (ADD-D05, SUB-D06, MUL-D05, DIV-D05).
8. **Editable Incomplete Multi-Digit Input and Auto-Submission Coverage**:
   - Multi-digit pending editability: Wrong first digit of a multi-digit answer (e.g. entering `1` for `6 × 6 = 36`) keeps the partial buffer editable and pending without submitting or mutating learning state.
   - Backspace / Delete correction: Backspace removes the trailing digit from an incomplete buffer; empty-buffer Backspace is a safe no-op.
   - Full wrong digit count submission: Typing the full expected digit count (e.g. `12` for `36`) auto-submits exactly once as Incorrect without post-submission editing.
   - Single-digit immediate submission: One-digit expected answers auto-submit immediately on the first digit for both correct and incorrect inputs.
   - Multi-digit (3/4-digit) partial editability: Intermediate lengths (e.g. entering `1` then `14` for `144`) remain pending and editable until full length is reached.
   - Timer continuity through edits: Partial entry, pauses, and Backspace deletions do not reset the active response timer; measured `ResponseLatencyMs` spans the full active duration.
   - Partial-input timeout semantics: If the deadline expires while a partial buffer exists, it is recorded as `AttemptOutcome.Timeout` without creating an Incorrect attempt or submitting partial buffer digits.
   - Input-path parity: Physical keyboard number row, physical Numpad, and on-screen keypad share identical buffering and auto-submission semantics.
   - Learning-state boundary: Incomplete buffers do not mutate `PracticePosition`, `ItemLearningState`, attempt history, FSRS state, or progression counters.

### Verification Evidence (MF-LEARN-003)
- **Targeted Reviewed Test Suites**:
  - `ResponsiveAndCorrectAnswerFlowTests`: 42 passed
  - `NumericInputAndKeypadTests`: 65 passed
  - `TimedTrainingOutcomeTests`: 46 passed
  - `AdaptivePaceRuntimeTests`: 66 passed
  - `AndroidInputContractTests`: 14 passed
  - `WindowsUxContractTests`: 11 passed
  - `AdaptiveLearningUxCompletionTests`: 17 passed
- **Full Core Test Suite**: 790 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Independent Review**: Package-wide corrective independent review approved (`REVIEW_PASS`).
- **Lifecycle Status**: Merged to `main` via PR #16 at commit `9a9e5c43d1f1685cf21e766e0890b790ab09040c`.

---

## 12. MF-REL-001 Android Packaging and Release Automation Contracts

MF-REL-001 contract, hygiene, and validation coverage enforces Android App Bundle packaging invariants across automated test suites in `MathFirst.Core.Tests`:

1. **Manifest and Backup Policy Contracts (`AndroidPackagingContractTests`)**:
   - Asserts total absence of network permissions (`android.permission.INTERNET`, `android.permission.ACCESS_NETWORK_STATE`).
   - Asserts `android:allowBackup="true"`, `android:supportsRtl="true"`, and approved native icon references.
   - Asserts canonical application ID `com.tachiguro.mathfirst`, display version `1.0`, build `1`, target framework `net10.0-android36.0`, and Android minimum SDK `24.0`.
   - Asserts `.gitignore` contains explicit fail-closed ignore rules for keystores (`*.keystore`, `*.jks`, `*.p12`, `*.pfx`), secret configurations (`signing.properties`, `.env`), and release artifacts (`artifacts/`, `*.aab`, `*.apk`).
   - Asserts multi-generation backup rules: API 24–27 deny-all fallback across 9 storage domains, API 28–30 device-to-device-only for learner SQLite files (`mathfirst_learner.db`, `mathfirst_learner.db-wal`, `mathfirst_learner.db-shm`), and API 31+ deny-all cloud backup with device transfer restricted to learner SQLite files.
   - Asserts ADR-0006 architectural boundaries and terminology.

2. **Release Packaging Script & Tool Invariants (`AabPackagingScriptValidationTests`)**:
   - Asserts `PackageRequest` and CLI reject Debug configuration, dirty escape hatches, and unknown options.
   - Asserts `RepositoryPolicy` enforces clean working tree, clean index, zero untracked files, 40-character SHA matching `HEAD`, exact branch for `SourceCandidate`, and synchronized `main` (`HEAD == local main == origin/main`) for `Distributable`.
   - Asserts `VersionPolicy` delegates to shared `AppBuildInfoMetadataParser` contract, rejects invalid overrides, and accepts valid overrides.
   - Asserts MSBuild property evaluation invocation and JSON parsing.
   - Asserts fail-closed external process execution.
   - Asserts `SigningPolicy` rejects missing/empty alias, malformed fingerprints, relative secret paths, missing files, repository/artifact-contained files, and reparse-point paths.
   - Asserts `SigningInputs` exposes no plaintext passwords and `publish` invocation passes password files via `AndroidSigningStorePass=file:...` and `AndroidSigningKeyPass=file:...`.
   - Asserts `ArtifactWorkspace` fixed hierarchy, path-escape rejection, reparse-point rejection, single-AAB discovery, promotion collision rejection, unvalidated distributable rejection, incomplete provenance rejection, atomic directory move, and owned-staging cleanup.
   - Asserts artifact naming format: `MathFirst-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-{Classification}`.
   - Asserts provenance schema v1 structure, mandatory fields, and typed serialization.
   - Asserts `scripts/package-android-aab.ps1` and `scripts/validate-android-aab.ps1` are thin wrappers delegating to `MathFirst.ReleaseTool`.

3. **Authoritative Offline AAB Validation Invariants (`AndroidAabValidationTests`)**:
   - Asserts `AndroidAabValidator` rejects missing AAB/provenance files, malformed SHAs, malformed provenance JSON, schema version != 1, file name / size / hash mismatches, expected/commit SHA mismatches, dirty working trees, non-Release configurations, non-net10.0-android36.0 frameworks, incorrect SDK versions, and application ID mismatches.
   - Asserts `bundletool validate` failure rejection.
   - Asserts binary manifest validation: rejects wrong package ID, mismatched version name/code, min SDK != 24, target SDK != 36, `debuggable="true"`, declared `INTERNET` or `ACCESS_NETWORK_STATE`, `allowBackup != true`, or invalid backup rule resource references.
   - Asserts resource validation: rejects missing backup XML resources in bundle table or archive (`backup_rules.xml`, `xml-v28/backup_rules.xml`, `data_extraction_rules.xml`).
   - Asserts DEX bytecode validation: rejects missing DEX entries or `dexdump -f` execution failures.
   - Asserts `JarSignatureInspector` accepts valid Android self-signed signatures with standard trust/timestamp warnings; rejects exit code != 0, unsigned jars, or unconfirmed verification.
   - Asserts certificate extraction via `keytool`: rejects zero signers, handles certificate chains by extracting the leaf certificate SHA-256, rejects multiple independent signers, rejects malformed signer blocks.
   - Asserts profile-specific signer policies: `SourceCandidate` accepts development/debug signing (non-distributable); `Distributable` rejects Android Debug signers, rejects signer fingerprint mismatches, accepts valid release signers matching expected SHA-256, and rejects expired certificates.
   - Asserts packaging integration fails and promotes nothing when validation fails.

### Verification Evidence & Lifecycle Boundary

- **Targeted Test Suites**:
  - `AndroidPackagingContractTests`
  - `AabPackagingScriptValidationTests`
  - `AndroidAabValidationTests`
- **Full Core Test Suite**: Complete current Core-suite count will be formally established during candidate `FULL_VALIDATION` (focused packaging/validation suites passed during implementation; final remediation verified 141 directly relevant tests; consolidated `REVIEW_ONLY` did not rerun the full suite).
- **Compilation**: Android and Windows Release builds compile with 0 warnings and 0 errors.
- **Evidence Boundary**: Automated tests in `MathFirst.Core.Tests` execute against synthetic fixtures, mocked tool runners, and isolated temporary directories. These tests verify tool logic and contracts; they do **not** constitute formal release candidate `FULL_VALIDATION`, real candidate packaging, or Google Play verification. Real candidate packaging and full validation remain separate subsequent lifecycle concerns.
