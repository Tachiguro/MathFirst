# MathFirst Current Operational Work

This document provides operational context for current repository work.

> [!IMPORTANT]
> This document provides operational context only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Operational State

- **Active Task**: Task 9 — Documentation Reconciliation & ADR-0007 Authoring ([Forensic Remediation Plan](superpowers/plans/2026-09-15-native-v1-release-blocker-fact-eligibility-and-startup-recovery.md)).
- **Current Operation Mode**: `IMPLEMENT_SLICE`.
- **Active Task Branch**: `handoff/task3-partial-laptop-20260916`.
- **Remediation Context**: Native V1 release candidate `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` and signed Android AAB SHA-256 `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe` were rejected during physical-device verification (`REAL_DEVICE_VERIFICATION_FAILED`, `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`) due to curriculum fact-eligibility and startup recovery defects.
- **Remediation Progress**:
  - Tasks 1–8 (domain eligibility resolver, snapshot parity, SQLite streaming candidate filter, selector pure defense, persistence gate validation, migration dormancy, startup recovery, and long-run 500-step simulation) are implemented on the task branch.
  - Task 9 (Documentation Reconciliation & ADR-0007 Authoring) is active in this slice.
- **Branch and Merge State**: The remediation branch is 9 commits ahead of `main` (`4e997f35b4a7884b4b0592beab682a36319ea358`) and is not yet merged.
- **Next Technical Lifecycle**: `REVIEW_ONLY` — Comprehensive Task 9 documentation and remediation-state review.
- **Authority Invariant**: Live Git and GitHub state always takes precedence over documentation.

---

## 2. Completed Implementation Foundation (Tasks 1–8)

1. **Task 1 (Domain Eligibility)**: Pure domain contract `AcquisitionOwnershipResolver.IsEligible` with fail-closed edge cases.
2. **Task 2 (Snapshot Fallback Parity)**: `PracticeSelectionEvidence.FromSnapshot` filters item states with `CurrentBandIndex`.
3. **Task 3 (SQLite Streaming & Anti-Poisoning)**: `SqliteLearnerStore.ReadCandidatesAsync` streams rows and applies `IsEligible` before window truncation.
4. **Task 4 (Selector Pure Defense)**: `AdaptivePracticeSelector.SelectTargetFact` defensively filters all review pools.
5. **Task 5 (Persistence Gate)**: `SqliteLearnerStore.ValidateNewAcceptedSubmission` rolls back attempts on future locked facts.
6. **Task 6 (Migration Dormancy)**: Schema migrations preserve historical rows losslessly while keeping future facts dormant.
7. **Task 7 (Startup Recovery)**: Fail-closed initialization and dedicated `Home.razor` startup error boundary without `CurrentFact` evaluation.
8. **Task 8 (Simulation & Regression)**: 500-step deterministic continuous simulation proving zero ineligible runtime presentations (1169 Core tests passing).

---

## 3. Explicit Downstream Roadmap Boundaries

The following stages are strictly sequential and require separate authorization:

1. **Comprehensive Review (`REVIEW_ONLY`)**: Package-wide review of forensic plan, code, and documentation.
2. **Full Validation (`FULL_VALIDATION`)**: Execution of automated regression and verification suites.
3. **Pull Request & Explicit Merge Authorization**: Dedicated PR to `main` with affirmative user approval.
4. **Post-Merge Synchronization & Exact Candidate Establishment**: Establishing the new exact candidate SHA on synchronized `main`.
5. **Production Packaging & Signing**: Creation of a production `Distributable` AAB package signed with release credentials.
6. **Physical Android Technical Smoke Verification**: Verification on clean physical hardware.
7. **Manual Physical-Device Functional Verification**: Verifying curriculum boundaries in manual practice.
8. **Google Play Publication Gate**: Separate decision on store upload.

---

## 4. Current Operational Boundary

- Remediation is implemented on `handoff/task3-partial-laptop-20260916` and not yet merged to `main`.
- No new release candidate is currently approved.
- Post-remediation full validation has not yet run.
- No production `Distributable` AAB has been created.
- No production signing has occurred.
- No post-remediation physical-device verification has been performed.
- No Google Play upload has been performed or authorized.
