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
3. **Coverage-First Dense Selection & Materialization Safety (Historical Baseline & Reconciled Authority)**:
   - Historical selector precedence: 1. eligible Remediation ($\ge 4$ distance); 2. Dense Coverage-First New; 3. ordinary requested-role chains.
   - Reconciled under current requested-role review authority (MF-STAB-002 / MF-STAB-003 / MF-LEARN-004): Unseen Dense material does not globally or unconditionally override non-New roles (`Due`, `Maintenance`, `Frontier`). Dense New introductions occur during designated `Requested New` opportunities (4 slots per 10-attempt period), giving learners balanced review and retention opportunities alongside new curriculum acquisition.
   - Materialization invariant: Unmaterialized facts enter practice strictly via explicit `Requested New` turns when unmaterialized candidates exist and are presentation-eligible. Non-New resolved roles strictly cannot materialize facts.
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
- **Delivery Status**: Merged into `main` via PR #17 at commit `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`.
- **Compilation**: Android and Windows Release builds compile with 0 warnings and 0 errors.
- **Evidence Boundary**: Automated tests in `MathFirst.Core.Tests` execute against synthetic fixtures, mocked tool runners, and isolated temporary directories.

---

## 13. MF-SET-001 Practice Configuration, Visibility, Timer, and Persistence Contracts

MF-SET-001 permanent regression coverage validates the final practice-configuration behavior across `MathFirst.Core.Tests`:

1. **Answer Entry Reliability & Fact-Instance Input Reset**:
   - Partial multi-digit input such as `2` for `7 + 4 = 11` remains editable and creates no premature Incorrect attempt.
   - Backspace correction works, a complete answer submits once, single-digit correct answers retain immediate auto-submit, and every newly generated problem starts with an empty input field.
   - Fact-instance lifecycle tracking (`FactInstanceRevision` on `TrainingSession` and DOM element keying via `_inputRenderVersion` in `Home.razor`) guarantees that new problems always begin with clean empty buffers while the active exercise retains partial input across transient UI interactions.

2. **Operation Configuration, Onboarding, and HUD Visibility**:
   - All four controls remain visible in Settings and Onboarding; all operations default enabled; valid one-, two-, three-, and four-operation subsets persist; the last enabled operation cannot be disabled; and corrupt all-false state falls back to all four.
   - Enabled operations are both selection and display filters. One, two, three, and four enabled operations produce the corresponding number of HUD indicators; disabled operations retain all learner state. Returning learners with existing progress view a progress overview on the readiness gate.
   - Settings changes preserve the displayed question, update the HUD on return, and apply to the next generated question.
   - The developer Statistics / Diagnostics section is absent from Settings while the operation HUD remains. Learner attempts, FSRS data, progression, and item state are not deleted.

3. **Enabled-Subset Scheduling and Evidence**:
   - `AdaptivePracticeSelector` schedules index $(P - 1) \bmod k$ and per-operation ordinal $\lfloor(P - 1) / k\rfloor + 1$ from the enabled list.
   - `PracticeSelectionEvidence` is bound to both operation and prospective Practice Position. Cached evidence cannot change the scheduled operation or satisfy a different operation/position.
   - Required scheduled-operation evidence loads asynchronously on demand. Disabled-operation evidence prefetch is best effort; its failure cannot block enabled practice. No arbitrary wrong-operation fallback is permitted.

4. **Schedule-Agnostic SQLite Persistence and Exactly-Once Recovery**:
   - Single-, two-, and three-operation long-running sessions persist without reconstructing a fixed four-operation rotation.
   - Validation covers Practice Position monotonicity, attempt/progression consistency, canonical fact identity and result, answer/outcome consistency, item and FSRS transitions, canonical four-entry `OperationProgression`, attempted-operation mutation boundaries, band progression, revision conflicts, duplicate positions, transaction atomicity, and idempotency.
   - Durable commit failure remains distinct from post-write next-exercise evidence/preparation failure. English uses “Progress could not be saved.” versus “Next exercise could not be loaded.”; German uses “Fortschritt konnte nicht gespeichert werden.” versus “Nächste Aufgabe konnte nicht geladen werden.”; Russian equivalents have localization coverage.
   - Recovery after a successful durable write cannot duplicate attempt history, Practice Position, item state, FSRS repetitions, or store revision; it prepares the required next-exercise evidence and resumes.

5. **Practice Time, Active Latency, and Timer Lifecycle**:
   - Standard leaves the adaptive deadline unchanged; 30 s, 45 s, and 60 s apply $\max(\text{adaptive deadline}, \text{configured floor})$ without changing easy/fluency thresholds, FSRS rating semantics, or raw active latency.
   - Manual Pause and Settings freeze elapsed and remaining time, suppress timeout while inactive, and resume the same question with its remaining time.
   - Same-process background/foreground preserves remaining time behind the resume gate. A cold process restart begins the in-flight timer fresh while retaining preferences and learner progress.
   - Only active answering time feeds learning evaluation and persisted attempt metrics. In-flight elapsed/remaining time and pause/active segment timestamps are not persisted.

6. **Reset, Check-In, and Localization Contracts**:
   - Reset Learning Progress preserves enabled-operation and Practice Time preferences; Restore Default Settings restores all four operations and Standard time while preserving learner progress; Full Local Reset performs both resets.
   - Session check-in every 20 accepted attempts accurately reports completed count, correct count, stage progressions, and median latency of correct attempts only.
   - English, German, and Russian remain in parity. Exactly 20 unique Diagnostics keys were removed (60 dictionary entries); `Diagnostics_Group_Learning` historically remained for the Home HUD accessible group label under MF-SET-001 until replaced with learner-facing `Training_OperationProgressGroupAriaLabel` in MF-UX-007.

### Reviewed Focused Test Evidence

- `PracticeVisibilityAndTimerLifecycleTests`: 13 passed
- `PolicyAndLocalizationTests`: 48 passed
- `RuntimePersistenceRegressionTests`: 8 passed
- `StaleSelectionEvidenceRemediationTests`: 9 passed
- `SqliteEnabledSubsetPersistenceTests`: 9 passed
- `SubmissionIntegrityAndPublishBoundaryTests`: 19 passed
- `PersistenceRecoveryAndLifecycleTests`: 8 passed
- `AnswerEntryReliabilityTests`: 10 passed
- `OnboardingAndProgressFeedbackTests`: 12 passed
- `PracticeConfigurationTests`: 38 passed
- `AdaptivePaceRuntimeTests`: 66 passed
- `ResetWorkflowTests`: 5 passed
- `AndroidLifecycleTimerTests`: 11 passed

### Final Reviewed Baseline and Evidence Boundary

- **Final Implementation HEAD**: `f453501412b7c9fce39356a0837a5b862d6221fd` (across 10 commits).
- **Full Core Test Suite**: 1037 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Compilation**: Windows Release 0 warnings / 0 errors; Android Release 0 warnings / 0 errors.
- **Independent Review**: `REVIEW_PASS` (Implementation Readiness: YES).
- **Android Validation Artifact**: `MathFirst-MF-SET-001-f453501-debug.apk`, SHA-256 `97f85e448d078e46d813aa1244e49b95933e51e1c9d64732faf3a614ae31db12`, provenance `f453501412b7c9fce39356a0837a5b862d6221fd`, package ID `com.tachiguro.mathfirst`, versionCode 1, versionName 1.0. It is an installable Debug validation APK, not a production Google Play AAB or release artifact.
- **Manual Physical Android Device Validation**: User confirmed successful physical device validation:
  - New exercise input reset: newly generated problems always start with clean empty buffers.
  - Same exercise partial input retention: partial input is preserved across transient UI interactions.
  - Settings/timer lifecycle: opening Settings pauses the active timer; returning resumes with same remaining time.
  - Onboarding operation choice: operation selection works during initial setup and subsequent Settings.
  - Returning progress presentation: preserved learner progress and HUD indicators display accurately for returning learners.
  - SessionCheckIn: 20-attempt summaries compute correct count and median correct latency accurately.
- **Lifecycle**: `DOCUMENT_ONLY`; the next successful lifecycle is `COMMIT_ONLY`.
- **Evidence Boundary**: Automated tests in `MathFirst.Core.Tests` execute against synthetic fixtures and isolated environments. Manual verification on physical device confirmed UI/runtime invariants. No production AAB, signing, Google Play upload, distribution, or release is implied.

---

## 14. MF-REL-002 Tester Distribution and Release Hardening Contracts

MF-REL-002 extends the offline release-tool contract while preserving the existing ArtifactId, Android artifact hierarchy, signing architecture, and validator implementation.

1. **Tester Evidence Generation (`TesterDistributionValidationTests`)**:
   - Verifies ValidationReceipt Schema v1, stable string serialization for `SourceCandidate` / `Distributable` and `ValidatorApproved`, absence of a `checks` property, fixed UTC timestamps, exact AAB/provenance hash binding, signer evidence, and profile-specific distributable state.
   - Verifies deterministic tester README boundaries and exact four-entry `SHA256SUMS` formatting: known filenames only, no duplicates, ordinal order, lowercase SHA-256, two-space delimiter, LF-only content, and one final LF.
   - Verifies evidence-only `ArtifactWorkspace` promotion rejects incomplete, extra, malformed, mismatched, reparse-point, overwrite, or incorrectly classified staged evidence before the atomic directory move.

2. **Packaging Integration Contract Migration (`AndroidAabValidationTests`)**:
   - Successful `SourceCandidate` and `Distributable` packaging scenarios now require exactly five promoted files: the signed `<ArtifactId>.aab`, provenance JSON, validation receipt JSON, `TESTER_README.md`, and `SHA256SUMS`.
   - The unsigned `com.tachiguro.mathfirst.aab` publish intermediate remains explicitly excluded.
   - Synthetic packaging fixtures use a representative attached non-`main` branch and no longer encode the historical MF-REL-001 branch name.

3. **Release Profile Characterization (`ReleaseProfileContractTests`)**:
   - Characterizes the already-correct Windows project contract: `WindowsPackageType == None`, Windows `SupportedOSPlatformVersion == 10.0.17763.0`, and Windows `TargetPlatformMinVersion == 10.0.17763.0`.
   - This is characterization coverage, not a manufactured RED behavioral slice; the project configuration is unchanged.

4. **TDD and Evidence Boundary**:
   - Genuine RED -> GREEN slices cover the generalized clean attached non-`main` SourceCandidate policy, evidence generation/promotion, and `AndroidPackageCommand` integration. The Windows project assertions characterize existing behavior.
   - All automated coverage is offline and uses synthetic bundle bytes, mocked external tool processes, fixed timestamps where receipt content is asserted, and isolated temporary directories. It does not execute real packaging, production signing, upload, distribution, installation, ADB, emulator/device work, or manual verification.

Focused MF-REL-002 validation can be run with:

```powershell
dotnet test tests/MathFirst.Core.Tests/MathFirst.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AabPackagingScriptValidationTests|FullyQualifiedName~TesterDistributionValidationTests|FullyQualifiedName~ReleaseProfileContractTests|FullyQualifiedName~AndroidAabValidationTests"
```

---

## 15. Forensic Remediation: Fact Eligibility Invariant and Startup Recovery Contracts

The forensic remediation following native V1 candidate rejection (`REAL_DEVICE_VERIFICATION_FAILED`) establishes permanent regression and evidence coverage for the Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and session startup resilience across `MathFirst.Core.Tests`:

1. **Practice Fact Eligibility Regression Coverage (`FactEligibilityRegressionTests`)**:
   - **Concrete $2 \times 8$ Multiplication Regression**: Verifies that `mul:2*8` (owned by BandIndex 7, `MUL-D08`) is strictly excluded from `Due`, `Maintenance`, `Early Review`, and `Remediation` selection at Stage 2 (`MUL-D02`, BandIndex 1) and Stage 3 (`MUL-D03`, BandIndex 2), but becomes eligible when progression reaches BandIndex 7.
   - **Universal 4-Operation Scope**: Verifies curriculum-bounded eligibility across Addition (tens facts vs dense band 0), Subtraction (inverse facts vs triangular band 0), Multiplication, and Division (large divisors vs band 1).
   - **Candidate-Window Anti-Poisoning & Starvation Defense**: Verifies that `SqliteLearnerStore.ReadCandidatesAsync` streams rows and applies `IsEligible` before window truncation, ensuring eligible reviews are discovered and returned even when preceded in FSRS due order by 70+ ineligible future due facts.
   - **Snapshot Fallback Parity**: Verifies that `PracticeSelectionEvidence.FromSnapshot` filters item states by `IsEligible` with identical candidate output as the SQLite streaming query.
   - **Selector Pure Defense**: Verifies that `AdaptivePracticeSelector.SelectTargetFact` defensively filters all semantic review pools against current `progression.BandIndex`, preventing future-fact presentation even if malformed evidence is injected.
   - **Persistence Gate Defense**: Verifies that `SqliteLearnerStore.ValidateNewAcceptedSubmission` validates `IsEligible(attempt.FactId, storedBandIndex)` before writing, throwing `InvalidOperationException` and rolling back attempts for future locked, malformed, or cross-operation facts (`PersistenceResult.InvalidSubmission`).
   - **Migration Stale-State Dormancy**: Verifies that Schema V4 $\to$ V5 $\to$ V6 migrations preserve historical future-band rows losslessly, while queries keep them dormant until progression unlocks their band.
   - **Band Unlock Transition**: Verifies that advancing progression immediately activates dormant historical facts for review with their prior FSRS intervals intact.
   - **Long-Run Deterministic Simulation**: 500-step continuous practice simulations across all 4 operations verify that 100% of presented `CurrentFact` instances satisfy $\text{owner}_O(F) \le \text{BandIndex}_O$ at the exact moment of presentation.

2. **Startup Initialization & Recovery Coverage (`StartupRecoveryRegressionTests`)**:
   - **Fail-Closed Initialization**: Verifies that store initialization or evidence loading failure leaves `TrainingSession.IsInitialized == false`.
   - **Safe UI Rendering Without `CurrentFact`**: Verifies that `Home.razor` catches initialization exceptions and renders a dedicated startup error boundary (`_startupFailed`) without evaluating `Session.CurrentFact`, `LastEvaluation`, or `LastPersistenceResult`, avoiding `NullReferenceException`.
   - **Non-Destructive Retry**: Verifies that activating retry re-invokes `Session.InitializeAsync` cleanly, produces a valid `CurrentFact` upon transient failure recovery, transitions to `InitialReadyGate`, and never deletes or resets learner progress.
   - **Dormancy Across Restart**: Verifies that application restart preserves stored learner state and maintains the fact eligibility invariant.

### Verified Automated Test Evidence (Remediation Task 8 Baseline)

- **Full Core Automated Suite**: 1169 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Targeted Suite Breakdown**:
  - `FactEligibilityRegressionTests`: 23 passed
  - `StartupRecoveryRegressionTests`: 6 passed
  - `LongRunIndependentProgressionTests`: 7 passed
  - `AcquisitionOwnershipResolverTests`: 18 passed
  - `DeterministicSelectorTerminalLivenessTests`: 11 passed

### Evidence Boundary Principles
- This automated evidence establishes domain, application, and persistence invariant conformance in `MathFirst.Core.Tests` using synthetic fixtures and isolated temporary databases.
- It is Core automated regression evidence only. It does **not** claim execution of the post-remediation `FULL_VALIDATION` lifecycle.
- It does **not** claim that release builds for Android or Windows were recompiled or validated following remediation.
- It does **not** claim that physical-device remediation verification has occurred.

---

## 16. MF-UX-005 Repeatable Tester Artifact / APK Workflow Contracts (Historical Automated Evidence)

MF-UX-005 establishes a dedicated, repeatable Tester APK workflow enabling local packaging, offline validation, and atomic evidence promotion of installable Android APKs alongside the existing AAB release pipeline across automated test suites in `MathFirst.Core.Tests`:

1. **Tester APK Packaging Policy & Configuration Contracts (`TesterApkPackagingContractTests`)**:
   - Asserts `ReleaseProfile.Tester` enforces `ApplicationId = com.tachiguro.mathfirst.tester`, `BuildClassification = Tester`, `MathFirstSourceCommit = <SHA>`, `Configuration = Release`, `TargetFramework = net10.0-android36.0`, and `AndroidKeyStore = false`.
   - Asserts branch policy accepts any clean attached non-`main` branch or clean synchronized `main`, requiring exact full 40-character commit SHA matching `HEAD`.
   - Asserts CLI rejects release keystore signing parameters (`--keystore-path`, `--key-alias`, `--store-pass-file`, `--key-pass-file`, `--expected-signer-sha256`) when packaging with `--profile Tester`.
   - Asserts deterministic artifact naming: `MathFirst-Tester-v{DisplayVersion}-b{BuildNumber}-{ShortCommit}-tester.apk`.
   - Asserts deterministic destination routing: `artifacts/android/tester/<ArtifactId>/`.

2. **Authoritative Offline APK Validation Contracts (`AndroidApkValidationTests`)**:
   - Asserts `AndroidApkValidator` enforces structural, manifest, bytecode, and cryptographic signature constraints on `.apk` packages.
   - Asserts invocation of `apksigner verify --verbose --print-certs` (strictly rejecting `jarsigner` for APK validation) to verify APK signature schemes (v1/v2/v3/v4), extract leaf X.509 certificate fingerprints, verify development-debug certificate classification, and enforce the exactly-one-signer policy.
   - Asserts binary manifest inspection via `aapt2 dump xmltree`: package name equals `com.tachiguro.mathfirst.tester`, version code/name match provenance, minimum SDK is 24, target SDK is 36, `android:debuggable != "true"`, and backup rules are correctly wired (`backup_rules.xml`, `xml-v28/backup_rules.xml`, `data_extraction_rules.xml`).
   - Asserts zero network permissions: strictly rejects packages containing `android.permission.INTERNET` or `android.permission.ACCESS_NETWORK_STATE`.
   - Asserts DEX bytecode validation: extracts `.dex` payloads and runs `dexdump -f`.
   - Asserts provenance matching: verifies Schema Version 1, recomputes exact SHA-256 hash and byte size of the `.apk`, and checks build/source metadata consistency.

3. **Tester Evidence Generation and Promotion Contracts (`TesterApkArtifactEvidenceTests`)**:
   - Asserts ValidationReceipt Schema v1 serialization with `profile: "Tester"`, `status: "ValidatorApproved"`, `isDistributable: false`, exact artifact SHA-256, staged provenance SHA-256, signer certificate SHA-256, and signer classification `development-debug`.
   - Asserts deterministic `TESTER_README.md` generation documenting tester distribution boundaries, development-debug signing update invariants, and coexistence with production apps.
   - Asserts profile-aware `SHA256SUMS` generation containing exactly four ordinally sorted entries for the `.apk`, provenance JSON, validation receipt JSON, and tester README (lowercase SHA-256, two-space delimiter, LF endings).
   - Asserts atomic directory promotion (`Directory.Move`) to `artifacts/android/tester/<ArtifactId>/` requiring exactly five promoted files and cleaning up staging directories.

4. **Tester APK Workflow Integration Contracts (`TesterApkWorkflowIntegrationTests`)**:
   - Asserts `AndroidPackageCommand` handles `--profile Tester` end-to-end: evaluating MSBuild properties, executing `dotnet publish`, writing provenance, validating the staged APK, generating evidence files, and promoting the five-file directory atomically.
   - Asserts fail-closed behavior on build failure, manifest violation, unsigned APK, signature verification error, provenance mismatch, or destination collision.
   - Asserts `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1` are thin wrappers delegating to `MathFirst.ReleaseTool`.

### Historical Verified Automated Test Evidence (Tester APK Workflow Baseline)

- **Full Core Automated Suite**: 1462 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Targeted Suite Breakdown**:
  - `TesterApkPackagingContractTests`: 19 passed
  - `AndroidApkValidationTests`: 42 passed
  - `TesterApkArtifactEvidenceTests`: 16 passed
  - `TesterApkWorkflowIntegrationTests`: 15 passed
  - `AndroidPackagingContractTests`: 18 passed
  - `AabPackagingScriptValidationTests`: 23 passed
  - `AndroidAabValidationTests`: 48 passed

### Evidence Boundary Principles
- All automated testing executes against synthetic packages, mocked tool processes, and isolated temporary filesystems.
- It does **not** claim real packaging execution during tests.
- It does **not** claim physical APK installation, ADB interaction, emulator testing, or device validation.
- Full validation (`FULL_VALIDATION`), production packaging, and Google Play publication remain separate authorized lifecycle stages.

---

## 17. MF-UX-005 Release Size Hygiene and Timer Remediation (Historical Automated Evidence)

These results belong to their respective completed changes. They are not a new `FULL_VALIDATION` run for a later production candidate.

### PR #37 — Release Bootstrap Source-Map Exclusion

- `AndroidPackagingContractTests`: 9/9 passed in Release configuration.
- Full `MathFirst.Core.Tests` Release suite: 1463/1463 passed.
- The contract verifies the Release-only `Content Remove` rule for `wwwroot\lib\bootstrap\dist\css\bootstrap.min.css.map` while retaining the runtime Bootstrap CSS.

### PR #38 — Timer Visual Remediation

- `TeachingLockAndVisualFeedbackTests`: 9/9 passed in Release configuration.
- Full `MathFirst.Core.Tests` Release suite: 1463/1463 passed.
- The contract verifies bold white tabular Timer text with a restrained local dark shadow/contour, no backing pill, and no heavy text stroke while retaining timer rendering isolation and timing behavior.

### Automated Evidence Boundary

- Automated evidence is separate from physical-device observations.
- These historical targeted and full-suite results do not constitute the pending exact-candidate `FULL_VALIDATION` lifecycle.
- They do not establish production packaging, signing, release-grade physical validation, or Google Play readiness.

---

## 18. MF-UX-005 Timer-Specific Physical Evidence

The physical target for this bounded `TEST_ONLY` run was a Samsung SM-S948B running Android 16 on arm64-v8a. No One UI version was recorded.

Verified observations:

- Replacement Tester APK installation succeeded.
- Existing app and learner data were preserved.
- The Timer backing pill was absent.
- White Timer digits and restrained dark local contrast were visible.
- Timer centering was preserved.
- Light-theme Timer presentation was checked.
- Dark-theme Timer presentation was checked.
- No Time Pressure retained a visible elapsed count-up Timer presentation.
- The user explicitly accepted the Timer result.

### Physical Evidence Boundary

- This evidence is limited to the Timer-specific run above.
- It is not final production-candidate certification and does not replace `FULL_VALIDATION`, production packaging/signing, or final release-grade physical validation.

---

## 19. MF-UX-006 Privacy, Copy, and Localization Hardening Contracts & Full Validation Evidence

MF-UX-006 contract coverage validates localization string integrity, reset copy semantics, offline in-app privacy navigation, zero-network permissions, and static multilingual host fallback across automated test suites in `MathFirst.Core.Tests`:

1. **Reset Copy & Objective Localization Contracts (`PolicyAndLocalizationTests`)**:
   - Asserts Restore Default Settings copy across English, German, and Russian explicitly specifies PC numpad (`Keypad_Numpad`) default layout restoration.
   - Asserts Russian haptic feedback confirmation format string avoids duplicate period punctuation.
   - Asserts Russian operation progress HUD accessibility label uses learner-facing progression terminology (`Стадия прогресса`).
   - Asserts English, German, and Russian localization key parity and placeholder format token consistency across all dictionary entries.

2. **In-App Privacy Surface & Navigation Contracts (`PolicyAndLocalizationTests`, `AppBackNavigationTests`)**:
   - Asserts presence and structure of dedicated `/privacy` Blazor route with complete EN/DE/RU key coverage across Overview, No Remote Collection or Sharing, Local Storage and Device Transfer, Removing Local Data, and Contact sections.
   - Asserts total absence of remote network fetch primitives, external HTTP links, or third-party tracking references in the privacy surface.
   - Asserts Settings privacy entry point and navigation action contract.
   - Asserts `IAppBackNavigationCoordinator` integration handling system Back from `/privacy` to `/settings`.

3. **Android Zero-Network & Release Packaging Regression (`AndroidPackagingContractTests`)**:
   - Asserts continuous absence of `android.permission.INTERNET` and `android.permission.ACCESS_NETWORK_STATE` permissions in `AndroidManifest.xml`.
   - Asserts backup and data extraction rules remain restrictive and conformant to ADR-0006.

4. **Static Fatal Host Fallback Contracts (`NativeVisualIdentityContractTests`, `StartupWhiteFlashTests`)**:
   - Asserts `src/MathFirst.App/wwwroot/index.html` static fatal host fallback contains language-neutral error title and static reload links in English (`Reload`), German (`Neu laden`), and Russian (`Перезагрузить`).
   - Asserts fallback operates purely via static HTML markup without runtime localization or Blazor dependencies.
   - Asserts preservation of brand-green startup background handoff (`#176B4D`) and dark mode first-paint styles.

### Historical Slice-Level Test Evidence

- **Slice 1 (Objective Localization & Reset Copy)**:
  - `PolicyAndLocalizationTests`: 54/54 passed
  - Adjacent targeted regression (`OnboardingAndProgressFeedbackTests`, `PracticeVisibilityAndTimerLifecycleTests`): 99/99 passed
- **Slice 2 (Offline Privacy Surface & Back Navigation)**:
  - `PolicyAndLocalizationTests` + `AppBackNavigationTests`: 72/72 passed
  - `AndroidPackagingContractTests`: 9/9 passed
  - Adjacent workflow tests: 64/64 passed
- **Slice 3 (Fatal Host Fallback Hardening)**:
  - `NativeVisualIdentityContractTests`: 5/5 passed
  - `StartupWhiteFlashTests`: 5/5 passed
  - Combined Policy / Back / Android Packaging: 81/81 passed

### Exact-Candidate FULL_VALIDATION Evidence

- **Candidate Commit SHA**: `606158a233d7cface85fa0ef7bd03c2f9ef4f4cb`
- **Validation Result**: `FULL_VALIDATION_PASS`
- **Core Test Suite**: 1476 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`)
- **Windows Release Build**: 0 warnings, 0 errors (`net10.0-windows10.0.19041.0`)
- **Android Release Build**: 0 warnings, 0 errors (`net10.0-android36.0`)
- **NuGet Vulnerability Audit**: 0 vulnerable packages across the solution
- **NuGet Deprecation Audit**: 0 deprecated packages in application/runtime/tooling projects; `MathFirst.Core.Tests` references legacy deprecated `xunit 2.9.3` (NuGet alternative: `xunit.v3`)
- **Repository Markdown Link Integrity**: All relative links valid across 27 inspected Markdown files
- **ADR Registry Integrity**: ADR-0001 through ADR-0007 verified present
- **Candidate Diff & Whitespace Audit**: Clean `git diff --check` against base `60dba236aa38bca138ab583f610c9ff876994b04`
- **Merge Integration**: Merged to `main` via PR #40 at `ae69f4ae27397fc6edf36a23bb671b0410680be1`
- **Tree Hash Identity**: Candidate tree `739fed4c0e9ea3565c096d95ba9d7aca114e76bc` == Merge tree `739fed4c0e9ea3565c096d95ba9d7aca114e76bc`

### Evidence Boundary Principles

- All automated tests run offline against synthetic fixtures, source files, and isolated test environments.
- The merged content is Git-tree-identical to the exact candidate that passed `FULL_VALIDATION`; the merge commit itself was not separately retested.
- Automated tests do **not** prove rendered visual appearance on native devices, native screen-reader accessibility behavior, or physical hardware execution.
- Production packaging, signing, and Google Play publication remain separate authorized lifecycle stages.

---

## 20. MF-STAB-003 Enabled-Subset Scheduling and Current-Fact Reconciliation Contracts

MF-STAB-003 contract, regression, and integration coverage validates independent per-operation role ordinals, durable count reconstruction in Schema V6, and authoritative Settings/current-fact reconciliation across automated test suites in `MathFirst.Core.Tests`:

1. **Exact 26-Addition $\to$ Subtraction-Only Restart Regression (`SqliteEnabledSubsetPersistenceTests`, `AuthorityHardeningAndRecoveryInvariantTests`)**:
   - Asserts that practicing Addition for 26 accepted attempts, disabling Addition in favor of Subtraction only, and restarting the application begins Subtraction at Ordinal 1 (`PracticeSelectionRole.New`), resolving the physical-device release blocker.
   - Asserts that Subtraction introduces valid `sub:0-0` rather than skipping to ordinal 27 or querying non-existent Due/Frontier material.

2. **Per-Operation Role-Cycle Independence & Ordinal Authority (`AuthorityHardeningAndRecoveryInvariantTests`, `IndependentSelectorTests`, `DeterministicSelectorTerminalLivenessTests`)**:
   - Asserts `AdaptivePracticeSelector` requires explicit positive `ScheduledOperationAttemptOrdinal` in `PracticeSelectionContext` and resolves requested roles exclusively via $\text{AcceptedAttemptCount}(O) + 1$.
   - Asserts the 10-slot role cycle: 1 New, 2 Due, 3 New, 4 Maintenance, 5 Frontier, 6 New, 7 Due, 8 New, 9 Due, 10 Frontier, mapping $i = (\text{ordinal} - 1) \bmod 10$ to `New` at $\{0, 2, 5, 7\}$, `Due` at $\{1, 6, 8\}$, `Maintenance` at $\{3\}$, `Frontier` at $\{4, 9\}$.
   - Asserts elimination of the legacy global fallback formula based on `ProspectivePracticePosition`.
   - Asserts independent role-cycle progression across all four operations without cross-operation coupling.
   - Asserts strict authority boundaries: `AcceptedAttemptCount(O)` is authoritative strictly for that operation's role ordinal, while global `PracticePosition` governs attempt ordering, permutation bag scheduling, FSRS virtual time, and persistence validation. Existing coverage, pace, fluency, progression, and item-state authorities remain untouched.

3. **Durable Count Reconstruction & Schema V6 Preservation (`SqliteLearnerStore`, `LearnerSnapshot`, `AuthorityHardeningAndRecoveryInvariantTests`)**:
   - Asserts `AcceptedAttemptCount(O)` is reconstructed from `SUM(item_learning_state.total_attempts WHERE operation = O)`.
   - Asserts Schema V6 persistence format is preserved without schema migration or redundant counter columns.
   - Asserts fail-closed behavior on null snapshot counts or empty item states with non-zero total attempts.
   - Asserts open-ended arithmetic support and `Int32` checked arithmetic safety without artificial ~400-fact catalog caps.

4. **Persistence Invariants & Recovery Safety (`AuthorityHardeningAndRecoveryInvariantTests`)**:
   - **Exact-Once Commit Increment**: Successful persistence commits increment operation attempt counts by exactly 1.
   - **Duplicate Replay Idempotency**: Replaying an already-committed `SubmissionId` returns success without incrementing attempt counts.
   - **Persistence Failure Rollback**: Failed database writes do not mutate in-memory attempt counts.
   - **Revision-Conflict Recovery**: Store revision conflicts trigger clean reload of authoritative attempt counts from SQLite.
   - **Reset Invariants**: `Reset Learning Progress` produces zero attempt counts across all operations; `Full Local Reset` clears counts and restores default preferences.
   - **Subset Continuity**: Disabling and subsequently re-enabling an operation preserves all attempt history, item strength, FSRS state, and attempt counts.
   - **Global FSRS Time Preservation**: Global `PracticePosition` advances monotonically across subset configurations without losing virtual time alignment.

5. **Authoritative Settings & Current-Fact Reconciliation (`PracticeConfigurationReconciliationTests`)**:
   - **Zero-Mutation Replacement (Case A / Case C)**: When an unsubmitted fact becomes invalid (operation disabled or fact ineligible), `TrainingSession.ReconcilePracticeConfigurationAsync` discards the fact and immediately prepares a valid replacement for the same prospective global `PracticePosition`. Discarding creates zero attempt records, no timeout, no score mutation, no FSRS card mutation, no progression increment, and no attempt-count mutation.
   - **Retained Valid Fact (Case B)**: When an unsubmitted fact remains enabled and eligible, reconciliation preserves its exact identity, `FactInstanceRevision`, partial answer input (`CurrentAnswerInput`), and remaining paused timer deadline.
   - **Accepted-Feedback Deferral**: When an answer has been accepted and the session is in `CorrectFeedback`, `IncorrectFeedback`, `TimeoutFeedback`, `TeachingIntervention`, or `SessionCheckIn`, reconciliation defers next-fact preparation until explicit feedback dismissal, preserving accepted learner state.
   - **Evidence Cache Invalidation**: Reconciliation clears cached evidence for disabled operations and reloads required evidence asynchronously.
   - **Awaited Navigation & Recovery**: `Settings.razor` awaits session reconciliation before navigation; failure surfaces a recoverable error while preserving persisted preferences for retry or restart.
   - **Subset Coverage**: Validates single-, two-, three-, and four-operation configurations.

### Reviewed Test Suite Evidence (MF-STAB-003 Task Branch Baseline)

- **Full Core Test Suite**: 1,516 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Targeted Suite Breakdown**:
  - `AuthorityHardeningAndRecoveryInvariantTests`: 20 passed
  - `PracticeConfigurationReconciliationTests`: 19 passed
  - `SqliteEnabledSubsetPersistenceTests`: 14 passed
  - `StartupRecoveryRegressionTests`: 14 passed
  - `IndependentSelectorTests`: 8 passed
  - `DeterministicOperationSchedulerTests`: 18 passed
  - `DeterministicSelectorTerminalLivenessTests`: 10 passed
  - `StaleSelectionEvidenceRemediationTests`: 10 passed
  - `PolicyAndLocalizationTests`: 56 passed

### Evidence Boundary Principles

- Automated test suites execute offline against synthetic fixtures and temporary SQLite databases.
- The 1,516 passing tests represent verified implementation on task branch `feat/mf-stab-003-enabled-subset-scheduling-current-fact-reconciliation` during `REVIEW_ONLY` and do not claim execution of the pending exact-candidate `FULL_VALIDATION` lifecycle.
- Automated tests do not constitute physical hardware revalidation (Steps 30/31).

---

## 21. MF-LEARN-004 Guided Four-Operation Number-Space Gate Contracts & Test Evidence

MF-LEARN-004 contract, regression, persistence, and session integration coverage validates the cross-operation number-space gate, streaming candidate anti-poisoning in SQLite, and dynamic practice-configuration reconciliation across 1,590 automated tests in `MathFirst.Core.Tests`:

1. **Guided Mode vs. Custom Mode Gate Invariants (`GuidedNumberSpaceGateTests`, `GuidedNumberSpaceSelectionTests`)**:
   - Asserts Guided Mode is active if and only if all four operations are enabled (`Addition`, `Subtraction`, `Multiplication`, `Division`).
   - Asserts Custom Mode is active whenever any other valid subset is selected (e.g. Multiplication + Division, Multiplication only, Addition + Subtraction); cross-operation gating is inactive and operations advance without an Addition ceiling.
   - Asserts the Addition Ceiling formula calculates the maximum represented number across all facts in the complete unlocked canonical Addition curriculum prefix ($b \in [0, B_{\text{ADD}}]$):
     $$\text{AdditionCeiling} = \max_{b \in [0, B_{\text{ADD}}]} \left( \max_{F \in \text{Frontier}(b)} \left( \max(F.\text{LeftOperand}, F.\text{RightOperand}, F.\text{CorrectResult}) \right) \right)$$
   - Asserts Multiplication presentation eligibility: presentable in Guided Mode iff $\text{owner}_{\text{MUL}}(F) \le B_{\text{MUL}}$ AND $F.\text{CorrectResult} \le \text{AdditionCeiling}$.
   - Asserts Division presentation eligibility: presentable in Guided Mode iff $\text{owner}_{\text{DIV}}(F) \le B_{\text{DIV}}$ AND $F.\text{LeftOperand} \le \text{AdditionCeiling}$ (dividend is the total quantity partitioned).
   - Asserts Addition establishes the ceiling and is not cross-operation gated; Subtraction operates in the same elementary number space and is not cross-operation gated.
   - Asserts pure domain factory projection `PracticeSelectionEvidence.ForGuidedNumberSpace(...)` filters in-memory review candidate pools and unmaterialized frontier facts without mutating durable state.

2. **Candidate Streaming Anti-Poisoning & State Preservation (`GuidedNumberSpacePersistenceTests`)**:
   - Asserts `SqliteLearnerStore.ReadCandidateRowsAsync` applies the `isEligible` predicate directly during row streaming before candidate buffer capping (64 rows per semantic pool), guaranteeing that eligible reviews are never starved or displaced by preceding ineligible future or gated facts.
   - Asserts snapshot fallback `PracticeSelectionEvidence.FromSnapshot` applies identical eligibility filtering.
   - Asserts that gated facts remain dormant in SQLite storage with full attempt history, item learning states, and FSRS intervals preserved losslessly; advancing the Addition ceiling or switching to Custom Mode dynamically unlocks them.
   - Asserts `SqliteLearnerStore.ValidateNewAcceptedSubmission` enforces presentation eligibility at persistence commit time (`PersistenceResult.InvalidSubmission`).

3. **Session Lifecycle, UI Interaction Deferral & Settings Reconciliation (`GuidedNumberSpaceSessionTests`)**:
   - Asserts `TrainingSession.ReconcilePracticeConfigurationAsync` discards an unsubmitted active problem that becomes Guided-ineligible upon Settings modification, preparing a valid replacement at the same prospective practice position with zero learning mutations (no score change, no timeout, no FSRS update, no position increment).
   - Asserts valid problems preserve partial answer buffer input and remaining paused timer deadline across Settings visits.
   - Asserts interaction-state deferral: when an answer is submitted and the session is in a feedback or intervention state (`CorrectFeedback`, `IncorrectFeedback`, `TimeoutFeedback`, `TeachingIntervention`, `SessionCheckIn`), reconciliation defers next-fact replacement until explicit feedback dismissal.
   - Asserts that gated facts in remediation (`NeedsRemediation == true`) or due status are closed to presentation until unlocked by Addition progress.
   - Asserts restart and crash recovery re-evaluates the Guided Gate from durable state without ephemeral session leaks.

4. **Integration Simulation Coverage & Test-Helper Maintenance (`GuidedNumberSpaceSessionTests`, `BoundedSelectionIntegrationTests`)**:
   - Validates end-to-end multi-step simulations across diverse learner behaviors:
     - Guided all-four / 100 correct simulation: Multiplication and Division progress in lockstep as Addition unlocks expanding number spaces.
     - Guided Addition always wrong: Multiplication and Division remain strictly capped at the initial Addition ceiling (e.g. Addition ceiling 2; Multiplication presents only $1 \times 1 = 1$, $1 \times 2 = 2$, $2 \times 1 = 2$; Division presents only $1 \div 1 = 1$, $2 \div 1 = 2$, $2 \div 2 = 1$; higher facts like $2 \times 2 = 4$ or $4 \div 2 = 2$ cannot be presented).
     - Guided Addition 50% mixed errors / slow progression.
     - Custom Mode (Multiplication + Division only / 100 correct): Multiplicative operations advance freely without an Addition ceiling gate.
   - Corrected test helper in `BoundedSelectionIntegrationTests.cs` to resolve requested roles via per-operation attempt ordinals ($\text{AcceptedAttemptCount}(O) + 1$) rather than legacy global practice position, aligning tests with the authoritative scheduling architecture.

### Reviewed Test Suite Evidence (MF-LEARN-004 Task Branch Baseline)

- **Full Core Test Suite**: 1,590 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Targeted MF-LEARN-004 Breakdown (73 new tests)**:
  - `GuidedNumberSpaceGateTests`: 17 passed
  - `GuidedNumberSpaceSelectionTests`: 10 passed
  - `GuidedNumberSpacePersistenceTests`: 17 passed
  - `GuidedNumberSpaceSessionTests`: 29 passed
- **Adjacent Verified Test Suites**:
  - `BoundedSelectionIntegrationTests`: passed
  - `SqliteEnabledSubsetPersistenceTests`: passed
  - `StartupRecoveryRegressionTests`: passed
  - `FinalIntegrationCoverageTests`: passed
  - `LongRunIndependentProgressionTests`: passed

### Evidence Boundary Principles & Downstream Release State

- All 1,590 automated tests execute offline against synthetic fixtures, pure domain models, and temporary SQLite databases.
- Automated tests do not constitute physical hardware revalidation.
- Build 3 (`MathFirst-v1.0-b3-739fed4-release.aab` / `MathFirst-v1.0-b3-739fed4-Distributable-evidence.zip`) is historical evidence; its source tree predates MF-LEARN-004.
- Any future packaging candidate requires `versionCode >= 4`. Build 4 does not exist yet; no release packaging, signing, AAB/APK build, or Google Play upload is claimed.

---

## 22. MF-LEARN-005 Adaptive Practice Balance and Foundational Coverage Contracts & Test Evidence

MF-LEARN-005 contract, unit, integration, persistence, and long-run simulation coverage validates protected New material acquisition, remediation authority preservation, same-operation diversity, and fallback liveness across 1,604 automated tests in `MathFirst.Core.Tests`:

1. **Protected New Acquisition & Remediation Exhaustion Fallback (`IndependentSelectorTests`)**:
   - Asserts that when `requestedRole == PracticeSelectionRole.New` and the active introduction frontier has at least one eligible unmaterialized fact, remediation does *not* preempt New introduction, proceeding through normal New fallback.
   - Asserts remediation-exhaustion fallback: when the New frontier is exhausted (all owned material has been materialized or no eligible unseen candidate exists), an eligible remediation candidate preempts requested New if available.
   - Asserts that for non-New roles (`Due`, `Maintenance`, `Frontier`), remediation precedence is preserved: an eligible remediation candidate preempts the requested role provided spacing $\ge 4$ from previous remediation presentations is satisfied.
   - Asserts that remediation preemption remains subject to the $\ge 4$ attempt spacing constraint across all operational states.

2. **Same-Operation Diversity & Liveness Relaxation (`IndependentSelectorTests`)**:
   - Asserts strict selection tier enforces same-operation diversity: candidate filtering avoids repeating the immediately preceding same-operation `FactId` whenever another viable candidate exists in the candidate set.
   - Asserts liveness relaxation fallback: when the candidate pool contains only the single preceding same-operation fact (or no other viable candidate remains), the selector relaxes the diversity constraint to guarantee selection liveness and prevent deadlocks or unserved positions.

3. **Multi-Operation Sustained-Failure Integration & SQLite Restart (`PracticeBalanceIntegrationTests`)**:
   - Validates multi-operation sustained-failure sessions under Guided Mode:
     - Guided 100% failure on Addition (0% correct) does not trap the session; Addition, Subtraction, Multiplication, and Division progress through their respective balanced cycles without unmaterialized starvation.
     - Guided 100% failure on Multiplication (0% correct) preserves overall session throughput and introduces new foundational facts appropriately.
   - Validates Custom Mode sustained-failure sessions:
     - Custom Mode with 100% failure on Addition (0% correct) maintains balanced selection and introduces new facts across enabled operations.
   - Validates SQLite persistence and restart under struggle:
     - Session state, item learning states, attempts, and scheduler progress persisted to SQLite during sustained struggle recover correctly across engine restart without state corruption, duplicate positions, or lost fact introductions.
   - Validates 500-position deterministic replay asserting identical sequence of `PracticePosition`, `Operation`, `FactId`, and `RequestedRole` across independent runs with identical initial seeds and response profiles.

4. **Long-Run Multi-Operation Failure Matrix Simulations (`PracticeBalanceIntegrationTests`)**:
   - Validates long-run multi-operation scenarios across diverse error profiles:
     - Guided Addition 50% mixed errors / slow progression.
     - Guided Addition 25% mixed errors.
     - Guided all-0% failure across 1,000 attempts: asserts that all 13 initial eligible foundational facts across the four operations were introduced (Addition: 4, Subtraction: 3, Multiplication: 4, Division: 2) despite zero correct answers.
     - Guided all-100% success baseline.
     - Guided alternating 50% mixed performance.
     - Custom Mode Multiplication + Division asymmetric progression.

### Reviewed Test Suite Evidence (MF-LEARN-005 Task Branch Baseline)

- **Full Core Test Suite**: 1,604 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Package-Focused Selector Regression**: 77 passed, 0 failed, 0 skipped (across `AdaptiveReviewStabilizationTests`, `PracticeSequenceDiversityTests`, and `IndependentSelectorTests`).
- **PracticeBalanceIntegrationTests**: 11 passed, 0 failed, 0 skipped (Guided Addition 0%, Guided Multiplication 0%, Custom Addition 0%, SQLite restart under struggle, 500-position deterministic replay, and 6-scenario long-run failure matrix).
- **Package-Relevant Regression Suite**: 201 passed, 0 failed, 0 skipped (across scheduler, selector, gate, persistence, and balance integration suites).

### Evidence Boundary Principles & Downstream Validation State

- All 1,604 automated tests execute offline against synthetic fixtures, pure domain models, and temporary SQLite databases.
- The 1,604 passing tests represented verified implementation on task branch `codex/mf-learn-005-adaptive-practice-balance` during `REVIEW_ONLY` (`REVIEW_APPROVED`).
- MF-LEARN-005 subsequently achieved exact-candidate `FULL_VALIDATION_PASS` (candidate `dd4b1b48f67cbb333f913eb2ec6aa37e895a8ebf`) and was merged to `main` through Pull Request #46 at merge commit `c7fea74554abe01181b7a0e3d3c4e554c48f7d1a`.
- Testing on physical hardware (`Samsung Galaxy S26 Ultra`) remains scheduled for downstream tester APK validation phases.

---

## 23. MF-UX-007 Progress Presentation Cleanup Contracts & Test Evidence

MF-UX-007 localization, semantic structure, responsive styling, and accessibility contracts are validated across 1,605 automated tests in `MathFirst.Core.Tests`:

1. **Progress Localization & Terminology Parity (`PolicyAndLocalizationTests`)**:
   - Asserts exact key and placeholder parity across English, German, and Russian for concise Stage terminology (`Training_ProgressStageDisplay`), full localized Stage descriptions (`Training_OperationProgressStage`), unavailable fallbacks (`Training_OperationProgressUnavailable`), and learner-facing HUD group labeling (`Training_OperationProgressGroupAriaLabel`).
   - Asserts elimination of internal diagnostic jargon ("progression stage", "Fortschrittsstufe", "этап прогресса") in favor of natural learner-facing wording ("Stage", "Stufe", "уровень") consistent with Session Check-In terminology.

2. **Returning Learner Ready Overview Contracts (`OnboardingAndProgressFeedbackTests`, `ResponsiveAndCorrectAnswerFlowTests`)**:
   - Asserts that the progress overview on the initial Ready Gate renders strictly when `Session.PracticeGate == PracticeGateState.InitialReadyGate && Session.HasCompletedPracticeHistory`.
   - Asserts structured semantic list markup (`role="list"`, `role="listitem"`, `aria-label="@progressLabel"`) with dedicated operation symbol, localized name, and Stage display classes (`ready-progress-symbol`, `ready-progress-name`, `ready-progress-stage`).
   - Asserts decorative mathematical glyphs are hidden from assistive technology (`aria-hidden="true"`).
   - Asserts that fresh learners receive no fabricated progress overview.

3. **Active Practice HUD Accessibility & Responsive Contracts (`ResponsiveAndCorrectAnswerFlowTests`)**:
   - Asserts compact visible presentation format (symbol + numeric Stage).
   - Asserts HUD container uses `role="group"` with `aria-label="@Localizer[\"Training_OperationProgressGroupAriaLabel\"]"`, removing internal diagnostic labels.
   - Asserts individual HUD entries provide complete localized context via `aria-label` and native tooltip `title`.
   - Asserts responsive grid rules in CSS for 1, 2, 3, and 4 operations, including 2-column mobile wrapping for 3 and 4 operations on viewports $\le 480\text{ px}$.

### Reviewed Test Suite Evidence (MF-UX-007 Task Branch Baseline)

- **Full Core Test Suite**: 1,605 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Focused Package Review Suite**: 120 passed, 0 failed, 0 skipped (across `PolicyAndLocalizationTests`, `OnboardingAndProgressFeedbackTests`, and `ResponsiveAndCorrectAnswerFlowTests`).
- **Windows Release Build**: Succeeded with 0 warnings and 0 errors (`net10.0-windows10.0.19041.0`).

### Evidence Boundary Principles & Downstream Validation State

- All 1,605 automated tests execute offline against synthetic fixtures, pure domain models, and temporary SQLite databases.
- The 1,605 passing tests represent verified implementation on task branch `codex/mf-ux-007-progress-presentation-cleanup` during `REVIEW_ONLY` (`REVIEW_APPROVED`).
- Automated tests prove semantic source, DOM roles, CSS selectors, and localization strings. They do not prove final candidate `FULL_VALIDATION`, Android Release build for the docs-final candidate, ReleaseTool verification, physical rendering on Samsung Galaxy S26 Ultra, or live screen-reader pronunciation.
- Testing on physical hardware (`Samsung Galaxy S26 Ultra`) remains scheduled for downstream tester APK validation phases.

---

## 24. P2 Direct-to-Practice Startup and Onboarding Removal Contracts & Reviewed Test Evidence

The P2 implementation eliminates the multi-step onboarding wizard and router gate, launching fresh learners directly into active Addition practice while preserving the Initial Ready Gate and progress overview strictly for returning learners with completed practice history. This behavior is covered by automated contract and regression suites in `MathFirst.Core.Tests` and `MathFirst.App`:

1. **Direct-to-Practice Startup Contracts (`DirectToPracticeStartupTests`)**:
   - **Fresh Learner Direct Startup**: Asserts fresh learners (without completed practice history) bypass all onboarding and Initial Ready Gate steps, landing directly in active Practice on `/`.
   - **Addition-Only Initial Preference**: Asserts initial operation preferences default strictly to `[OperationType.Addition]`.
   - **Timing Activation on Surface Interaction**: Asserts active interaction timing begins only after the practice surface mounts and activates.
   - **Returning Learner Gate Preservation**: Asserts returning learners with accepted practice history encounter the Initial Ready Gate and progress overview before practice resumes.

2. **Absence Guards & Production Hygiene**:
   - **`OnboardingHost` Absence**: Asserts complete removal of `OnboardingHost.razor` from the component tree.
   - **Localization Resource Absence**: Asserts removal of all historical `Onboarding_*` string resources across English, German, and Russian dictionaries (`Resources.resx`, `Resources.de.resx`, `Resources.ru.resx`).
   - **CSS Absence**: Asserts complete removal of `.onboarding-host` and related onboarding styles from application stylesheets.
   - **Preference Store API Absence**: Asserts removal of `GetOnboardingCompleted` and `SetOnboardingCompleted` from `IPreferenceStore`, removal of `OnboardingKey` and obsolete preference accessors from `MauiPreferenceStore`, and removal of corresponding members from test doubles.
   - **Router Gate Absence**: Asserts `Routes.razor` contains zero onboarding redirect gates or preference checks.

3. **Reset Lifecycle Contracts (`ResetWorkflowTests`, `DirectToPracticeStartupTests`)**:
   - **Reset Learning Progress**: Clears learner attempt history, item states, FSRS states, and progression while preserving UI preferences; no onboarding state exists.
   - **Reset UI Preferences (Restore Default Settings)**: Restores UI and practice preferences to defaults (Addition only, Standard time, Numpad keypad, System theme, System language, haptics enabled), preserves learner progress/history, and navigates to `/` without onboarding.
   - **Full Local Reset**: Clears all learner progress, restores all preferences to defaults (including Addition only), purges telemetry share cache, clears the persistent installation ID (a later consumer lazily creates a fresh ID through the normal provider lifecycle), and navigates to `/` into direct Addition practice without onboarding.

4. **Operation Preference and Domain Fallback Boundary Contracts (`PracticeConfigurationTests`, `PracticeOperationPreferencePolicy`)**:
   - **Application Preference Default**: Fresh installations and preference resets default strictly to Addition only (`[OperationType.Addition]`). This policy is enforced at the application/preference store boundary via `MauiPreferenceStore` missing-key semantics.
   - **Generic Domain Fallback**: `PracticeOperationPreferencePolicy.NormalizeEnabledOperations(null)` and `NormalizeEnabledOperations(empty)` normalize to `AllOperations` (`[Addition, Subtraction, Multiplication, Division]`). The generic domain fallback represents domain-level safety for unconfigured callers and must not be coupled to application-level fresh-user defaults.
   - **Explicit Subsets**: Explicit non-empty subsets (e.g. `[Addition, Subtraction]`) remain preserved without modification.

### Evidence Layers & Validation History (P2 Task Branch)

#### A. Original P2 Implementation & Review Evidence
- **Slice-Level Evidence**:
  - Slice 1 (Addition-only default): 59 passed, 40 targeted regressions passed
  - Slice 2 (Direct Practice startup & returning gate): 7 passed, 28 lifecycle passed, 85 timer/flow passed
  - Slice 3 (Router gate & reset flow): 38 passed, 28 startup regressions passed, MathFirst.App Windows build (0 warnings / 0 errors)
  - Slice 4 (Asset & localization cleanup): 120 passed, 116 passed, 13 passed, MathFirst.App Windows build (0 warnings / 0 errors)
  - Slice 5 (Preference API removal & absence guards): 1 contract passed, Core test build (0 warnings / 0 errors), 97 preference regressions passed, 54 startup/recovery regressions passed, MathFirst.App Windows build (0 warnings / 0 errors)
- **Original Review Verdict**: `P2_REVIEW_APPROVED` (184 targeted tests passed, 0 failed, 0 skipped; 0 Blocker, 0 Major, 2 non-blocking Minor findings).

#### B. Failed Formal FULL_VALIDATION (Candidate `42d8c0884ad3350cad5774ecd5b0098d13ed3e74`)
- **Target Candidate**: `42d8c0884ad3350cad5774ecd5b0098d13ed3e74` (documentation synchronization checkpoint).
- **Debug Full Core Suite**: 1,975 tests completed before abort; 1,941 passed; 34 failed; 0 skipped; exit code 1. The 5-minute blame-hang inactivity detector aborted the testhost during `DeterministicSelectorTerminalLivenessTests.SelectorTotalityProperty_ValidReachableLearnerStatesAlwaysReturnDeterministicFact` (its test-side scheduling search could not encounter non-Addition operations because the generic domain fallback had been narrowed to Addition-only, preventing loop termination). The 34 failures were distributed across multiple regression families. `LongRunIndependentProgressionTests` contributed five of those failures, including synthetic progression-evidence cases where the broken unparameterized operation schedule produced duplicate practice positions correctly rejected by validation; those LongRun failures were separate from the testhost hang.
- **Release Full Core Suite**: NOT EXECUTED (aborted early due to Debug full suite failure).
- **Windows Release Build**: Succeeded with 0 warnings and 0 errors (`net10.0-windows10.0.19041.0`).
- **Android Release Build**: Succeeded with 0 warnings and 0 errors (`net10.0-android36.0`).
- **NuGet Vulnerability Audit**: Succeeded with 0 vulnerable packages.
- **Focused P2 Release Confirmation**: 76 passed, 0 failed.
- **Overall Formal Verdict**: `FULL_VALIDATION_FAILED`. Partial build and audit successes do not override full test suite validation failure.

#### C. Validation-Fix Targeted Evidence (Candidate `085b929f058eb1ba4477f2bdc1412a09c218d648`)
- **Target Candidate**: `085b929f058eb1ba4477f2bdc1412a09c218d648` (`fix(practice): separate app defaults from domain fallback`).
- **Review Verdict**: `P2_VALIDATION_FIX_REVIEW_APPROVED`.
- **TDD RED Phase**: `PracticeConfigurationTests` reproduced three domain-boundary failures (missing/empty selection normalized to Addition-only instead of `AllOperations`; corrupt all-false recovery produced one operation instead of four; unparameterized scheduling defaulted away from canonical four-operation scheduling).
- **TDD GREEN Phase**:
  - `PracticeConfigurationTests`: 46 passed, 0 failed.
  - Previously failing regression families after correction:
    - `IndependentSelectorTests`: 52 passed, 0 failed
    - `GuidedNumberSpaceSelectionTests`: 10 passed, 0 failed
    - `DenseProgressionTests`: 27 passed, 0 failed
    - `FactEligibilityRegressionTests`: 23 passed, 0 failed
    - `LongRunIndependentProgressionTests`: 7 passed, 0 failed (duplicate-position failures resolved)
    - `DeterministicSelectorTerminalLivenessTests`: 57 passed, 0 failed (no hang)
    - `FinalIntegrationCoverageTests`: 2 passed, 0 failed
    - `BoundedSelectionIntegrationTests` + `SubmissionIntegrityAndPublishBoundaryTests` + `StaleSelectionEvidenceRemediationTests`: 33 passed, 0 failed
  - Focused P2 contract regression set: 77 passed, 0 failed.
- **Evidence Boundary Principles**: Targeted implementation evidence only. Overlapping test runs are not summed into fabricated test totals. This evidence does NOT constitute `FULL_VALIDATION`.
- **Validation & Merge State**: Corrected candidate `985012dfe6d0415bfa8e8c730ffa8f9fb548effd` passed formal full validation (`P2_FULL_VALIDATION_PASSED`) with 2,014 passing Core tests; merged into `main` via PR #63 at merge commit `1b485091755294221b6f242e174d99c168fc8e9d`.

---

## 25. P2b Gameplay and Startup Refinements Contracts & Reviewed Test Evidence

The bounded P2b package implements digit-scaled Cyber Defense critical hit timing based on answer length, aligns radar countdown arc and damage scoring to a single shared authority, and places fresh practice startup behind `InitialReadyGate` orientation without active timing before explicit Start. This behavior is covered by automated contract and regression suites in `MathFirst.Core.Tests` and `MathFirst.App`:

1. **Digit-Scaled Critical Hit Timing Contracts (`CyberDefenseCalibrationGateTests`, `Session`)**:
   - **Product Rule**: $\text{CriticalHitThresholdMs} = \text{CurrentFactEasyThresholdMs} \times \text{DigitCount}(\text{CurrentFact.CorrectResult})$.
   - **Boundary Scaling**: Asserts $1\times$ easy threshold for 1-digit results ($0, 9$), $2\times$ for 2-digit results ($10, 99$), $3\times$ for 3-digit results ($100, 999$), and $4\times$ for 4-digit results ($1000$).
   - **Pace Calibration Readiness Gate**: Asserts Critical Hits are awarded only after pace calibration readiness ($\ge 24$ timing-eligible positioned Correct attempts). Before calibration, all correct answers deal 1 HP normal damage regardless of response speed.
   - **Correct-But-Slow Damage**: Asserts correct answers outside the critical hit window deal standard 1 HP normal damage.

2. **Shared Radar Arc and Scoring Authority (`TrainingSession`, `CyberDefenseBattleScene`)**:
   - **Single Authority**: Asserts both the Cyber Defense radar countdown arc and the combat damage scoring decision consume `Session.CurrentFactCriticalHitThresholdMs`.
   - **Radar Visual Alignment**: Asserts the radar sweep/countdown visually matches the exact mathematical window allowed for a Critical Hit.

3. **Fresh Startup Ready Gate Orientation Contracts (`DirectToPracticeStartupTests`, `Home.razor`)**:
   - **Pre-Attempt Orientation (`InitialReadyGate`)**: Asserts both fresh learners and returning learners mount Practice behind `InitialReadyGate`.
   - **Fresh vs. Returning Presentation**: Asserts fresh learners (without completed practice history) see no progress overview, while returning learners see a concise progress overview of enabled operations.
   - **Active Timing Suppression Before Start**: Asserts active timing does not run before explicit Start (`IsTimingActive == false`, `ActiveElapsed == 0`, `IsInterrupted == false`).
   - **Running Practice Transition**: Asserts pressing explicit "Start" / "Resume" transitions to `PracticeGate.Running` and begins active interaction timing from zero.
   - **No Wizard Restoration**: Asserts zero onboarding wizard components, routes, or questionnaires are reintroduced.

4. **Strict Learning Non-Interference Boundary Contracts (`AdaptiveLearningPolicyFinalRegressionTests`, `TelemetryLearningNonInterferenceTests`)**:
   - Asserts digit-scaled Critical Hit timing is strictly a presentation and gameplay threshold.
   - Asserts zero mutation to underlying `CurrentFactEasyThresholdMs`, `CurrentFactFluencyThresholdMs`, `CurrentFactExpectedPaceMs`, `ResponseLatencyMs`, `AttemptOutcome`, `IsFluent`, `IsInterrupted`, `TimingEvidenceEligible`, `AdaptiveAttemptClassifier`, `FsrsRatingMapper`, FSRS card state, adaptive pace shrinkage, band progression, remediation, `PracticePosition`, telemetry export schema v2, or SQLite persistence.
   - Asserts that a multi-digit answer may exceed the normal learning Easy threshold (rated non-Easy / non-fluent) while earning a Cyber Defense Critical Hit; this separation is intentional and verified.

### Implementation & Review Test Evidence (Candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b`)

- **Candidate Commit**: `11181fe0d3e4b7c752e15815432f66b6c862a61b` (Slice 3 HEAD on `feat/p2b-gameplay-startup-refinements`).
- **Base Commit**: `1b485091755294221b6f242e174d99c168fc8e9d` (`main` after PR #63 merge).
- **Consolidated Review Verdict**: `P2B_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 1 Note regarding test helper naming).
- **Implementation Core Test Evidence**: **2,027 passed**, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Slice-Level Targeted Evidence**:
  - *Slice 1 (`33d3552fa619d24a759022096695ac069faa0260`)*:
    - `CyberDefenseCalibrationGateTests` GREEN: 20 passed, 0 failed, 0 skipped.
    - Targeted Cyber Defense / adaptive regressions: 319 passed, 0 failed, 0 skipped.
  - *Slice 2 (`cb07353507755712223db58539b48276302410e0`)*:
    - `CyberDefenseUiContractTests` + `CyberDefenseCalibrationGateTests`: 58 passed, 0 failed, 0 skipped.
    - CyberDefense filtered group: 79 passed, 0 failed, 0 skipped.
    - Adaptive rating / calibration / telemetry non-interference group: 49 passed, 0 failed, 0 skipped.
    - Full Core at Slice 2: 2,026 passed, 0 failed, 0 skipped.
  - *Slice 3 (`11181fe0d3e4b7c752e15815432f66b6c862a61b`)*:
    - `DirectToPracticeStartupTests`: 8 passed, 0 failed, 0 skipped.
    - Adjacent interruption / onboarding / reset / responsive group: 92 passed, 0 failed, 0 skipped.
    - Full Core: 2,027 passed, 0 failed, 0 skipped.
  - *Evidence Note*: Focused runs overlap across target filter boundaries and are reported independently without summing.

### Evidence Boundary Principles & Validation State

- **Implementation Evidence Boundary**: The 2,027 passing Core tests represent implementation and review evidence. They do **NOT** constitute formal exact-candidate `FULL_VALIDATION` (which requires complete dual Debug/Release execution, clean builds, NuGet security audit, and full repository hygiene checks).
- **Physical Device Boundary**: Pre-P2b observations on Samsung SM-S948B (Galaxy S26 Ultra, Android 16) motivated the P2b refinements; candidate `11181fe0d3e4b7c752e15815432f66b6c862a61b` has **NOT** been tested on physical hardware.
- **Delivery State**: P2b achieved exact-candidate `FULL_VALIDATION_PASS` and was merged into `main` via PR #64 at merge commit `f580a7154a4043a5097ffd852b5cf454be2cc397`.

---

## 26. P3 Cumulative Operation Unlock Progression Contracts & Reviewed Test Evidence

The P3 implementation establishes a cumulative arithmetic progression across four discrete stages in `PracticeMode.CurriculumManaged`, governed by monotonic persisted learner state, tolerant prerequisite D01 frontier readiness, and aggregate broad weakness gating. This behavior is covered by automated contract, regression, and simulation suites in `MathFirst.Core.Tests`:

1. **Cumulative Operation Unlock Progression Contracts (`CurriculumUnlockPolicyTests`, `CurriculumStage`)**:
   - **Four Cumulative Stages**:
     - Stage 1: Addition ($+$)
     - Stage 2: Addition & Subtraction ($+$, $-$)
     - Stage 3: Addition, Subtraction, & Multiplication ($+$, $-$, $\times$)
     - Stage 4: Addition, Subtraction, Multiplication, & Division ($+$, $-$, $\times$, $\div$)
   - **Tolerant Prerequisite D01 Readiness**: Stage transitions require full D01 frontier introduction (`IntroducedFactIds.ContainsAll(prerequisiteFrontier)`) and $\le 1$ prerequisite `NeedsRemediation` fact (or `BandIndex >= 1`).
   - **Aggregate Broad Weakness Gating**: Stage advancement is blocked if aggregate broad weakness is detected ($\ge 2$ eligible `NeedsRemediation` facts across all active operations). Broad weakness respects acquisition ownership, active progression, and effective Guided number space gates.
   - **Decoupled Non-Factors**: Operation unlock does not depend on chronological age, school grade, onboarding answers, arbitrary attempt counts, response latency, pace calibration readiness, Cyber Defense Critical Hit scoring, or fluency alone.

2. **Persistence Schema V9 & Conservative Migration Contracts (`SchemaV9MigrationTests`, `LearnerStore`)**:
   - **Schema V9 Invariants**: Persists `curriculum_stage` INTEGER NOT NULL DEFAULT 1 (`CHECK (curriculum_stage BETWEEN 1 AND 4)`) in table `learner_progression`.
   - **Atomic State Commit**: `CurriculumStage` is committed atomically alongside attempts, item learning states, operation progression, FSRS states, and `PracticePosition`. Persistence failures roll back completely without publishing an unlock.
   - **Conservative Migration**: V8 $\to$ V9 migration computes stage from cumulative historical evidence (BandIndex $\ge 1$ or full D01 introduction with $\le 1$ weak fact). UI preference booleans, historical Custom-mode access, and unearned Division attempts have zero stage migration authority.
   - **Dormant History Preservation**: Historical progression, FSRS states, and attempt records for locked operations remain preserved and resume seamlessly when the operation is unlocked.

3. **Monotonicity & Reset Lifecycle Contracts (`P3MonotonicityRegressionTests`, `ResetWorkflowTests`)**:
   - **Monotonic Progression (M1–M7 Properties)**: An earned `CurriculumStage` never regresses during normal learning. Emergent broad weakness blocks only advancement to the *next* stage and cannot relock the current stage.
   - **Reset Invariants**: Explicit learning-destructive resets (`Reset Learning Progress`, `Full Local Reset`) restore stage to Stage 1. UI preference resets (`Reset UI Preferences`) restore settings defaults without altering `CurriculumStage`.

4. **Practice Mode Separation & Settings Contracts (`SettingsUnlockContractTests`, `PracticeSession`)**:
   - **CurriculumManaged Mode**: Standard practice mode where active operations derive strictly from `CurriculumStage`. Operation controls in Settings render as read-only unlock indicators. Guided Number-Space Gate operates with Addition Ceiling coupling until G3 decoupling at `BandIndex >= 3`.
   - **Custom Mode**: Explicit diagnostic/testing/legacy mode where active operations derive from stored or programmatic operation preferences. Normal application Settings remains read-only for curriculum operation status; P3 does not expose interactive Custom-mode operation controls.
   - **Benchmark Isolation**: The 482 multi-operation benchmark suite executes under `PracticeMode.Custom` with all four operations enabled from start to isolate MF-LEARN-006 remediation authority from curriculum unlock gates.

5. **Learner Persona Simulations & Regression Coverage (`P3PersonaSimulationRegressionTests`, `P3SchedulerTransitionRegressionTests`, `P3LegacyDormantEvidenceRegressionTests`)**:
   - **Persona A (Struggling Beginner)**: Retained safely in Stage 1 Addition; broad weakness prevents premature Subtraction unlock until foundational mastery is achieved.
   - **Persona B (Mixed Learner with Single Weak Fact)**: Advances through Stage 2 and Stage 3 despite a single isolated difficult fact; broad weakness triggers only upon accumulating $\ge 2$ concurrent weak facts.
   - **Persona C (Fluent / Accelerated Learner)**: Rapidly unlocks Stage 2, Stage 3, and Stage 4 upon completing foundational D01 frontiers with high accuracy.
   - **Scheduler Transitions**: Verifies smooth 10-slot role distribution expansion across 1 $\to$ 2 $\to$ 3 $\to$ 4 active operations without starvation, role inversion, or duplicate positions.

### Implementation & Review Test Evidence (Candidate `d1e794cf529598e1d57ae39dbe790bc51ede81d5`)

- **Candidate Commit**: `d1e794cf529598e1d57ae39dbe790bc51ede81d5` (Slice 4 HEAD on `feat/p3-cumulative-operation-unlock-progression`).
- **Base Commit**: `f580a7154a4043a5097ffd852b5cf454be2cc397` (`main` after PR #64 merge).
- **Consolidated Review Verdict**: `P3_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 3 informational Notes).
- **Implementation Core Test Evidence**: **2,196 passed**, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Focused P3 Test Evidence (169 passed, 0 failed, 0 skipped)**:
  - `CurriculumUnlockPolicyTests`: 42 passed
  - `SchemaV9MigrationTests`: 23 passed
  - `TrainingSessionUnlockIntegrationTests`: 21 passed
  - `SettingsUnlockContractTests`: 15 passed
  - `P3PersonaSimulationRegressionTests`: 18 passed
  - `P3MonotonicityRegressionTests`: 20 passed
  - `P3SchedulerTransitionRegressionTests`: 16 passed
  - `P3LegacyDormantEvidenceRegressionTests`: 14 passed
- **High-Risk Adjacent Suites**: 190 passed, 0 failed, 0 skipped (across `PracticeBalanceIntegrationTests`, `GuidedNumberSpaceSelectionTests`, `PracticeSessionIntegrationTests`, `StaleSelectionEvidenceRemediationTests`, `BoundedSelectionIntegrationTests`, and `ResetWorkflowTests`).

### Evidence Boundary Principles & Validation State

- **Implementation Evidence Boundary**: The 2,196 passing Core tests represent REVIEW_ONLY implementation and review evidence. They do **NOT** constitute formal exact-candidate `FULL_VALIDATION` (which requires dual Debug/Release execution, clean builds, NuGet security audit, and full repository hygiene checks).
- **Delivery State**: P3 achieved exact-candidate `FULL_VALIDATION_PASS` and was merged into `main` via PR #65 at merge commit `759389650778f5d7b6a334b15556c5f31f6de5d0` (post-merge tests: 2,196 passed, 0 failed, 0 skipped; Schema V9 live; ADR-0012 authoritative).

---

## 27. P4 Settings Simplification Contracts & Reviewed Test Evidence

The P4 implementation simplifies the normal Settings page by removing user-facing Practice Time selection (Standard, No Time Pressure, 30s, 45s, 60s), eliminating obsolete transitional UI while preserving all required Settings surfaces, read-only curriculum operation status, and lower-level Practice Time compatibility plumbing. This behavior is covered by the permanent P4 contract suite in `MathFirst.Core.Tests`:

1. **Absence of Practice Time UI & Bindings (`SettingsSimplificationContractTests`)**:
   - Asserts complete removal of the Practice Time card, mode choices (Standard, No Time Pressure, 30s, 45s, 60s), radio buttons, and UI bindings from `Settings.razor`.
   - Asserts removal of unused practice-time UI CSS classes from `app.css`.
   - Asserts that normal practice operates deadline-free (`HasEnforcedDeadline = false`) without presenting deadline options.

2. **Retention of Lower-Level Practice Time Plumbing (`SettingsSimplificationContractTests`)**:
   - Asserts full retention of `PracticeTimeSetting`, `PracticeTimePreferencePolicy`, `IPreferenceStore` practice-time methods (`GetPracticeTime`, `SetPracticeTime`), `MauiPreferenceStore` persistence, and `TrainingSession` timing plumbing.
   - Asserts retention of `AttemptOutcome.Timeout` semantics and explicit/legacy timeout grading paths for backward compatibility, custom testing, and domain plumbing.
   - Asserts that existing persisted Practice Time preference values remain completely harmless with zero database or preference migration required.

3. **Retention of Required Settings Surfaces (`SettingsSimplificationContractTests`)**:
   - Asserts retention of Language selection (English, German, Russian).
   - Asserts retention of Theme / Appearance selection (System, Light, Dark).
   - Asserts retention of read-only arithmetic operation status display.
   - Asserts retention of Keypad layout selection (Numpad, Phone).
   - Asserts retention of Haptic feedback toggle.
   - Asserts retention of in-app Privacy Policy entry card and navigation.
   - Asserts retention of three distinct two-step reset actions (*Reset Learning Progress*, *Restore Default Settings*, *Full Local Reset*).
   - Asserts retention of Version / Build identity information.
   - Asserts retention of Tester diagnostics and telemetry export/share controls.

4. **Read-Only Curriculum Operation Presentation & Authority (`SettingsSimplificationContractTests`)**:
   - Asserts operation controls render strictly as read-only "Locked" / "Unlocked" status indicators derived from `CurriculumUnlockPolicy` and `CurriculumStage`.
   - Asserts Settings contains no interactive operation toggles, cannot unlock locked operations, and cannot disable unlocked `CurriculumManaged` operations.
   - Asserts `CurriculumStage` remains the sole operation-unlock authority under `PracticeMode.CurriculumManaged`.
   - Asserts `PracticeMode.Custom` remains explicit preference-driven behavior without interference from normal Settings simplifications.

5. **Reset Semantics & Settings Lifecycle (`SettingsSimplificationContractTests`)**:
   - Asserts *Restore Default Settings* resets preferences (including restoring default plumbing values) and preserves learning progress and `CurriculumStage`.
   - Asserts *Reset Learning Progress* resets learning data and resets `CurriculumStage` to Stage 1 while preserving UI preferences.
   - Asserts *Full Local Reset* resets learning data, resets `CurriculumStage` to Stage 1, restores default preferences, and purges telemetry cache.
   - Asserts navigating to Settings cleanly pauses active interaction timing, and returning to practice preserves or cleanly reconciles prospective question state.

### Implementation & Review Test Evidence (Candidate on `feat/p4-settings-simplification`)

- **Permanent Contract Suite**: `tests/MathFirst.Core.Tests/SettingsSimplificationContractTests.cs` (8 tests).
- **Consolidated Review Verdict**: `P4_COMPLETE_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit, 0 Note; test classification: 2 UNIQUE P4 CONTRACT, 6 VALUABLE CONSOLIDATION).
- **Test Evidence Reported & Confirmed**:
  - *Dedicated P4 Contract Suite*: **8 passed**, 0 failed, 0 skipped.
  - *Focused Settings / Reset / Config set*: **220 passed**, 0 failed, 0 skipped.
  - *P3 High-Risk Regression set*: **169 passed**, 0 failed, 0 skipped.
  - *P1/P1b Adjacent Regression set*: **107 passed**, 0 failed, 0 skipped.
  - *Full Core Test Suite*: **2,204 passed**, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - *`git diff --check`*: PASS.

### Evidence Boundary Principles & Validation State

- **Implementation Evidence Boundary**: The 2,204 passing Core tests represent implementation and review evidence.
- **Exact-Candidate FULL_VALIDATION Evidence**: Candidate `edcc150039f369b8f809982499a5e1b2714e064c` achieved `FULL_VALIDATION_PASS` across Core Debug (2,204 passed), Core Release (2,204 passed), Windows Release build (0 warnings / 0 errors), Android Release build (0 warnings / 0 errors), NuGet vulnerability audit (0 vulnerable packages), and markdown link audit (36 documents / 297 relative links / 0 broken).
- **Delivery State**: P4 was merged into `main` via PR #66 at merge commit `8400151ff080caecf024a418a9b6b8ada4873c2d` on 2026-10-07.

---

## 28. P5 Cyber Defense Visual Consistency Contracts & Review Test Evidence

The P5 implementation aligns secondary application surfaces (Settings, Privacy, secondary dialogs, overlays, and Not Found) with the established Cyber Defense visual identity following Option A (Scoped Cyber Defense Pattern Reuse), without introducing a new design system or parallel token subsystem. This behavior is covered by three permanent contract suites in `MathFirst.Core.Tests`:

1. **Secondary Surface Visual Contracts (`SecondarySurfaceVisualContractTests`) (6 tests)**:
   - Asserts Settings cards, read-only operation status, and Unlocked/Locked status badges use technical surface framing and emerald cyber accents.
   - Asserts Keypad selection cards and previews reuse Cyber Defense keypad styling.
   - Asserts Reset confirmation panels and Version/Build metadata share secondary styling.
   - Asserts Tester diagnostics and telemetry export actions integrate with secondary card styling.
   - Asserts Privacy Policy surface shares unified secondary card styling.
   - Asserts read-only operation cascade specificity prevents inadvertent toggle behavior.

2. **Secondary Dialog Visual Contracts (`SecondaryDialogVisualContractTests`) (6 tests)**:
   - Asserts `InitialReadyGate` progress overview uses technical surface framing and high-contrast typography.
   - Asserts `ManualPauseGate` session summary retains danger-accented pause treatment while adopting cyber framing.
   - Asserts `TeachingIntervention` dialog adopts technical card framing with zero learning mutation.
   - Asserts `SessionCheckIn` dialog retains Keep Going / Take a Break actions with dark cyber framing.
   - Asserts `IncorrectFeedback` card uses semantic danger accents with cyber framing.
   - Asserts `NotFound.razor` reuses secondary card styling while strictly preserving `@page "/not-found"`, `MainLayout`, localized title/description, `IAppBackNavigationCoordinator`, and navigation to `"/"`.

3. **Secondary Visual & Accessibility Contracts (`SecondaryVisualAccessibilityContractTests`) (6 tests)**:
   - Asserts centralized keyboard focus ring contract covers `.keypad-choice-card:focus-visible`.
   - Asserts transitions and hover transforms are suppressed under `@media (prefers-reduced-motion: reduce)`.
   - Asserts static theme token contrast contracts for Light, Dark, and System appearance modes.
   - Asserts responsive and overflow safeguards for secondary surfaces.
   - Asserts read-only operation status cascade specificity.
   - Asserts strict isolation and zero impact on active gameplay coordinates (`MF-UX-008`).

### Implementation & Review Test Evidence (Candidate on `feat/p5-cyber-defense-visual-consistency`)

- **Permanent Contract Suites**:
  - `tests/MathFirst.Core.Tests/SecondarySurfaceVisualContractTests.cs`: 6 tests
  - `tests/MathFirst.Core.Tests/SecondaryDialogVisualContractTests.cs`: 6 tests
  - `tests/MathFirst.Core.Tests/SecondaryVisualAccessibilityContractTests.cs`: 6 tests
  - Total permanent P5 contract tests: **18 tests**
- **Consolidated Review Verdict**: `P5_COMPLETE_REVIEW_APPROVED` (Slice 1 `P5_SLICE_1_REVIEW_APPROVED`, Slice 2 `P5_SLICE_2_REVIEW_APPROVED`, Slice 3 `P5_SLICE_3_REVIEW_APPROVED`).
- **Test Evidence Reported & Confirmed**:
  - *Dedicated P5 Contract Suites*: **18 passed**, 0 failed, 0 skipped.
  - *Full Core Test Suite (Debug)*: **2,222 passed**, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - *`git diff --check`*: PASS.

### Evidence Boundary Principles & Validation State

- **Implementation Evidence Boundary**: The 2,222 passing Core tests represent implementation and review evidence.
- **Exact-Candidate FULL_VALIDATION Evidence**: Candidate `80f08e4ad2eb33c5e884e869766bb765bbf1277a` achieved `FULL_VALIDATION_PASS` across Core Debug (2,222 passed), Core Release (2,222 passed), Windows Release build (0 warnings / 0 errors), Android Release build (0 warnings / 0 errors), NuGet vulnerability audit (0 vulnerable packages), and markdown link audit.
- **Delivery State**: P5 was merged into `main` via PR #67 at merge commit `aeb7bc46e8b425d9da95493a367f99f7ed330871` on 2026-10-07.

---

## 29. P6 Tester Diagnostics / Telemetry Release Boundary Contracts & Review Test Evidence

The P6 implementation establishes strict compile/profile boundaries isolating Tester diagnostic and telemetry export controls from non-Tester/production builds while preserving existing telemetry, persistence, and reset contracts. This behavior is covered by permanent contract and regression suites across 11 test suites in `MathFirst.Core.Tests`:

1. **Build Property and Compile Symbol Isolation (`ReleaseProfileContractTests`, `MathFirst.App.csproj`)**:
   - Asserts MSBuild property `MathFirstEnableTesterDiagnostics` defaults to `true` under `Configuration == 'Debug'` and `false` under non-Debug/Release configurations.
   - Asserts explicit caller-provided values (`-p:MathFirstEnableTesterDiagnostics=true` or `false`) override configuration defaults deterministically.
   - Asserts compile symbol `MATHFIRST_TESTER_DIAGNOSTICS` is defined if and only if `MathFirstEnableTesterDiagnostics == true`.

2. **Component Isolation & Dedicated Presentation (`TesterDiagnosticsContractTests`, `SettingsTelemetryUiContractTests`)**:
   - Asserts `Settings.razor` delegates tester diagnostics presentation to the isolated `TesterDiagnosticsSection` component.
   - Asserts `Settings.razor` no longer directly injects or depends on `IAppPlatformInfo`, `IClipboardService`, `TelemetryExportCoordinator`, `ITelemetryJsonSerializer`, or `ITelemetryShareService`.
   - Asserts that when `MATHFIRST_TESTER_DIAGNOSTICS` is defined, `TesterDiagnosticsSection` renders Copy Diagnostic Info and Export Telemetry actions with busy-state indicators and localized feedback.
   - Asserts that when `MATHFIRST_TESTER_DIAGNOSTICS` is absent, `TesterDiagnosticsSection` compiles to an empty dependency-free component rendering zero diagnostic DOM markup or actions.

3. **Conditional vs. Shared DI Composition (`TesterDiagnosticsContractTests`, `MauiProgram.cs`)**:
   - Asserts Tester-only services (`IAppPlatformInfo`, `IClipboardService`, `ITelemetryJsonSerializer`, `ITelemetryShareService`, `TelemetryExportCoordinator`) are registered conditionally if and only if `MATHFIRST_TESTER_DIAGNOSTICS` is enabled.
   - Asserts shared application services (`AppBuildInfo`, `IInstallationIdStore`, `IInstallationIdProvider`, `ITelemetryShareCacheCleaner`, `IAppResetCoordinator`) remain registered across all profiles.

4. **Shared Full Local Reset Cache Cleanup (`AppResetCoordinatorTests`, `ResetWorkflowTests`)**:
   - Asserts `ITelemetryShareCacheCleaner` and `MauiTelemetryShareCacheCleaner` purge the `telemetry-share` cache directory on Full Local Reset across all build profiles (Tester, Local Release, Production).
   - Asserts Full Local Reset clears the persistent installation ID, resets learner database, restores UI and operation preferences, and navigates to `/` across all profiles.

5. **ReleaseTool Metadata Propagation (`ReleaseProfileContractTests`, `TesterApkPackagingContractTests`, `AndroidPackageCommand.cs`)**:
   - Asserts `tools/MathFirst.ReleaseTool` explicitly propagates `MathFirstBuildClassification` (`Tester`, `SourceCandidate`, `Production`) and `MathFirstSourceCommit` (authoritative repository HEAD SHA) to MSBuild invocations.
   - Asserts Tester profile sets `MathFirstEnableTesterDiagnostics = true` and `ApplicationId = com.tachiguro.mathfirst.tester`.
   - Asserts SourceCandidate and Production profiles set `MathFirstEnableTesterDiagnostics = false` and `ApplicationId = com.tachiguro.mathfirst`.

6. **Telemetry, Persistence, and Learning Non-Interference (`TelemetryExportCoordinatorTests`, `NativeIdentityContractTests`, `AndroidFileProviderContractTests`)**:
   - Asserts learner persistence Schema V9, `attempt_history` schema, FSRS telemetry, and `telemetry_export_schema_v2` (16 properties) remain untouched without data loss or schema migrations.
   - Asserts Android `FileProvider` configuration strictly limits access to the `telemetry-share` cache subpath.

### Implementation & Review Test Evidence (Candidate on `feat/p6-tester-diagnostics-release-boundary`)

- **Permanent Contract & Regression Suites (117 focused P6 tests)**:
  - `ReleaseProfileContractTests`: 5 passed
  - `TesterApkPackagingContractTests`: 23 passed
  - `TesterDiagnosticsContractTests`: 9 passed
  - `SettingsTelemetryUiContractTests`: 10 passed
  - `SettingsUnlockContractTests`: 12 passed
  - `SettingsSimplificationContractTests`: 8 passed
  - `NativeIdentityContractTests`: 6 passed
  - `AndroidFileProviderContractTests`: 16 passed
  - `AppResetCoordinatorTests`: 7 passed
  - `ResetWorkflowTests`: 8 passed
  - `TelemetryExportCoordinatorTests`: 13 passed
- **Adjacent P5 Visual Regression**: 18 passed (`SecondarySurfaceVisualContractTests`, `SecondaryDialogVisualContractTests`, `SecondaryVisualAccessibilityContractTests`).
- **Full Core Test Suite (Debug)**: **2,228 passed**, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Windows Compile Matrix Verification**:
  - `Configuration=Debug`: 0 warnings, 0 errors
  - `Configuration=Release`: 0 warnings, 0 errors
  - `Configuration=Release -p:MathFirstEnableTesterDiagnostics=true`: 0 warnings, 0 errors
- **Android Target `Compile` Verification**:
  - Tester-like (`Configuration=Release -p:MathFirstEnableTesterDiagnostics=true -p:MathFirstBuildClassification=Tester -p:ApplicationId=com.tachiguro.mathfirst.tester`): 0 warnings, 0 errors
  - SourceCandidate-like (`Configuration=Release -p:MathFirstEnableTesterDiagnostics=false -p:MathFirstBuildClassification=SourceCandidate -p:ApplicationId=com.tachiguro.mathfirst`): 0 warnings, 0 errors
  - Production-like (`Configuration=Release -p:MathFirstEnableTesterDiagnostics=false -p:MathFirstBuildClassification=Production -p:ApplicationId=com.tachiguro.mathfirst`): 0 warnings, 0 errors
- **Consolidated Review Verdict**: `P6_COMPLETE_REVIEW_APPROVED` (Slice 1 `P6_SLICE_1_REVIEW_APPROVED`, Slice 2 `P6_SLICE_2_REVIEW_APPROVED`, Slice 3 `P6_SLICE_3_REVIEW_APPROVED`).

### Evidence Boundary Principles & Formal Validation State

- **Implementation Evidence Boundary**: The 2,228 passing Core tests and compile matrix results represent implementation and review evidence.
- **Tooling & Device Boundary**: Android compile verification used `-t:Compile` to verify compilation across profiles without executing packaging, signing, emulator, simulator, physical device, or ADB operations.

#### Historical First Formal FULL_VALIDATION Attempt (Candidate `a711c07d80ab2cc3873cbb5a0de96803fabe116b`)
- **Candidate**: `a711c07d80ab2cc3873cbb5a0de96803fabe116b`
- **Result**: `P6_FULL_VALIDATION_FAILED` (Historical)
- **Failure Phase**: Documentation current-state consistency gate (`FINDING-P6-DOC-STALE-STATE`)
- **Failure Cause**: Committed candidate documentation still described pre-commit lifecycle state (asserting P6 was uncommitted, 0 commits ahead of main, in DOCUMENT_ONLY, awaiting initial documentation review and commit).
- **Execution & Skipped Gate Evidence Boundary**:
  - Exact candidate identity, path existence, and whitespace diff checks passed (`git diff --check`).
  - P6 static contract presence was confirmed.
  - MSBuild property matrix was skipped fail-closed.
  - Core Debug test suite execution was skipped.
  - Core Release test suite execution was skipped.
  - Windows builds were skipped.
  - Android builds were skipped.
  - NuGet vulnerability security audit was skipped.
  - Markdown repository link and anchor audit was skipped.
  - No implementation, compilation, or test regression was established.
  - Downstream gates were skipped fail-closed. Documentation was subsequently remediated in commit `bceede18dd5bc2007f4bdc211f721979a50f2c35`.

#### Final Formal P6 FULL_VALIDATION (Candidate `bceede18dd5bc2007f4bdc211f721979a50f2c35`)
- **Candidate**: `bceede18dd5bc2007f4bdc211f721979a50f2c35`
- **Result**: `P6_FULL_VALIDATION_PASSED`
- **Formal Verification Evidence**:
  - **Core Debug Test Suite**: 2,228 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - **Core Release Test Suite**: 2,228 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - **Dedicated Permanent P6 Contracts**: 117 passed across 11 suites.
  - **P5 UI Regressions**: 18 passed across 3 suites.
  - **Windows Build Matrix**:
    - `Release` (non-Tester default): 0 warnings, 0 errors
    - `Release -p:MathFirstEnableTesterDiagnostics=true`: 0 warnings, 0 errors
    - `Debug`: 0 warnings, 0 errors
  - **Android Profile Compile Matrix**:
    - Tester-like (`Release -p:MathFirstEnableTesterDiagnostics=true -p:MathFirstBuildClassification=Tester -p:ApplicationId=com.tachiguro.mathfirst.tester`): 0 warnings, 0 errors
    - SourceCandidate-like (`Release -p:MathFirstEnableTesterDiagnostics=false -p:MathFirstBuildClassification=SourceCandidate -p:ApplicationId=com.tachiguro.mathfirst`): 0 warnings, 0 errors
    - Production-like (`Release -p:MathFirstEnableTesterDiagnostics=false -p:MathFirstBuildClassification=Production -p:ApplicationId=com.tachiguro.mathfirst`): 0 warnings, 0 errors
  - **NuGet Vulnerability Security Audit**: 0 known vulnerable packages.
  - **Markdown Repository Link & Anchor Audit**: 36 documents, 299 relative file links, 1 relative fragment link, 0 broken file links, 0 broken fragments.
  - **Candidate Diff Check**: `git diff --check` PASS.
  - **Final Repository Immutability**: PASS.
- **Integration & Delivery Evidence**:
  - Pushed to `origin/feat/p6-tester-diagnostics-release-boundary`.
  - Pull Request #68 merged to `main` at merge commit `049ec1d5d3859a139f8d5493d6dae7607d321b02`.
  - Merged tree `817ee7b250c5bed555f4c4bce8852dce5d8dbf68` is identical to validated candidate tree `817ee7b250c5bed555f4c4bce8852dce5d8dbf68`.
  - `POST_MERGE_SYNC_ONLY` completed with clean working tree, clean index, and zero content drift.
  - Step 55 remains unauthorized; Build 4 does not exist.

---

## 30. MF-AUDIT-002 / P8 Test-Coverage Audit & Targeted Hardening Contracts & Reviewed Test Evidence

The bounded P8 package executes a factual test-coverage baseline audit across the four production assemblies and hardens critical domain, persistence, and release policy invariants across three reviewed implementation slices plus one review-approved validation test-infrastructure remediation. This hardening is covered by permanent unit, property, and contract suites in `MathFirst.Core.Tests`:

1. **Critical Domain Invariants Hardening (Slice 1, Commit `76e718006c2adc5953995d99bbfc1451dc8e63c1`)**:
   - **Broad Weakness Policy Contracts (`BroadWeaknessPolicyTests.cs`)**: Verifies multi-operation and single-operation broad weakness detection rules ($\ge 2$ active weak facts across active curriculum/operation space), context filtering, and suppression of opportunistic `New` fact acquisition during struggle.
   - **Curriculum Invariant Properties (`CurriculumInvariantPropertyTests.cs`)**: Property-based and deterministic verification of curriculum band structures, monotonically ordered fact indices, exact fact ID formation, digit novelty thresholds, and non-empty band definitions across Addition, Subtraction, Multiplication, and Division.
   - **Unlock Policy Boundary Regressions (`CurriculumUnlockPolicyTests.cs`)**: Verifies prerequisite D01 frontier readiness, single-error tolerance, monotonic stage properties, and aggregate weakness gating across Stage 1 $\to$ 2 $\to$ 3 $\to$ 4 transitions.
   - **Deterministic Scheduler Bounds (`DeterministicOperationSchedulerTests.cs`)**: Verifies permutation bag turn allocations, bounded operation frequency, deterministic bag resets, and zero-RNG state neutrality.

2. **Persistence Recovery & Long-Run Invariants Hardening (Slice 2, Commit `56880b3ff3da2c6e06840226ac236dafedba34bd`)**:
   - **SQLite Persistence Conformance (`SqlitePersistenceConformanceTests.cs`)**: Verifies Schema V9 persistence contracts, transaction atomicity, dirty/duplicate submission rejection, practice position monotonicity, revision conflict detection, and idempotent commit replay.
   - **Persistence Recovery & Lifecycle (`PersistenceRecoveryAndLifecycleTests.cs`)**: Verifies post-write evidence preparation recovery, separation of durable database commit from transient session presentation, and non-destructive retry mechanics without duplicate attempt records or store revisions.
   - **Long-Run Independent Progression (`LongRunIndependentProgressionTests.cs`)**: Verifies multi-operation progression stability across extended simulated learning sessions, dual-window structured band advancement, and restart equivalence without session state drift.

3. **Release Tooling & Security Boundaries Hardening (Slice 3, Commit `3558f8cee7b3aad031459990276ff99d73312379`)**:
   - **Release CLI Fail-Closed Contracts (`ReleaseCliFailClosedContractTests.cs`)**: Verifies `MathFirst.ReleaseTool` command-line argument validation, rejection of invalid combinations, fail-closed handling of malformed input files, and clean error reporting without stack trace leaks.
   - **Packaging Input & Signature Verification**: Verifies release keystore input validation, certificate fingerprint validation, and profile boundary assertions across Tester, SourceCandidate, and Production profiles.
   - **Reset Telemetry Share Cache Purge**: Verifies profile-wide `ITelemetryShareCacheCleaner` execution on Full Local Reset across all build profiles.

4. **Audited No-New-Test Decisions (`NO_NEW_TEST_REQUIRED`)**:
   - Formally audited candidate areas and confirmed existing coverage is already sufficient:
     - *Archive Validation*: Existing `AndroidAabValidationTests` and `AndroidApkValidationTests` thoroughly cover archive extraction, bundle structure, and DEX inspection.
     - *Build Profile Boundaries*: Existing `ReleaseProfileContractTests` thoroughly cover compile symbols and MSBuild property defaults.
     - *Reset Exceptions*: Existing `AppResetCoordinatorTests` and `ResetWorkflowTests` thoroughly cover reset failures and rollback handling.
     - *Telemetry / Privacy Boundaries*: Existing `AndroidPackagingContractTests` and `TelemetryLearningNonInterferenceTests` enforce zero network permissions and privacy sanitization.
     - *Guided Gate G3 Decoupling*: Existing `GuidedNumberSpaceSelectionTests` thoroughly cover independent multiplication/division ceiling decoupling at BandIndex $\ge 3$.
     - *Acquisition Ownership Resolver*: Existing `AcquisitionOwnershipResolverTests` thoroughly cover fact-to-band ownership across all operations.

5. **Validation Test-Infrastructure Stabilization & Serialization Architecture**:
   - **Historical Failure**: The first formal exact-candidate FULL_VALIDATION attempt on candidate `0e76bf907653cd1d3256854a53e0c77684787ad4` failed at the Full Core Release gate (2,298 passed / 1 failed; `MathFirst.Core.Tests.TesterApkPackagingContractTests.FailClosed_CliValidate_RejectsTesterProfile` failed with `System.ObjectDisposedException: Cannot write to a closed TextWriter` while `ReleaseCli.RunAsync` wrote through a process-global `Console.Error` stream; historical verdict `MF_AUDIT_002_FULL_VALIDATION_FAILED`).
   - **Failure Classification & Root Cause**: Classified as a TEST-INFRASTRUCTURE CONCURRENCY / ISOLATION DEFECT. ReleaseCli test classes redirected process-global `Console.Error` and `Console.Out` to local `StringWriter` instances. Default xUnit class/collection parallelism allowed another ReleaseCli test to write while a redirected writer had been disposed/restored. Zero production `ReleaseTool` defect established, zero product behavior defect established, 0 production code changes required.
   - **Remediation Architecture**: Introduced the dedicated non-parallel xUnit test collection definition:
     ```csharp
     [CollectionDefinition("ReleaseCli process console", DisableParallelization = true)]
     public sealed class ReleaseCliProcessConsoleCollectionDefinition;
     ```
     and applied `[Collection("ReleaseCli process console")]` across all four participating test suites:
     - `tests/MathFirst.Core.Tests/ReleaseCliFailClosedContractTests.cs`
     - `tests/MathFirst.Core.Tests/TesterApkPackagingContractTests.cs`
     - `tests/MathFirst.Core.Tests/AabPackagingScriptValidationTests.cs`
     - `tests/MathFirst.Core.Tests/TesterApkWorkflowIntegrationTests.cs`
   - **Parallelism Scope**: Serializes only test suites intercepting process-global console streams; assembly-wide parallelism remains active for all other tests.
   - **Non-Blocking Review NIT**: `AabPackagingScriptValidationTests.cs` contains an explicit `using Xunit;` redundant with global using directives; classified as harmless non-blocking NIT.
   - **Remediation Stability Evidence**:
     - Focused four-class Release filter (156 tests): 5 consecutive runs, all 156 passed (0 failed, 0 skipped).
     - Full Core Release: 3 consecutive runs, all 2,299 passed (0 failed, 0 skipped).
     - Full Core Debug: 2,299 passed, 0 failed, 0 skipped.
   - **Remediation Review Verdict**: `MF_AUDIT_002_VALIDATION_REMEDIATION_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 NIT; confirmed 156 focused Release tests passed, 2,299 Core Release tests passed).

### Corrected Normalized Cobertura Coverage Evidence (Release Configuration)

Coverage measurement methodology uses `coverlet.collector 6.0.4` under `Release` configuration across all four production assemblies with identical test project and parser denominators.

> [!IMPORTANT]
> **Historical Coverage Delta Correction**:
> The earlier PLAN_ONLY percentage delta (+2.73 pp line / +2.64 pp branch; 19,678/21,576 lines, 5,816/7,290 branches) resulted from mixing an earlier multi-run sequence-point framework with the normalized Cobertura parser and is **superseded**. The authoritative apples-to-apples baseline and post-P8 measurements are given below.

| Assembly | Metric | Base (Pre-P8) | Head (Post-P8) | Delta | Delta (pp) |
|---|---|---|---|---|---|
| **MathFirst.Application** | Lines | 4,080 / 4,326 (94.31%) | 4,087 / 4,326 (94.48%) | +7 | +0.16 pp |
| | Branches | 1,289 / 1,537 (83.86%) | 1,296 / 1,537 (84.32%) | +7 | +0.46 pp |
| **MathFirst.Domain** | Lines | 777 / 872 (89.11%) | 811 / 872 (93.00%) | +34 | +3.90 pp |
| | Branches | 385 / 460 (83.70%) | 413 / 460 (89.78%) | +28 | +6.09 pp |
| **MathFirst.Infrastructure.Sqlite** | Lines | 1,695 / 1,781 (95.17%) | 1,701 / 1,781 (95.51%) | +6 | +0.34 pp |
| | Branches | 429 / 540 (79.44%) | 435 / 540 (80.56%) | +6 | +1.11 pp |
| **MathFirst.ReleaseTool** | Lines | 1,710 / 1,889 (90.52%) | 1,731 / 1,889 (91.64%) | +21 | +1.11 pp |
| | Branches | 815 / 1,082 (75.32%) | 839 / 1,082 (77.54%) | +24 | +2.22 pp |
| **TOTAL** | **Lines** | **8,262 / 8,868 (93.17%)** | **8,330 / 8,868 (93.93%)** | **+68** | **+0.77 pp** |
| | **Branches** | **2,918 / 3,619 (80.63%)** | **2,983 / 3,619 (82.43%)** | **+65** | **+1.80 pp** |

### Implementation, Historical Validation & Remediation Evidence

- **Historical Implementation Checkpoints (Original Slice 1–3 Scope)**:
  - Slice 1: `76e718006c2adc5953995d99bbfc1451dc8e63c1` (`MathFirst-Checkpoint: MF-AUDIT-002 1/3 critical-domain-invariants`)
  - Slice 2: `56880b3ff3da2c6e06840226ac236dafedba34bd` (`MathFirst-Checkpoint: MF-AUDIT-002 2/3 persistence-recovery-long-run`)
  - Slice 3: `3558f8cee7b3aad031459990276ff99d73312379` (`MathFirst-Checkpoint: MF-AUDIT-002 3/3 release-security-reset`)
- **Original Slice 1–3 Implementation Scope (Historical)**:
  - 3 added test files: `BroadWeaknessPolicyTests.cs`, `CurriculumInvariantPropertyTests.cs`, `ReleaseCliFailClosedContractTests.cs`.
  - 5 modified test files: `CurriculumUnlockPolicyTests.cs`, `DeterministicOperationSchedulerTests.cs`, `SqlitePersistenceConformanceTests.cs`, `PersistenceRecoveryAndLifecycleTests.cs`, `LongRunIndependentProgressionTests.cs`.
  - Focused P8 test suite: **155 passed**, 0 failed, 0 skipped.
  - Consolidated Review Verdict: `MF_AUDIT_002_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 0 Nit; 0 confirmed defects; 0 unresolved P0/P1 gaps).
- **Historical First Formal FULL_VALIDATION Attempt (Candidate `0e76bf907653cd1d3256854a53e0c77684787ad4`)**:
  - Candidate HEAD: `0e76bf907653cd1d3256854a53e0c77684787ad4`
  - Overall Verdict: `MF_AUDIT_002_FULL_VALIDATION_FAILED` (Historical)
  - Focused P8 Release Gate: 155 passed, 0 failed, 0 skipped.
  - Full Core Debug Gate: 2,299 passed, 0 failed, 0 skipped.
  - Full Core Release Gate: 2,298 passed, 1 failed, 0 skipped (`TesterApkPackagingContractTests.FailClosed_CliValidate_RejectsTesterProfile` failed with `ObjectDisposedException` on process-global `Console.Error`).
  - Normalized Cobertura Coverage Gate: 93.93% lines (8,330/8,868), 82.43% branches (2,983/3,619).
  - NuGet Vulnerability Audit: 0 known vulnerable packages.
  - Markdown Link & Anchor Audit: 36 documents, 297 relative file links, 1 relative fragment, 0 broken file links, 0 broken fragments.
  - Candidate Repository Immutability: PASS.
- **Validation Test-Infrastructure Remediation & Review**:
  - Remediated 4 test files (`AabPackagingScriptValidationTests.cs`, `ReleaseCliFailClosedContractTests.cs`, `TesterApkPackagingContractTests.cs`, `TesterApkWorkflowIntegrationTests.cs`) with shared xUnit non-parallel collection isolation.
  - Stabilization Focused Filter: **156 passed**, 0 failed, 0 skipped (5 consecutive runs).
  - Full Core Release: **2,299 passed**, 0 failed, 0 skipped (3 consecutive runs).
  - Remediation Review Verdict: `MF_AUDIT_002_VALIDATION_REMEDIATION_REVIEW_APPROVED` (0 Blocker, 0 Major, 0 Minor, 1 non-blocking NIT; 0 production defects).
- **Final Package-Wide Scope (Relative to Base `main@4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`)**:
  - 11 test paths (3 added, 8 modified).
    - Added (3): `tests/MathFirst.Core.Tests/BroadWeaknessPolicyTests.cs`, `tests/MathFirst.Core.Tests/CurriculumInvariantPropertyTests.cs`, `tests/MathFirst.Core.Tests/ReleaseCliFailClosedContractTests.cs`.
    - Modified (8): `tests/MathFirst.Core.Tests/AabPackagingScriptValidationTests.cs`, `tests/MathFirst.Core.Tests/CurriculumUnlockPolicyTests.cs`, `tests/MathFirst.Core.Tests/DeterministicOperationSchedulerTests.cs`, `tests/MathFirst.Core.Tests/LongRunIndependentProgressionTests.cs`, `tests/MathFirst.Core.Tests/PersistenceRecoveryAndLifecycleTests.cs`, `tests/MathFirst.Core.Tests/SqlitePersistenceConformanceTests.cs`, `tests/MathFirst.Core.Tests/TesterApkPackagingContractTests.cs`, `tests/MathFirst.Core.Tests/TesterApkWorkflowIntegrationTests.cs`.
  - 8 documentation paths: `CHANGELOG.md`, `docs/BACKLOG.md`, `docs/CURRENT_WORK.md`, `docs/NEW_CHAT_BOOTSTRAP.md`, `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`, `docs/TESTING.md`, `docs/V1_PRE_STEP55_REFINEMENT_PLAN.md`.
  - Total package changed paths: **19 paths**.
  - Test case growth: Pre-P8 Base 2,228 passed $\to$ Post-P8 2,299 passed (**+71 automated test cases**).
  - 0 production code changes, 0 tooling production code changes, 0 script changes, 0 configuration changes, 0 schema changes, 0 migrations, 0 runtime behavioral changes.
- **Exact-Candidate Formal FULL_VALIDATION Evidence (Candidate `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`)**:
  - Candidate Commit SHA: `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`
  - Final Validation Result: `MF_AUDIT_002_FULL_VALIDATION_PASSED`
  - Focused Console-Stabilization Filter: 156 passed, 0 failed, 0 skipped
  - Full Core Release Gate: 2,299 passed, 0 failed, 0 skipped (twice consecutively)
  - Full Core Debug Gate: 2,299 passed, 0 failed, 0 skipped
  - Normalized Cobertura Coverage Gate: 93.93% lines (8,330 / 8,868), 82.43% branches (2,983 / 3,619)
  - Windows Release Build: 0 warnings, 0 errors (`net10.0-windows10.0.19041.0`)
  - Android Release Compilation: 0 warnings, 0 errors (`net10.0-android36.0`, Target `Compile`)
  - NuGet Vulnerability Security Audit: 0 known vulnerable packages
  - Markdown Link & Anchor Audit: 36 documents, 297 relative file links, 0 broken
  - Candidate Diff Check: `git diff --check` PASS
  - Candidate Repository Immutability: PASS
- **Integration & Delivery Evidence**:
  - Pull Request #70 merged to `main` at merge commit `cc81242177dd75114934c3ad48b830c9ce87c70b` (Merge parent 1: `4ba870ad0bd6c516e74d2000a8f5c0878fb609a5`, Merge parent 2: `cbbbd31ec00a1a2b55a4dea5b827761157a67a89`).
  - Merged tree `7f30fd950e21098a5376ac7251e906d29db78879` is identical to validated candidate tree `7f30fd950e21098a5376ac7251e906d29db78879`.
  - Post-merge synchronization completed (`MF_AUDIT_002_POST_MERGE_SYNC_COMPLETED`) with clean working tree, clean index, and zero content drift.
  - Post-merge local `main` at `cc81242177dd75114934c3ad48b830c9ce87c70b`.
  - No new development package has been selected following P8.
  - Step 55 remains unauthorized; Build 4 does not exist.

### Evidence Boundary Principles

- All automated tests run offline against synthetic fixtures, source files, and isolated test environments.
- The merged content is Git-tree-identical to the exact candidate that passed `FULL_VALIDATION` (`7f30fd950e21098a5376ac7251e906d29db78879`).
- Automated tests do not constitute physical hardware verification or store publishing.
- **Release Boundaries**: P7 remains DEFERRED. Roadmap Step 55 remains **NOT EXECUTED / NOT AUTHORIZED**. Build 4 does **NOT EXIST**. Production packaging, signing, and store publication remain strictly unauthorized.
