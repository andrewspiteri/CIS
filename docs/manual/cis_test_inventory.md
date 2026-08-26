---
title: "cis test inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-inventory
---

# `cis test inventory`

Parse the canonical suite profile and persist its derived inventory.

```text
cis test inventory [--repo <path>] [--format <human|json|agent>]
```

The inventory is written to `.cis/local/testing/inventory.json`; Markdown remains canonical.
