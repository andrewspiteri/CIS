---
title: "Task Type: Documentation and Contracts"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.documentation.contracts
---

# Task Type: Documentation and Contracts

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.documentation.contracts` |
| Provider | CIS Plan core module |
| Creation policy | Always |
| Required predecessor(s) | Coordination; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | All implementation and verification tasks |

Keep canonical specifications, decisions, contracts, dictionaries, references, and navigation aligned with the approved change. This task owns documentary truth and traceability, not production behavior.

## Activation and inputs

### Creation evidence

- Every governed feature creates this task.
- Additional affected documents are selected from accepted impacts, graph relationships, task targets, and repository catalogs.

### Required inputs

- Approved scope, requirements, exclusions, impacts, and decisions.
- Repository catalog, specifications, ADRs, references, contracts, and drift rules.
- Approved wireframe/design digests when documentation describes UI behavior.

## Required activities

1. Inventory every canonical document affected by the approved behavior.
2. Update the authoritative document instead of creating a parallel description.
3. Preserve stable IDs, provenance, lifecycle, ownership, cross-links, and catalog registration.
4. Reconcile API, event, permission, configuration, package, data, workflow, route, and traceability references as applicable.
5. Run strict documentation and deterministic drift validation.

## Required outputs, dependencies, and authority

- Updated canonical documents and catalog/navigation entries.
- Traceability from requirements and decisions to changed documentation.
- Exact validation results and any approved documentary deferrals.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] All affected canonical sources agree with approved behavior, terminology, constraints, and exclusions.
- [ ] No duplicate authority or stale contradictory reference remains.
- [ ] Strict documentation validation and applicable drift checks pass.

## Negative criteria

- Do not treat generated summaries or external issues as canonical.
- Do not invent product decisions to fill documentation gaps.
- Do not mark implementation behavior complete merely because documentation was updated.

## Validation and completion evidence

- Run `cis docs validate --strict` and repository-specific contract/reference drift checks.
- Record changed document IDs/paths, commands, exit codes, and reviewed diffs.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for a bounded update; medium for several authorities or contract families; high when authority conflicts or broad migrations require independent children.

## External issue hints

- Title: `Documentation and Contracts: <feature title>`.
- Labels: `cis`, `task-type:core.documentation.contracts`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
