---
title: "Task Type: Data Migration and Backfill"
type: task-type-definition
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on planning-model change"
cis:
  stable_id: change-impact-studio:task-type:core.data.backfill
---

# Task Type: Data Migration and Backfill

## Identity and boundary

| Property | Value |
| --- | --- |
| Stable type key | `core.data.backfill` |
| Provider | CIS Plan core module |
| Creation policy | Conditional |
| Required predecessor(s) | Database Migration when structural change exists; Data and Persistence; approved Visual Design for UI-bearing scope |
| Primary consumer(s) | Rollout, verification, observability, and final delivery |

Own transformation, reconciliation, repair, or reindexing of existing records as an observable restartable operation distinct from schema deployment.

## Activation and inputs

### Creation evidence

- Backfill, existing-data transformation, reconciliation, repair, reindex, historical conversion, or bulk migration evidence.

### Required inputs

- Source/target semantics, population and volume profile, schema readiness, mapping rules, data-quality constraints, and recovery decision.

## Required activities

1. Define selection, batching, checkpoints, idempotency keys, throttling, and restart behavior.
2. Provide dry-run and bounded execution modes with no sensitive-data leakage.
3. Measure source/target counts, invalid rows, duplicates, drift, and reconciliation tolerances.
4. Define failure isolation, repair, rollback/compensation, and operator controls.
5. Validate on representative scale and document production run ownership.

## Required outputs, dependencies, and authority

- Versioned backfill/reconciliation implementation.
- Dry-run, progress, checkpoint, error, and reconciliation reports.
- Runbook with start/stop, recovery, ownership, and completion criteria.

Only tasks without an unresolved dependency may run concurrently. Approved Visual
Design is a direct global dependency for every downstream UI-bearing task. Human
authority is required for scope changes, deferrals, residual-risk acceptance, and
any repository policy exception; task execution and evidence collection do not imply
that authority.

## Acceptance criteria

- [ ] Eligible records transform completely and deterministically with safe repeat/restart behavior.
- [ ] Counts and invariants reconcile within an explicitly approved zero or nonzero tolerance.
- [ ] Failures are observable, isolated, recoverable, and do not corrupt already-processed data.

## Negative criteria

- Do not make a one-shot unbounded production script the only artifact.
- Do not silently skip invalid records or erase source provenance.
- Do not use production personal or sensitive data in committed evidence.

## Validation and completion evidence

- Run dry-run, small batch, interruption/restart, repeat, invalid-row, reconciliation, and representative-volume checks.
- Record dataset characteristics, commands, checkpoints, counts, errors, timings, and recovery results.

Completion evidence must identify the exact baseline, affected target, command or
review method, timestamp, actor/tool version, result, and artifact path or digest.
Open placeholders, unchecked acceptance/validation items, unexplained warnings, or
missing evidence prevent completion.

## Deferral and complexity

Any deferral records the unmet criterion, reason, owner, consequence, revisit
condition, residual risk, and human approval. Low for small deterministic conversion; medium for batching/restart; high for large, cross-store, irreversible, or low-quality data and must be decomposed.

## External issue hints

- Title: `Data Migration and Backfill: <feature title>`.
- Labels: `cis`, `task-type:core.data.backfill`, plus affected repository/component labels.
- Body: canonical task link, source digest, targets, dependencies, gates, acceptance,
  validation, and evidence expectations.
- External status never grants CIS approval, accepts risk, or replaces the canonical
  Markdown task lifecycle.
