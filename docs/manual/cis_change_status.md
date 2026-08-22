---
title: "cis change status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-status
---

# `cis change status`

Reports the lifecycle and exact baseline of one change dossier.

```text
cis change status <change-id> [--repo <path>] [--format <human|json|agent>]
```

This is a read-only view over `proposal.md`; it does not infer completion from plan or
Git state.
