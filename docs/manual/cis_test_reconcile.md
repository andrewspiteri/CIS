---
title: "cis test reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-reconcile
---

# `cis test reconcile`

Classify native test results from one exact workflow run and preserve typed evidence.

```text
cis test reconcile --run <workflow-run-id> [--suite <suite-id> ...] [--repo <path>] [--format <human|json|agent>]
```

The command records profile digest, repository revision, cases, coverage, mutation, and artifact hashes. A successful process with missing or unreadable declared output is `invalid-evidence`.

By default, reconciliation assesses every suite in the canonical profile. An unbound
or unfinished suite keeps that full result incomplete. For a workflow intentionally
verifying a smaller task scope, repeat `--suite` to name the exact registered suites.
Each selected suite must have a bound step in the recorded workflow; unknown, blank
or duplicate IDs are rejected. All existing native report, input and threshold
checks still apply.

Selected-suite evidence uses schema 2 and status `scope-passed`, with explicit
selected and omitted suite IDs. This is a pass for the selected suites; omitted
suites remain unassessed. The scope digest binds the original workflow execution,
completion time, profile and execution-input digests. Its immutable record is under
`.cis/local/testing/scoped-runs/<scope-reference>/`; it preserves the original native
workflow and artifact run IDs and never replaces the full record in `runs/`.
Repeated identical reconciliation reuses the stored bytes. A different observation
at the same scope identity is rejected; retain it and use a new actual execution.

The returned run reference identifies that scoped record. Supply it explicitly to
`test trace` or `test status --run` when needed. Default latest status continues to
select a full reconciliation. Older completion readers reject schema 2; current
readers validate its explicit scope before assessing the requested native gate.

Reconciliation also hashes every bounded workflow-attempt log and every file beneath
`.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/`. Artifact records bind the run ID, attempt
when applicable, suite, component, repository revision, and executed `TC-*` identities.
An individual diagnostic artifact may not exceed 10 MiB and a suite may contribute at
most 200 diagnostic files; exceeding either limit is invalid evidence.
Files older than the workflow start are treated as stale and excluded rather than being
misattributed to the current run.
