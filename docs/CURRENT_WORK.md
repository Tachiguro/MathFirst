# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Package**: `MF-UX-007` — Progress Presentation Cleanup
- **Current Lifecycle**: `DOCUMENT_ONLY`
- **Task Branch**: `codex/mf-ux-007-progress-presentation-cleanup`
- **Implementation Candidate HEAD Before Documentation**: `37efd21b788e27f5683cc5382fe7c38dc1b7a05f`
- **Authoritative Baseline (`main` / `origin/main`)**: `c7fea74554abe01181b7a0e3d3c4e554c48f7d1a`
- **Most Recently Merged Implementation Package on `main`**: `MF-LEARN-005` — Adaptive Practice Balance and Foundational Coverage (PR #46 merge commit at `c7fea74554abe01181b7a0e3d3c4e554c48f7d1a`, validated candidate `dd4b1b48f67cbb333f913eb2ec6aa37e895a8ebf`, merge tree identical to validated candidate tree `385ae4edcc6acb1a6817294b72e068c51b5a36de`, `REVIEW_APPROVED`, `FULL_VALIDATION_PASS`, Schema V6 preserved)
- **Status of MF-UX-007**: Implemented across two slices, review-approved (`REVIEW_APPROVED`), documentation reconciliation in progress; **NOT MERGED YET** (not yet `FULL_VALIDATED`, not pushed, no open PR, not merged)
- **Next Lifecycle for MF-UX-007**: `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`

---

## 2. MF-UX-007 Implementation Checkpoints

The package implementation was completed across two structured checkpoint commits on task branch `codex/mf-ux-007-progress-presentation-cleanup`:

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

## 4. MF-UX-007 Review Status & Findings

- **Verdict**: `REVIEW_APPROVED`
- **Implementation Candidate HEAD Before Documentation**: `37efd21b788e27f5683cc5382fe7c38dc1b7a05f`
- **Package Base**: `c7fea74554abe01181b7a0e3d3c4e554c48f7d1a`
- **Blocker Findings**: 0
- **Major Findings**: 0
- **Minor Findings**: 0
- **Notes**: 3 (Accessible listitem WAI-ARIA announcement pattern, Constrained viewport overflow scrolling in `.training-host`, Evidence boundary to manual physical validation)
- **Core Review Evidence**: 1,605 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
  - Focused package review: 120 passed, 0 failed, 0 skipped (across `PolicyAndLocalizationTests`, `OnboardingAndProgressFeedbackTests`, and `ResponsiveAndCorrectAnswerFlowTests`)
  - Windows Release build: 0 warnings, 0 errors
- **Schema**: V6 unchanged (no migration, no table or column additions).
- **Merge Status**: **NOT MERGED YET**. MF-UX-007 is implemented and review-approved, but has not yet undergone formal candidate `FULL_VALIDATION`, is not pushed, has no open PR, and is not merged.

---

## 5. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, and `MF-UX-007` and no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after MF-UX-007 merge will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Authorized Downstream Project Sequence:
1. Complete MF-UX-007 lifecycle (`DOCUMENT_ONLY` $\to$ `COMMIT_ONLY` $\to$ `FULL_VALIDATION` $\to$ `PUSH_ONLY` $\to$ `PR_ONLY` $\to$ Manual User Merge $\to$ `POST_MERGE_SYNC_ONLY`).
2. Final V1 gap audit (no new package identifier may be invented here).
3. Build a fresh Tester APK from the then-current synchronized `main`.
4. Install the Tester APK on the user's current physical test device: Samsung Galaxy S26 Ultra.
5. Manual physical-device tester validation.
6. Only after successful tester validation: create the next production candidate with `versionCode >= 4`.
7. Step 30 technical smoke verification.
8. Step 31 production physical-device verification.
9. Google Play Step 32 publication gate (user responsibility in Google Play Console).

> [!IMPORTANT]
> MF-UX-007 is active candidate work in `DOCUMENT_ONLY`. Downstream packages or release steps must not be autonomously activated without explicit user dispatch.
