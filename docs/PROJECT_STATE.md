# MathFirst Verified Project State

This document records stable, verified facts about MathFirst. It excludes transient package workflow state and distinguishes current implementation from accepted target architecture.

---

## 1. Project Identification

- **Project Name**: MathFirst
- **Repository URL**: https://github.com/Tachiguro/MathFirst
- **Default Branch**: `main`
- **License**: Apache License 2.0 (see [LICENSE.txt](../LICENSE.txt))
- **Current Status**: `MF-LEARN-001`, `MF-UX-002`, `MF-UX-003`, `MF-STAB-001`, and `MF-LEARN-002` are complete and merged into `main` (`MF-LEARN-002` merged through PR #15 at `b2c5a43707167d5f6720d66834ebbbd3c6ede1d1`). `MF-LEARN-003` (Acclimation Timing, Rapid Dense Expansion, and Keypad Defaults) is fully implemented across 5 checkpoint slices on branch `feat/mf-learn-003-acclimation-rapid-dense` at HEAD `9e07ec34e42a688847e23e53e47adb728fbec428`, verified with 769 passing Core tests (471 focused package tests), and independently reviewed with `REVIEW_PASS`. Active work is `DOCUMENT_ONLY` documentation reconciliation for `MF-LEARN-003`.

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
- **Keypad Defaults & Ergonomics**: Numeric keypad layout defaults to `Numpad` (`7 8 9` at top). Onboarding and Settings display choices with Numpad first (left) and Phone second (right). Initial Home backing state and Full Local Reset default to Numpad. Existing explicitly stored preferences (`Phone` or `Numpad`) remain preserved.
- **Selector Model & Coverage-First Dense Selection**: Selector precedence evaluates: 1. eligible Remediation ($\ge 4$ distance); 2. Dense Coverage-First New; 3. ordinary requested-role fallback chains (`Requested New`, `Requested Due`, `Requested Maintenance`, `Requested Frontier`). Dense Coverage-First materializes unseen owned-frontier facts as `PracticeSelectionRole.New` across nominal Due/Maintenance/Frontier turns until first-pass coverage is complete. Structured bands do not receive Coverage-First selection. Non-New resolved roles strictly cannot materialize unseen facts. `Early Review` acts as a strictly bounded liveness bridge.
- **Adaptive Pace & Acclimation Deadlines**: Hierarchical shrinkage pace estimation ($P_0 = 4500\text{ ms}$; learner $W=12, N=30$; operation $W=8, N=20$; band $W=6, N=15$; fact $W=4, N=5$) clamped to $[600, 12000]\text{ ms}$. Unproven facts (`CorrectAttempts == 0`) receive digit-aware novelty floors (15s for 1 digit, 20s for 2 digits, 25s for 3 digits, 30s for 4+ digits) and multi-digit entry allowances ($+1000\text{ ms} \cdot \max(0, \text{DigitCount} - 1)$). Adaptive deadline is $\lceil (2 \cdot P_{\text{fact}} + \text{Allowance} + \text{EntryAllowance}) / 100 \rceil \cdot 100\text{ ms}$, clamped $[3000, 30000]\text{ ms}$. Proven facts use the adaptive deadline; unproven facts receive $\min(30000, \max(\text{AdaptiveDeadline}, \text{NoveltyFloor}))$. Novelty floors expand opportunity only and do not alter latency measurement, fluency thresholds, or FSRS ratings.
- **Adaptive Fluency & Ratings**: FSRS ratings adapt to fact pace: Easy $\le \text{clamp}(\lfloor 0.85 \cdot P_{\text{fact}} \rceil, 600, 2000)\text{ ms}$, Good $\le \text{clamp}(\lfloor 1.25 \cdot P_{\text{fact}} \rceil, 1500, 4000)\text{ ms}$, Hard $> \text{FluencyThreshold}$, and Again on error or timeout.
- **Authoritative Dense Band Progression**: Dense bands advance based on complete owned-frontier coverage and latest-outcome correctness ($C \cdot 10 \ge N \cdot 9$, where $N$ is owned frontier size and $C$ is count of facts whose latest attempt is Correct) evaluated over `PracticePosition > BandStartedPracticePosition` with in-memory candidate overlay. Errors and timeouts are recoverable by subsequent correct attempts. Fast Acquisition and the `MUL-D01` special 12-attempt bootstrap are retired; `MUL-D01` advances under the universal Dense rule ($N=4 \implies 4$ Correct). Structured bands retain the conservative 40-attempt rolling window gate ($\ge 38$ Correct, $\ge 34$ Fluent, $\ge 20$ frontier attempts, $\ge \min(16, N)$ distinct frontier facts, and 16 introductions).
- **Weak-Fact Continuity**: Dense advancement at $\ge 90\%$ preserves weak facts in `ItemLearningState`, FSRS card state, and remediation queues; advancement does not clear or reset weakness.
- **Teaching Interventions**: Non-mutating canonical equation teaching overlay triggers on a second consecutive session error on the same exact `FactId`, requiring the learner to press "I understand" before proceeding without generating attempt records, advancing Practice Position, or mutating FSRS card state.
- **Session Check-ins**: Periodic checkpoint occurs every 20 accepted attempts, presenting correct count and median latency of correct attempts only, offering "Keep Going" and "Take a Break" choices with guaranteed zero-timing pause semantics.
- **Schema V6 Persistence & Partial Index**: Learner persistence format is **Schema V6** with partial index `ix_attempt_history_operation_fact_position` on `attempt_history(operation, fact_id, practice_position DESC) WHERE practice_position IS NOT NULL`. Store contract `LoadLatestFrontierAttemptsAsync` queries bounded latest-per-frontier attempts ($N \le 25$). Index creation and repair are idempotent physical maintenance without store revision increment.
- Exact facts use stable, presentation-direction-sensitive canonical IDs.
- `FSRS.Core` 1.0.7 is integrated with 95% desired retention, 21 parameters, disabled fuzzing, deterministic per-FactId card identity, and Practice Position virtual time.
- `MathFirst.Infrastructure.Sqlite` owns the concrete native `SqliteLearnerStore` and `Microsoft.Data.Sqlite`; the Application layer owns persistence contracts. Accepted submissions commit attempt, item, FSRS, and progression changes atomically with revision checks, idempotent SubmissionId replay, and publish-after-successful-persistence session semantics.
- Reset Learning Progress clears learner attempts, item state, FSRS state, and progression while preserving UI/onboarding preferences. Full Local Reset additionally restores applicable UI and onboarding preferences. Ready, Pause, and Background-Resume gates are transient and are not stored in the learner schema.
- Practice UI provides a clean, distraction-free environment: the transient session score and progression HUD indicators have been removed from active practice. The Pause button is styled consistently in danger red.

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
  - Coverage-First Dense acquisition prioritizing unmaterialized owned-frontier facts;
  - authoritative bounded latest-per-frontier persistence evidence via `LoadLatestFrontierAttemptsAsync` and Schema V6 partial index `ix_attempt_history_operation_fact_position`;
  - correctness-driven Dense band progression ($C \cdot 10 \ge N \cdot 9$) with in-memory candidate overlay and recoverable error/timeout votes;
  - separation of curriculum expansion from fluency/latency evaluations;
  - retirement of Fast Acquisition and the `MUL-D01` special bootstrap;
  - retention of Structured rolling-window advancement.

---

## 5. Native Identity and Version Baseline

- MF-UX-002, **Deterministic Practice Personality and Contextual Copy**, is complete and merged. Its deterministic contextual copy remains presentation-only and does not affect Schema V6, learning-policy semantics, or accepted-attempt behavior.
- MF-UX-003 establishes the canonical V1 identity: application title `MathFirst`, application identifier `com.tachiguro.mathfirst`, display version `1.0`, build `1`, and company `Tachiguro`. Product and assembly titles are projected from the canonical title; Android and unpackaged Windows identity remain unchanged.
- Settings displays localized version/build information. `AppBuildInfo` reads generated application metadata through the shared platform-neutral parser, which requires complete, non-blank, valid numeric display-version metadata and a positive invariantly parsed build number.
- Native branding uses a white geometric MF mark, primary `#176B4D`, companion `#0F523A`, light host background `#F4F7F5`, dark host background `#121916`, and adaptive icon foreground scale `0.65`. The MAUI single-project `MauiIcon`/`MauiSplashScreen` architecture generates native identity assets; manually maintained Android or Windows icon sets are not used.
- Native Not Found content is localized in English, German, and Russian; the pre-Blazor host placeholder is the language-neutral `MathFirst`. The former unused template image/raw payloads were removed.
- Contract coverage verifies canonical project properties and MSBuild projection, metadata parsing including fail-closed invalid inputs, localization and version display, SVG and native-host XAML structure, Android palette/identity, Windows unpackaged identity, removed template payloads, Not Found localization, and the startup identity.

---

## 6. Current Feature Candidate State

- Active candidate branch: `feat/mf-learn-003-acclimation-rapid-dense` at HEAD commit `9e07ec34e42a688847e23e53e47adb728fbec428` (Checkpoint 5/5 `rapid-dense-progression`).
- All behavioral implementations across 5 slices, test suites (769 Core tests, 471 focused package tests), and independent review have completed with `REVIEW_PASS`.
- Active operation mode: `DOCUMENT_ONLY` documentation reconciliation.
- Next lifecycle steps: `COMMIT_ONLY` staging and commit creation, followed by `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, manual merge, and `POST_MERGE_SYNC_ONLY`.

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
- MF-LEARN-003 completed behavioral implementation across 5 checkpoint slices with `REVIEW_PASS`, adding answer-length acclimation deadlines, durable fact proof, Numpad layout defaults, Coverage-First Dense selection, authoritative latest-per-frontier persistence evidence via `LoadLatestFrontierAttemptsAsync` and Schema V6 partial index `ix_attempt_history_operation_fact_position`, and correctness-driven Dense progression ($C \cdot 10 \ge N \cdot 9$) with recoverable errors, verified with 769 passing Core tests (471 focused package tests). The candidate has not yet undergone `FULL_VALIDATION`; release builds, packaging, and integration remain pending.

---

## 8. Durable Product Boundaries

- Core practice is offline-first and requires no account.
- Correctness and response latency are separate learning evidence.
- Negative subtraction, division with remainder, cloud synchronization/accounts, export/import, and larger-than-`Int32` arithmetic remain deferred.
- Packaging, signing, store publication, and deployment require separate authorized lifecycle work.
