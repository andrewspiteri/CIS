---
title: "cis workflow run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-workflow-run
---

# `cis workflow run`

Start or resume a checkpointed workflow run.

```text
cis workflow run <workflow> [--run-id <id>] [--repo <path>] [--format <human|json|agent>]
```

Commands execute without a shell. Successful steps are skipped on resume; definition drift requires a new run.
