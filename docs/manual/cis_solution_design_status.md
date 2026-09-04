---
title: "cis solution-design status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-solution-design-status
---

# `cis solution-design status`

```text
cis solution-design status [--workspace <path>] [--format <human|json|agent>]
```

Reports paths, source digest, component inventory, lifecycle, validity, currency, warnings,
and errors for the overall solution design and component sheet without changing canonical
state. `Active` is reported only when both files remain the exact approved bundle.
