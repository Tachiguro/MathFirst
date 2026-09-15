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
16. Complete final exact-candidate Native V1 validation on synchronized `main` (exact candidate SHA to be established from live synchronized `main` following post-MF-REL-002 project-state documentation reconciliation merge).
17. Perform separately authorized build, package, and signing steps. AAB generation is not publication.
18. Perform separately authorized final real-device verification.
19. Make a separate later decision about Google Play upload.
