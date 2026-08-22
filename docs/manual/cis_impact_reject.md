---
title: "cis impact reject"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-reject
---

# `cis impact reject`

Records explicit human exclusion of one finding with a durable rationale.

```text
cis impact reject <change-id> <finding-id> --reason <text>
  [--repo <path>] [--format <human|json|agent>]
```

Rejected findings remain visible as reviewed evidence and are not converted into
bounded work.
