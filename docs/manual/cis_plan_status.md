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
cis plan status <change-id> [--summary] [--repo <path>] [--format <human|json|agent>]
```

Status is read-only and does not infer implementation progress from Git or agent
activity.

`--summary` returns compact JSON in every output format. It includes lifecycle state, application result, exit code, task totals, up to eight task rows, material blocker counts and bounded blocker messages. It discloses omitted rows and clipped text and points back to ordinary `--format json` for complete records. The complete projection is capped at 12,000 characters and 48,000 UTF-8 bytes; the underlying plan and default JSON shape are unchanged.
