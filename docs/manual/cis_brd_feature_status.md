---
title: "cis brd feature status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-19"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-status
---

# `cis brd feature status`

```text
cis brd feature status --item <HLT-ID>
  [--workspace <path>] [--format <human|json|agent>]
```

Reports the linked feature specification's document state, effective lifecycle state,
validation errors, source-item drift, and approval drift. Feature currency is bound to
the selected high-level item rather than the whole backlog file, so linking or preparing
other features does not revoke an otherwise unchanged feature review.

Use the result to distinguish `Review Required`, `Ready for Approval`, `Active`, and
`Stale`. Status is read-only and never grants approval.
