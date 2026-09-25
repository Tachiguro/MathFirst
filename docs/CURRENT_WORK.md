# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Package**: `MF-DOC-008` — Post-MF-UX-007 Merge State Reconciliation
- **Current Lifecycle**: `DOCUMENT_ONLY`
- **Task Branch**: `docs/mf-doc-008-post-mf-ux-007-merge-reconciliation`
- **Authoritative Baseline (`main` / `origin/main`)**: `d37fbe3347679220bf847b06c83f7f9366738d03`
- **Most Recently Merged Implementation Package on `main`**: `MF-UX-007` — Progress Presentation Cleanup (PR #47 merge commit at `d37fbe3347679220bf847b06c83f7f9366738d03`, validated candidate `5f489bae56cfd3bb9ea0b895baaf6778382ef867`, candidate tree identical to merged main tree `6761a9eb217bcea11f0f5bc7e14cc594100efd95`, `REVIEW_APPROVED`, `FULL_VALIDATION_PASS`, exact-candidate test evidence: 1,605 passed / 0 failed / 0 skipped, Schema V6 preserved without migration)
- **Status of Active Work**: Active documentation reconciliation package in `DOCUMENT_ONLY`. No implementation package is currently active.
- **Next Lifecycle for MF-DOC-008**: `REVIEW_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`.

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
1. Complete MF-DOC-008 lifecycle (`DOCUMENT_ONLY` $\to$ `REVIEW_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`).
2. After completion and merge of MF-DOC-008 and subsequent `POST_MERGE_SYNC_ONLY`, the next user-directed activity is `PLAN_ONLY` for:
   **MathFirst Motivation / Gamification / Progression Architecture**
   covering:
   - Kyu/Dan progression;
   - visible learner progress;
   - daily streaks;
   - hidden achievements;
   - notification strategy;
   - anti-grind principles;
   - separation of internal curriculum state from learner-facing progression;
   - reproducible Progression-Coherence test planning for the observed `+10 / -20 / ×5 / ÷5` state.
   *(Do NOT activate or invent an implementation package for that future work.)*
3. Subsequent release preparation sequence (final V1 gap audit, fresh Tester APK build, manual physical-device tester validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 30–32) remains deferred until explicitly authorized.

> [!IMPORTANT]
> There is currently no active implementation package. Downstream packages or release steps must not be autonomously activated without explicit user dispatch.
