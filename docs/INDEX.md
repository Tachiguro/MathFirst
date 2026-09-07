# MathFirst Documentation Index & Task Router

This document serves as the authoritative entry point and task-based router for the MathFirst repository documentation. Contributors and autonomous agents must consult the specific documents indicated below according to their current task.

---

## 1. Documentation Index

| Document | Primary Responsibility | Authority Level |
|---|---|---|
| [AGENTS.md](../AGENTS.md) | Universal repository invariant rules and safety boundaries. | Authoritative for core repository invariants and safety. |
| [docs/INDEX.md](INDEX.md) | Documentation index, routing matrix, and authority boundaries. | Authoritative for document routing. |
| [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md) | Live-state discovery protocol for new chat sessions. | Authoritative for chat discovery & session bootstrap. |
| [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md) | Operation Modes, prompt structure, model routing, and hard stops. | Authoritative for prompt formats & Operation Modes. |
| [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | Package lifecycle, slicing, TDD, reviews, validation, and PR workflow. | Authoritative for lifecycle execution & branch workflow. |
| [docs/TESTING.md](TESTING.md) | Test taxonomy, evidence boundaries, fail-closed validation, and privacy. | Authoritative for testing & validation evidence. |
| [docs/BACKLOG.md](BACKLOG.md) | Authoritative registry for accepted inactive work (`MF-*` IDs). | Authoritative for inactive backlog items. |
| [docs/CURRENT_WORK.md](CURRENT_WORK.md) | Operational tracking of the active package in flight. | Operational evidence (subordinate to live Git state). |
| [docs/PROJECT_STATE.md](PROJECT_STATE.md) | Durable, verified factual baseline and project capabilities. | Authoritative for stable project facts. |
| [docs/ROADMAP.md](ROADMAP.md) | High-level strategic roadmap, phases, and milestone ordering. | Authoritative for strategic sequencing. |
| [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) | Lifecycle boundaries for build, package, sign, publish, and release. | Authoritative for build and release boundaries. |
| [docs/PRODUCT.md](PRODUCT.md) | Functional product contract, learning model, progression rules, and MVP boundary. | Authoritative for product requirements & MVP scope. |
| [docs/decisions/README.md](decisions/README.md) | Architecture Decision Record (ADR) registry and standards. | Authoritative for architecture decisions. |
| [.github/pull_request_template.md](../.github/pull_request_template.md) | Standard GitHub Pull Request template and review checklist. | Authoritative for PR structure. |
| [CHANGELOG.md](../CHANGELOG.md) | Project change history in Keep a Changelog format. | Authoritative for release notes & change history. |

---

## 2. Task Routing Matrix

Agents should read only the documentation required for their specific task context:

| Context / Operation Mode | Mandatory Reading | Secondary / As-Needed Reading |
|---|---|---|
| **Always (Every write operation)** | [AGENTS.md](../AGENTS.md) | [docs/INDEX.md](INDEX.md) |
| **New Chat / Session Bootstrap** | [AGENTS.md](../AGENTS.md)<br>[docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md)<br>[docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md)<br>[docs/INDEX.md](INDEX.md) | [docs/CURRENT_WORK.md](CURRENT_WORK.md)<br>[docs/PROJECT_STATE.md](PROJECT_STATE.md) |
| **Package Planning (`PLAN_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)<br>[docs/ROADMAP.md](ROADMAP.md) | [docs/PROJECT_STATE.md](PROJECT_STATE.md)<br>[docs/PRODUCT.md](PRODUCT.md)<br>[docs/BACKLOG.md](BACKLOG.md)<br>[docs/decisions/README.md](decisions/README.md) |
| **Implementation (`IMPLEMENT_SLICE`, `IMPLEMENT`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)<br>[docs/TESTING.md](TESTING.md) | [docs/CURRENT_WORK.md](CURRENT_WORK.md)<br>[docs/PRODUCT.md](PRODUCT.md)<br>Relevant ADRs in [docs/decisions/](decisions/README.md) |
| **Testing & Verification (`TEST_ONLY`, `FULL_VALIDATION`)** | [AGENTS.md](../AGENTS.md)<br>[docs/TESTING.md](TESTING.md) | [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) |
| **Review (`REVIEW_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md)<br>[docs/TESTING.md](TESTING.md) | [.github/pull_request_template.md](../.github/pull_request_template.md) |
| **Documentation Authoring (`DOCUMENT_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | [docs/INDEX.md](INDEX.md)<br>[docs/PRODUCT.md](PRODUCT.md)<br>[docs/PROJECT_STATE.md](PROJECT_STATE.md)<br>[CHANGELOG.md](../CHANGELOG.md) |
| **Committing & Staging (`COMMIT_ONLY`, `CHECKPOINT_COMMIT_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | [docs/CURRENT_WORK.md](CURRENT_WORK.md) |
| **Push & PR (`PUSH_ONLY`, `PR_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | [.github/pull_request_template.md](../.github/pull_request_template.md) |
| **Post-Merge Sync (`POST_MERGE_SYNC_ONLY`)** | [AGENTS.md](../AGENTS.md)<br>[docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) | [docs/CURRENT_WORK.md](CURRENT_WORK.md) |
| **Build, Package & Release Operations** | [AGENTS.md](../AGENTS.md)<br>[docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) | [docs/PROJECT_STATE.md](PROJECT_STATE.md) |
| **Architecture Decision Authoring** | [AGENTS.md](../AGENTS.md)<br>[docs/PRODUCT.md](PRODUCT.md)<br>[docs/decisions/README.md](decisions/README.md) | [docs/PROJECT_STATE.md](PROJECT_STATE.md)<br>[docs/ROADMAP.md](ROADMAP.md) |

---

## 3. Authority & Duplication Boundaries

To prevent conflicting rules across documents:
1. **Safety & Git Operations**: [AGENTS.md](../AGENTS.md) is the single authoritative source.
2. **Prompts & Modes**: [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md) is authoritative.
3. **Workflow & Lifecycle**: [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) is authoritative.
4. **Testing Standards**: [docs/TESTING.md](TESTING.md) is authoritative.
5. **Product Requirements & MVP Scope**: [docs/PRODUCT.md](PRODUCT.md) is authoritative.
6. **Durable Facts vs. Active State**:
   - [docs/PROJECT_STATE.md](PROJECT_STATE.md) contains durable, verified project facts only.
   - [docs/CURRENT_WORK.md](CURRENT_WORK.md) contains active package operational status only and is subordinate to live Git state.
   - [docs/BACKLOG.md](BACKLOG.md) contains accepted inactive items only.
