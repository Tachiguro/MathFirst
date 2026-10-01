# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: None. No active repository implementation is currently authorized; awaiting explicit dispatch.
- **Current Lifecycle**: None (operational and documentation baseline is synchronized following Pre-Step55 refinement program reconciliation).
- **Status of Active Work**: No feature, implementation, or documentation package is currently in flight.
  - **Completed Testing Steps (Steps 51–54)**:
    - **Step 51 (Final V1 Gap Audit)**: `STEP_51_READY_FOR_STEP_52` (read-only audit of `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, zero Step-52 blockers found).
    - **Step 52 (Tester APK Packaging & Offline Validation)**: `STEP_52_TESTER_APK_PASS` (built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f`, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, APK SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`, development/debug signed, repository remained clean).
    - **Step 53 (Physical Installation on S26 Ultra)**: `STEP_53_INSTALL_PASS` (Samsung SM-S948B / m3q, Android 16 / API 36, package `com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, production package unmodified).
    - **Step 54 (Manual Physical-Device Validation)**: `STEP_54_MANUAL_VALIDATION_PASS` (physical device Samsung Galaxy S26 Ultra; manual validation covered launch, practice, correctness, pause/resume, Cyber Defense combat presentation, multi-question continuity, background/resume, Settings, Privacy, telemetry export/share, localization; zero manual findings reported; source repository unchanged).
  - **Roadmap Step 55 Status**: **NOT EXECUTED / NOT AUTHORIZED** (explicitly declined by user after Step 54; deferred pending pre-Step55 refinement program; no production AAB, no versionCode 4, no release signing, no Google Play upload).
  - **Established Refinement Plan**: Canonical pre-production program documented in [docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md).
- **Next Planned Work Item**: **P0 — Zero-Answer / `0 + 0` Core-Flow Freeze** under `PLAN_ONLY` / systematic debugging mode, to be executed in a separately authorized task upon explicit user dispatch. P0 implementation authorization is **NOT GRANTED** by this documentation task.

### 1.1 Pre-Step55 Refinement Program Sequence

The canonical pre-production program ([docs/V1_PRE_STEP55_REFINEMENT_PLAN.md](V1_PRE_STEP55_REFINEMENT_PLAN.md)) governs downstream work:
1. **P0**: Zero-Answer / `0 + 0` Core-Flow Freeze (`P0 Blocker` — Next planned item under `PLAN_ONLY`)
2. **P1**: Normal Practice Without Deadline Failure (remove timeout failure, preserve response latency & pace modeling)
3. **P1b**: Active Thinking Time / Interruption Safety (pause on interruptions, neutralize contaminated latency)
4. **P2**: Direct-to-Practice Start / Remove Onboarding (direct launch, system defaults, Addition only)
5. **P3**: Cumulative Operation Unlock Progression ($+ \to + - \to + - \times \to + - \times \div$; demonstrated mathematical evidence, 3 simulation personas)
6. **P4**: Settings Simplification (streamlined settings, remove practice time selection, prevent unlock bypass)
7. **P5**: Cyber Defense Visual Consistency (dark technical surfaces, restrained neon/cyber accents)
8. **P6**: Tester Diagnostics / Telemetry Release Boundary (hard compile/profile boundary isolating Tester diagnostic controls from Distributable UI)
9. **P8**: Test-Coverage Audit & Targeted Hardening (factual baseline, critical invariant coverage)
10. **P7**: Later Game-Design & Game-Polish Program (*Deferred / Post-Core*)

### 1.2 Current Verified Quality State

- **Complete-Package Review**: `REVIEW_PASS` (`MF-TELEM-001` completed with zero Blocker, zero Major, zero Minor findings).
- **Merge Commit**: `bbdf62652927efa26475a9f2d83778de6465f5e1` (PR #56).
- **Post-Merge Verification**: `POST_MERGE_SYNC_COMPLETE` on canonical checkout `C:\Dev\MathFirst`; post-merge Core tests: `1,906 passed, 0 failed, 0 skipped`; Android and Windows builds: `0 warnings, 0 errors`.
- **Benchmark & Determinism**: Exact 482 strong-learner benchmark (`ADD-D10 -> ADD-P1-ANCHOR` at global accepted attempt 482; 121 Addition attempts), real-SQLite restart equivalence, and cold-restart next-selection determinism locked.
- **Persistence Contract**: Schema V7 live.

---

## 2. Historical Merged Implementation Packages

- **MF-UX-008 (Static Combat Layout & Boss Presentation)**: Merged via PR #55 (`76116d11b8563b0407188ba53ccefd998eda958d`). Delivered static combat layout positional stability (keypad and arithmetic typography stationary across combat transitions), layout-isolated boss presentation (`clamp(90px, 16vh, 140px)`), visual layering (`z-index: 4` + scrim protection), progressive opponent scaling (0.65 to 1.50), tier-preserving feedback scale composition (`SCALE_PRESERVED_ACROSS_ALL_STATES`), and scoped active-gameplay scroll suppression (`.training-host.active-gameplay`). Post-merge validation: 1,786 Core tests passed.
- **MF-LEARN-006 (Adaptive Learning Policy & 482 Benchmark)**: Merged via PR #54 (`bc7471b098e2f79262ff6e71302820bd281a14d5`). Delivered Option-B Evidence-Adaptive Discovery, universal no-immediate-fact-repetition invariant ($\text{FactId}(t+1) \ne \text{FactId}(t)$), tiered remediation (cooldown 4 / 2; broad weakness threshold 2), Guided Gate G3 soft decoupling at `BandIndex >= 3`, pace calibration readiness at $\ge 24$ positioned Correct attempts, calibrated Cyber Defense Critical Hits, and the exact 482 strong-learner benchmark. Post-merge validation: 1,772 Core tests passed.
- **MF-UX-007 (Progress Presentation Cleanup)**: Merged via PR #47 (`d37fbe3347679220bf847b06c83f7f9366738d03`). Delivered concise learner-facing Stage terminology, returning learner Ready Gate overview, active HUD accessibility (`Training_OperationProgressGroupAriaLabel`), and responsive layout preservation. Post-merge validation: 1,605 Core tests passed.

---

## 3. Downstream Release Context & Planned Sequence

### Release Context:
- **Build 2 Rejection**: Historical. Build 2 was rejected (`REAL_DEVICE_VERIFICATION_FAILED` at Step 31) due to the selector crash/starvation bug on operation reconfiguration and restart, resolved by `MF-STAB-003`.
- **Build 3 Status**: Historical only. Build 3 passed technical smoke (Step 30) and manual physical-device verification (Step 31) on Samsung SM-S948B, Android 16. However, Build 3 source predates `MF-LEARN-004`, `MF-LEARN-005`, `MF-UX-007`, `MF-LEARN-006`, `MF-UX-008`, and `MF-TELEM-001` and no longer represents current repository source.
- **Step 52 Tester Build**: Built from `main@8fb7568cb015101259c22285a4b5a7fdf6c1d63f` (`com.tachiguro.mathfirst.tester`, version `1.0 / versionCode 1`, SHA-256 `40e2b5e3f23e90a2dbe695db4d724e4375c5f2ef51709a801fa81cf6e06f4039`), installed and manually validated on Samsung Galaxy S26 Ultra in Steps 53–54 (`STEP_54_MANUAL_VALIDATION_PASS`).
- **Future Production Candidate**:
  - Any future production candidate packaging after pre-Step55 refinement will have `versionCode >= 4`.
  - Build 4 does **not** exist yet (not packaged, not signed, not tested).
  - Roadmap Step 55 production packaging is **NOT AUTHORIZED**.

### Authorized Downstream Project Sequence:
1. Documentation reconciliation for the Pre-Step55 V1 Refinement Program is completed and recorded in repository documentation.
2. The next planned work item is **P0 — Zero-Answer / `0 + 0` Core-Flow Freeze** (`PLAN_ONLY` mode, requiring explicit future dispatch; implementation not authorized).
3. Downstream P-item sequence ($\text{P0} \to \text{P1} \to \text{P1b} \to \text{P2} \to \text{P3} \to \text{P4} \to \text{P5} \to \text{P6} \to \text{P8}$) must be completed and merged before Step 55 may be proposed.
4. Step 55 production packaging and release operations remain deferred and strictly require separate affirmative user authorization.

> [!IMPORTANT]
> Step 55 is **NOT AUTHORIZED**. The next task is P0 planning under `PLAN_ONLY`. No implementation, build, package, signing, ADB, or release action is authorized without explicit user dispatch.
