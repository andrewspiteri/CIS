---
title: "cis tracker resolve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-tracker-resolve
---

# `cis tracker resolve`

Records a human-reviewed disposition for one task/provider conflict.

```text
cis tracker resolve <change-id> <task-id> --provider <key>
  --use <cis|remote|unlink> --reviewer <identity> --reason <rationale>
  [--repo <path>] [--format <human|json|agent>]
```

`cis` schedules the canonical projection for a subsequent reviewed push. `remote`
acknowledges a deliberate remote divergence but does not import approval, completion,
or task content into CIS. `unlink` retains the audit record and stops synchronization.
The decision is appended to the canonical task with reviewer, timestamp, and rationale.

