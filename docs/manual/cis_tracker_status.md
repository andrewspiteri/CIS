---
title: "cis tracker status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-tracker-status
---

# `cis tracker status`

Reports configured provider instances, durable task mappings, and open or resolved
conflicts without writing remote state.

```text
cis tracker status [--repo <path>] [--format <human|json|agent>]
```

Disabled provider rows remain visible. Exit `5` means one or more open conflicts;
exit `4` means stored tracker state is unavailable or invalid.

