---
title: "cis security inventory"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-inventory
---

# `cis security inventory`

Parse the canonical security-suite profile and persist the selected inventory under `.cis/local/security/inventory.json`.

```text
cis security inventory [--repo <path>] [--format <human|json|agent>]
```

Inventory is derived state and does not run scanners or approve exceptions.
