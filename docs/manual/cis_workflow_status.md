---
title: "cis workflow status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workflow-status
---

# `cis workflow status`

Report checkpointed workflow run state.

```text
cis workflow status <run-id> [--repo <path>] [--format <human|json|agent>]
```

Run state and logs live under `.cis/local/workflows/`.
