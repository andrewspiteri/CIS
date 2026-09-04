---
title: "cis artifacts inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-inventory
---

# `cis artifacts inventory`

Inventory configured local artifact entries, sizes, hashes, timestamps, and retention candidates.

```text
cis artifacts inventory [--family <name>] [--repo <path>] [--format <human|json|agent>]
```

This command is read-only. Exit code `0` means the profile and requested family are valid; `4` means configuration or repository state is invalid.
