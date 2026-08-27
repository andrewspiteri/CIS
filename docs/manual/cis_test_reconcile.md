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
cis test reconcile --run <workflow-run-id> [--repo <path>] [--format <human|json|agent>]
```

The command records profile digest, repository revision, cases, coverage, mutation, and artifact hashes. A successful process with missing or unreadable declared output is `invalid-evidence`.

Reconciliation also hashes every bounded workflow-attempt log and every file beneath
`.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/`. Artifact records bind the run ID, attempt
when applicable, suite, component, repository revision, and executed `TC-*` identities.
An individual diagnostic artifact may not exceed 10 MiB and a suite may contribute at
most 200 diagnostic files; exceeding either limit is invalid evidence.
Files older than the workflow start are treated as stale and excluded rather than being
misattributed to the current run.
