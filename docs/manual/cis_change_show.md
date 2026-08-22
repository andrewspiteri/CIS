---
title: "cis change show"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-change-show
---

# `cis change show`

Reads one canonical change proposal and reports its outcome, baseline, roots, lifecycle,
and dossier path.

```text
cis change show <change-id> [--repo <path>] [--format <human|json|agent>]
```

The command does not rebuild the graph or alter the dossier. A missing or malformed
change returns exit `2`.
