---
title: "cis test status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-status
---

# `cis test status`

Report executed case traceability for a change using an exact or latest reconciled run.

```text
cis test status <change-id> [--run <workflow-run-id>] [--repo <path>] [--format <human|json|agent>]
```

Use `--run` in governed evidence so the result cannot drift to a later execution.
