---
title: "cis decision promote"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-decision-promote
---

# `cis decision promote`

Promotes a resolved durable decision into an accepted architecture decision record.

```text
cis decision promote <change-id> <decision-id> [--title <text>]
  [--repo <path>] [--format <human|json|agent>]
```

The command creates a uniquely numbered Markdown ADR beneath
`architecture/decisions/`, catalogs it, records alternatives, evidence, rationale and
originating change provenance, and links the decision to the ADR. Only resolved
decisions may be promoted; repeated promotion returns `unchanged`.
