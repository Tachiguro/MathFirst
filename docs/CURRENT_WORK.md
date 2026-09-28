# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: None. Package `MF-UX-008` (Static Combat Layout, Layout-Isolated Boss Presentation, Combat Visual Layering, Progressive Opponent Sizing, and Feedback Scale Composition) is complete and merged into `main` via PR #55 at merge commit `76116d11b8563b0407188ba53ccefd998eda958d`. There is currently no active implementation package in flight. Next product or design lifecycle requires explicit user dispatch.
- **Current Lifecycle**: `POST_MERGE_RECONCILIATION_COMPLETE`
- **Status of Active Work**: No implementation package in flight. `MF-UX-008` delivered static combat layout positional stability (keypad and arithmetic display remain static across enemy changes, boss appearances, hits, crits, blocked attacks, and text updates), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), explicit visual layering (`z-index: 4` on `.scene-intel` with localized gradient scrim over background artwork `z-index: 1`), enhanced title and description legibility, progressive opponent visual scaling via `CyberDefenseOpponentScalePolicy` (Small 0.65, MediumSmall 0.78, Medium 0.90, Large 1.05, Boss 1.42, SectorBoss 1.50) without structural layout shift, tier-preserving feedback animation scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES` across idle, hover, hit, crit, blocked, and reduced-motion states), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`) with `overscroll-behavior: none;`.
  - **Technical Review**: Concluded with `REVIEW_PASS` (0 Blocker, 0 Major, 0 Minor findings).
  - **Physical Device User Acceptance**: Concluded with `USER_PHYSICAL_DEVICE_ACCEPTANCE_PASS`. The user physically inspected the current Android behavior on a physical device and confirmed the result looks very good, is acceptable for current MF-UX-008 scope, and is no longer blocked by previous layout issues.
  - **Pull Request & Merge**: PR #55 merged into `main` at merge commit `76116d11b8563b0407188ba53ccefd998eda958d` (feature HEAD `43949fdc7513714d9e4cbb755d0c5c8da5ba4a8b`).
  - **Post-Merge Verification**: Synchronized on canonical checkout `C:\Dev\MathFirst` (`POST_MERGE_SYNC_COMPLETE`); post-merge validation: 39 focused Cyber Defense tests passed, Debug build 0 warnings / 0 errors, 1,786 full Debug tests passed (0 failed, 0 skipped).
  - **Nonblocking Visual Fine-Tuning**: Remaining exact opponent size and placement tuning is explicitly deferred as nonblocking future visual polish without allocating a new canonical `MF-*` package identifier.
  - **Preserved Unallocated Topics**: Adaptive Timing and Early Calibration Redesign (`DESIGN_REQUIRED`), Tester Telemetry Export and Share (`DESIGN_REQUIRED`), and Light-Theme Cyber Defense Visual Reconciliation (`DEFERRED`).
- **Next Lifecycle Step**: Next work requires explicit user instruction and prompt dispatch; no package is autonomously selected.

### 1.1 MF-UX-008 Merged Package Summary

The completed package merged on `main` via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`) delivers:
1. **Positional Stability Invariant**: Keypad, arithmetic problem typography, and answer input area remain strictly stationary across enemy changes, boss spawns, HP/shield mutations, and combat floating feedback;
2. **Layout-Isolated Boss Presentation**: Boss visual scaling uses compositor-driven CSS transforms (`scale(1.42)` / `scale(1.50)`) within the identical structural artwork slot (`clamp(90px, 16vh, 140px)`), preventing container expansion or layout shifts (`visual scale != layout scale`);
3. **Combat Visual Layering**: Elevates `.scene-intel` with higher `z-index: 4` and localized gradient scrim protection over background artwork (`z-index: 1`), with enhanced title/description contrast and font weight across themes;
4. **Progressive Opponent Scaling**: Pure domain `CyberDefenseOpponentScalePolicy` scaling early regular enemies smaller (`0.65`), growing progressively (`0.78` -> `0.90` -> `1.05`) to bosses (`1.42`) and sector bosses (`1.50`) without structural layout shifts;
5. **Tier-Preserving Feedback Scale Composition**: Combat feedback animations (hit recoil, critical recoil, blocked deflect) preserve exact base scale tier across all opponent types (`SCALE_PRESERVED_ACROSS_ALL_STATES`), with explicit tier preservation under `prefers-reduced-motion`;
6. **Scoped Active-Gameplay Scroll Suppression**: Scroll suppression and overscroll containment (`overflow: hidden; overscroll-behavior: none;`) scoped strictly to `.training-host.active-gameplay`, leaving Settings and Onboarding naturally scrollable (`overflow-y: auto;`);
7. **Zero Learning Mutation**: Combat UI remains strictly a presentation consumer; zero mutation of FSRS, item states, progression, or learning telemetry; Schema V6 preserved intact.

### 1.2 Current Verified Quality State

- **Complete-Package Review**: `REVIEW_PASS` (`MF-UX-008` concluded with zero Blocker, zero Major, zero Minor findings).
- **Physical Acceptance**: `USER_PHYSICAL_DEVICE_ACCEPTANCE_PASS` on physical Android device.
- **Merge Commit**: `76116d11b8563b0407188ba53ccefd998eda958d` (PR #55).
- **Post-Merge Verification**: `POST_MERGE_SYNC_COMPLETE` on canonical checkout `C:\Dev\MathFirst`; post-merge Debug tests: `1,786 passed, 0 failed, 0 skipped`; Debug build: `0 warnings, 0 errors`.
- **Benchmark & Determinism**: Exact 482 strong-learner benchmark (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt 482; 121 Addition attempts), real-SQLite restart equivalence, and cold-restart next-selection determinism locked.
- **Persistence Contract**: Schema V6 preserved intact without migration, table, or column additions.

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
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, and `MF-LEARN-006` and no longer represents current repository source.
- **Future Production Candidate**:
  - Any future production candidate packaging after package merges will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).

### Authorized Downstream Project Sequence:
1. Packages `MF-LEARN-006` and `MF-UX-008` are complete, fully validated, merged to `main` via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`) and PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`), and synchronized (`POST_MERGE_SYNC_COMPLETE`).
2. Subsequent release preparation sequence (final V1 gap audit, fresh Tester APK build, manual physical-device tester validation on Samsung Galaxy S26 Ultra, production packaging `versionCode >= 4`, Steps 30–32) remains deferred until explicitly authorized.

> [!IMPORTANT]
> Package `MF-UX-008` is complete and merged into `main`. Downstream release preparation or next feature/design package must not be autonomously activated without explicit user dispatch.
