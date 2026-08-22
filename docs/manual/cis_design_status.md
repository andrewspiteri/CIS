---
title: "cis design status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-status
---

# `cis design status`

Shows the design gate, approval status, renderer, and current PNG artifact inventory.

```text
cis design status <change-id> [--repo <path>] [--format <human|json|agent>]
```

This command is read-only. Agents should query it before attempting any downstream
task transition for a UI-bearing feature.

