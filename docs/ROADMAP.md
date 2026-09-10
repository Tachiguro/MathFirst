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
- **Implementation**: Complete — independent operation progression, deterministic bounded selection, Schema V5 persistence, and open-ended curriculum runtime are implemented.
- **Review and validation**: `REVIEW_APPROVED`; full validation passed.
- **Delivery status**: Merged into `main` through Pull Request #9. Web implementation remains deferred.

### Phase 6: Native V1 Release Readiness
1. Reconcile post-merge current-state documentation (`MF-DOC-001`) — complete and merged.
2. Deliver deterministic practice personality and contextual copy (`MF-UX-002`) — implementation complete and `REVIEW_APPROVED` on candidate branch `feat/mf-ux-002-contextual-copy`; documentation and delivery lifecycle steps remain before merge.
3. Reconcile native identity, version, and visual treatment (`MF-UX-003`) — next only after MF-UX-002 is merged and synchronized.
4. Establish Android internal AAB packaging and release automation (`MF-REL-001`).
5. Complete final exact-candidate Native V1 validation.
6. Perform separately authorized build, package, and signing steps.
7. Perform separately authorized final real-device verification.
8. Make a separate later decision about Google Play upload.
