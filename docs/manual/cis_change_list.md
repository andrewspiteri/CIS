---
title: "cis change list"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-list
---

# `cis change list`

Lists valid repository-owned change dossiers in stable ID order.

```text
cis change list [--repo <path>] [--format <human|json|agent>]
```

The command is read-only. It reports ID, lifecycle status, baseline, title, and path.
Malformed folders are not presented as valid dossiers.
