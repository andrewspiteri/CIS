---
title: "cis test trace"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-trace
---

# `cis test trace`

Prove that every generated `TC-*` identity appears in a passed case in an exact reconciled run.

```text
cis test trace <change-id> --run <workflow-run-id> [--repo <path>] [--format <human|json|agent>]
```

Source-code mentions and file paths are routing evidence only; they cannot satisfy executed traceability.
