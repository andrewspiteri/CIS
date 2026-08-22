---
title: "Task Type: Lifecycle and Carry-Forward"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.lifecycle.carry-forward
---

# Task Type: Lifecycle and Carry-Forward

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.lifecycle.carry-forward` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Backend and Data where applicable; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Integration, verification, rollout, and final delivery |

Own conversion, carry-forward, inheritance, archive/restore, retention, history, and identity/provenance behavior across lifecycle transitions.

## Activation and inputs

### Creation evidence

- Lifecycle, conversion, carry-forward, inheritance, archive, restore, history, retention, renewal, or transition evidence.

### Required inputs

- Workflow states, invariants, identity/ownership, permissions, retention, audit, conversion rules, and prior/new record relationships.

## Required activities

1. Define allowed transitions, triggers, actors, preconditions, side effects, and terminal/reversible states.
2. Preserve or deliberately transform identity, provenance, ownership, visibility, audit, and history.
3. Prevent duplicate active records, repeated carry-forward, orphaning, and inconsistent partial transition.
4. Specify permission and retention behavior before, during, and after transition.
5. Test primary, repeated, denied, concurrent, partial-failure, archive/restore, and historical views.

## Required outputs, dependencies, and authority

- Lifecycle implementation and updated workflow/state references.
- Identity/provenance and non-duplication evidence.
- Transition, audit, permission, retention, and recovery results.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Approved transitions preserve canonical ownership, provenance, visibility, history, audit, and retention semantics.
- [ ] Repeated or concurrent transitions do not duplicate or corrupt state.
- [ ] Positive, denied, failure, and recovery transition checks pass.

## Negative criteria

- Do not implement carry-forward as blind cloning without identity/provenance rules.
- Do not erase history or bypass retention during conversion.
- Do not expose archived/restricted state contrary to the actor model.

## Validation and completion evidence

- Run state-machine, conversion, repeat, concurrency, non-duplication, audit, permission, archive/restore, retention, and recovery tests.
- Record before/after state, identities, commands, results, and residual risks.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for one reversible transition; medium for conversion/history; high for multi-entity, cross-period, irreversible, or concurrency-sensitive lifecycle and must be decomposed.

## External issue hints

- Title: `Lifecycle and Carry-Forward: <feature title>`.
- Labels: `cis`, `task-type:core.lifecycle.carry-forward`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
