---
title: "cis brd backlog status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-status
---

# `cis brd backlog status`

```text
cis brd backlog status [--workspace <path>] [--format <human|json|agent>]
```

Reports `Missing`, `Review Required`, `Ready for Approval`, `Active`, or `Stale` from
coverage, semantic source currency, technical-intent readiness, and approval evidence.
Managed provenance-only changes in the BRD or technical-intent baseline do not make an
otherwise current approved backlog stale.
