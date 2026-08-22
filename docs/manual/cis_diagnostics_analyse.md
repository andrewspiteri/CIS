---
title: "cis diagnostics analyse"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-diagnostics-analyse
---

# `cis diagnostics analyse`

Persist deterministic error and warning analysis.

```text
cis diagnostics analyse [--repo <path>] [--format <human|json|agent>]
```

Analysis lives under `.cis/local/diagnostics/` and cannot approve or change canonical work.
