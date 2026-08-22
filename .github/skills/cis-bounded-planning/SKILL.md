---
name: cis-bounded-planning
description: Build, import feature specifications into, inspect, and validate dependency-aware CIS work plans from accepted impact findings. Use when impact review is complete and work must be bounded by complexity, objectives, dependencies, acceptance criteria, validation, and approval gates before implementation.
---

# CIS Bounded Planning

1. Run `cis impact completeness <change-id>` and stop if planning is not ready.
2. Run `cis decision list <change-id>` and surface blocking or advisory decisions.
3. When a governed feature specification exists, run `cis plan import-spec <change-id> --file <repo-relative-spec.md>`; otherwise run `cis plan build <change-id>`.
4. Run `cis plan show <change-id>` and open every linked `agent-tasks/WORK-NNN.md`; the plan table is only the dependency ledger, not the complete task contract. After re-import, inspect `agent-tasks/retired/`, archived catalog routes, and `plan-task-retired|reactivated` events; preserved evidence never grants renewed completion.
5. Verify each task has bounded required changes, exclusions, accepted graph evidence, dependencies and gates, acceptance checklists, targeted validation, a completion-evidence table, and explicit deferral/residual-risk handling.
6. Classify every UI-bearing requirement, screen, wireframe task, design task, and frontend implementation task as exactly `public`, `customer`, or `backoffice`. Generate a matched wireframe -> design -> frontend chain for each affected type.
7. For every unauthenticated endpoint, require `PUBLIC-ENDPOINT-CACHE` obligations in Security, API contract, Backend, Observability, Verification, and Independent assurance: every response traverses a governed cache and the public route/controller/handler never directly accesses a database or repository, including on cache miss.
8. For UI-bearing scope, require `coordination -> wireframe approval -> design approval -> every downstream task`. Treat `wireframes.md` and `design.md` as canonical review records. When design enters `ReadyForReview`, stop all non-review work until explicit approval.
9. Require applicable documentation, security, data, database-migration, backfill, API, backend, frontend, integration, infrastructure, observability, lifecycle, rollout, verification, assurance, and final-sweep workstreams. Product-specific search/projection belongs to an extension provider, not CIS core.
10. Confirm every task is low, medium, or high complexity and every high item is a decomposed parent with at least two bounded children.
11. Run `cis plan validate <change-id>` and resolve every error. Use `cis plan task transition <change-id> <task-id> --status <state> --actor <identity> --reason <rationale>` for lifecycle changes. Completion requires resolved evidence and snapshots sanitized tool-usage counts/savings into the task and `verification.md`.
12. If planning reports an extension capability conflict, run `cis plan capability status`, present all candidates, and use `cis plan capability select` only after explicit human selection. Selection never migrates existing tasks; use `cis plan task migrate-type` with human reviewer and rationale for each compatible instance.
13. Run `cis plan approve <change-id> --reviewer <identity> --reason <rationale>` only after the user explicitly approves the exact validated issue pack.

Never approve a plan autonomously, hide advisory warnings, remove accepted scope to make validation pass, or treat a draft plan as implementation authority.
