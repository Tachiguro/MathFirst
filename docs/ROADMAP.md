# MathFirst Strategic Development Roadmap

This document defines the high-level development sequence and milestone phases for MathFirst.

---

## 1. Roadmap Principles

1. **Phased Progression**: Each phase establishes the prerequisites for subsequent phases.
2. **Product-Driven Architecture**: Technical architecture is decided only after product requirements, target platforms, and user workflows are explicitly defined.
3. **Multi-Platform Design Objective**: MathFirst should maximize shared domain and application logic across all supported platforms (Android, Web, Windows) where technically sensible, while allowing platform-specific UI or integration code where justified.
4. **Thin Vertical Slices**: Implementation focuses on delivering end-to-end user workflows before expanding into broad infrastructure foundations.
5. **No Speculative Package Allocation**: Specific canonical `MF-*` package identifiers are created only when work packages are formally defined.

---

## 2. Current Platform Priority

- Windows and Android are the active native product priorities.
- Core product and learning-engine work is completed for the native applications first.
- Windows remains a primary target.
- Android is increasingly prioritized toward eventual Google Play publication; packaging, signing, and publication still require separately authorized lifecycle work.
- Web remains part of the supported architecture established by [ADR-0001](decisions/ADR-0001-cross-platform-application-topology-and-stack-baseline.md), but runtime implementation is explicitly deferred until native Windows/Android V1 work and release readiness are complete.
- iOS and Mac Catalyst are not active targets.

MF-LEARN-001 is complete and integrated into `main` through Pull Request #9. It does not begin Web implementation or release activity. Windows and Android remain the native priorities while Native V1 release readiness is completed through separately governed packages and release lifecycle steps.

---

## 3. Sequence of Development Phases

### Phase 1: Governance & Repository Foundation
- Establish repository governance, invariant rules, safety protocols, and multi-mode agent lifecycle (`MF-GOV-001`).

### Phase 2: Product Definition
- Define the core purpose of MathFirst.
- Identify target users and primary mathematical/educational problems solved.
- Define the core user workflow and Minimum Viable Product (MVP) scope.
- Establish platform-specific UX and operational requirements for the confirmed target platforms:
  - **Android**
  - **Web**
  - **Windows**
- Establish offline/online operational constraints, storage needs, and data privacy requirements.
- Evaluate monetization only if relevant to the product model.

### Phase 3: Architecture Decisions
- Formulate and approve Architecture Decision Records (ADRs) required to support the approved product definition.
- Evaluate technology stack options (languages, runtimes, UI frameworks, storage, build tools) against the mandatory constraint to support **Android**, **Web**, and **Windows**.
- Select the architecture baseline that best satisfies cross-platform domain sharing and platform-tailored user experience.

### Phase 4: Thin Vertical Slice
- Implement one complete, minimal end-to-end user workflow from UI/input through mathematical evaluation to result presentation.
- Validate end-to-end developer experience, testing harnesses, and user experience across target platforms before scaling.

### Phase 5: Iterative Packages
- Deliver modular features and capabilities through bounded, TDD-driven `MF-*` packages under the governed lifecycle.
- Complete accepted shared learning architecture in the native Windows and Android applications before beginning the deferred Web runtime.

### MF-LEARN-001 Delivery State
- **Implementation**: Complete - independent operation progression, deterministic bounded selection, Schema V5 persistence, and open-ended curriculum runtime are implemented.
- **Review and validation**: `REVIEW_APPROVED`; full validation passed.
- **Delivery status**: Merged into `main` through Pull Request #9. Web implementation remains deferred.

### Phase 6: Native V1 Release Readiness & Adaptive Learning

1. Reconcile post-merge current-state documentation (`MF-DOC-001`) - complete and merged through Pull Request #10.
2. Deliver deterministic practice personality and contextual copy (`MF-UX-002`) - complete and merged through Pull Request #11.
3. Reconcile native identity, version, and visual treatment (`MF-UX-003`) - complete and merged through Pull Request #12.
4. Stabilize practice progression and the compact HUD (`MF-STAB-001`) - complete and merged through Pull Request #13.
5. Capture adaptive learning product direction and repository handoff (`MF-DOC-002`) - complete and merged through Pull Request #14.
6. Deliver adaptive pace, fast acquisition, and practice interventions (`MF-LEARN-002`) - complete and merged through Pull Request #15.
7. Deliver acclimation timing, rapid dense expansion, and keypad defaults (`MF-LEARN-003`) - complete and merged through Pull Request #16 at `9a9e5c43d1f1685cf21e766e0890b790ab09040c`.
8. Establish Android internal AAB packaging and release automation (`MF-REL-001`) - complete and merged through Pull Request #17 at `e1a0ae5a4a557f434f29ae04b7f8bcd32f3d2d88`.
9. Practice Configuration: Operation Selection and Adjustable Practice Time (`MF-SET-001`) - complete and merged through Pull Request #18 at `a051518420db3b45f8ca1074bac27e9b4d1b799d`. Implementation completed across 10 commits with 1037 Core tests passing, clean Windows and Android Release builds (0 warnings / 0 errors), and confirmed manual validation on a physical Android device. Future child accounts, multi-user profiles, and parental controls remain separate scope outside MF-SET-001.
10. Reconcile post-merge project state and V1 baseline documentation (`MF-DOC-003`) - complete and merged through Pull Request #19 at `6e137471a16ffced5f5a62daa1a3f5143b5bbb7a`.
11. Standalone MathFirst Privacy Policy (`PRIVACY.md`) - complete and merged through Pull Request #20 at `c4ae75a99f7971033ca9887bb2277b217979be03`.
12. Stabilize deterministic practice diversity, bounded operation scheduling, and review balance (`MF-STAB-002`) - complete and merged across three slices: Slice 1 (PR #21 at `7cc6caebec1798d5cfb3c48172b6f78360fb2442`), Slice 2 (PR #22 at `33d745d270745c2de65e47519cb98959278bbcbd`), and Slice 3 (PR #23 at `2d59464fd3de8cefa5b0f404c00e9bcc74c98dc9`).
13. Reconcile post-stabilization project state documentation (`MF-DOC-004`) - complete and merged through Pull Request #24 at `3e471e20e2d452ee1e383579ed3d0831e1825d3e`.
14. Deliver keypad press feedback and responsive validation (`MF-UX-004`) - complete and merged through Pull Request #25 at `46a7158d3c7fbdf6bc43fe120c35863ac55bb78b`.
15. Deliver tester distribution and release hardening (`MF-REL-002`) - complete and merged through Pull Request #26 at `79e0d48c058747e8112388d31d721a049ec2857a`.
16. Physical-device verification of native V1 candidate `bf1d1cb5c7ceab8b4c18dd1bc9204ec0444b1f10` and signed Android AAB SHA-256 `0d188406aa32001a354c140587a7c4355b44bccee879d73e7975d5737cf53cfe` resulted in rejection (`REAL_DEVICE_VERIFICATION_FAILED`, `RELEASE_CANDIDATE_REJECTED_PENDING_REMEDIATION`) due to future-fact review eligibility and in-place startup recovery defects.
17. Author forensic remediation plan (`docs/native-v1-release-blocker-forensic-plan`) - complete and merged through Pull Request #28 at `4e997f35b4a7884b4b0592beab682a36319ea358`.
18. Deliver forensic remediation implementation for curriculum fact eligibility and startup resilience ([ADR-0007](decisions/ADR-0007-curriculum-fact-eligibility-invariant-and-startup-resilience.md)) - complete and merged through Pull Request #29 at `156d5afd32299ba19d8ca2a8f2f56a7babfe31d8`.
19. Deliver Native UX, Responsiveness, and Interaction Polish Slices 1–6 (`MF-UX-005`) - complete and merged through Pull Request #30 at `bde91a7578753f5c468d0534282edd9fd13f32a0`.
20. Remove cold-launch startup white flash (`MF-UX-005 Slice 7`) - complete and merged through Pull Request #31 at `15d39ac73ecdc276db2d3f1a9b6ea3ac0f8849b6`.
21. Remediate Android Release startup resource initialization order (`MF-UX-005 Blocker Remediation`) - complete and merged through Pull Request #32 at `cf1d2a3f4779c16f1c6104fe8736f405eb14d94e`.
22. Deliver runtime build identity metadata (`MF-UX-005 Tester Ergonomics Slice A`) - complete and merged through Pull Request #33 at `83b767c2265c1baba560abdeaa9fedad365d70da`.
23. Deliver tester diagnostics formatter and Settings copy action (`MF-UX-005 Tester Ergonomics Slice B`) - complete and merged through Pull Request #34 at `82c117912f72d6efe16055f8be754c9c577ae15a`.
24. Reconcile post-PR #34 documentation baseline (`MF-UX-005 Post-PR #34 Documentation Reconciliation`) - complete and merged through Pull Request #35 at `336322b5386a872ebb726c7bdcf34bb207592650`.
25. Deliver repeatable Tester APK workflow (`MF-UX-005`) - **COMPLETED** through Pull Request #36 at `e908bf2fba820f4f6adf03b278861b956e5dbbd5` (historical evidence: 1462 passing Core tests).
26. Complete remaining MF-UX-005 areas — **COMPLETED** through Installed Size / App Data / RAM investigation, PR #37 Release source-map exclusion, Timer-specific physical UX verification, PR #38 Timer visual remediation (`d1705bbdc0013372e44eadf7310ff2f313ecdcef`), and PR #39 final documentation reconciliation (`60dba236aa38bca138ab583f610c9ff876994b04`).
27. Deliver V1 Privacy, Copy, and Localization Hardening (`MF-UX-006`) — **COMPLETED** through Pull Request #40 at `ae69f4ae27397fc6edf36a23bb671b0410680be1` (validated candidate `606158a233d7cface85fa0ef7bd03c2f9ef4f4cb`, 1476 passing Core tests, 0 warnings/0 errors on Windows and Android Release builds, merged tree identical to candidate tree).
28. Execute full exact-candidate automated validation (`FULL_VALIDATION`) for MF-UX-006 candidate `606158a233d7cface85fa0ef7bd03c2f9ef4f4cb` — **COMPLETED** (`FULL_VALIDATION_PASS`).
29. Reconcile post-MF-UX-006 documentation baseline (`MF-DOC-005`) — **COMPLETED** through Pull Request #41 at `284d7cf2c50be6e2d4f219c00aa20d92387338f9`.
30. Deliver Enabled-Subset Scheduling and Current-Fact Reconciliation (`MF-STAB-003`) — **COMPLETED** and merged to `main` through Pull Request #42 at `caffe0e883f83249bee2c9a1f2122543e88c9ab0` (validated candidate `766d8ea7692d139425e2301121f93af7901cf238`, merge tree identical to candidate tree `fcab56b3a886ed0c5018d4f8a16304ee83378b26`, `REVIEW_APPROVED`, 1,516 Core tests passed, Schema V6 preserved, [ADR-0008](decisions/ADR-0008-independent-per-operation-role-ordinals-and-practice-configuration-reconciliation.md)).
31. Production Candidate Packaging & Physical Device Verification (`Build 2 / versionCode 2`): Candidate packaged and Step 30 technical smoke passed (`TECHNICAL_SMOKE_PASS`) for historical Build 2 only; Step 31 manual physical-device verification failed (`REAL_DEVICE_VERIFICATION_FAILED`) due to selector crash/starvation when disabling operations after practice and restarting. Build 2 is **REJECTED**. Root cause resolved in merged code on `main` by `MF-STAB-003`.
32. Execute full exact-candidate automated validation (`FULL_VALIDATION`) for MF-STAB-003 candidate `766d8ea7692d139425e2301121f93af7901cf238` — **COMPLETED** (`FULL_VALIDATION_PASS`).
33. Reconcile post-MF-STAB-003 documentation baseline (`MF-DOC-006`) — **COMPLETED** and merged to `main` through Pull Request #43 at `a9d232f78e2f9ebcbc431bd18109a0aeb08a9303`.
34. Production Candidate Build 3 (`versionCode 3`): Candidate packaged and Step 30 technical smoke passed (`TECHNICAL_SMOKE_PASS`) and Step 31 manual physical-device verification passed on Samsung SM-S948B, Android 16. Build 3 is historical: its source predates MF-LEARN-004 and no longer represents current repository source.
35. Deliver Guided Four-Operation Number-Space Gate (`MF-LEARN-004`) — **COMPLETED** and merged to `main` through Pull Request #44 at `8f4ae59110abf6ea9d365733297a0c15d4c296ea` (validated candidate `51a2bd9907ebdcf738a348bc90de29d31d6b68b6`, merge tree identical to candidate tree `053311fed6a6827af690a6138fd83cc2c63bb7de`, `REVIEW_APPROVED`, 1,590 Core tests passed, Schema V6 preserved, [ADR-0009](decisions/ADR-0009-guided-four-operation-number-space-gate.md)).
36. Execute full exact-candidate automated validation (`FULL_VALIDATION`) for MF-LEARN-004 candidate `51a2bd9907ebdcf738a348bc90de29d31d6b68b6` — **COMPLETED** (`FULL_VALIDATION_PASS`).
37. Reconcile post-MF-LEARN-004 documentation baseline (`MF-DOC-007`) — **IN PROGRESS** (`DOCUMENT_ONLY` on task branch `docs/mf-doc-007-post-mf-learn-004-merge-reconciliation`).
38. Perform production packaging and signing for future candidate (`versionCode` $\ge 4$) — **PENDING** and separately authorized following MF-LEARN-004 merge (agent-executable when explicitly authorized; Build 4 does not exist yet).
39. Perform technical smoke verification (Step 30) on the new production candidate (`versionCode` $\ge 4$) — **PENDING** (agent-executable when explicitly authorized).
40. Perform manual physical-device functional verification (Step 31) on the new production candidate (`versionCode` $\ge 4$) (Samsung SM-S948B, Android 16) — **PENDING** (agent-executable when explicitly authorized).
41. Google Play gate (Step 32) — **PENDING** (BLOCKED until Step 31 passes; separately authorized, user responsibility in Google Play Console).

### Future Planned Roadmap Work
- **MF-UX-007**: Progress Presentation Cleanup (accepted in Backlog).
- **Weak-Frontier / Adaptive Practice Balance**: Extended diagnostic simulation finding (repetition under complete failure); recognized as an uncommitted future architectural inquiry requiring a separate `PLAN_ONLY` decision (not an accepted package).
