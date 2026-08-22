---
title: "cis api inventory"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-api-inventory
---

# `cis api inventory`

Reads normalized API state without rescanning source or invoking a model.

```text
cis api inventory [--repo <path>] [--exposure <class>] [--module <id>] [--format <human|json|agent>]
```

Exposure and module filters are exact and case-insensitive. Exit `4` means discovery
state is absent or invalid; run `cis api discover`. The inventory is a routing and
validation aid, not a replacement for the canonical API dictionary.

