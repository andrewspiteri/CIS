---
title: "cis plan status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-status
---

# `cis plan status`

Reports plan lifecycle, work-item count, validation readiness, impact coverage, and
open-decision count.

```text
cis plan status <change-id> [--repo <path>] [--format <human|json|agent>]
```

Status is read-only and does not infer implementation progress from Git or agent
activity.
