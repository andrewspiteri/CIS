---
title: "Task Type: Final Delivery Sweep"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.delivery.final-sweep
---

# Task Type: Final Delivery Sweep

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.delivery.final-sweep` |
| Provider | CIS Plan core module |
| Creation policy | Always |
| Required predecessor(s) | Independent Assurance and all applicable delivery tasks |
| Primary consumer(s) | Coordination final human acceptance and change closure |

Perform the deterministic planned-versus-actual completion audit and construct a reproducible handoff. This task recommends readiness; it does not own final human feature acceptance.

## Activation and inputs

### Creation evidence

- Every governed feature creates this task.

### Required inputs

- Approved plan revision, all task documents/status histories, implementation diff/baseline, verification and assurance evidence, documentation/catalog state, deferrals, residual risks, and release/operations artifacts.

## Required activities

1. Compare planned requirements, impacts, decisions, tasks, targets, outputs, and exclusions with actual changes and evidence.
2. Confirm every child has a valid terminal disposition and every approval/deferral/risk authority is recorded.
3. Run repository completion gates, strict docs validation, final affected checks, and evidence integrity checks.
4. Identify unplanned work, missing artifacts, stale generated state, blockers, and handoff/cleanup obligations.
5. Produce a readiness recommendation for Coordination without converting any blocker into a pass.

## Required outputs, dependencies, and authority

- Planned-versus-actual report and final evidence index.
- Reproducible handoff with baseline, diff, commands, artifacts, deferrals, residual risks, and operational/release state.
- Ready/not-ready recommendation to Coordination.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Every planned item and actual change is reconciled with valid disposition and evidence.
- [ ] Final deterministic gates pass; approved deferrals and residual risks are explicit and linked to authority.
- [ ] A fresh reviewer can reproduce the completion claim and handoff from recorded evidence.

## Negative criteria

- Do not record final stakeholder acceptance or close the change.
- Do not turn blocked, missing, warning-only, or skipped evidence into success.
- Do not omit unplanned changes or cleanup obligations from planned-versus-actual reporting.

## Validation and completion evidence

- Run `cis plan validate`, strict documentation validation, repository completion commands, final affected checks, evidence hash/link checks, and planned-versus-actual diff review.
- Record baseline/head, changed paths, commands, results, artifact digests, child dispositions, deferrals, risks, and readiness recommendation.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low only for a small fully evidenced change; medium for several workstreams; high for multi-repository, release/migration, or substantial deferral/risk and must be decomposed.

## External issue hints

- Title: `Final Delivery Sweep: <feature title>`.
- Labels: `cis`, `task-type:core.delivery.final-sweep`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
