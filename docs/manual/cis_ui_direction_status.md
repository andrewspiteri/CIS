---
title: "cis ui-direction status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ui-direction-status
---

# `cis ui-direction status`

```text
cis ui-direction status [--workspace <path>] [--format <human|json|agent>]
```

Reports lifecycle, validity, currency, canonical path, source versions, and exact errors or warnings
without changing the workspace. Missing, Review Required, Ready for Approval, Active, and Stale are
kept distinct.
