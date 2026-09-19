# MathFirst Verified Project State

This document records stable, verified facts about MathFirst. It excludes transient package workflow state and distinguishes current implementation from accepted target architecture.

---

## 1. Project Identification

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Current Status**: `MF-UX-005` (Native UX, Responsiveness, and Interaction Polish) is the most recently completed merged package. The synchronized merged baseline is `main` at `60dba236aa38bca138ab583f610c9ff876994b04` (PR #39 merge: MF-UX-005 final documentation reconciliation). The current active package `MF-UX-006` (V1 Privacy, Copy, and Localization Hardening) is implemented across three checkpoint commits on task branch `feat/mf-ux-006-v1-privacy-copy-localization` (candidate pre-doc HEAD `130cae92032ebdfbd6f430b5cf353192d5e39ba3`), has passed consolidated review (`REVIEW_PASS`), and is undergoing `DOCUMENT_ONLY` reconciliation. MF-UX-006 is unmerged, not pushed, not in a PR, and `FULL_VALIDATION` remains pending. MF-UX-005 delivery history spans PR #30 through PR #39: PR #36 merged the repeatable Tester APK workflow, the Installed Size/App Data/RAM investigation completed with explicit evidence limitations, PR #37 merged Release source-map exclusion, PR #38 merged Timer visual remediation accepted in a bounded physical Timer `TEST_ONLY` run, and PR #39 merged post-remediation documentation state. MF-UX-005 did not create a replacement production AAB or perform production signing. The separate `FULL_VALIDATION` lifecycle, final release-grade physical validation, and Google Play publication gate remain pending and separately authorized; this state is not a release-readiness declaration.

---

## 2. Supported Architecture and Platform State

- **Supported target architecture**: Android, Web, and Windows remain the confirmed targets. C#/.NET 10, a shared Domain/Application core, MAUI Blazor Hybrid native hosts, and a future standalone Blazor WebAssembly PWA are established by [ADR-0001](decisions/ADR-0001-cross-platform-application-topology-and-stack-baseline.md).
- **Current native implementation**: The MAUI application currently targets and runs on Windows and Android. Shared presentation, learning, timing, input, and Application/Domain logic are present.
- **Web status**: Web remains a supported future target but has no active runtime implementation and is deferred until native Windows/Android V1 work and release readiness are complete.
- **Inactive platforms**: iOS and Mac Catalyst are not active targets.
- **Current priority**: Windows and Android are the active native product priorities; Android is increasingly prioritized for eventual Google Play publication while Windows remains primary.

---

## 3. Current Implemented Learning and Persistence Baseline

- The current runtime implements independent progression for Addition, Subtraction, Multiplication, and Division. The dense foundation contains 121 Addition, 121 Subtraction, 169 Multiplication, and 156 Division facts (567 total), followed by structured open-ended bands; the curriculum has no finite total size or permanent level-10 ceiling. Terminal dense bands are Addition 125 (`ADD-P8-D0`), Subtraction 135 (`SUB-P8-D0`), Multiplication 32 (`MUL-P7-SCALED`), and Division 32 (`DIV-P7-SCALED`).
- Operation scheduling and role selection are deterministic, and selection uses bounded evidence windows with lazy fact materialization. The global Mixed Checkpoint and global introduction lockstep are not active runtime concepts.
- **Deterministic Bounded Operation Scheduling (MF-STAB-002 Slice 2)**: Enabled operations are scheduled using deterministic bounded operation permutation bags. Every enabled operation appears exactly once per bag. Scheduling is deterministic, restarts deterministically, is independent of the order in which operations were enabled, and guarantees bounded per-operation frequency. Same-operation adjacency may occur across bag boundaries. The scheduler operates without RNG and without persisted scheduler state. Disabled operations are excluded from practice turns and hidden from the progress HUD while retaining attempt history, `ItemLearningState`, FSRS state, and `OperationProgression`; re-enabled operations resume preserved state.
- **Selector Model & Requested-Role Authority (MF-STAB-002 Slices 1 & 3)**:
  - Highest precedence: eligible Remediation ($\ge 4$ distance from previous error/timeout).
  - Deterministic requested-role cycle per operation across a 10-attempt period:
    1. New
    2. Due
    3. New
    4. Maintenance
    5. Frontier
    6. New
    7. Due
    8. New
    9. Due
    10. Frontier
  - Requested New slots may introduce unseen owned-frontier material.
  - Unseen Dense material no longer globally overrides requested Due, Maintenance, or Frontier turns; requested-role authority is restored during Dense acquisition.
  - Non-New fallback chains (`Requested Due`, `Requested Maintenance`, `Requested Frontier`) strictly cannot introduce unseen material.
  - Frontier is materialized active-band consolidation; Due and Maintenance provide retention and review opportunities.
  - `Early Review` remains a fallback-only liveness bridge within review chains.
  - Ordinary New introductions are bounded by the four requested New opportunities per ten per-operation attempts (no claim of exactly 40% actual New across all scenarios or a fixed individual-card review gap).
  - Practice selection incorporates deterministic fact candidate ranking, anti-ladder candidate selection when alternatives exist, preserved exact (3-fact) and commutative mirror (3-fact) cooldown semantics, and deterministic role-specific ranking domains.
- **Returning Learner Progress & Session Check-In Summary**: A returning learner with prior practice sees a compact progress presentation before starting practice. Only enabled operations are displayed with their current Stage (`BandIndex + 1`), without invented mastery percentages or fake statistics, and practice start remains explicit. Every 20 accepted attempts ($20, 40, 60, \dots$), a session check-in modal displays completed attempts (20), correct count, median latency of correct attempts only, and actual Stage changes occurred in that segment. Check-in summary state is process-local and is not persisted to SQLite. Continuing practice starts a new summary segment; taking a break ends the current segment and transitions to manual pause, after which a later Start begins a fresh summary segment.
- **Schedule-Agnostic SQLite Persistence Contract**: `SqliteLearnerStore` persists submitted learner-state transitions and validates Practice Position monotonicity, attempt/progression consistency, canonical fact identity, result correctness, answer/outcome consistency, item and FSRS state consistency, canonical four-entry `OperationProgression`, attempted-operation mutation boundaries, band progression, revision conflicts, duplicate positions, transaction atomicity, and idempotency. It does not reconstruct operation scheduling. Enabled-operation preferences are separate from learner SQLite, and Schema V6 requires no schema change.
- **Selection Evidence and Exactly-Once Recovery**: `PracticeSelectionEvidence` is scoped by `ArithmeticOperation` and `ProspectivePracticePosition`; evidence cannot cross either boundary. Cached evidence is optimization only and never scheduling authority. Required enabled scheduled-operation evidence is loaded asynchronously on demand for the exact operation and prospective position. Disabled-operation evidence may be prefetched best effort, and its failure cannot block enabled practice. There is no arbitrary wrong-operation fallback. If a durable submission succeeds but next-exercise evidence preparation fails, recovery prepares the required evidence without duplicating attempt history, Practice Position, item/FSRS changes, or store revision.
- **Current-Fact, HUD, and Settings Cleanup**: A Settings change preserves the currently displayed question. On return, the HUD immediately displays only the enabled operations; the next generated question uses the updated subset. The operation HUD remains learning/progression feedback. The developer Statistics / Diagnostics section was removed from Settings as UI cleanup only; learner data remains intact.
- **Practice Time and Runtime Timer**: Settings provides Standard adaptive timing or 30 s, 45 s, and 60 s deadline floors via $\max(\text{adaptive deadline}, \text{configured floor})$, as well as No Time Pressure mode (`NoTimePressure = -1`) which disables deadlines and timeouts while measuring and persisting active latency uncapped. These choices do not alter raw active response latency, pace thresholds, or FSRS ratings. Manual Pause, Settings, and same-process background/suspend time freeze elapsed and remaining time and cannot trigger timeout. A true cold restart begins the in-flight timer fresh while preserving the Practice Time preference and learner progress. In-flight elapsed/remaining time and pause/segment timestamps are process-local; only completed attempt latency/outcome is persisted.
- **Keypad Defaults, Visual Feedback, and Ergonomics**: Numeric keypad layout defaults to `Numpad` (`7 8 9` at top). Onboarding and Settings display choices with Numpad first (left) and Phone second (right). Initial Home backing state and Full Local Reset default to Numpad. Existing explicitly stored preferences (`Phone` or `Numpad`) remain preserved. Enabled keypad buttons provide CSS active-press visual styling, `:focus-visible` keyboard focus outlines remain preserved, and disabled buttons maintain dimmed styling without introducing JavaScript or Blazor pointer-state machinery. Keypad container is keyed per fact instance revision to ensure clean visual reset between turns.
- **Answer Entry Lifecycle & Input Reset Invariant**: Unsubmitted partial input on the same exercise instance is preserved across manual Pause, Settings navigation, and same-process background/resume transitions. Every new exercise instance begins with a completely empty answer input field. Process-local `TrainingSession.FactInstanceRevision` increments for every new exercise instance (not depending on operand equality) and `TrainingSession.CurrentAnswerInput` manages transient answer input without SQLite persistence or scoring/FSRS impact. `Home.razor` keys the input element via `_inputRenderVersion` so a new exercise gets a fresh input element, resolving an Android input retention defect after session check-in breaks. Multi-digit incomplete input remains editable with Backspace without premature incorrect submission. Single-digit canonical answers auto-submit immediately upon digit entry.
- **Adaptive Pace & Acclimation Deadlines**: Hierarchical shrinkage pace estimation ($P_0 = 4500\text{ ms}$; learner $W=12, N=30$; operation $W=8, N=20$; band $W=6, N=15$; fact $W=4, N=5$) clamped to $[600, 12000]\text{ ms}$. Unproven facts (`CorrectAttempts == 0`) receive digit-aware novelty floors (15s for 1 digit, 20s for 2 digits, 25s for 3 digits, 30s for 4+ digits) and multi-digit entry allowances ($+1000\text{ ms} \cdot \max(0, \text{DigitCount} - 1)$). Adaptive deadline is $\lceil (2 \cdot P_{\text{fact}} + \text{Allowance} + \text{EntryAllowance}) / 100 \rceil \cdot 100\text{ ms}$, clamped $[3000, 30000]\text{ ms}$. Proven facts use the adaptive deadline; unproven facts receive $\min(30000, \max(\text{AdaptiveDeadline}, \text{NoveltyFloor}))$. Novelty floors expand opportunity only and do not alter latency measurement, fluency thresholds, or FSRS ratings.
- **Adaptive Fluency & Ratings**: FSRS ratings adapt to fact pace: Easy $\le \text{clamp}(\lfloor 0.85 \cdot P_{\text{fact}} \rceil, 600, 2000)\text{ ms}$, Good $\le \text{clamp}(\lfloor 1.25 \cdot P_{\text{fact}} \rceil, 1500, 4000)\text{ ms}$, Hard $> \text{FluencyThreshold}$, and Again on error or timeout.
- **Authoritative Dense Band Progression**: Dense bands advance based on complete owned-frontier coverage and latest-outcome correctness ($C \cdot 10 \ge N \cdot 9$, where $N$ is owned frontier size and $C$ is count of facts whose latest attempt is Correct) evaluated over `PracticePosition > BandStartedPracticePosition` with in-memory candidate overlay. Errors and timeouts are recoverable by subsequent correct attempts. Fast Acquisition and the `MUL-D01` special 12-attempt bootstrap are retired; `MUL-D01` advances under the universal Dense rule ($N=4 \implies 4$ Correct). Structured bands retain the conservative 40-attempt rolling window gate ($\ge 38$ Correct, $\ge 34$ Fluent, $\ge 20$ frontier attempts, $\ge \min(16, N)$ distinct frontier facts, and 16 introductions).
- **Weak-Fact Continuity**: Dense advancement at $\ge 90\%$ preserves weak facts in `ItemLearningState`, FSRS card state, and remediation queues; advancement does not clear or reset weakness.
- **Teaching Interventions**: Non-mutating canonical equation teaching overlay triggers on a second consecutive session error on the same exact `FactId`, requiring the learner to observe a 3-second visible lockout before Continue enables, acknowledging without generating attempt records, advancing Practice Position, or mutating FSRS card state.
- **Session Check-ins & Pause Summaries**: Periodic checkpoint occurs every 20 accepted attempts, presenting correct count, median latency of correct attempts only, and actual Stage changes, offering Keep Going and Take a Break choices with guaranteed zero-timing pause semantics. Manual Pause displays lightweight current-session metrics (Completed, Correct, Current streak, Median correct latency).
- **Schema V6 Persistence & Partial Index**: Learner persistence format is **Schema V6** with partial index `ix_attempt_history_operation_fact_position` on `attempt_history(operation, fact_id, practice_position DESC) WHERE practice_position IS NOT NULL`. Store contract `LoadLatestFrontierAttemptsAsync` queries bounded latest-per-frontier attempts ($N \le 25$). Index creation and repair are idempotent physical maintenance without store revision increment.
- **Practice Fact Eligibility Invariant & Defense-in-Depth ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md))**:
  - For any presented arithmetic fact $F$ for operation $O$ at current progression band $B$: $\text{owner}_O(F) \le B$.
  - Universal applicability across all presentation roles: `New`, `Frontier`, `Due`, `Maintenance`, `EarlyReview`, and `Remediation`.
  - Canonical ownership is determined uniquely by `AcquisitionOwnershipResolver`.
  - Persisted/materialized historical facts with $\text{owner}_O(F) > B$ remain preserved losslessly in SQLite (`item_learning_state`, `fsrs_card_state`) as dormant learner evidence; they are never deleted or reset. When progression advances to their band, they automatically become eligible for review with historical FSRS intervals intact (`PERSISTED != CURRENTLY PRESENTABLE` and `DUE != AUTOMATICALLY ELIGIBLE`).
  - Defense-in-depth across layers: pure domain `IsEligible` resolver failing closed on invalid/future facts, snapshot filtering, SQLite candidate streaming before 64-item truncation (preventing window poisoning and candidate starvation), selector pure defense across all review pools, and persistence validation in `SqliteLearnerStore.ValidateNewAcceptedSubmission` rejecting future-fact attempts with transaction rollback (`PersistenceResult.InvalidSubmission`).
- **Session Startup Resilience & Recovery ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md))**:
  - `TrainingSession.InitializeAsync` fails closed on store or evidence failure, leaving `IsInitialized == false`.
  - `Home.razor` catches initialization exceptions during `OnInitializedAsync` to render a dedicated startup error boundary (`_startupFailed`) without evaluating `CurrentFact`, `LastEvaluation`, or `LastPersistenceResult`, preventing Blazor lifecycle crashes and `NullReferenceException`.
  - Non-destructive retry re-invokes `InitializeAsync` cleanly and transitions to `InitialReadyGate` on success without deleting or resetting learner progress.
  - Startup recovery is architecturally separated from post-submission `PersistenceFailure` recovery.
- Exact facts use stable, presentation-direction-sensitive canonical IDs.
- `FSRS.Core` 1.0.7 is integrated with 95% desired retention, 21 parameters, disabled fuzzing, deterministic per-FactId card identity, and Practice Position virtual time.
- `MathFirst.Infrastructure.Sqlite` owns the concrete native `SqliteLearnerStore` and `Microsoft.Data.Sqlite`; the Application layer owns persistence contracts. Accepted submissions commit attempt, item, FSRS, and progression changes atomically with revision checks, idempotent SubmissionId replay, and publish-after-successful-persistence session semantics.
- **Answer Entry Reliability & Reset Semantics**: Typing a wrong first digit of a multi-digit answer leaves the buffer editable without premature submission; Backspace deletes trailing digits; single-digit correct answers auto-submit immediately; and every newly generated fact starts with an empty answer input buffer. Reset Learning Progress clears learner attempts, item state, FSRS state, and progression while preserving UI/onboarding preferences, operation preferences, and practice-time preferences. Restore Default Settings restores all four operations and Standard practice time while preserving learner progress. Full Local Reset additionally restores all UI, onboarding, operation, and practice-time preferences to defaults. Ready, Pause, and Background-Resume gates are transient and are not stored in the learner schema.
- Practice UI provides a clean, distraction-free environment: the transient session score is absent, while the compact operation progress HUD remains and shows only enabled operations. The Pause button is styled in non-destructive amber/yellow (`.button-pause`), while destructive reset actions retain danger red.

---

## 4. Accepted Target Learning Architecture

- [ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md) records the accepted and implemented MF-LEARN-001 architecture:
  - independent per-operation band progression;
  - a 567-fact exhaustive dense foundation followed by deterministic structured arithmetic families;
  - one acquisition-owner band per exact fact;
  - representative 16-fact acquisition for structured bands;
  - correctness, latency, frontier evidence, and coverage as advancement gates;
  - deterministic operation/role scheduling, procedural generation, and lazy materialization;
  - Schema V5 progression and nullable per-attempt Practice Position;
  - atomic preservation of valid V4 learner evidence without automatic reset;
  - removal of the global Mixed Checkpoint and fixed level-10/418-fact acquisition ceiling.
- [ADR-0004](decisions/ADR-0004-adaptive-pace-fast-acquisition-and-practice-interventions.md) records the accepted and implemented MF-LEARN-002 architecture:
  - adaptive pace estimation via hierarchical shrinkage and dynamic per-fact answer deadlines;
  - adaptive fluency thresholds and FSRS rating mapping;
  - Schema V6 persistence with persisted attempt fluency (`is_fluent`);
  - role-specific selector chains with early review fallback and elimination of `AnyMaterialized`;
  - non-mutating repeated-error teaching overlays;
  - 20-attempt session check-ins with zero-timing pause flow;
  - distraction-free practice HUD.
- [ADR-0005](decisions/ADR-0005-acclimation-timing-and-rapid-dense-progression.md) records the accepted and implemented MF-LEARN-003 architecture:
  - answer-length acclimation deadlines and entry allowances for unproven facts (`CorrectAttempts == 0`);
  - Numpad default layout and visual priority (Numpad left, Phone right) across Onboarding and Settings;
  - Coverage-First Dense acquisition prioritizing unmaterialized owned-frontier facts (subsequently refined by MF-STAB-002 to restore requested-role review authority without unconditional override);
  - authoritative bounded latest-per-frontier persistence evidence via `LoadLatestFrontierAttemptsAsync` and Schema V6 partial index `ix_attempt_history_operation_fact_position`;
  - correctness-driven Dense band progression ($C \cdot 10 \ge N \cdot 9$) with in-memory candidate overlay and recoverable error/timeout votes;
  - separation of curriculum expansion from fluency/latency evaluations;
  - retirement of Fast Acquisition and the `MUL-D01` special bootstrap;
  - retention of Structured rolling-window advancement.
- [ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md) records the accepted and implemented Practice Fact Eligibility Invariant and Startup Resilience architecture:
  - establishes the Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) across all presentation roles (`New`, `Frontier`, `Due`, `Maintenance`, `EarlyReview`, `Remediation`);
  - explicitly supersedes the ADR-0003 fact-space review-eligibility semantics ("reviewable regardless of the current band");
  - preserves persisted future-band facts as dormant historical evidence without data deletion or progress reset;
  - enforces multi-layer defense-in-depth across pure domain resolver, snapshot filtering, SQLite candidate streaming, selector pure defense, and persistence validation gates;
  - establishes startup initialization recovery and fail-closed error boundaries in `Home.razor`;
  - records the formal rejection of former candidate `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` and signed Android AAB `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe` (`REAL_DEVICE_VERIFICATION_FAILED`, `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`).

---

## 5. Native Identity and Version Baseline

- MF-UX-002, **Deterministic Practice Personality and Contextual Copy**, is complete and merged. Its deterministic contextual copy remains presentation-only and does not affect Schema V6, learning-policy semantics, or accepted-attempt behavior.
- MF-UX-003 establishes the canonical V1 identity: application title `MathFirst`, application identifier `com.tachiguro.mathfirst`, display version `1.0`, build `1`, and company `Tachiguro`. Product and assembly titles are projected from the canonical title; Android and unpackaged Windows identity remain unchanged.
- MF-UX-005 extends runtime build identity metadata (PR #33) and tester diagnostics UX (PR #34): `AppBuildInfo` exposes `ApplicationId`, `SourceCommit`, `ShortSourceCommit`, `BuildClassification` (`Local`, `Tester`, `SourceCandidate`, `Production`), and `IsTesterBuild`. Settings displays localized version/build, build classification, short source commit, and provides a localized "Copy diagnostic info" action formatting support-safe runtime and platform metadata. Tester profile packages explicitly configure `ApplicationId = com.tachiguro.mathfirst.tester` and `BuildClassification = Tester` to enable side-by-side coexistence with production builds.
- Native branding uses a white geometric MF mark, primary `#176B4D`, companion `#0F523A`, light host background `#F4F7F5`, dark host background `#121916`, and adaptive icon foreground scale `0.65`. The MAUI single-project `MauiIcon`/`MauiSplashScreen` architecture generates native identity assets; manually maintained Android or Windows icon sets are not used.
- Native Not Found content is localized in English, German, and Russian; startup white-flash handoff uses a continuous `#176B4D` background across splash, Android WebView, and static HTML first paint.
- Contract coverage verifies canonical project properties and MSBuild projection, metadata parsing including fail-closed invalid inputs, localization and version display, SVG and native-host XAML structure, Android palette/identity, Windows unpackaged identity, removed template payloads, Not Found localization, and startup identity.
- MF-UX-006 candidate hardening aligns reset copy across English, German, and Russian to accurately specify PC numpad default restoration, resolves Russian haptic feedback punctuation formatting, replaces Russian HUD accessibility terminology with learner-appropriate progression language, implements an offline in-app `/privacy` surface accessible from Settings with `IAppBackNavigationCoordinator` Back integration, and hardens the static fatal host fallback in `index.html` with language-neutral error messaging and static English, German, and Russian reload affordances without runtime localization dependencies.

---

## 6. Android Packaging Automation and Release Hardening Baseline

- **Release Tooling & Entry Points**: Substantive packaging, signing policy, repository inspection, provenance emission, and offline validation logic is centralized in `tools/MathFirst.ReleaseTool` with thin PowerShell entry points:
  - `scripts/package-android-aab.ps1`: AAB packaging for `SourceCandidate` and `Distributable`.
  - `scripts/validate-android-aab.ps1`: Offline validation for `.aab` bundles.
  - `scripts/package-android-tester-apk.ps1`: APK packaging for `Tester` profile.
  - `scripts/validate-android-apk.ps1`: Offline validation for `.apk` packages.
- **Release Profiles**:
  - `SourceCandidate`: Supports any clean attached non-`main` branch without package-specific branch hardcoding; `.aab` format; development/debug signed (`-p:AndroidKeyStore=false`); non-distributable.
  - `Distributable`: Requires clean, synchronized `main` (`HEAD == local main == origin/main`); `.aab` format; release signed via external keystore and password files (`file:<path>`); distributable only after release-certificate verification.
  - `Tester`: Supports any clean attached non-`main` branch or clean synchronized `main`; `.apk` format; development/debug signed (`-p:AndroidKeyStore=false`); `ApplicationId = com.tachiguro.mathfirst.tester`; `BuildClassification = Tester`; non-distributable (tester distribution only).
- **Evidence Promotion Contract**: Successful packaging promotion for all profiles requires `ValidatorApproved` and populates an exact five-file directory (`artifacts/android/<profile>/<ArtifactId>/`):
  1. `<ArtifactId>.<ext>` (compiled `.aab` or `.apk`)
  2. `<ArtifactId>.provenance.json` (Schema v1 metadata, serialized once with immutable bytes)
  3. `<ArtifactId>.validation.json` (ValidationReceipt Schema v1)
  4. `TESTER_README.md` (deterministic distribution boundaries document)
  5. `SHA256SUMS` (exact lowercase SHA-256 bindings for the four payload files)
- **ValidationReceipt Schema v1**: Enforces fail-closed validation with string enums (`SourceCandidate`/`Distributable`/`Tester`, `ValidatorApproved`); rejects numeric enum representations; captures exact artifact SHA-256, staged provenance SHA-256, signer certificate SHA-256, and signer classification (`development-debug` or `release-distributable`).
- **Offline APK Validation (`AndroidApkValidator`)**: Enforces `apksigner` signature inspection (rejecting `jarsigner`), `aapt2 dump xmltree` manifest inspection (`com.tachiguro.mathfirst.tester`, minimum SDK 24, target SDK 36, `debuggable != true`, zero network permissions `INTERNET`/`ACCESS_NETWORK_STATE`, backup wiring), resource inspection, `dexdump -f` bytecode verification, and single development-debug signer verification.
- **Release Size Hygiene (PR #37)**: Release configuration removes `wwwroot\lib\bootstrap\dist\css\bootstrap.min.css.map` through the `MathFirst.App.csproj` `Content Remove` rule. The measured map payload was 589,893 bytes uncompressed and contributed 90,534 compressed bytes to the analyzed APK.
- **Signing, Update, and Coexistence Invariants**:
  - Development/debug keystores are per-machine; in-place Android updates (`adb install -r`) require matching certificate signatures.
  - Distinct `ApplicationId` (`com.tachiguro.mathfirst.tester`) isolates the tester app in its own Android sandbox, coexisting with production without data conflict.
- **Release Profile Characterization**: Characterizes Release build properties for Android and unpackaged Windows targets.
- **Installed Size / App Data / RAM Investigation Evidence**:
  - Universal Tester APK: 33,256,092 bytes.
  - ZIP payload: 32,976,458 bytes compressed and 72,020,118 bytes uncompressed.
  - `arm64-v8a` native payload: 12,319,237 bytes compressed and 25,926,096 bytes uncompressed.
  - `x86_64` native payload: 12,890,294 bytes compressed and 26,058,992 bytes uncompressed.
  - DEX payload: 5,682,213 bytes compressed.
  - Installed evidence: `base.apk` was 33,256,092 bytes; extracted ARM64 native libraries were 25,926,096 bytes; Android Settings reported approximately 75.86 MB total.
  - Approximately 16.68 MB remained system-owned or otherwise unexplained because shell permissions prevented direct attribution. This remainder is not labeled as ART.
  - App Data measured approximately 4.40 MB initially and approximately 8.99 MB after extended use. Cache measured approximately 401 KB initially and approximately 422 KB later. Exact internal App Data attribution was unavailable, so growth is not assigned to a storage or runtime subsystem.
  - RAM verdict: `NO_CLEAR_LEAK_SIGNAL`. No sustained monotonic PSS growth was observed, the native-memory category was broadly stable, and memory was reclaimed across lifecycle transitions. This does not prove that no leak can exist.
  - Play delivery verdict: `CURRENT_PLAY_DELIVERY_SIZE_NOT_MEASURED`.
  - The universal Tester APK includes `arm64-v8a` and `x86_64`. ARM64-only Tester packaging was evaluated and deferred.
  - No V1 change was justified for full IL trimming, AOT changes, R8/ProGuard, GC tuning, WebView architecture, or manual Kotlin/AndroidX metadata stripping.
- **Timer-Specific Physical Evidence**: On Samsung SM-S948B, Android 16, arm64-v8a, replacement Tester APK installation succeeded while preserving existing app/learner data. The Timer pill was absent; white digits, restrained dark contrast, and centering were present; Light, Dark, and No Time Pressure presentations were checked; and the user explicitly accepted the result. This bounded evidence is not final production-candidate certification.
- **Downstream Release State**:
  - Former native V1 candidate `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` and signed Android AAB `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe` were rejected during physical-device verification (`REAL_DEVICE_VERIFICATION_FAILED`, `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`); preserved as historical evidence only.
  - Forensic remediation was merged to `main` via PR #29 (`156d5afd32299ba19d8ca2a8f2f56a7babfe31d8`).
  - MF-UX-005 implementation and investigation scope is complete through PR #39 on synchronized `main` (`60dba236aa38bca138ab583f610c9ff876994b04`).
  - Repeatable Tester APK Workflow was merged through PR #36 (`e908bf2fba820f4f6adf03b278861b956e5dbbd5`).
  - Release Bootstrap source-map exclusion was merged through PR #37 (`dbc6bf045454a78a1ba32793fa02245f83b50437`).
  - Timer visual remediation was merged through PR #38 (`d1705bbdc0013372e44eadf7310ff2f313ecdcef`) and accepted in the bounded physical Timer run.
  - Replacement production AAB created by MF-UX-005: **NO**.
  - Production signing performed by MF-UX-005: **NO**.
  - Full validation (`FULL_VALIDATION`): **PENDING / SEPARATE LIFECYCLE**.
  - Production packaging and signing (`Distributable` AAB): **PENDING / SEPARATELY AUTHORIZED**.
  - Final release-grade physical validation of the exact production candidate: **PENDING / SEPARATE LIFECYCLE**.
  - Google Play publication gate: **PENDING / SEPARATELY AUTHORIZED**.

---

## 7. Durable Delivery Evidence

- MF-AND-001 was merged to `main` through GitHub Pull Request #7 on 2026-09-08, adding the Android V1 runtime and native SQLite layer while preserving Windows behavior.
- Repository history records 178 passing automated unit and simulation tests before the Android V1 package; the merged Android package adds deterministic lifecycle, input, responsive-layout, localization, and SQLite architecture coverage.
- Repository release notes record a completed real-device Android spot-check for responsive layout, keypad/native IME behavior, Ready/Pause/Resume, smart auto-submit, and Incorrect/Timeout feedback context.
- MF-LEARN-001 completed with `REVIEW_APPROVED`, a full Core suite of 409 passed, 0 failed, and 0 skipped, Windows and Android Release builds with 0 warnings and 0 errors, clean vulnerability and static-regression audits, Pull Request #9 merge, and successful post-merge synchronization.
- MF-UX-002 was merged to `main` through Pull Request #11 on 2026-09-10, adding deterministic contextual practice copy.
- MF-UX-003 was merged to `main` through Pull Request #12 on 2026-09-10, reconciling native identity, versioning, and visual assets.
- MF-STAB-001 was merged to `main` through Pull Request #13 on 2026-09-10 at `45ef623f44df87c0da97460d38dbb797c2aa18bf`, stabilizing practice progression (MUL-D01 bootstrap profile), 30-second countdown bar, compact progress HUD, transient session score, and danger Pause action, with 546 passing Core tests and 0 warnings/errors on Windows/Android Release builds.
- MF-LEARN-002 was merged to `main` through Pull Request #15 on 2026-09-11 at `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1` (head `1efc34c09c3611ff221204966104eb99ec03c7a3`), adding Schema V6 persistence, adaptive pace & answer deadlines, adaptive fluency mapping, fast acquisition for dense bands, role-specific selector chains, repeated-error teaching overlay, session check-ins with zero-timing pause semantics, and clean practice HUD, verified with 708 passing Core tests and clean post-merge synchronization.
- MF-LEARN-003 was merged to `main` through Pull Request #16 on 2026-09-11 at `9a9e5c43d1f1685cf21e766e0890b790ab09040c`, adding answer-length acclimation deadlines, durable fact proof, Numpad layout defaults, Coverage-First Dense selection, authoritative latest-per-frontier persistence evidence via `LoadLatestFrontierAttemptsAsync` and Schema V6 partial index `ix_attempt_history_operation_fact_position`, correctness-driven Dense progression ($C \cdot 10 \ge N \cdot 9$) with recoverable errors, and editable incomplete multi-digit answers before auto-submission, verified with 790 passing Core tests in `MathFirst.Core.Tests` and clean post-merge synchronization.
- MF-REL-001 was merged to `main` through Pull Request #17 on 2026-09-12 at `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`, establishing Android internal AAB packaging automation, `tools/MathFirst.ReleaseTool`, release profiles (`SourceCandidate`, `Distributable`), offline bundle validator, companion schema-v1 provenance JSON, Android manifest security hardening, and multi-generation backup rules.

- MF-SET-001 was merged to `main` through Pull Request #18 on 2026-09-12 at `a051518420db3b45f8ca1074bac27e9b4d1b799d`: added configurable operation subsets in Settings and Onboarding, returning learner progress presentation, session check-in 20-attempt summaries, configurable Practice Time floors, enabled-only HUD visibility, schedule-agnostic persistence, operation/position-scoped evidence, exactly-once post-write recovery, active-only process-local timing, Settings diagnostics removal, and answer-input lifecycle reliability (editable multi-digit input, new-fact empty input invariant). Verified with 1037 Core tests passing, clean Windows/Android Release builds, and confirmed manual validation on a physical Android device (`MathFirst-MF-SET-001-f453501-debug.apk` SHA-256 `97f85e448d078e46d813aa1244e49b95933e51e1c9d64732faf3a614ae31db12`). Post-merge synchronization is complete.
- MF-DOC-003 was merged to `main` through Pull Request #19 at `6e137471a16ffced5f5a62daa1a3f5143b5bbb7a`, reconciling post-merge project state and V1 baseline documentation following MF-SET-001.
- MathFirst Privacy Policy was merged to `main` through Pull Request #20 at `c4ae75a99f7971033ca9887bb2277b217979be03`, establishing a standalone offline privacy policy document (`PRIVACY.md`) satisfying Google Play Store requirements.
- MF-STAB-002 was merged to `main` across three targeted stabilization slices:
  - **Slice 1 (Practice Selection Diversity)** merged through Pull Request #21 at `7cc6caebec1798d5cfb3c48172b6f78360fb2442`, adding deterministic fact candidate ranking, anti-ladder candidate selection when alternatives exist, and preserved exact/mirror cooldown semantics.
  - **Slice 2 (Bounded Operation Scheduling)** merged through Pull Request #22 at `33d745d270745c2de65e47519cb98959278bbcbd`, adding deterministic bounded operation bags guaranteeing every enabled operation appears exactly once per bag without RNG or persisted scheduler state.
  - **Slice 3 (Review Balance Stabilization)** merged through Pull Request #23 at `2d59464fd3de8cefa5b0f404c00e9bcc74c98dc9`, restoring requested-role authority during Dense acquisition, bounding New introductions to 4 requested slots per 10 per-operation attempts, and enabling Due/Maintenance/Frontier review opportunities without global suppression by unseen Dense material. Verified with 1074 passing Core tests in `MathFirst.Core.Tests`.
- MF-DOC-004 was merged to `main` through Pull Request #24 at `3e471e20e2d452ee1e383579ed3d0831e1825d3e` (head `1b2391ab4afa04e90c41bcb76afa4ac3cafcb3e1`), reconciling post-stabilization project state across repository documentation (`docs/CURRENT_WORK.md`, `docs/BACKLOG.md`, `docs/ROADMAP.md`, `docs/PROJECT_STATE.md`, `docs/NEW_CHAT_BOOTSTRAP.md`, `CHANGELOG.md`).
- MF-UX-004 was merged to `main` through Pull Request #25 at `46a7158d3c7fbdf6bc43fe120c35863ac55bb78b` (head `ab0cd8946e904362d7a9c36495f4a324f5bf4a45`), adding CSS active-press visual feedback for enabled numeric keypad buttons, preserving `:focus-visible` keyboard focus outlines, and validating responsive layout behavior across viewports without introducing JavaScript or Blazor pointer-state machinery.
- MF-REL-002 was merged to `main` through Pull Request #26 at `79e0d48c058747e8112388d31d721a049ec2857a` (head `5db6f65bffb57bf35c895a729e3bd54f7f607600`), generalizing `SourceCandidate` packaging policy to accept any clean attached non-`main` branch without package-specific branch hardcoding, establishing strict validator-approved five-file evidence promotion (`<ArtifactId>.aab`, `<ArtifactId>.provenance.json`, `<ArtifactId>.validation.json`, `TESTER_README.md`, `SHA256SUMS`), binding immutable provenance bytes, implementing ValidationReceipt Schema v1, generating deterministic `TESTER_README.md`, defining exact `SHA256SUMS` contract, and characterizing release build profiles including unpackaged Windows release properties.
- Post-Release Project State Reconciliation was merged to `main` through Pull Request #27 at `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` (head `b6e0ed39edc6630a353b0372c0487cf8f6cf4f3d`), reconciling documentation after MF-REL-002.
- Native V1 Release Blocker Forensic Remediation Plan was merged to `main` through Pull Request #28 at `4e997f35b4a7884b4b0592beab682a36319ea358` (head `9f080ef44f58e1dc90c06cb729a178104972def2`), documenting forensic analysis and remediation steps for fact eligibility and startup resilience defects.
- Native V1 Release Blocker Remediation was merged to `main` through Pull Request #29 at `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8` (head `d055565808c197c95666b7239be73b0a88112d4a`), implementing Practice Fact Eligibility Invariant ($\text{owner}_O(F) \le B$) and session startup error boundary / non-destructive recovery ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)) with 1169 passing Core tests.
- MF-UX-005 Slices 1–6 (Native UX Polish Baseline) was merged to `main` through Pull Request #30 at `bde91a7578753f5c468d0534282edd9fd13f32a0` (head `83ac5f32f9b82d739b19680f2a0f881a57ec36a3`), adding timer render isolation (~10 Hz in child component), keypad reset per fact, KnownFirst-style vertically stacked full-width onboarding action layout preserving 5-step flow and draft state, application-owned `IAppBackNavigationCoordinator` handling Android system Back across Onboarding/Settings/Practice, 3-second dwell lockout on repeated-error teaching dialog, non-destructive amber Pause styling (`.button-pause`), the then-current Timer pill treatment later superseded by PR #38, configurable haptic feedback with normal `VIBRATE` permission, No Time Pressure practice time mode with count-up elapsed display, restrained textual streak indicator ($\ge 3$), and transient Pause session summary with 1269 passing Core tests.
- MF-UX-005 Slice 7 (Startup White-Flash Elimination) was merged to `main` through Pull Request #31 at `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6` (head `0ac5ef63f3f02342d313e72bc51b050086c984b9`), eliminating cold-launch white flash during native-to-Blazor handoff by setting Android BlazorWebView background to `#176B4D`, adding synchronous inline first-paint styling in `index.html`, and removing raw unstyled placeholder with 1274 passing Core tests.
- MF-UX-005 Blocker Remediation (Android Release Startup Resource Order Fix) was merged to `main` through Pull Request #32 at `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e` (head `1d61b11ebee3584685265e67d1ebee0651476a1c`), resolving startup `XamlParseException` by deferring `MainPage` resolution until `CreateWindow()`, verified with 6 contract regression tests and 1280 passing Core tests.
- MF-UX-005 Tester Ergonomics — Slice A (Build Identity Metadata) was merged to `main` through Pull Request #33 at `83b767c2265c1baba560abdeaa9fedad365d70da` (head `22a1d53f58b11ffd557d7f744d6d45eea545afdf`), extending runtime build metadata model/parser with `ApplicationId`, `SourceCommit`, and explicit fail-closed `BuildClassification` (`Local`, `Tester`, `SourceCandidate`, `Production`), deterministic short SHA formatting, MSBuild projection in `MathFirst.App.csproj`, and `AppBuildInfo` integration with 59 targeted tests and 1318 total passing Core tests.
- MF-UX-005 Tester Ergonomics — Slice B (Diagnostics & Settings UX) was merged to `main` through Pull Request #34 at `82c117912f72d6efe16055f8be754c9c577ae15a` (head `c3b468687db571a3eab41278d5df2653b4a747ed`), adding support-safe deterministic `AppDiagnosticFormatter`, minimal platform info and clipboard abstractions (`IAppPlatformInfo`, `IClipboardService`) with MAUI implementations, Settings footer build identity display and localized "Copy diagnostic info" action with async execution and feedback status, complete English/German/Russian localization keys, verified with 114 targeted unit/contract tests and 1325 total passing Core tests.
- MF-UX-005 Post-PR #34 Documentation Reconciliation was merged to `main` through Pull Request #35 at `336322b5386a872ebb726c7bdcf34bb207592650` (head `0b3c41b22e1f689a8b44b28a93a7228db6b8f66e`), reconciling baseline documentation on `main` following merge of PR #34.
- MF-UX-005 Repeatable Tester APK Workflow was merged to `main` through Pull Request #36 at `e908bf2fba820f4f6adf03b278861b956e5dbbd5` (head `2acfccabb81d814f966a197f0b2d7cb310b9e125`), adding `ReleaseProfile.Tester`, deterministic APK naming and workspace routing, `AndroidApkValidator` with `apksigner`/`aapt2`/`dexdump` inspection, ValidationReceipt Schema v1 Tester evidence, `AndroidPackageCommand` integration, and the PowerShell entrypoints `scripts/package-android-tester-apk.ps1` and `scripts/validate-android-apk.ps1`; historical automated evidence was 1462 passing Core tests.
- MF-UX-005 Release Bootstrap Source-Map Exclusion was merged to `main` through Pull Request #37 at `dbc6bf045454a78a1ba32793fa02245f83b50437` (head `a06d0b3c2eee184ac0b067d4536b9434c8da644a`), adding the Release-only `MathFirst.App.csproj` exclusion and its packaging contract coverage; 9/9 targeted tests and 1463/1463 full Release Core tests passed.
- MF-UX-005 Timer Visual Remediation was merged to `main` through Pull Request #38 at `d1705bbdc0013372e44eadf7310ff2f313ecdcef` (head `2a7021977571e8c056a4ab2128ddc74e79b0f6b3`), removing the Timer backing pill and applying the current restrained local dark shadow to bold white tabular numerals; 9/9 targeted tests and 1463/1463 full Release Core tests passed.
- MF-UX-005 Installed Size / App Data / RAM investigation completed with the documented attribution limits and verdicts `NO_CLEAR_LEAK_SIGNAL` and `CURRENT_PLAY_DELIVERY_SIZE_NOT_MEASURED`.
- MF-UX-005 Timer-specific physical verification on Samsung SM-S948B, Android 16, arm64-v8a was explicitly accepted by the user. This evidence remains separate from final release-grade physical validation.
- MF-UX-005 Final Documentation Reconciliation was merged to `main` through Pull Request #39 at `60dba236aa38bca138ab583f610c9ff876994b04` (head `abfa73ef3a5a2ef9cf0c5d3cc3e3f48a5e607a78`), establishing the synchronized post-MF-UX-005 documentation baseline on `main`.
- **Candidate In-Flight State (MF-UX-006)**: Implemented across three checkpoint commits on task branch `feat/mf-ux-006-v1-privacy-copy-localization` (candidate pre-doc HEAD `130cae92032ebdfbd6f430b5cf353192d5e39ba3`), passing consolidated package review (`REVIEW_PASS`); currently undergoing `DOCUMENT_ONLY` reconciliation; `FULL_VALIDATION` pending.

---

## 8. Durable Product Boundaries

- Core practice is offline-first and requires no account.
- Local in-app Privacy surface (`/privacy`) is readable offline without network access, analytics, or user accounts.
- Zero runtime networking or network permissions contract is preserved.
- Correctness and response latency are separate learning evidence.
- Negative subtraction, division with remainder, cloud synchronization/accounts, export/import, and larger-than-`Int32` arithmetic remain deferred.
- Packaging, signing, store publication, and deployment require separate authorized lifecycle work.
