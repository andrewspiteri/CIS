---
title: "Task Type Contract"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-10"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:spec:task-type-contract
---

# Task Type Contract

## 1. Purpose

This specification defines the common contract used to discover, instantiate,
review, execute, and verify CIS delivery task types. It is the stable boundary
between feature decomposition and later projections into systems such as GitHub
Issues or Jira.

The controlling principles are:

- Markdown in the change dossier remains canonical.
- A task type describes reusable process; a task instance applies it to one change.
- External issues mirror task instances and never silently become the authority.
- Deterministic rules may create and validate tasks; humans retain approval,
  deferral, risk-acceptance, and scope-change authority.

## 2. Task type definition

Every registered task type must define the following fields.

| Field | Required | Meaning |
| --- | --- | --- |
| Stable type key | Yes | Namespaced, immutable identity such as `core.coordination.scope-guard`. |
| Version | Yes | Contract version used to create or reconcile task instances. |
| Provider | Yes | Core CIS module or capability extension that owns the definition. |
| Title and purpose | Yes | Human-readable responsibility and its boundary. |
| Creation policy | Yes | `always`, `conditional`, or `extension`; includes deterministic trigger rules. |
| Inputs | Yes | Canonical documents, graph evidence, decisions, or predecessor outputs consumed. |
| Required activities | Yes | Process that an implementing agent or person must perform. |
| Required outputs | Yes | Code, documents, designs, migrations, evidence, or other artifacts produced. |
| Dependencies | Yes | Required predecessors, permitted concurrency, and downstream consumers. |
| Approval gates | Yes | Decisions that require explicit human authority. `none` must be stated explicitly. |
| Acceptance criteria | Yes | Positive conditions required for completion. |
| Negative criteria | Yes | Prohibited behavior and boundary conditions that must remain absent. |
| Validation contract | Yes | Deterministic commands, reviews, and evidence appropriate to the task. |
| Completion evidence | Yes | Minimum reproducible evidence that must be recorded. |
| Deferral rules | Yes | Required reason, owner, follow-up, residual risk, and approving authority. |
| Complexity rules | Yes | Low, medium, and high thresholds and mandatory decomposition conditions. |
| External issue hints | Yes | Neutral title, labels, hierarchy, links, and status semantics. |

A provider may add fields, but it may not weaken the common human-authority,
traceability, evidence, or completion requirements.

## 3. Core and extension task types

Core task types describe generally applicable software-delivery work. Conditional
core types are registered even when a particular feature does not instantiate them.

Capability-specific work is contributed by a loaded CIS module. For example, a
PARR search provider may register projection and retrieval work without making
search a universal CIS concern. An extension definition declares its activation
evidence, dependencies, conflicts or replacement rules, acceptance contract,
validation contract, and provider-qualified type key.

Missing optional providers must not make an unrelated plan invalid. If repository
evidence requires a provider that is unavailable, planning reports the missing
capability rather than silently omitting the work.

## 4. Task instance contract

An instantiated task is a catalogued Markdown document under the change dossier's
`agent-tasks/` directory. It records at least:

- stable task ID and stable task-type key/version;
- change ID, parent ID, and dependency task IDs;
- source feature-specification path and digest;
- requirement IDs, accepted-impact IDs, and relevant decision IDs;
- repository/component targets;
- `frontend_type` for every wireframe, visual-design, and frontend-implementation
  instance, with exactly `public`, `customer`, or `backoffice`;
- complexity and lifecycle status;
- objective, required activities, outputs, constraints, and exclusions;
- acceptance and negative criteria;
- approval gates and targeted validation;
- completion evidence; and
- deferrals and residual risk.

The task ID is derived from change identity, task-type identity, and, for UI-bearing
task types, frontend type. The three frontend types are intentional qualified
decomposition and may instantiate the same core task-type definition once per affected
type. Re-importing the
same specification reconciles the same task rather than creating a duplicate.
Reconciliation may refresh deterministic content but preserves human review,
approval, evidence, deferral, and external-link fields.

## 5. Dependency and execution model

Dependencies form a directed acyclic graph. Display order is derived from that
graph and human-readable phase preferences; a numeric prefix is not authoritative.

Planning validation rejects missing or duplicate identities, self-dependencies,
cycles, absent required task types, unrecorded trigger evidence, uncovered
requirements or accepted impacts, undecomposed high-complexity work, and production
implementation that bypasses a required approval gate.

For UI-bearing work, duplicate identity means the same task-type key and frontend
type. `core.design.wireframe`, `core.design.visual`, and
`core.frontend.implementation` may each occur up to once for every affected frontend
type; other core task types retain one unqualified instance.

Tasks without a dependency relationship may run concurrently. Concurrency does not
permit consuming an output that has not been produced or approved.

For a change that instantiates Wireframe and Visual design, every other delivery task
is downstream of approved design. When the design pack enters `ReadyForReview`, the
change has a global pause: only review, decision recording, and rejected-design
revision work is permitted until human approval releases the gate.

## 6. Lifecycle and authority

The common task lifecycle is:

```text
Draft -> ReadyForReview -> Approved -> InProgress -> Complete
                              |             |
                              +-> Blocked <-+
                              +-> Deferred
```

`Cancelled` is used only after containing scope is explicitly changed or withdrawn.
Transitions record actor, timestamp, reason, and evidence where applicable.

Agents may draft tasks, propose dependencies, execute approved tasks, and attach
evidence. Humans approve the plan and gated artifacts, accept scope changes and
residual risk, and authorize deferrals. A task cannot become `Complete` solely
because an agent reports success.

## 7. Complexity and decomposition

- `low`: bounded work with one principal output and low coordination or rollback risk;
- `medium`: multiple outputs, states, components, or validation layers, but still
  executable as one independently verifiable task; and
- `high`: work whose risk, breadth, uncertainty, or coordination prevents reliable
  execution and verification as one task.

A high-complexity task must be decomposed into independently completable child tasks.
The parent remains a coordination and completeness record, not one implementation
assignment.

## 8. External issue projection boundary

GitHub Issues, Jira, and similar systems are handled through the provider-neutral
tracker host after the task contract is stable. An adapter may synchronize titles,
descriptions, labels, links, hierarchy, assignees, and mirrored status, but preserves
CIS identity, the canonical document link, dependencies, approval boundaries,
evidence, and conflicts when either or both sides change. Identity, authority,
direction, deletion, conflict, retry, credentials, and durable-link behavior are
governed by `external-tracker-synchronization-spec.md`.

## 9. Initial core taxonomy

1. Coordination / scope guard.
2. Wireframe.
3. Visual and interaction design.
4. Documentation and contracts.
5. Security and permissions.
6. Data and persistence.
7. Database migration.
8. Data migration or backfill.
9. API and contract.
10. Backend behavior.
11. Frontend implementation.
12. Integration and handoff.
13. Infrastructure and deployment.
14. Observability and operations.
15. Lifecycle and carry-forward.
16. Rollout and release.
17. Verification.
18. Independent assurance.
19. Final delivery sweep.

All nineteen core types have individual Draft definitions linked from
`core-task-type-catalog.md`. Search, projection, and other product capabilities are
extension task types rather than members of this core list. Provider identity,
conflict, replacement, upgrade, and removal rules are governed by
`task-type-extension-policy.md`.

## 10. Final completion authority

Final Delivery Sweep owns the deterministic planned-versus-actual audit, completion
gate, evidence index, and reproducible handoff. It directly depends on every
applicable delivery task and produces a `ready` or `not-ready` recommendation.
It cannot record stakeholder acceptance or close the change.

Coordination / scope guard owns final human feature acceptance. It may complete only
after Final Delivery Sweep is Complete and every child has a valid terminal
disposition. The acceptance record identifies the human actor, timestamp, rationale,
accepted deferrals/residual risks, and exact final-sweep evidence. External tracker
closure, merge, deployment, or release never exercises this authority implicitly.
