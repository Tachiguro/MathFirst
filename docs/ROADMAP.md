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

MF-LEARN-001 implementation and review are complete for the native Windows and Android targets on the local feature branch. It does not begin Web implementation or any release activity; the branch remains local-only until the governed documentation commit, validation, push, Pull Request, manual merge, and post-merge synchronization lifecycle completes.

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
- **Implementation**: Complete — independent operation progression, deterministic bounded selection, Schema V5 persistence, and open-ended curriculum runtime are implemented.
- **Review**: `REVIEW_APPROVED`.
- **Current lifecycle**: Documentation reconciliation is complete and awaits `COMMIT_ONLY` on the local feature branch.
- **Delivery status**: Not yet committed as documentation, fully validated at the final candidate, pushed, submitted as a Pull Request, or merged. Web implementation remains deferred.
