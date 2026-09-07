# MathFirst Testing Standards & Evidence Principles

This document defines the authoritative testing taxonomy, evidence boundaries, validation protocols, and privacy safeguards for MathFirst.

---

## 1. Test Scope Taxonomy

All automated testing in MathFirst is categorized into distinct scope layers:

| Scope Layer | Focus | Typical Boundaries |
|---|---|---|
| **Unit Tests** | Isolated pure functions, domain logic, and mathematical algorithms. | In-memory, zero I/O, zero network, deterministic. |
| **Integration Tests** | Interactions between multiple internal modules or persistence adapters. | Temporary local directories, synthetic fixtures, mock services. |
| **Contract / API Tests** | Protocol and data serialization conformance. | Schema validation, contract invariants, offline fixtures. |
| **Adapter Conformance Tests** | Conformance of platform persistence adapters against the shared persistence contract. | Unified reusable test contract verifying atomicity, revision conflicts, versioning, corruption handling, and reopen persistence. |
| **Build Checks** | Compilation, type checking, and raw artifact generation. | Build tool verification, clean compilation, zero warnings. |
| **GUI / UI Tests** | User interface interactions and rendering logic. | Headless test harnesses, component snapshots, synthetic events. |
| **Package Validation** | Distribution package integrity and file layout. | Archive inspection, manifest checks. |

---

## 2. Evidence Boundaries & Non-Extrapolation

- **Strict Evidence Truth**: Agent reports must claim only what was explicitly executed and proven.
- A passing unit test suite does **not** prove end-to-end integration.
- A successful build does **not** prove runtime behavioral correctness.
- A clean linter check does **not** prove absence of functional bugs.

---

## 3. Real Private User Data Protection

Automated test suites and test runners must **NEVER**:
- Open or read real private user documents or directories.
- Copy, migrate, alter, or delete real user data.
- Run tests against production user environments without explicit authorization.

### Permitted Test Fixtures
All automated testing must use:
- Synthetic in-memory fixtures.
- Isolated temporary filesystem directories (e.g. wiped after test run).
- Mock HTTP and network interceptors.
- Deterministic clocks and fixed timestamps.

---

## 4. Deterministic & Offline Defaults

- **Offline by Default**: All automated unit and integration tests must run fully offline without dependencies on external network services.
- **Deterministic**: Tests must not rely on non-deterministic seeds, wall-clock timing, or race conditions.
- Tests requiring live external network APIs must be explicitly tagged and isolated into optional suites requiring affirmative opt-in.

---

## 5. Fail-Closed Validation Policy

- Testing tools, validation scripts, and test runners must operate on a **fail-closed** model: any error, timeout, or ambiguity is treated as a test failure.
- `TEST_ONLY` and `FULL_VALIDATION` modes must **never** perform automatic code repairs or silent retry loops. Failures must be accurately reported in the technical output.

---

## 6. Pre-Technology-Stack Full Validation Concept

Before a language runtime or application technology stack is selected, `FULL_VALIDATION` evaluates repository and documentation integrity via:
1. **Governance & File Presence**: Verify all required repository files and paths exist.
2. **Markdown Link Integrity**: Verify that all repository-relative Markdown links resolve to existing files and valid anchors.
3. **Candidate Diff & Whitespace Audit**: Run `git diff --check <verified-base>...HEAD` against the dynamically verified base branch/commit to ensure no trailing whitespace or corrupt line endings exist in the candidate commit.
4. **Git Hygiene Audit**: Verify `git status` is clean on candidate HEAD and no unexpected untracked artifacts or temporary files are present.

*Note: Canonical stack-specific validation commands will be introduced when the product technology stack is formally established.*
