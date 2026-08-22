---
title: "Task Type: Coordination and Scope Guard"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-10"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.coordination.scope-guard
---

# Task Type: Coordination and Scope Guard

## 1. Identity

| Property | Value |
| --- | --- |
| Stable type key | `core.coordination.scope-guard` |
| Provider | CIS Plan core module |
| Creation policy | Always; exactly once per feature delivery pack |
| Parent | None; this is the root task for the feature pack |
| Children | Every task created to deliver or assure the feature |
| Canonical instance | `changes/<change-id>/agent-tasks/<task-id>.md` |

## 2. Purpose and boundary

The Coordination / scope guard task is the root completeness record for a feature.
It turns approved scope into a reviewable dependency graph, prevents exclusions and
unresolved decisions from disappearing during implementation, and holds the final
accountability for every child task.

It owns planning completeness and scope control. It does not own implementation,
design production, validation execution, issue-system administration, or approval
decisions delegated to another task type.

## 3. Creation triggers

The task is created whenever CIS imports a feature specification or creates a
feature delivery plan. Creation requires a stable change ID, a canonical feature
specification path and digest, at least one requirement or goal, and the exact graph
or Git baseline used for impact analysis.

If those inputs are incomplete, CIS may create a Draft task but marks it blocked and
identifies the missing evidence. It must not present the plan as approval-ready.
Repeated import reconciles the existing task rather than creating a second root.

## 4. Inputs

The task consumes:

- canonical feature specification, source digest, goals, requirements, and exclusions;
- repository classification and target components or repositories;
- accepted, rejected, and deferred impact findings;
- governing BRD requirements, architecture decisions, contracts, and references;
- open change decisions and human dispositions;
- task-type definitions registered by core and applicable extensions; and
- known risk, compliance, release, and operational constraints.

Source material is referenced rather than copied as a competing authority.

## 5. Required activities

The coordinating agent or owner must:

1. Confirm outcome, requirements, source digest, targets, and baseline agree with the
   accepted impact review.
2. Record explicit inclusions, exclusions, assumptions, constraints, and prohibited
   outcomes in language that can be tested.
3. Expose ambiguous scope and create decision records where human judgment is
   required; do not convert assumptions into decisions.
4. Evaluate every registered task type against its creation rules and record why
   each applicable task was created.
5. Map every requirement, accepted impact, exclusion, and required decision to one
   or more child tasks.
6. Assign dependencies and permitted concurrency, then verify the graph is complete
   and acyclic.
7. Evaluate complexity and decompose every high-complexity task until each leaf task
   can be independently executed, reviewed, and verified.
8. Identify design, security, migration, rollout, deferral, and residual-risk
   approval gates.
9. For UI-bearing scope, make every non-design delivery task depend on approved
   Visual design and enforce a global work pause while design is under review.
10. Present the complete pack for human review and preserve its feedback.
11. During delivery, reconcile approved scope changes, status, blockers, deferrals,
    and newly discovered work without rewriting historical evidence.
12. Before completion, confirm every child is complete, cancelled by approved scope
    change, or explicitly deferred with accepted residual risk.

## 6. Required outputs

This task produces and maintains:

- the root coordination task document;
- the feature plan and dependency ledger;
- the child-task inventory and stable relationships;
- requirement, impact, decision, and exclusion coverage mappings;
- the approval-gate inventory;
- the blocker, scope-change, and deferral record; and
- the final child-task disposition summary.

External issues may be linked but are not canonical outputs.

## 7. Dependencies and concurrency

The task has no predecessor. Its initial planning work precedes child execution.
After plan approval, it remains active while independent children execute according
to their dependencies.

Every child is contained by the approved scope represented by this task, but that
containment must not be encoded as an execution edge that makes the parent wait on
itself. The parent completes only after all child dispositions and final evidence
are resolved. In particular, `core.delivery.final-sweep` must be Complete before
Coordination records final human feature acceptance. Final Delivery Sweep owns the
deterministic readiness recommendation; Coordination owns acceptance authority.

## 8. Human approval gates

Explicit human authority is required for:

- initial plan approval;
- changes to approved requirements, exclusions, target repositories, or outcome;
- acceptance of a deferral or residual risk;
- cancellation of a required child;
- replacement of a required task type with a different delivery approach; and
- final acceptance that delivery satisfies the approved scope.

An agent may recommend these actions but may not perform the approval transition or
invent the approver identity or rationale.

## 9. Acceptance criteria

- [ ] Source specification, digest, change ID, and exact baseline are recorded.
- [ ] Every requirement or derived goal maps to at least one child task.
- [ ] Every accepted impact maps to at least one child task.
- [ ] Every exclusion is owned as negative criteria or verification coverage.
- [ ] Every applicable core or extension type is instantiated or has a reviewable
      non-applicability reason.
- [ ] Every child has stable identity, type/version, targets, dependencies,
      complexity, acceptance criteria, validation, and evidence requirements.
- [ ] Every high-complexity task is decomposed into independently completable children.
- [ ] The dependency graph contains no missing tasks, self-dependencies, or cycles.
- [ ] Required human gates and decision owners are visible.
- [ ] For UI-bearing scope, every downstream task is gated by approved design and no
      work continued during a design-review pause.
- [ ] No unresolved question is hidden as an implementation assumption.
- [ ] The plan has explicit human approval with actor, timestamp, and rationale.
- [ ] All children are complete, validly cancelled, or deferred with approved risk.
- [ ] Final planned-versus-actual scope and task disposition are recorded.
- [ ] Final Delivery Sweep is Complete and its exact readiness evidence is linked.
- [ ] A human has explicitly accepted the feature outcome, known deferrals, and
      residual risks with actor, timestamp, and rationale.

## 10. Negative criteria

The coordination process must not:

- add behavior absent from approved requirements or decisions;
- silently drop a requirement, accepted impact, exclusion, conditional task type,
  failed check, blocker, or child task;
- treat a generated pack as human approval;
- close because code merged or an external issue closed;
- use numeric order as a substitute for dependencies;
- turn deferred work into a successful completion claim;
- duplicate the root on re-import; or
- let an external issue overwrite newer canonical evidence without conflict review.

## 11. Validation contract

Deterministic validation checks:

- stable and unique task identities and exactly one coordination root;
- source path, digest, baseline, and target existence;
- requirement, accepted-impact, exclusion, and negative-criteria coverage;
- registered task-type trigger decisions;
- parent, child, and dependency integrity and acyclicity;
- high-complexity decomposition;
- approval records and valid lifecycle transitions; and
- a disposition and evidence link for every child before root completion.

Human review assesses whether decomposition is sensible, exclusions are faithful,
conditional types were triggered appropriately, and the work can produce the
requested outcome.

## 12. Completion evidence

| Evidence | Required content |
| --- | --- |
| Source snapshot | Feature path, SHA-256 digest, baseline, and target repositories |
| Coverage report | Requirement, impact, exclusion, and decision mappings |
| Dependency validation | Command, build ID, result, and diagnostics |
| Plan approval | Approver, timestamp, rationale, and approved revision |
| Child disposition | Task ID, final status, evidence link, and deferral/cancellation authority |
| Scope comparison | Approved versus delivered scope and explained variance |
| Final sweep | Completed task ID, planned-versus-actual report, evidence digest, and readiness recommendation |
| Final acceptance | Human actor, timestamp, rationale, and known residual risk |

A status summary without its supporting command, artifact, or approval is
insufficient evidence.

## 13. Deferral and scope-change rules

A deferred child states the unmet criterion, reason, owner, follow-up, target date
or revisit condition, impact on the outcome, residual risk, and human approval. The
root cannot complete if a deferral makes an approved requirement false unless the
approved scope also changes.

New work discovered after approval is added through an explicit plan revision.
Removed work remains in history with cancellation authority; it is not erased.

## 14. Complexity rules

| Level | Coordination characteristics |
| --- | --- |
| Low | One repository, few children, no elevated gate, and one delivery path. |
| Medium | Multiple components or surfaces, several dependencies, or one material gate. |
| High | Multiple repositories, high-risk security/data/rollout work, many interacting dependencies, or unresolved product/architecture decisions. |

A high-complexity coordination task is a decomposed parent. Its child graph is the
decomposition; the parent is not one indivisible implementation assignment.

## 15. External issue hints

- Kind: feature parent, epic, or tracking issue.
- Title: `<change-id>: <feature title>`.
- Labels: `cis`, `task-type:coordination`, and the change identifier.
- Body: scope, exclusions, approval state, canonical task link, and child links.
- Hierarchy: no parent; feature tasks are children or linked work items.
- Status: mapped from CIS without external closure proving CIS completion.
- Identity marker: stable CIS task ID and task-type key/version.

Detailed Jira and GitHub mappings follow only after the task model is approved.

## 16. Open review questions

1. Should Coordination and Final delivery sweep remain separate tasks?
2. Must every conditional omission have a reason, or only partial trigger matches?
3. Can the same human role approve the plan and final feature acceptance?
4. What threshold promotes coordination complexity from medium to high?
5. Does final acceptance belong here, in Final delivery sweep, or in both with
   different meanings?
