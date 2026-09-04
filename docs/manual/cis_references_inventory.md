---
title: "cis references inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-inventory
---

# `cis references inventory`

Read normalized reference state without rescanning source.

```text
cis references inventory [--kind <kind>] [--repo <path>] [--format <human|json|agent>]
```

Run `cis references discover` first when local state is unavailable or stale.
