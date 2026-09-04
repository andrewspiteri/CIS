---
title: "cis artifacts plan"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-plan
---

# `cis artifacts plan`

Preview retain, archive, and delete decisions without changing local state.

```text
cis artifacts plan [--family <name>] [--repo <path>] [--format <human|json|agent>]
```

Candidates must be older than the configured age and outside the newest-entry allowance.
