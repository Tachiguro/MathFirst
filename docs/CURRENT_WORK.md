# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-SET-001`
- **Title**: Practice Configuration: Operation Selection and Adjustable Practice Time
- **Active Task Branch**: `feat/mf-set-001-practice-configuration`
- **Base Branch / Commit**: `main` at `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`
- **Final Implementation HEAD**: `f453501412b7c9fce39356a0837a5b862d6221fd`
- **Final Implementation Parent**: `694b2e6b677b0f327f79f58ce5966871cd2b69ce`
- **Final Commit Subject**: `fix(practice): clear answer input for every new fact`
- **Final Checkpoint Trailer**: `MathFirst-Checkpoint: MF-SET-001 new-fact-input-reset`
- **Implementation**: Complete.
- **Review**: `REVIEW_PASS`.
- **Implementation Readiness**: YES.
- **Physical Android Validation**: Completed & confirmed on device by user.
- **Current Lifecycle**: `DOCUMENT_ONLY` (reconciling documentation).
- **Next Lifecycle**: `COMMIT_ONLY`.
- **Remote State**: Feature branch unpushed; no MF-SET-001 Pull Request; not merged.

## 2. Final Reviewed Candidate

- **Operation Configuration**: All four operations (Addition, Subtraction, Multiplication, Division) can be independently enabled. All four are enabled by default. Settings and fresh-install Onboarding permit any valid non-empty one-, two-, three-, or four-operation subset; the final enabled operation cannot be disabled. Corrupt all-false preferences safely normalize to all four. Both surfaces use the shared `IPreferenceStore` / `PracticeOperationPreferencePolicy` mechanism without duplicate models.
- **Selection and Display Filters**: Enabled operations filter both newly generated practice tasks and the operation progress HUD. Disabled operations are not selected and are hidden from the HUD, but retain attempt history, `ItemLearningState`, FSRS state, and `OperationProgression`; re-enabling resumes preserved learning state.
- **Onboarding Operation Choice**: Fresh-install onboarding includes an operation selection step where all four operations are preselected by default. Any non-empty subset is accepted, immediately applying the same preference policy.
- **Returning Learner Progress Overview**: A returning learner with prior practice sees a compact progress presentation before starting practice. Only enabled operations are displayed, showing each operation's current Stage (`BandIndex + 1`). No invented mastery percentages or fake statistics are shown, and practice start remains explicit via Start Practice.
- **Session Check-In Summary**: Every 20 accepted attempts ($20, 40, 60, \dots$), a session check-in modal displays completed attempts (20), correct count, median latency of correct attempts only, and actual Stage changes occurred in that segment. Check-in summary state is process-local and is not persisted to SQLite. Selecting Keep Going begins a new summary segment; selecting Take a Break ends the current segment, transitions to manual pause, and a subsequent Start begins a fresh summary segment.
- **Current-Fact Invariant**: Changing Settings does not replace the question already displayed. On return to Practice, the current question remains active and its in-flight timer resumes; the HUD reflects the updated enabled set, and the next generated question follows the updated configuration.
- **Settings Cleanup**: The developer Statistics / Diagnostics section was removed from Settings. This was presentation cleanup only; learner attempts, progress, FSRS data, item state, and progression calculations remain intact. The learning HUD remains present.
- **Scheduling Authority**: For enabled list $E$ of size $k$ and prospective global Practice Position $P$, `AdaptivePracticeSelector` schedules index $(P - 1) \bmod k$ and per-operation ordinal $\lfloor(P - 1) / k\rfloor + 1$. `SqliteLearnerStore` is schedule-policy agnostic.
- **Selection Evidence**: `PracticeSelectionEvidence` is scoped by `ArithmeticOperation` and `ProspectivePracticePosition`. Cached evidence is only an optimization and never changes the scheduled operation. Required scheduled-operation evidence is loaded on demand; disabled-operation prefetch is best effort and its failure cannot block enabled practice.
- **Persistence and Recovery**: `SqliteLearnerStore` atomically persists validated submission transitions, with monotonicity, fact/result/outcome, item/FSRS state, four-entry progression, mutation-boundary, band, revision, duplicate-position, and idempotency checks. Preferences remain outside learner SQLite and MF-SET-001 requires no schema change. If durable submission succeeds but next-exercise preparation fails, retry prepares the next exercise without duplicating an attempt, Practice Position, item/FSRS mutation, or store revision.
- **Practice Time and Timer**: Standard preserves the adaptive deadline; 30 s, 45 s, and 60 s apply $\max(\text{adaptive deadline}, \text{configured floor})$. Manual Pause, Settings, and same-process background/suspend freeze elapsed and remaining active time. A true cold process restart begins the in-flight timer fresh while retaining preferences and learner progress. Only active answering time contributes to raw response latency and persisted attempt metrics; in-flight elapsed time, remaining deadline, pause timestamp, and active segment timestamp are not persisted.
- **Answer Entry Lifecycle & Input Reset Invariant**:
  - *Same Exercise Instance*: Unsubmitted partial input is preserved across manual Pause, Settings navigation, and same-process background/resume transitions.
  - *New Exercise Instance*: Every new exercise instance always starts with a completely empty answer input field.
  - *Process-Local State*: `TrainingSession.FactInstanceRevision` increments for every new exercise instance (not depending on operand equality) and `TrainingSession.CurrentAnswerInput` manages transient answer input. Neither is persisted to SQLite or affects scoring, FSRS, or progression. `Home.razor` keys the input element via `_inputRenderVersion` to ensure a fresh input element on every new exercise.
  - *Resolved Defect*: Corrects an Android-reproduced defect where completing a SessionCheckIn, taking a break, and later starting practice retained previous answer input on the new exercise. Fixed at commit `f453501412b7c9fce39356a0837a5b862d6221fd`.
- **Multi-Digit Input**: Partial multi-digit input remains editable and Backspace works; complete answers submit correctly, single-digit correct answers retain immediate auto-submit, and incomplete input is never prematurely submitted as incorrect.
- **Reset Semantics**: Reset Learning Progress clears learner progress while preserving enabled-operation, practice-time, and UI preferences. Restore Default Settings enables all four operations and restores Standard Practice Time and Numpad layout while preserving learner progress. Full Local Reset performs both.
- **Localization**: 20 unique Diagnostics localization keys were removed, corresponding to 60 entries across English, German, and Russian. `Diagnostics_Group_Learning` remains because Home uses it as the localized accessible label for the operation progress HUD.

## 3. Final Validation Evidence

- **Core**: 1037 passed, 0 failed, 0 skipped (`MathFirst.Core.Tests`).
- **Windows Release**: 0 warnings, 0 errors.
- **Android Release**: 0 warnings, 0 errors.
- **Android Validation Artifact**: `C:\Dev\MathFirstArtifacts\MF-SET-001\MathFirst-MF-SET-001-f453501-debug.apk`.
- **Artifact SHA-256**: `97f85e448d078e46d813aa1244e49b95933e51e1c9d64732faf3a614ae31db12`.
- **Artifact Provenance**: `f453501412b7c9fce39356a0837a5b862d6221fd`.
- **Android Identity**: Package ID `com.tachiguro.mathfirst`, `versionCode` 1, `versionName` 1.0.
- **Physical Android Validation**: Completed and confirmed by user on a physical Android device with zero remaining defects. Verified scenarios include:
  - New exercise after SessionCheckIn and Take Break begins with empty input.
  - Same exercise through manual Pause and Resume preserves partial input.
  - Settings lifecycle freezes and resumes in-flight timer correctly.
  - Same-process background and resume lifecycle behaves correctly.
  - Onboarding operation selection behaves correctly.
  - Returning learner progress presentation displays accurately.
  - Practice HUD reflects enabled operations.
  - SessionCheckIn 20-attempt summaries calculate correctly.
- **Release Boundary**: This is an installable Debug validation APK, not a production Google Play artifact. No production AAB generation, production signing, upload, testing-track distribution, store listing, deployment, or release is claimed.

## 4. Historical Accuracy and Remaining MINOR Notes

The observed runtime failure came from an obsolete pre-remediation Debug build at `dc402c3967b808df7355ad1d7a6123cb3e036794`. A later candidate did not reproduce it against the copied database, and subsequent work corrected multiple independent enabled-subset persistence, evidence-freshness, deterministic scheduling, and recovery defects. The input retention defect after session break was isolated and resolved at `f453501412b7c9fce39356a0837a5b862d6221fd`.

The final review also recorded non-blocking release-polish notes that require a later authorized source package: Danger Zone copy retains developer-oriented Testing wording; `Settings.razor` retains minor unused using directives and duplicate source comments; and the dynamic three-/four-column HUD inline layout may override the older sub-480 px two-column rule on narrow screens. No device failure is claimed, and these notes do not block MF-SET-001.

## 5. Current Lifecycle Position

- **Last Completed Lifecycle Step**: Manual Android Validation (`MANUAL_ANDROID_VALIDATION`).
- **Current Lifecycle Step**: Documentation reconciliation (`DOCUMENT_ONLY`).
- **Next Lifecycle Step after successful documentation**: `COMMIT_ONLY`.
- **Not Authorized in this lifecycle**: staging, commit creation, push, Pull Request creation, merge, production packaging, installation, deployment, or release.
