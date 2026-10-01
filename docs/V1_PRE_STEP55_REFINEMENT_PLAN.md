# MathFirst V1 Pre-Step55 Refinement Program

This document defines the canonical specification, immutable principles, workstream sequence, and governance rules for the **MathFirst V1 Pre-Step55 Refinement Program**. It establishes durable repository truth for all work preceding any future consideration of Roadmap Step 55 production release packaging.

---

## 1. Purpose & Strategic Context

Following the completion of native testing up to Roadmap Step 54 (physical-device validation of the Tester APK on Samsung Galaxy S26 Ultra), the user and project governance explicitly declined proceeding immediately to Roadmap Step 55 (Production Packaging & Release).

Instead, a comprehensive pre-production refinement program was agreed upon to address physical-device findings, eliminate unnecessary user friction, realign practice timing with core learning goals, institute cumulative mathematical operation unlocks, and elevate visual consistency before any production release is authorized.

This document serves as the single durable specification of that program so that future chats and autonomous agents resume work strictly from repository truth rather than chat history.

> [!IMPORTANT]
> **No Implementation / Release Authorization**:
> - This document defines planned work and architectural intent.
> - No item in this plan (P0 through P8) is implemented by the authoring of this document.
> - Roadmap Step 55 (Production AAB packaging, release signing, and Google Play publication) remains **NOT EXECUTED** and **NOT AUTHORIZED**.

---

## 2. Core Product Invariants

All future planning, design, and implementation across MathFirst must conform to these five non-negotiable product invariants:

### 1. Learning First
Every user, regardless of age, background, or initial skill level, must become measurably and reliably better at mental arithmetic through continued use of MathFirst.
- Cyber Defense and all gamification mechanics exist solely to motivate continued, focused learning.
- Game mechanics, animations, or combat rules must **never** reduce the pedagogical quality, precision, or efficacy of arithmetic learning.

### 2. Gameplay Must Not Control Learning Truth
Game systems and state are strictly downstream presentation consumers of learning events and telemetry. Game state has **zero authority** over:
- Curriculum selection and candidate ranking
- Mathematical progression and stage advancement
- FSRS-6 item ratings and spaced repetition intervals
- Remediation triggers and cooldowns
- Fact presentation scheduling
- Mathematical correctness evaluation
- Durable learner state and attempt history

Game systems consume learning results; they never define, alter, or override learning truth.

### 3. Minimal Friction
Normal use of MathFirst must require as few non-mathematical interactions as reasonably possible:
- Avoid mandatory extra clicks, dismissals, or interstitial screens between arithmetic questions.
- Settings navigation, future shops, dialog overlays, animations, and game screens must not contaminate or inflate measured arithmetic response latency.

### 4. No Loss of Learning Progress from Game Failure
Game outcomes must remain completely decoupled from mathematical achievement:
- Losing shields, failing combat encounters, losing streaks, boss defeats, or running out of game resources must **never** erase, reset, or penalize mathematical learning progress, FSRS intervals, or unlocked arithmetic bands.

### 5. Skill-Based, Not Age-Based
Curriculum access and progression adapt dynamically to demonstrated mathematical competence and evidence:
- MathFirst does not gate learning by chronological age, school grade, or arbitrary mandatory question quotas when authentic learner performance evidence can determine readiness.

---

## 3. Historical Lifecycle Reconciliation (Steps 51–54)

The execution history preceding this refinement program is formally reconciled as follows:

| Roadmap Step | Phase / Objective | Result / Verdict | Key Technical & Validation Evidence |
|---|---|---|---|
| **Step 51** | Final V1 Gap Audit | `STEP_51_READY_FOR_STEP_52` | Read-only audit of `main` at commit `8fb7568cb015101259c22285a4b5a7fdf6c1d63f`. Confirmed zero blocking gaps for Step 52 packaging. |
| **Step 52** | Fresh Tester APK Packaging & Offline Validation | `STEP_52_TESTER_APK_PASS` | Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`. Package: `com.tachiguro.mathfirst.tester`, `versionName: 1.0`, `versionCode: 1`. APK SHA-256: `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`. Development/debug signed (`ReleaseProfile.Tester`). Promoted exact 5-file evidence bundle. Repository remained clean. |
| **Step 53** | Physical-Device Tester Installation | `STEP_53_INSTALL_PASS` | Installed Step-52 Tester APK onto physical Samsung Galaxy S26 Ultra (SM-S948B / m3q, Android 16 / API 36). Package `com.tachiguro.mathfirst.tester` installed side-by-side with production namespace preserved. |
| **Step 54** | Manual Physical-Device Tester Validation | `STEP_54_MANUAL_VALIDATION_PASS` | Manual human validation on Samsung Galaxy S26 Ultra. Validated: cold launch, normal practice, correct answer, incorrect answer, manual pause/resume, Cyber Defense combat presentation, multi-question continuity, background/resume, Settings, Privacy, telemetry export/share, and localization. Zero manual findings reported in that lifecycle. |

> [!NOTE]
> **Evidence Taxonomy Distinction**:
> Repository documentation explicitly distinguishes between repository-verifiable technical evidence (Git commits, reproducible builds, SHA-256 checksums, automated test receipts) and recorded human/manual physical-device validation evidence. Manual validation observations are factual records of user acceptance testing and cannot be reconstructed from Git history alone.

---

## 4. Roadmap Step 55 Status & Boundary

Roadmap Step 55 (Production Packaging & Signing) remains:
- **STATUS: NOT EXECUTED**
- **AUTHORIZATION: NOT AUTHORIZED**

Following Step 54, the user explicitly chose not to proceed to Step 55.

### Explicit Prohibitions:
- Do **NOT** generate a production Android App Bundle (`.aab`).
- Do **NOT** increment production `versionCode` (e.g. `versionCode 4`).
- Do **NOT** invoke production release signing with official keystores.
- Do **NOT** upload or distribute packages to Google Play Console.
- Do **NOT** mark the repository as containing a production release candidate.

Step 55 may only be proposed again after the relevant pre-Step55 refinement program (P0 through P6, and P8) has been fully implemented, reviewed, tested, and merged to `main`, and only after the user explicitly provides affirmative authorization for production release packaging at that future time. Completion of individual P-items does **not** imply authorization for Step 55.

---

## 5. Pre-Step55 Refinement Workstreams (P0–P8)

The refinement program consists of nine dedicated workstreams, executed in strict priority order.

```
[P0: 0+0 Freeze Blocker]
       │
       ▼
[P1: No Deadline Failure] ──► [P1b: Interruption Safety]
       │
       ▼
[P2: Direct-to-Practice (Remove Onboarding)]
       │
       ▼
[P3: Cumulative Operation Unlock Progression]
       │
       ▼
[P4: Settings Simplification]
       │
       ▼
[P5: Visual Consistency]
       │
       ▼
[P6: Diagnostics Release Boundary]
       │
       ▼
[P8: Coverage Audit & Hardening]
       │
  (Deferred / Post-Core)
       ▼
[P7: Later Game-Design & Polish]
```

---

### P0 — Zero-Answer / `0 + 0` Core-Flow Freeze
- **Priority**: P0 / Immediate Release Blocker.
- **Observed Physical Behavior**: In fresh or reset learner state, when the application presents `0 + 0` and the user enters `0`, the current Tester build on physical hardware can freeze or fail to advance to the next fact.
- **Architectural Fact**: `AnswerAutoSubmissionPolicy` is designed to accept parsed integer 0 when the expected answer is 0. The investigation must not assume the input parser is the sole root cause.
- **Investigation Chain**:
  $$\text{Input Entry} \longrightarrow \text{Auto-Submit Trigger} \longrightarrow \text{Submission Evaluation} \longrightarrow \text{Persistence Commit} \longrightarrow \text{Cyber Defense Feedback} \longrightarrow \text{Next-Fact Transition} \longrightarrow \text{UI State Release}$$
- **Required Deliverables**:
  1. Systematic reproduction from fresh/reset state in a dedicated `PLAN_ONLY` / systematic debugging lifecycle.
  2. Narrow root-cause identification before authoring code modifications.
  3. Focused regression tests specifically covering $0 + 0 = 0$ auto-submission and state release.
  4. Integration tests verifying the entire submit $\to$ commit $\to$ next fact pipeline reliably clears `submitting`/`awaiting` UI state.
  5. Physical-device verification on Samsung Galaxy S26 Ultra (when APK packaging/install is explicitly authorized).
- **Gate**: P0 must be fully resolved, reviewed, and merged before P1 implementation begins.

---

### P1 — Normal Practice Without Deadline Failure
- **Priority**: High (Core Learning Contract).
- **Product Decision**: Normal MathFirst practice must **never** terminate a question or record an arithmetic failure solely because wall-clock time elapsed. A learner who takes extended time and computes the correct answer has produced a mathematically **Correct** answer.
- **Preserved Semantics**:
  - Response latency ($\text{ResponseLatencyMs}$) continues to be accurately measured for active interaction.
  - Adaptive pace estimation ($P_{\text{fact}}$, hierarchical shrinkage) is preserved.
  - Adaptive fluency estimation (`IsFluent`) is preserved for differentiating automated recall from conscious calculation.
  - FSRS rating semantics remain intact (slow correct answers rated `Hard`; fast correct rated `Easy`/`Good`).
  - Learning analytics and telemetry export continue recording authentic latencies.
- **Removed Semantics**:
  - Elapsed time $\longrightarrow$ automatic `Timeout` outcome $\longrightarrow$ forced question termination is **completely removed** from normal practice.
  - Configurable normal-practice time limits (Standard deadline, 30s, 45s, 60s) are removed from the product direction.
- **Scope Boundary**: Boss timer mechanics are separate game features and are **not** implemented in P1.

---

### P1b — Active Thinking Time / Interruption Safety
- **Priority**: High (Timing Integrity).
- **Product Decision**: Measured response latency must reflect actual arithmetic cognitive effort, not unrelated wall-clock interruptions.
- **Interruption Exclusions**: The following events must pause active timing and must not inflate arithmetic response latency:
  - Opening Settings or in-app overlays.
  - Future Shop or cosmetic screens.
  - App backgrounding, OS interruptions, or phone calls.
  - Navigation away from active practice.
  - Long combat animations or presentation modals.
- **Tainted Sample Policy**:
  - If a timing sample is contaminated by an interruption, preserve the mathematical correctness outcome (`Correct`/`Incorrect`), but exclude or neutralize the contaminated latency sample from adaptive pace and fluency calculations rather than penalizing the learner with artificial slowness.
  - No naive global heuristic (e.g. "all answers over $N$ seconds are wrong"). Slow learners legitimately require thinking time.

---

### P2 — Remove Onboarding / Direct-to-Practice Start
- **Priority**: Medium-High (Friction Elimination).
- **Product Decision**: Eliminate the 5-step onboarding wizard. Fresh installs and post-reset sessions launch directly into active practice with zero preamble.
- **Default Application Configuration**:
  - **Language**: System / device language.
  - **Appearance**: System theme (Light/Dark auto-detection).
  - **Keypad Layout**: `Numpad` (7-8-9 on top).
  - **Haptic Feedback**: Enabled by default (on supported hardware).
  - **Starting Operation**: `Addition` only (Stage 1).
  - **Practice Timing**: Normal practice without deadline failure.
- **Ergonomics**: All preferences remain configurable in Settings. Contextual first-use hints appear only where a specific interaction genuinely requires explanation.

---

### P3 — Cumulative Operation Unlock Progression
- **Priority**: High (Curriculum Architecture).
- **Product Decision**: Replace the legacy model where all four operations could be manually enabled from the start. MathFirst adopts a disciplined, cumulative arithmetic progression:
  - **Stage 1**: Addition ($+$)
  - **Stage 2**: Addition & Subtraction ($+$, $-$)
  - **Stage 3**: Addition, Subtraction, & Multiplication ($+$, $-$, $\times$)
  - **Stage 4**: Addition, Subtraction, Multiplication, & Division ($+$, $-$, $\times$, $\div$)
- **Progression Principles**:
  - Previously unlocked operations remain active in the learning mix.
  - Operation unlocking is governed by **demonstrated mathematical evidence**, not age, school grade, arbitrary question counts, or manual onboarding selection.
  - Learners struggling with Addition/Subtraction remain focused there; strong learners unlock Multiplication and Division rapidly.
  - Single difficult facts must not permanently deadlock stage progression.
- **Prerequisite**: Requires a dedicated `PLAN_ONLY` lifecycle to reconcile existing systems:
  - Independent per-operation progression ([ADR-0003](decisions/ADR-0003-independent-operation-progression-and-open-ended-fact-space.md))
  - Guided Four-Operation Number-Space Gate ([ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md))
  - Bounded operation scheduling ([ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md))
  - Tiered remediation and evidence-adaptive discovery ([ADR-0010](decisions/ADR-0010-evidence-adaptive-discovery-operation-specific-guided-decoupling-and-pace-calibration.md))
- **Mandatory Simulation Validation**: Acceptance planning must validate three deterministic simulated learner personas:
  - **Persona A (Beginner / Young Child)**: Operates in small number spaces, slower latencies, frequent errors; experiences encouragement, success, and steady game progression without premature advanced operations.
  - **Persona B (Mixed / Specific Weakness)**: Generally capable but has distinct weak facts; remediation resolves weaknesses without permanent stagnation or infinite repetition of mastered facts.
  - **Persona C (Strong / Fluent Learner)**: High speed and accuracy; rapidly unlocks subsequent operations without artificial retention in trivial addition.

---

### P4 — Settings Simplification
- **Priority**: Medium.
- **Product Decision**: Streamline Settings following P1, P2, and P3 implementations:
  - **Retained Controls**: Language selection, Theme (System/Light/Dark), Keypad layout (Numpad/Phone), Haptic feedback toggle, Privacy Policy view, Full Reset, and Tester Diagnostics (in Tester builds only).
  - **Removed Controls**: Normal-practice time limit selection (Standard, 30s, 45s, 60s).
  - **Operation Controls Guardrail**: Settings must **not** allow bypassing mathematical unlock progression. Operation toggles may, at most, filter among *already-unlocked* operations.

---

### P5 — Cyber Defense Visual Consistency
- **Priority**: Medium.
- **Product Decision**: Bring all secondary UI surfaces (specifically Settings and dialogs after onboarding removal) into full visual alignment with the established Cyber Defense aesthetic:
  - Dark technical surfaces, restrained neon/cyber accents, high-contrast typography, and clear visual hierarchy.
  - Active training screen remains the primary visual reference.
  - Usability, legibility, and accessibility strictly outrank visual novelty.

---

### P6 — Tester Diagnostics / Telemetry Release Boundary
- **Priority**: Medium-High (Release Hygiene).
- **Product Decision**: Enforce a rigid compile/profile boundary between Tester and Production builds:
  - Tester-specific actions (e.g. "Copy diagnostic info", telemetry JSON export/share, troubleshooting resets) must be strictly isolated to `Tester` profile builds.
  - `Distributable` production builds must not compile or expose Tester diagnostic UI controls.
  - Underlying telemetry infrastructure (`attempt_history` presentation context, persistence contracts) remains intact for learner history.

---

### P7 — Later Game-Design & Polish Program (Deferred)
- **Priority**: Deferred / Future (Post-Core).
- **Status**: Non-blocking for V1 core release. Must not interrupt the P0–P6/P8 sequence.
- **Candidate Topics**:
  - Attack animation pacing, recoil readability, and visual hit feedback.
  - Handcrafted boss encounters and adaptive boss pressure mechanics.
  - Achievements, streak visual milestones, currency, and cosmetic shop.
  - Optional revive mechanics and broader meta-progression.
- **Boss Timer Principle**:
  - Normal practice timeout failure must **never** be reintroduced via boss encounters.
  - A boss timer creates *game pressure* (e.g. boss attacks, shield reduction, combo reset), but mathematical truth remains inviolate: a correct arithmetic answer submitted after a boss timer expires is still recorded as mathematically Correct in learning telemetry.
- **Procedural Enemy System (Future Architecture)**:
  - Deterministic seeded procedural generator creating combinatorial opponent varieties (archetype, appendages, sensors, palettes, name fragments).
  - Fully offline, deterministic, reproducible for testing, zero runtime generative-AI dependencies, and strictly presentation-only.
  - Bosses use recognizable handcrafted archetypes with procedural variants (e.g. *Nexus Overlord Alpha*, *Nexus Overlord Corrupted*).

---

### P8 — Test-Coverage Audit & Targeted Hardening
- **Priority**: Medium-High (Quality Assurance).
- **Product Decision**: Execute a factual, measurement-first test coverage audit before production release:
  - Measure line coverage, branch coverage, domain coverage, persistence integration, and ReleaseTool verification.
  - Focus highest testing rigor on critical invariants: learning engine, arithmetic correctness, FSRS scheduling, cumulative unlocks, pace/fluency estimation, remediation, and security/privacy boundaries.
  - Reject superficial percentage inflation on trivial UI markup in favor of robust behavioral invariants and mutation resilience.

---

## 6. Execution Order & Workflow

### Standard Sequence:
$$\text{P0} \longrightarrow \text{P1} \longrightarrow \text{P1b} \longrightarrow \text{P2} \longrightarrow \text{P3} \longrightarrow \text{P4} \longrightarrow \text{P5} \longrightarrow \text{P6} \longrightarrow \text{P8} \longrightarrow [\text{Step 55 Proposed}]$$

*(P7 remains deferred and is scheduled independently).*

### Chat / Handoff Protocol:
1. Each P-item is executed as a standalone, isolated development package. Multiple P-items are never batched into a single implementation pull request.
2. Every P-item follows the strict repository lifecycle:
   $$\text{PLAN\_ONLY} \longrightarrow \text{IMPLEMENT} \longrightarrow \text{REVIEW\_ONLY} \longrightarrow \text{COMMIT\_ONLY} \longrightarrow \text{PUSH\_ONLY} \longrightarrow \text{PR\_ONLY} \longrightarrow \text{MERGE} \longrightarrow \text{POST\_MERGE\_SYNC\_ONLY}$$
3. Upon completion and post-merge synchronization of a P-item:
   - The agent explicitly reports package completion to the user.
   - The agent recommends opening a **NEW CHAT** for the subsequent P-item.
   - The user provides the standard repository Blueprint.
   - The new chat discovers its next task exclusively from live repository truth and documentation.
   - Agents must never request a chat switch in the middle of an incomplete P-item.

---

## 7. Current Work State After Plan Reconciliation

- **Active Implementation**: NONE.
- **Next Planned Work Item**: `P0 — Zero-Answer / 0 + 0 Core-Flow Freeze`.
- **P0 Implementation Authorization**: NOT GRANTED (Requires dedicated `PLAN_ONLY` task dispatch).
- **Roadmap Step 55 Status**: NOT AUTHORIZED.
- **Production AAB Packaging**: NOT AUTHORIZED.
- **Google Play Release**: NOT AUTHORIZED.
