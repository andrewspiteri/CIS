---
title: "cis frontend inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-frontend-inventory
---

# `cis frontend inventory`

Read normalized frontend observations with optional framework or kind filters.

```text
cis frontend inventory [--framework <name>] [--kind <kind>] [--repo <path>] [--format <human|json|agent>]
```

This command does not rescan source. Missing derived state returns exit code `4`.
