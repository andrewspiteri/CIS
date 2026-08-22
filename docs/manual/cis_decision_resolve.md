---
title: "cis decision resolve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-decision-resolve
---

# `cis decision resolve`

Records the human-selected option and durable rationale.

```text
cis decision resolve <change-id> <decision-id> --option <recorded-option>
  --rationale <text> [--evidence <text>]
  [--repo <path>] [--format <human|json|agent>]
```

The selected option must exactly match one recorded option. A rationale is mandatory.
Repeating the same resolution is unchanged; changing an already resolved choice is
rejected and requires a superseding decision.
