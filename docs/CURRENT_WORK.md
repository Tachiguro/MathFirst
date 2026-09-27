# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: MF-LEARN-006: Evidence-Adaptive Discovery, Guided Soft Decoupling, and Pace Calibration (Local implementation package complete through Slice 7; complete-package review passed with REVIEW_PASS; operational status docs reconciled; awaiting FULL_VALIDATION)
- **Current Lifecycle**: DOCUMENT_ONLY complete (Operational status documentation reconciled) / Awaiting FULL_VALIDATION
- **Status of Adaptive Learning Work**: The full MF-LEARN-006 implementation package is complete locally on task branch `feat/mf-learn-006-adaptive-learning-policy` through checkpoint `b51a63b3e559d35a49dcca1833c0750a72eb5f88` (10 implementation-related commits ahead of main). Complete-package `REVIEW_ONLY` passed with `REVIEW_PASS` (zero CRITICAL findings, zero IMPORTANT findings; full Debug suite: 1,772 passed, 0 failed, 0 skipped). Option-B architecture and implementation are fully aligned; Option-B selector remediation and Slice 7 verification are complete. The exact canonical strong-learner benchmark (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt exactly 482; Addition accepted attempts: exactly 121), real-SQLite restart equivalence, cold-restart next-selection determinism, and Schema V6 preservation (zero schema changes/migrations) are permanently locked under regression tests. The branch is strictly local and unpublished (no push has occurred, no PR exists, no merge has occurred).
- **Next Lifecycle Step**: `FULL_VALIDATION — MF-LEARN-006 complete implementation package` (upon explicit user dispatch; followed if authorized by PUSH_ONLY, PR_ONLY, manual merge authorization, and POST_MERGE_SYNC_ONLY).

### 1.1 MF-LEARN-006 Local Implementation Package Summary

The completed package on `feat/mf-learn-006-adaptive-learning-policy` contains:
1. **Tiered Remediation and Broad Weakness**: Differentiates isolated slips (cooldown 4) from repeated local errors (tightened cooldown 2; presentation teaching interventions preserved) and broad systemic distress ($\ge 2$ active unresolved remediation facts within operation/context, suppressing New discovery);
2. **Evidence-Adaptive Discovery**: Review and consolidation turns are dynamically promoted to New when scheduled pools contain zero acquisition-blocking work and evidence is clean, achieving an emergent discovery ratio under the approved Option-B policy;
3. **Absolute No-Immediate-Fact-Repetition Invariant**: Strict $\text{FactId}(t+1) \ne \text{FactId}(t)$ across all attempt outcomes, strictly outranking anti-ladder candidate selection;
4. **Guided Gate G3 Soft Decoupling**: Multiplication and Division softly decouple from `AdditionCeiling` once their respective `OperationProgression.BandIndex >= 3` (proving factor mastery across 0/1/2/3), restoring autonomous canonical progression without magic jumps;
5. **Durable Pace Calibration Readiness**: Calibrated status declared at $\ge 24$ positioned Correct attempts (`PracticePosition > 0` and `Outcome == Correct`), where EasyThreshold drift drops reliably below 10%;
6. **Critical Hit Calibration Gate**: Downstream gamification presentation contract strictly consuming learning telemetry (1 HP before calibration, 2 HP Critical Hit for fast Correct $\le \text{EasyThresholdMs}$ after calibration, 1 HP for slow Correct), with zero mutation of learning telemetry, FSRS, item states, or progression;
7. **Option-B Architecture Reconciliation**: Reconciled and approved ADR-0010, canonical design specification, and PRODUCT.md to Option-B discovery policy;
8. **Option-B Selector Remediation**: Implemented Option-B adaptive discovery in `AdaptivePracticeSelector`, correctly promoting clean consolidation turns without premature throttling;
9. **Final Regression Package**: Exact 482 benchmark permanently locked (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt exactly 482, Addition accepted attempts: exactly 121), real-SQLite restart equivalence verified, cold-restart next-selection determinism verified, and Schema V6 preserved.

### 1.2 Current Verified Quality State

- **Complete-Package Review**: `REVIEW_PASS` (`REVIEW_ONLY` lifecycle concluded with zero CRITICAL findings and zero IMPORTANT findings).
- **Automated Test Suite**: Full Debug suite: `1,772 passed, 0 failed, 0 skipped` (`MathFirst.Core.Tests`).
- **Strong-Learner Benchmark**: Exact canonical benchmark `ADD-D10 -> ADD-P1-ANCHOR` achieved at global accepted attempt exactly `482`; Addition accepted attempts: exactly `121`.
- **Restart Equivalence & Determinism**: Real-SQLite restart equivalence verified; cold-restart next-selection determinism verified.
- **Persistence Contract**: Schema V6 preserved intact without migration, table, or column additions.
- **Remote / Delivery Boundaries**: No remote CI has run; no PR exists; no push has occurred; nothing has been merged.

---

## 2. Recently Merged Implementation Package: MF-UX-007 Summary

Package `MF-UX-007` was completed across two structured checkpoint commits on task branch `codex/mf-ux-007-progress-presentation-cleanup`, candidate-validated at `5f489bae56cfd3bb9ea0b895baaf6778382ef867`, and merged into `main` through PR #47 at commit `d37fbe3347679220bf847b06c83f7f9366738d03`:

1. **Slice 1: Progress Localization and Accessibility**
   - Commit SHA: `316fe525b136e7fee6dd1c7e7e7dec4636b6fcba`
   - Trailer: `MathFirst-Checkpoint: MF-UX-007 1/2 progress-localization-accessibility`
   - Scope:
     - Stage terminology refinement across English (`Stage {0}` / `{0}: Stage {1}`), German (`Stufe {0}` / `{0}: Stufe {1}`), and Russian (`Уровень {0}` / `{0}: уровень {1}`);
     - Removal of diagnostic jargon ("progression stage", "Fortschrittsstufe", "этап прогресса") to match Session Check-In terminology;
     - Accessible group label `Training_OperationProgressGroupAriaLabel` for learner-facing progress HUD;
     - Focused localization contract coverage in `tests/MathFirst.Core.Tests/PolicyAndLocalizationTests.cs` and `tests/MathFirst.Core.Tests/OnboardingAndProgressFeedbackTests.cs`.

2. **Slice 2: Progress UI and Responsive Presentation**
   - Commit SHA: `37efd21b788e27f5683cc5382fe7c38dc1b7a05f`
   - Trailer: `MathFirst-Checkpoint: MF-UX-007 2/2 progress-ui-responsive-presentation`
   - Scope:
     - Initial Ready Gate overview: structured semantic rows (`role="list"`, `role="listitem"`, `aria-label="@progressLabel"`) with operation symbol, localized name, and Stage display;
     - Active HUD markup polish: learner-facing group aria label, full localized Stage descriptions in `aria-label` and `title`, decorative symbols marked `aria-hidden="true"`;
     - CSS responsive polish: flex layout for Ready overview rows with `overflow-wrap: break-word` on operation names, surface elevation via design tokens `var(--color-surface-elevated)` and `var(--shadow-sm)`, preserved 1–4 operation HUD grid rules and $\le 480\text{ px}$ 2-column mobile behavior;
     - Semantic contract coverage in `tests/MathFirst.Core.Tests/ResponsiveAndCorrectAnswerFlowTests.cs`.

---

## 3. Package Summary & Authoritative Invariants

MF-UX-007 delivers a refined, accessible learner-facing progress presentation while preserving all repository and learning invariants:

1. **Learner-Facing Stage Terminology**:
   - Authoritative mapping remains strictly $\text{PresentationStage} = \text{BandIndex} + 1$.
   - Natural, concise Stage phrasing across English, German, and Russian.
2. **Returning Learner Ready Overview**:
   - Rendered strictly when `Session.PracticeGate == PracticeGateState.InitialReadyGate && Session.HasCompletedPracticeHistory`.
   - Displays enabled operations only via `ReadyOperationProgress`.
   - Semantic list and list-item structure with complete accessible labels; decorative symbols hidden from assistive technology.
   - Fresh learners receive no overview and zero fabricated progress.
   - Presentation remains non-mutating and timing-safe.
3. **Active Practice HUD**:
   - Intentionally compact visual representation (operation symbol + numeric Stage).
   - Learner-facing group accessibility (`Training_OperationProgressGroupAriaLabel`) replacing internal diagnostic labels.
   - Complete localized Stage description via `aria-label` and `title` per entry.
4. **Responsive Layout Preservation**:
   - Wide layout: 1 operation $\implies$ 1 column, 2 operations $\implies$ 2 columns, 3 operations $\implies$ 3 columns, 4 operations $\implies$ 4 columns.
   - Narrow layout ($\le 480\text{ px}$): 3 and 4 operations wrap into 2 columns.
5. **Strict Non-Changes**:
   - Zero change to `LearnerProgression`, `OperationProgression`, curriculum bands, fact spaces, or band advancement rules.
   - Zero change to `DeterministicOperationScheduler`, 10-slot role cycles, `PracticePosition`, FSRS parameters, `AdaptivePacePolicy`, or `GuidedNumberSpaceGate`.
   - Zero change to SQLite persistence, store contracts, or Schema V6.
   - No introduction of mastery percentages, completion percentages, fluency percentages, finite stage denominators, or FSRS details into learner-facing UI.

---

## 4. MF-UX-007 Review Status & Validation Evidence

- **Verdict**: `REVIEW_APPROVED`
- **Candidate FULL_VALIDATION**: `FULL_VALIDATION_PASS` on candidate `5f489bae56cfd3bb9ea0b895baaf6778382ef867` (candidate tree `6761a9eb217bcea11f0f5bc7e14cc594100efd95` identical to merged `main` tree `6761a9eb217bcea11f0f5bc7e14cc594100efd95`)
- **Merge Commit**: `d37fbe3347679220bf847b06c83f7f9366738d03` (PR #47)
- **Blocker Findings**: 0
- **Major Findings**: 0
- **Minor Findings**: 0
- **Notes**: 3 (Accessible listitem WAI-ARIA announcement pattern, Constrained viewport overflow scrolling in `.training-host`, Evidence boundary to manual physical validation)
- **Core Review Evidence**: 1,605 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - Focused package review: 120 passed, 0 failed, 0 skipped (across `PolicyAndLocalizationTests`, `OnboardingAndProgressFeedbackTests`, and `ResponsiveAndCorrectAnswerFlowTests`)
  - Windows Release build: 0 warnings, 0 errors
  - Android Release build: 0 warnings, 0 errors
  - MathFirst.ReleaseTool Release build: 0 warnings, 0 errors
  - Vulnerable packages: 0
- **Schema**: V6 unchanged (no migration, no table or column additions).

---

## 5. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, and `MF-UX-007` and no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after MF-UX-007 merge will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Authorized Downstream Project Sequence:
1. Execute `FULL_VALIDATION — MF-LEARN-006 complete implementation package` on task branch `feat/mf-learn-006-adaptive-learning-policy` when explicitly dispatched.
2. Subsequent lifecycles upon separate explicit user authorization:
   - `PUSH_ONLY`
   - `PR_ONLY`
   - manual merge authorization
   - `POST_MERGE_SYNC_ONLY`
3. Subsequent release preparation sequence (final V1 gap audit, fresh Tester APK build, manual physical-device tester validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 30–32) remains deferred until explicitly authorized.

> [!IMPORTANT]
> Package `MF-LEARN-006` is implemented locally and has passed complete-package review, but remains unpublished on local task branch `feat/mf-learn-006-adaptive-learning-policy`. Downstream lifecycle steps (`FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`) or release steps must not be autonomously activated without explicit user dispatch.
