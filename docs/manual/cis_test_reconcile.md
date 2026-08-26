---
title: "cis test reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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
