# MathFirst Backlog Registry

This document serves as the authoritative registry for **accepted inactive work** on MathFirst.

---

## 1. Backlog Governance Rules

1. **Authoritative Registry**: All accepted deferred packages, features, and non-immediate improvements must be recorded in this registry.
2. **Stable Package Identifiers**: Every backlog item is assigned a permanent, unique identifier in the canonical format `MF-<CATEGORY>-<NUMBER>` (e.g. `MF-GOV-001`, `MF-CALC-001`, `MF-UX-001`, `MF-PERSIST-001`).
   - Category prefixes are short, stable, and domain-meaningful.
   - The numeric portion is zero-padded (e.g. `001`, `002`).
3. **No Renumbering or Reuse**: Once assigned, an identifier is never renumbered, renamed, or reused for another purpose, even if the item is completed, declined, or superseded.
4. **No Speculative Items**: Do not add speculative ideas or placeholder packages without explicit product definition and approval.
5. **Inactive Work Only**: Active work in flight is tracked operationally in [docs/CURRENT_WORK.md](CURRENT_WORK.md), not here.

---

## 2. Item Schema Definition

When items are accepted into the backlog, they are recorded with:
- **ID**: Unique canonical `MF-<CATEGORY>-<NUMBER>` identifier.
- **Title**: Descriptive title.
- **Type**: `Governance`, `Product`, `Architecture`, `Feature`, `Refactoring`, or `Documentation`.
- **Status**: `Proposed`, `Accepted`, `Deferred`, `Superseded`, or `Completed`.
- **Dependencies**: Prerequisites required before implementation.
- **Description**: Brief scope summary and acceptance criteria.

---

## 3. Current Inactive Backlog Registry

### MF-LEARN-002: Adaptive Pace, Fast Acquisition, and Practice Interventions

- **ID**: `MF-LEARN-002`
- **Title**: Adaptive Pace, Fast Acquisition, and Practice Interventions
- **Type**: `Feature`
- **Status**: `Accepted`
- **Dependencies**: `MF-STAB-001` complete (merged through PR #13)
- **Description**:
  Comprehensive adaptive learning model enhancement focusing on dual optimization of arithmetic correctness and retrieval speed:
  1. **Primary Learning Goal**: Improve both correctness and recall speed. Ensure strong learners move rapidly past trivial or already-known facts to reach genuinely challenging material without artificial repetition, while weak or forgotten facts recur more frequently.
  2. **One Adaptive Visible Timer**: A single visible countdown bar serving as the actual answer deadline for each presentation (no dual timers). The deadline dynamically adapts to learner performance: tightening upon consistent fast correctness and loosening upon slow/incorrect/unstable responses, bounded strictly between a sensible minimum (e.g. ~3s candidate) and an absolute maximum of 30 seconds (initial deadline roughly in the 8–10s candidate region).
  3. **Multi-Dimensional Adaptation**: Calibration incorporates learner performance, operation-specific pace, curriculum-band difficulty, exact-fact history, latency, and recent stability so different facts can receive appropriate deadlines.
  4. **FSRS Core Preservation**: Preserves real `FSRS.Core` 1.0.7 integration (0.95 desired retention, Practice Position virtual time, deterministic per-FactId cards, disabled fuzzing) as the spaced-repetition due-distance scheduler (learning queue) without exposing internal mechanics in the UI.
  5. **Adaptive Rating & Fluency**: Replaces fixed latency thresholds (1000/2500ms) with adaptive FSRS ratings (`Again` on timeout/error, `Hard`/`Good`/`Easy` relative to expected adaptive pace) and reconciles adaptive fluency with band advancement gates.
  6. **Fast Acquisition for Dense Bands**: Generalizes fast progression through small dense bands (e.g. `0 + 0 = 0`, factor-0/1 multiplication) upon confident first-pass demonstration without requiring fixed 40-attempt minimums.
  7. **Prevention of Boring Non-Due Repetition**: Refines selector fallback behavior to prevent fully mastered facts from reappearing as repetitive filler when candidate pools are empty. Mastered facts appear only via legitimate FSRS due review or maintenance.
  8. **Repeated-Error Learning Intervention**: Repeated mistakes on the same fact trigger a focused teaching pause / corrective echo (e.g. displaying `7 × 8 = 56` with direct/encouraging copy and requiring deliberate acknowledgement) that is strictly non-scored (does not increment Practice Position, affect session score, or generate false advancement evidence) before returning later via normal scheduling.
  9. **Minimalist Practice UI & Periodic Summaries**: Maintains a distraction-free arithmetic practice surface (operation progress HUD, expression, answer input, timer bar). Permanent transient score visibility vs. periodic check-ins (accuracy summaries, pace recognition, break suggestions) will be evaluated in planning.
  10. **UI Defect Correction**: Reconciles the Pause button rendering defect (reported as appearing blue in live application) with intended danger/red semantics. The existing timer bar visual itself is approved and not the defect.
  11. **Onboarding Communication**: Adds concise, human-oriented onboarding copy explaining the adaptive time and recall dynamics.
  12. **Curriculum Hard Stop & Safety**: Enforces checked arithmetic boundaries preventing `Int32` overflow, ensuring fail-closed stability at the end of curriculum. Endgame achievement ("Math God") is recognized as a deferred gamification concept.
  13. **Open Planning & Calibration Requirement**: All exact numerical parameters, formulas, time windows, scaling steps (5% fixed step was rejected as too slow), and thresholds remain unresolved and must be researched and justified during `PLAN_ONLY`.

> [!NOTE]
> Active work is tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md). High-level development phases and sequencing are outlined in [docs/ROADMAP.md](ROADMAP.md). MF-LEARN-002 is intentionally sequenced before MF-REL-001.
