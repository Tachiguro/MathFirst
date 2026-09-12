# MathFirst Current Operational Work

This document provides operational context for the package currently in flight.

> [!IMPORTANT]
> This document provides transient operational evidence only. **Live Git and GitHub repository state always takes precedence** over the contents of this file. Newly initialized sessions must inspect live state first (see [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)).

---

## 1. Active Package Details

- **Active Package ID**: `MF-REL-001`
- **Title**: Android Internal AAB Packaging and Release Automation
- **Active Task Branch**: `feat/mf-rel-001-android-aab-packaging`
- **Base Branch / Commit**: `main` at `9a9e5c43d1f1685cf21e766e0890b790ab09040c`
- **Current Implementation Candidate HEAD**: `0c896e6eac827bf3dddca244bc507306997fc2f9`
- **Operation Mode**: `DOCUMENT_ONLY`
- **Status**: Implementation complete and verified; consolidated `REVIEW_ONLY` re-review passed (`REVIEW_PASS`); documentation reconciliation completed locally under `DOCUMENT_ONLY`; documentation changes uncommitted; four local corrective commits unpushed; remote PR #17 remains open and stale.
- **Implementation & Review State**:
  - Implementation slices completed: `android-manifest-hygiene-hardening`, `aab-packaging-provenance-automation`, `local-aab-validation-harness`.
  - Consolidated re-review outcome: `REVIEW_PASS` (Implementation-layer readiness: YES).
  - Verified remediations: R01 (remove jarsigner -strict, accept self-signed/trust/timestamp warnings while requiring cryptographic verification evidence), R02 (parse keytool Signer #N blocks, extract Certificate #1 leaf SHA-256 fingerprint, ignore chain certificates as independent signers), R03 (remove --no-build from scripts/validate-android-aab.ps1 so the tool can build in a fresh checkout).
  - Open non-blocking review items recorded: R04 (origin/main presence requirement), R05 (MSBuild evaluated property parsing robustness), R06 (redundant resource path matching).
  - Test suite (`MathFirst.Core.Tests`): Targeted packaging, script, and validation suites passed during implementation; final review remediation verified 141 directly relevant tests; full Core suite validation remains pending for formal FULL_VALIDATION.
  - Compilation: Android and Windows Release builds compile with 0 warnings, 0 errors.
  - Remote PR #17: Open on GitHub (base `main`, remote head `eccfa823e365dd728b3c63b5065601f561969f9c`, 6 commits). Four local corrective commits are unpushed. Remote PR body is intentionally stale with invalid historical claims and will be updated during `PR_ONLY`.

---

## 2. Operational Rules

1. **Subordinate Status**: If this file differs from the current branch, working tree, or GitHub state, live repository evidence is authoritative.
2. **Lifecycle Isolation**: `DOCUMENT_ONLY` modifies documentation files without staging, committing, pushing, or running build/test commands.
3. **No Behavioral Implementation**: No code or tests under `src/`, `tests/`, or `tools/` are modified during documentation reconciliation.
4. **Planned Future Work**: `MF-SET-001` (Practice Configuration: Operation Selection and Adjustable Base Time) is recorded as `Proposed` in `docs/BACKLOG.md` and is inactive until `MF-REL-001` is merged and synchronized.

---

## 3. Current Lifecycle Position

- **Immediate Next Lifecycle Step**: `COMMIT_ONLY` staging and commit creation for the reconciled documentation.
- **Subsequent Delivery Sequence**: `COMMIT_ONLY` → `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` (reconcile PR #17 metadata and validation claims) → manual user merge → `POST_MERGE_SYNC_ONLY`.
- **Release Boundary Invariant**: Local AAB packaging and validation do not imply Google Play upload, distribution, or production release. Google Play release remains a separately authorized lifecycle gate.
