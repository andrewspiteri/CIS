---
title: "cis change close"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-close
---

# `cis change close`

Records explicit human closure in `proposal.md` and appends a lifecycle event.

```text
cis change close <change-id> [--repo <path>] [--format <human|json|agent>]
```

Repeating closure returns `unchanged`. This command records lifecycle state only; it
does not approve verification, commit files, or claim delivery completion.
